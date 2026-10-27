# AI Usage Log

Running log of where AI assistants were used during development, and what was reviewed or changed by hand.
This log feeds the "AI usage" section of the README.

Tools: **Claude Code** (agent in VS Code, writes code and docs) and **Claude (chat)** (used by the author as a second
reviewer of plans and outputs).

| # | Area / task | What the AI did | What was reviewed / changed manually |
|---|---|---|---|
| 1 | T-01 Work plan | Claude Code drafted `docs/PLAN.md` from the specification (requirement IDs, tasks, estimates, traceability). | The plan was reviewed with the help of a second AI assistant (Claude chat); the author decided on and directed the changes: `RequestId` = ticket FK with a separate `CorrelationId`; cross-cutting task moved before the first endpoint; test tasks moved right after each feature; total hours corrected; CancellationToken/logging added to acceptance criteria; cache justification + scale-out as deliverables, invalidation after commit; `requests.http` task; deterministic + parallel concurrency tests; RowVersion in body (decided and documented); full build + all tests in final verification; index list marked as hypothesis until measured; this log. |
| 2 | T-02 Tooling | Checked installed versions; proposed .NET 10 instead of .NET 8 (not installed, near end of support). | Confirmed by the author – `dotnet --version` → `10.0.303`. |
