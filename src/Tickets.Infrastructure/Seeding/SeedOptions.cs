namespace Tickets.Infrastructure.Seeding;

/// <summary>Options of the <c>seed</c> command: <c>seed [--count N] [--reset]</c>.</summary>
public sealed record SeedOptions(int Count, bool Reset)
{
    public const string CommandName = "seed";
    public const int DefaultCount = 100_000;
    public const int MaxCount = 2_000_000;

    /// <summary>Returns null when <paramref name="args"/> is not a seed command.</summary>
    public static SeedOptions? TryParse(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var count = DefaultCount;
        var reset = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--reset":
                    reset = true;
                    break;
                case "--count" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed):
                    count = parsed;
                    i++;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete seed argument '{args[i]}'. Usage: seed [--count N] [--reset]");
            }
        }

        if (count is < 1 or > MaxCount)
        {
            throw new ArgumentException($"--count must be between 1 and {MaxCount:N0}.");
        }

        return new SeedOptions(count, reset);
    }
}
