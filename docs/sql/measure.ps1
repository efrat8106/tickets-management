<#
.SYNOPSIS
  Measures the queries in docs/sql/queries against the running SQL Server container.

.DESCRIPTION
  For every query file:
    1. Cold run  – CHECKPOINT; DBCC DROPCLEANBUFFERS; DBCC FREEPROCCACHE; then one run (data read from disk, plan compiled).
    2. Warm runs – the file is run -WarmRuns times in a row (data in memory, plan cached); the median is reported.
    3. Plan      – one more run with SET STATISTICS XML ON; the actual execution plan(s) are saved as .sqlplan files
                   (open in SSMS / Azure Data Studio) and summarized as a list of operators.
  Raw sqlcmd output (STATISTICS IO / TIME messages) is kept unchanged in results/<Label>/raw.

  Per run, logical reads / CPU / elapsed are summed over the statements of the file (e.g. COUNT + page query).
  Only the "Execution Times" that directly follow a statement's IO line are counted; the extra line that
  sp_executesql prints for the outer call (which includes compilation) is ignored.

.EXAMPLE
  ./docs/sql/measure.ps1 -Label before
#>
param(
    [Parameter(Mandatory)] [string] $Label,
    [int] $WarmRuns = 5,
    [string] $Container = 'tickets-sqlserver',
    [string] $Password = $(if ($env:MSSQL_SA_PASSWORD) { $env:MSSQL_SA_PASSWORD } else { 'Tickets_Dev_Passw0rd' }),
    [string] $Database = 'TicketsDb'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$queryDir = Join-Path $root 'queries'
$outDir = Join-Path $root "results/$Label"
$rawDir = Join-Path $outDir 'raw'
$planDir = Join-Path $outDir 'plans'
New-Item -ItemType Directory -Force $rawDir, $planDir | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)

docker exec $Container mkdir -p /tmp/queries | Out-Null
docker cp "$queryDir/." "${Container}:/tmp/queries" | Out-Null

function Invoke-Sql([string[]] $Arguments) {
    $output = docker exec $Container /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $Password -C -d $Database -f 65001 @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $($output -join "`n")" }
    return ($output -join "`n")
}

# Parses one run's STATISTICS output into totals.
function Get-RunStats([string] $text) {
    $logical = 0; $physical = 0; $cpu = 0; $elapsed = 0; $pendingIo = $false
    foreach ($line in $text -split "`n") {
        if ($line -match "^Table '.+'\. Scan count \d+, logical reads (\d+), physical reads (\d+), page server reads \d+, read-ahead reads (\d+)") {
            $logical += [int]$Matches[1]; $physical += [int]$Matches[2] + [int]$Matches[3]; $pendingIo = $true
        }
        # The time match must be evaluated last: every -match overwrites $Matches.
        elseif ($pendingIo -and $previous -match 'Execution Times' -and $line -match 'CPU time = (\d+) ms,\s+elapsed time = (\d+) ms') {
            $cpu += [int]$Matches[1]; $elapsed += [int]$Matches[2]; $pendingIo = $false
        }
        $previous = $line
    }
    [pscustomobject]@{ LogicalReads = $logical; PhysicalReads = $physical; CpuMs = $cpu; ElapsedMs = $elapsed }
}

function Get-Median([double[]] $values) {
    $sorted = $values | Sort-Object
    $n = $sorted.Count
    if ($n % 2) { $sorted[[int][math]::Floor($n / 2)] } else { ($sorted[$n / 2 - 1] + $sorted[$n / 2]) / 2 }
}

