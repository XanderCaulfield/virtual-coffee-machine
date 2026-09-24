# Architecture Decision Records

Short, dated records of the consequential choices in this codebase, kept so
reviewers can see *why* things are the way they are.

## ADR-0001 — Blazor WebAssembly (hosted) over a JavaScript SPA

**Context.** The assignment is a UI-driven machine: coin insertion, a
multi-step brew animation, sounds and keyboard handling. A React/Vue SPA would
mean two languages, duplicated DTO types, and hand-rolled serialization.

**Decision.** Blazor WebAssembly in the ASP.NET Core *hosted* model: one
`CoffeeMachine.Contracts` assembly is shared by the API and the client, so the
DTOs (`MachineStateDto`, `PurchaseResponse`, …) are defined exactly once and
compiled into both sides. The API project serves the compiled WASM bundle, so
a single deployable serves everything.

**Consequences.** C# end-to-end with compile-time contract checking; no
JavaScript build toolchain; the domain stays in a dependency-free project.
Cost: a larger initial WASM download (mitigated by Release publishing and a
loading splash).

## ADR-0002 — Greedy change, and why it is provably minimal

**Context.** Change must be broken into coinage, and a demo for a .NET
interview invites scrutiny of the algorithm. The obvious alternative is
dynamic programming.

**Decision.** A greedy pass over `[200, 100, 50, 20, 10, 5]` — always take the
largest coin that fits. Australian denominations form a *canonical* coin
system (each denomination divides the next-larger one), in which greedy is
provably minimal for every amount. Because all prices (300/350/400¢) and all
accepted coins are multiples of 5¢, change is always exact and never emits the
rejected 1¢/2¢ denominations.

**Consequences.** O(1) constant work per denomination instead of an O(n·d) DP
table. The claim is not taken on faith: an exhaustive test breaks down every
amount from 0–4000¢ and asserts the greedy result equals a DP reference
implementation coin-for-coin. If coin denominations ever change to a
non-canonical set, this ADR flags that the algorithm must be revisited.

## ADR-0003 — SQLite + EF Core for the demo, MySQL as a documented provider swap

**Context.** The plan specified a relational store with EF Core. MySQL needs a
running server; this demo runs in a single container on Render's free tier
with an ephemeral disk.

**Decision.** EF Core with SQLite: zero-infrastructure, file-backed, and the
same `AppDbContext`/entities/migrations code path a real database would use.
`DB_PATH` controls the file location; the Dockerfile points it at a writable
`/data` volume owned by the non-root app user.

**Consequences.** Persistence survives refresh and restart of the process but
is intentionally ephemeral across redeploys (each visitor gets a fresh
machine anyway — see ADR-0004). Swapping to MySQL for a "real" deployment is a
one-line provider change (`UseSqlite` → `UseMySql`) plus a connection string;
no schema or query changes.

## ADR-0004 — Per-tab machines via a sessionStorage GUID, persisted server-side

**Context.** The UI must survive a refresh mid-brew, and two reviewers in two
tabs must not fight over one machine's balance.

**Decision.** Each browser tab mints a GUID machine id, stored in
`sessionStorage` (per-tab, unlike `localStorage`). The server creates the
machine on first touch and persists its state — balance, coin stock, item
stock and the append-only ledger — in SQLite keyed by that id.

**Consequences.** Refresh restores balance and stock; two tabs are fully
independent; ledger rows are attributable per machine. The GUID never leaks
between tabs, and a shared multi-user machine (one id, broadcast stock) is a
documented roadmap item rather than an accident of the demo.

## ADR-0005 — Swagger enabled in Production

**Context.** Default ASP.NET Core templates gate Swagger to Development. This
project is a hosted demo that interviewers will explore without cloning it.

**Decision.** `UseSwagger`/`UseSwaggerUI` run unconditionally, including
`ASPNETCORE_ENVIRONMENT=Production` on Render, exposing the full REST surface
at `/swagger` next to the live UI.

**Consequences.** Reviewers can exercise every endpoint interactively against
the live demo. The API is public and unauthenticated by design (nothing to
protect — no user data); if real accounts ever appear, Swagger gating is the
first thing to revisit.
