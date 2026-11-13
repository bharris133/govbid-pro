# ADR-0001: Modular monolith on .NET 10, Razor Pages and strict TypeScript

- Status: Accepted
- Date: 2026-11-13
- Source: Handoff v4 §2, §4, §8

## Context

The handoff fixes the stack: C# on .NET 10 LTS, ASP.NET Core Web API plus Razor Pages, PostgreSQL with EF Core 10 and Npgsql, strict TypeScript for all browser logic, Docker for packaging, and a modular monolith rather than network microservices.

## Decision

- Projects: `Domain` (no dependencies), `Application` (depends on Domain; uses EF Core abstractions only), `Infrastructure` (EF/Npgsql, Identity, storage, adapters), `Web` (composition root, controllers, Razor Pages). A `Worker` process will be added with the ingestion milestone (P1.2); until then the retention purge runs as a disabled-by-default hosted service.
- Browser code is TypeScript only, compiled by `tsc` to ES modules in `wwwroot/dist` (generated, not committed). No bundler yet; adopting Vite would need a consistent switch (handoff §8).
- Analyzers on, warnings as errors, central package management, NuGet and npm lock files.
- RFC 9457 problem details; enum values serialized as strings; EF entities never returned directly from controllers except admin import-run records.
- Pinned toolchain: .NET SDK via `global.json` (10.0.1xx feature band), Node 22 via `.nvmrc`, TypeScript 5.9.3.

## Consequences

- One deployable image; the worker split is a deployment change, not a domain split.
- `Application` referencing `Microsoft.EntityFrameworkCore` (for `DbSet`/LINQ) is a pragmatic trade-off. It does not reference any provider.
- Identity column sizes and schema version are pinned in the DbContext so the EF model is identical whether built by the web host, tests or tooling. A CI step fails if migrations drift from the model.
