# Performance baseline

This is a baseline to catch regressions, not a load test and not a capacity claim.

## What is measured

`HardeningTests.Search_and_dashboard_stay_within_the_baseline_budget_at_five_thousand_notices` seeds 5,000 opportunity notices through the real import service into PostgreSQL 16, then times the queries a user runs most. Each figure is the median of five runs after one warm-up, for one user, with the database on the same machine.

| Operation | Measured (2026-11-16, development container) | Budget enforced in CI |
|---|---|---|
| Import of 5,000 notices | about 4 s | not enforced |
| Search, no filters, first page | 7 ms | 2 s |
| Search by keyword | 14 ms | 2 s |
| Search by keyword, soonest deadline, page 20 | 15 ms | 2 s |
| Dashboard | 13 ms | 2 s |

The budget is deliberately loose. It fails only on an order-of-magnitude regression, such as a lost index or a query per row, and not on a slow CI runner. Read the measured figures from the test output when comparing two builds.

## What this does not tell you

- **Concurrency.** Nothing here measures many users at once, connection pool limits, or the rate limiter under load.
- **Network and hosting.** A hosted database adds network latency per query; container CPU limits matter.
- **Larger data.** Keyword search uses a case-insensitive substring match with no full-text index. It is fast at 5,000 notices and will slow down roughly in line with the number of notices. Live SAM.gov data is far larger than the fixture set, so add PostgreSQL full-text search (a standard PostgreSQL feature, no cloud dependency) before relying on live sync at scale.
- **Uploads and scanning.** Upload time is dominated by the malware scanner and storage, which are not measured.

## Before production

Run a load test against the real hosting environment with production-like data volume and the agreed number of concurrent users, and set response-time objectives with the product owner (OPS-01). Record the results here.
