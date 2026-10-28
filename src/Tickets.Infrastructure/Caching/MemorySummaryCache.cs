using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Tickets.Application.Abstractions;
using Tickets.Application.Tickets.Dtos;

namespace Tickets.Infrastructure.Caching;

/// <summary>
/// In-process cache for the ticket summary (registered as Singleton).
/// <list type="bullet">
/// <item><b>Expiration:</b> absolute, <see cref="Expiration"/> – a safety net that bounds staleness even if an
/// invalidation is missed (e.g. data changed by the seed command or by another server instance).</item>
/// <item><b>Invalidation:</b> <see cref="Invalidate"/> after every committed status change.</item>
/// <item><b>Stampede protection:</b> on a miss, only one request computes the summary; concurrent requests wait for it.</item>
/// <item><b>No stale write-back:</b> a computation that started before an invalidation is not stored, because it may
/// have read data from before the change (version check).</item>
/// </list>
/// </summary>
public sealed class MemorySummaryCache(IMemoryCache cache, ILogger<MemorySummaryCache> logger) : ISummaryCache, IDisposable
{
    public static readonly TimeSpan Expiration = TimeSpan.FromSeconds(60);
    private const string Key = "tickets:summary";

    private readonly SemaphoreSlim _fillLock = new(1, 1);
    private long _version;

    public async Task<TicketSummaryDto> GetOrCreateAsync(Func<CancellationToken, Task<TicketSummaryDto>> factory, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(Key, out TicketSummaryDto? cached))
        {
            return cached!;
        }

        await _fillLock.WaitAsync(cancellationToken);
        try
        {
            // Another request may have filled the cache while this one was waiting.
            if (cache.TryGetValue(Key, out cached))
            {
                return cached!;
            }

            var versionAtStart = Interlocked.Read(ref _version);
            var summary = await factory(cancellationToken);

            if (Interlocked.Read(ref _version) == versionAtStart)
            {
                cache.Set(Key, summary, Expiration);
                logger.LogDebug("Ticket summary computed and cached for {Seconds} s", Expiration.TotalSeconds);
            }
            else
            {
                logger.LogDebug("Ticket summary computed but not cached: invalidated while it was being computed");
            }

            return summary;
        }
        finally
        {
            _fillLock.Release();
        }
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _version);
        cache.Remove(Key);
    }

    public void Dispose() => _fillLock.Dispose();
}
