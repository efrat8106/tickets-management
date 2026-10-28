# Caching

## What is cached – and why only the summary

**Cached:** the result of `GET /api/tickets/summary` (counts by status, counts by priority, top 10 assignees by open
tickets). One key: `tickets:summary`.

**Why the summary:**
- **Expensive relative to its size:** three `GROUP BY` queries over the whole `Tickets` table on every call, to
  produce ~17 numbers.
- **Read often:** the summary panel is shown on the main screen and refreshed after each update, by every user.
- **Same for everyone:** it has no parameters, so one cache entry serves all users – a very high hit rate.
- **Tolerates short staleness:** it is an overview, not a source of truth for a decision on a single ticket.

**Why not the ticket list (or single tickets):**
- **Huge key space:** every combination of filters, search text, sort and page is a different key; the hit rate
  would be low and memory use unbounded.
- **Must be fresh:** users act on list rows (status updates with RowVersion). A stale row means an avoidable 409.
- **Already cheap:** paging returns 20 rows through indexes; the database's own buffer pool caches hot pages.
- **Invalidation would be hard:** one status change affects an unknown subset of cached list pages.

## Expiration

Absolute expiration of **60 seconds** (`MemorySummaryCache.Expiration`). It is a safety net, not the main freshness
mechanism: it bounds staleness for changes the application cannot see (e.g. the seed command run from another process,
manual SQL, or – in a multi-instance deployment – an update handled by another server).

## Invalidation

- After a **committed** status change, `ISummaryCache.Invalidate()` removes the entry. The next request recomputes it.
- **Only after success:** a 404 / 409 / 422 changes nothing in the database, so the cached summary stays valid.
- **Single update:** invalidated after `SaveChanges` returned successfully.
- **Bulk update:** invalidated **once** at the end of the request if at least one item was committed – in a `finally`
  block, so it also happens if the request is cancelled or fails midway (items saved before that point are already
  committed).
- **No stale write-back:** if a summary computation started *before* an invalidation and finishes *after* it, its
  result may be based on data from before the change. A version counter is incremented on every invalidation; a
  computation only stores its result if the version did not change while it ran.

## Stampede protection

On a cache miss, a `SemaphoreSlim` lets a single request compute the summary; concurrent requests wait and then read
the freshly cached value (double-checked after acquiring the lock) instead of all running the three aggregation
queries at once.

## Scale-out (multiple server instances)

`IMemoryCache` lives inside one process. With several API instances behind a load balancer:

- Each instance has its own copy; an update handled by instance A invalidates only A's cache.
- Instance B keeps serving its cached summary until its 60-second expiration – staleness is **bounded** but real.

Required adjustment:

1. **Distributed cache:** implement `ISummaryCache` with Redis (`IDistributedCache` / StackExchange.Redis). All
   instances read and invalidate the **same** key, so an invalidation is visible everywhere immediately. Callers do
   not change – this is why the cache is behind an interface.
2. **Alternative – keep local caches, broadcast invalidation:** publish an "invalidate summary" message (Redis Pub/Sub
   or a message bus) that every instance subscribes to. Keeps reads in-process (fastest), at the cost of an extra
   moving part.
3. **Stampede across instances:** the `SemaphoreSlim` only coordinates requests within one process. With N instances,
   up to N computations can run on a simultaneous miss – acceptable for this query; a distributed lock would be
   over-engineering here.
4. The version check against stale write-back would move into the distributed cache as well (e.g. a version key
   checked with a conditional write), or be replaced by short expiration.

Until then, the 60-second expiration is the documented upper bound on staleness in a multi-instance deployment
(known limitation).