# Short operator list of an actual plan, e.g. "Clustered Index Scan[PK_Tickets] > Sort > Top".
function Get-PlanSummary([xml] $plan) {
    $ns = New-Object System.Xml.XmlNamespaceManager($plan.NameTable)
    $ns.AddNamespace('p', 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    $ops = foreach ($relOp in $plan.SelectNodes('//p:RelOp', $ns)) {
        $index = $relOp.SelectSingleNode('./*/p:Object/@Index', $ns)
        $name = $relOp.PhysicalOp
        if ($index) { $name += "[$($index.Value.Trim('[', ']'))]" }
        $name
    }
    ($ops | Select-Object -Unique) -join ' > '
}

$rows = @()
foreach ($file in Get-ChildItem $queryDir -Filter *.sql | Sort-Object Name) {
    $name = $file.BaseName
    $containerFile = "/tmp/queries/$($file.Name)"
    Write-Host "Measuring $name ..."

    Invoke-Sql @('-Q', 'CHECKPOINT; DBCC DROPCLEANBUFFERS WITH NO_INFOMSGS; DBCC FREEPROCCACHE WITH NO_INFOMSGS;') | Out-Null
    $coldText = Invoke-Sql @('-i', $containerFile)
    [IO.File]::WriteAllText((Join-Path $rawDir "$name.cold.txt"), $coldText, $utf8)
    $cold = Get-RunStats $coldText

    $warm = @()
    $warmText = New-Object System.Text.StringBuilder
    for ($i = 1; $i -le $WarmRuns; $i++) {
        $text = Invoke-Sql @('-i', $containerFile)
        [void]$warmText.AppendLine("===== warm run $i =====").AppendLine($text)
        $warm += Get-RunStats $text
    }
    [IO.File]::WriteAllText((Join-Path $rawDir "$name.warm.txt"), $warmText.ToString(), $utf8)

    $planWrapper = "/tmp/plan-wrapper.sql"
    docker exec $Container sh -c "printf 'SET STATISTICS XML ON;\n:r $containerFile\n' > $planWrapper" | Out-Null
    $planText = Invoke-Sql @('-y', '0', '-i', $planWrapper)
    $plans = [regex]::Matches($planText, '<ShowPlanXML[\s\S]*?</ShowPlanXML>')
    $summaries = @()
    for ($p = 0; $p -lt $plans.Count; $p++) {
        [IO.File]::WriteAllText((Join-Path $planDir "$name.$($p + 1).sqlplan"), $plans[$p].Value, $utf8)
        $summaries += "stmt $($p + 1): " + (Get-PlanSummary ([xml]$plans[$p].Value))
    }

    $rows += [pscustomobject]@{
        Query          = $name
        LogicalReads   = (Get-Median ($warm | ForEach-Object LogicalReads))
        ColdPhysical   = $cold.PhysicalReads
        ColdElapsedMs  = $cold.ElapsedMs
        WarmCpuMs      = (Get-Median ($warm | ForEach-Object CpuMs))
        WarmElapsedMs  = (Get-Median ($warm | ForEach-Object ElapsedMs))
        WarmElapsedAll = (($warm | ForEach-Object ElapsedMs) -join '/')
        Plan           = $summaries -join ' | '
    }
}

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Measurement: $Label").AppendLine()
[void]$md.AppendLine("Measured $(Get-Date -Format 'yyyy-MM-dd HH:mm') on SQL Server $((Invoke-Sql @('-h', '-1', '-Q', 'SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY(''ProductVersion'') AS varchar(20))')).Trim()) (Docker), $((Invoke-Sql @('-h', '-1', '-Q', 'SET NOCOUNT ON; SELECT COUNT(*) FROM Tickets')).Trim()) tickets. Warm = median of $WarmRuns runs.").AppendLine()
[void]$md.AppendLine('| Query | Logical reads | Cold: physical + read-ahead reads | Cold elapsed (ms) | Warm CPU (ms) | Warm elapsed (ms) | Warm runs (ms) |')
[void]$md.AppendLine('|---|---:|---:|---:|---:|---:|---|')
foreach ($r in $rows) {
    [void]$md.AppendLine("| $($r.Query) | $($r.LogicalReads) | $($r.ColdPhysical) | $($r.ColdElapsedMs) | $($r.WarmCpuMs) | $($r.WarmElapsedMs) | $($r.WarmElapsedAll) |")
}
[void]$md.AppendLine().AppendLine('## Actual plan operators').AppendLine()
foreach ($r in $rows) { [void]$md.AppendLine("- **$($r.Query)**: $($r.Plan)") }
[IO.File]::WriteAllText((Join-Path $outDir 'summary.md'), $md.ToString(), $utf8)

$rows | Format-Table Query, LogicalReads, ColdPhysical, ColdElapsedMs, WarmCpuMs, WarmElapsedMs -AutoSize
Write-Host "Summary written to $(Join-Path $outDir 'summary.md')"
