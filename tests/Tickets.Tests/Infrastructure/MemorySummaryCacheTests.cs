using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Tickets.Application.Tickets.Dtos;
using Tickets.Infrastructure.Caching;

namespace Tickets.Tests.Infrastructure;

public sealed class MemorySummaryCacheTests : IDisposable
{
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly MemorySummaryCache _cache;
    private int _computations;

    public MemorySummaryCacheTests() =>
        _cache = new MemorySummaryCache(_memoryCache, NullLogger<MemorySummaryCache>.Instance);

    [Fact]
    public async Task Concurrent_misses_compute_the_summary_only_once()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _cache.GetOrCreateAsync(async ct =>
        {
            Interlocked.Increment(ref _computations);
            await Task.Delay(100, ct);
            return Summary(total: 42);
        }, CancellationToken.None)));

        _computations.Should().Be(1);
        results.Should().OnlyContain(s => s.TotalCount == 42);
    }

    [Fact]
    public async Task Invalidate_removes_the_cached_summary()
    {
        await _cache.GetOrCreateAsync(Compute(total: 1), CancellationToken.None);

        _cache.Invalidate();
        var after = await _cache.GetOrCreateAsync(Compute(total: 2), CancellationToken.None);

        after.TotalCount.Should().Be(2);
        _computations.Should().Be(2);
    }

    [Fact]
    public async Task Summary_computed_across_an_invalidation_is_returned_but_not_cached()
    {
        // A computation that started before an update may have read pre-update data; caching it after the
        // invalidation would bring stale data back for the whole expiration period.
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var inFlight = _cache.GetOrCreateAsync(async _ =>
        {
            started.SetResult();
            await release.Task;
            return Summary(total: 1);
        }, CancellationToken.None);

        await started.Task;
        _cache.Invalidate();
        release.SetResult();

        (await inFlight).TotalCount.Should().Be(1, "the caller still gets its result");
        (await _cache.GetOrCreateAsync(Compute(total: 2), CancellationToken.None)).TotalCount
            .Should().Be(2, "the stale result must not have been cached");
    }

    [Fact]
    public async Task Failed_computation_releases_the_lock_and_caches_nothing()
    {
        var failing = () => _cache.GetOrCreateAsync(_ => throw new InvalidOperationException("db down"), CancellationToken.None);
        await failing.Should().ThrowAsync<InvalidOperationException>();

        (await _cache.GetOrCreateAsync(Compute(total: 3), CancellationToken.None)).TotalCount.Should().Be(3);
    }

    private Func<CancellationToken, Task<TicketSummaryDto>> Compute(int total) => _ =>
    {
        Interlocked.Increment(ref _computations);
        return Task.FromResult(Summary(total));
    };

    private static TicketSummaryDto Summary(int total) => new(total, [], [], [], DateTime.UtcNow);

    public void Dispose()
    {
        _cache.Dispose();
        _memoryCache.Dispose();
    }
}
