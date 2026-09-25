# Virtual Coffee Machine — Build Plan

Take-home assignment for CJ Global Tech (grad/junior .NET role). Orchestrator: OpenCode agent.
Status: executed — archived here for transparency about the AI-assisted workflow.

## Locked decisions

| Decision | Choice |
|---|---|
| Hosting | Render free tier (Docker + Blueprint `render.yaml`) |
| Front-end | Blazor WebAssembly (ASP.NET Core Hosted) |
| Scope | Full showcase (ledger, CI/CD, sounds, docs) |
| Timeline | As fast as possible, quality non-negotiable |
| Repo | `virtual-coffee-machine`, public, on user's GitHub |
| Repo | `virtual-coffee-machine` on GitHub |
| .NET | 8 LTS (SDK installed to `~/.dotnet` if missing) |
| DB | EF Core + SQLite (documented MySQL swap) |

## 1. Assignment brief (verbatim requirements)

- Accept 1,2,5,10,20,50 cent, 1 and 2 dollar coins. **1c and 2c are rejected.**
- When inserted total >= cost, user may choose one of three coffees:
  1. Cappuccino $3.50
  2. Latte $3.00
  3. Decaf $4.00
- Machine dispenses the correct coffee and change, **broken into coinage**.

## 2. Architecture

```
virtual-coffee-machine/
├── CoffeeMachine.sln
├── src/
│   ├── CoffeeMachine.Contracts/      # DTOs shared by Api + Client (canonical below)
│   ├── CoffeeMachine.Domain/         # pure C#, zero dependencies
│   │   ├── Money/    Denomination.cs, CoinValidator.cs, ChangeCalculator.cs
│   │   ├── Machine/  VendingMachine.cs, MachineState.cs, Inventory.cs
│   │   ├── Menu/     MenuItem.cs, CoffeeMenu.cs
│   │   └── Ledger/   Transaction.cs, TransactionLedger.cs
│   ├── CoffeeMachine.Api/            # Web API (controllers) + hosts WASM client on publish
│   │   ├── Controllers/  MenuController, MachinesController, TransactionsController, AdminController
│   │   ├── Data/         AppDbContext.cs (SQLite), MachineRegistry.cs
│   │   └── Program.cs    (Swagger, ProblemDetails, /healthz)
│   └── CoffeeMachine.Client/         # Blazor WASM
│       ├── Components/  Machine.razor, Display, CoinSlot, CoinReturn, CoffeeButton, CupArea, ChangeTray, LedgerPanel, ServicePanel
│       ├── Services/    MachineApiClient.cs
│       └── wwwroot/     css/, js/sound.js (interop only, WebAudio-generated)
├── tests/
│   ├── CoffeeMachine.Domain.Tests/
│   └── CoffeeMachine.Api.Tests/      # WebApplicationFactory + in-memory SQLite
├── render.yaml                        # Render Blueprint
├── Dockerfile                         # multi-stage, non-root, HEALTHCHECK /healthz
├── .github/workflows/ci.yml           # build+test on PR; publish on main
├── ADR.md, README.md, LICENSE
```

### Domain model
- `Denomination` enum: 1,2,5,10,20,50,100,200 cents. `CoinValidator` rejects 1¢/2¢/unknown.
- `ChangeCalculator`: greedy over `[200,100,50,20,10,5]` — AUD denominations are canonical ⇒ greedy is provably minimal. All prices & accepted coins are multiples of 5 ⇒ exact change always exists given inventory.
- `VendingMachine` state machine: `Idle → AwaitingSelection → Dispensing → Idle`; `Cancel` refunds at any accept state. Server transitions synchronously; **client plays the 2–3s brew animation locally** (no Brewing state on the wire).
- `Inventory`: inserted coins feed stock; change drawn from stock. Insufficient stock for required change ⇒ **"Exact change only"**: refund inserted coins, refuse sale. Admin refill via service panel.
- Per-visitor machine: GUID from localStorage; state + append-only ledger persisted to SQLite ⇒ refresh restores balance/mid-brew state.

### Canonical DTO contracts (Contracts project — A1 owns, A2/A3 consume)

```csharp
public sealed record MenuItemDto(string Id, string Name, int PriceCents, string PriceDisplay, bool InStock);
public sealed record InventoryDto(IReadOnlyDictionary<int, int> Coins, IReadOnlyDictionary<string, int> Items); // denominationCents -> count; itemId -> stock
public sealed record MachineStateDto(string MachineId, string State, int BalanceCents, string BalanceDisplay,
                                     IReadOnlyList<MenuItemDto> Menu, InventoryDto Inventory, string? StatusMessage);
public sealed record InsertCoinRequest(int DenominationCents);
public sealed record InsertCoinResponse(bool Accepted, string? RejectionReason, int BalanceCents, string BalanceDisplay);
public sealed record SelectItemRequest(string ItemId);
public sealed record ChangeCoinDto(int DenominationCents, int Count);
public sealed record PurchaseResponse(string ItemId, string ItemName, int PriceCents, int PaidCents,
                                      int ChangeTotalCents, IReadOnlyList<ChangeCoinDto> Change);
public sealed record CancelResponse(int RefundedCents, IReadOnlyList<ChangeCoinDto> Refund);
public sealed record TransactionDto(Guid Id, DateTimeOffset Timestamp, string MachineId, string ItemId,
                                    int PaidCents, int ChangeCents, string Status);
public sealed record RefillRequest(int[] CoinDenominations, int[] CoinCounts, Dictionary<string,int>? Items);
```

### API (`/api/v1`, Swagger at `/swagger`, RFC 7807 ProblemDetails on errors)

| Endpoint | Notes |
|---|---|
| `GET /menu` | menu with price display `"$3.50"` |
| `GET /machines/{id}` | full state (created on demand) |
| `POST /machines/{id}/coins` | `{denominationCents}` → accepted/rejected + balance |
| `POST /machines/{id}/selections` | `{itemId}` → 200 purchase w/ change; 409 `insufficient-funds` / `out-of-stock` / `exact-change-only` |
| `POST /machines/{id}/cancel` | refund inserted coins |
| `GET /transactions?machineId=&limit=` | paged ledger |
| `POST /admin/refill` | service panel restock |

States: `Idle`, `AwaitingSelection`, `Dispensing`.

### UI spec
LED 7-segment credit display + status text (`INSERT COIN`, `1¢ REJECTED`, `EXACT CHANGE ONLY`, `ENJOY!`); coin buttons 5¢–$2 **plus visible 1¢/2¢ that get rejected** into the coin-return tray; three drink buttons glow when affordable; purchase = cup drops → pours → steam → change coins cascade one-by-one with clinks; coin-return tray clickable to "take" coins; ledger panel; discreet service panel. Sounds: WebAudio-generated (coin clink, pour, hum), mute toggle, unlock on first gesture. Keyboard: `5`–`2` coins, `1/2/3` select, `C` cancel. A11y: aria-live display, labels, `prefers-reduced-motion`. Responsive to mobile.

## 3. Testing strategy
- Unit: validator matrix; exhaustive change test (balance 0–4000¢ × all 3 prices ⇒ sum correct, minimal count, never 1¢/2¢); state transitions; inventory & exact-change; cancel; ledger.
- Integration: WebApplicationFactory + SQLite in-memory — full purchase flows and every error case.
- Manual browser checklist: mobile width, two tabs independent, refresh mid-brew, reduced motion.
- CI badge (build/tests) in README.

## 4. CI/CD & hosting
- GitHub Actions: PRs → build+test; `main` → test + publish artifact. Render auto-deploys `main` via Dockerfile.
- Docker: multi-stage `sdk:8.0` → `aspnet:8.0`, non-root `USER app`, `ENV ASPNETCORE_URLS=http://+:8080`, `EXPOSE 8080`, HEALTHCHECK `/healthz`.
- `render.yaml` Blueprint: web service, docker runtime, `PORT=8080` env override, `healthCheckPath: /healthz`, free plan. User does a one-click Blueprint connect (or supplies Render API key; fallback: dashboard service pointing at Dockerfile).
- Free tier sleeps after ~15 min ⇒ keep-warm ping + hero GIF in README.

## 5. Submission artifacts
- Public repo, conventional commits, feature branches → PRs.
- README: live URL, hero GIF, features, Mermaid architecture diagram, quickstart (`dotnet run` / Docker), curl examples, testing, badges.
- `ADR.md`: why Blazor WASM; why greedy change; why SQLite-over-MySQL for demo; why per-visitor machines.
- Roadmap: MAUI/Blazor Hybrid port, shared multi-user machine, hardware coin-acceptor (YachtPilot hook).
- Cover-email draft (3–4 lines: live link, repo, proudest design decision).

## 6. Sub-agent roster & orchestration

File ownership is exclusive — no two agents may edit the same files. Each agent: read this plan first, work on its branch, conventional commits, report with acceptance evidence.

| # | Agent | Owns | Mission | Depends on |
|---|---|---|---|---|
| A0 | Scaffold & Toolchain | solution skeleton, .gitignore, Directory.Build.props, CI skeleton, Dockerfile, git setup | SDK check/install, 6 projects, hosted-WASM wiring, build+test green, gh auth check, push main | — |
| A1 | Domain Core | Domain + Contracts + Domain.Tests | Implement canonical contracts, validator, change calculator, state machine, inventory, menu, ledger + exhaustive tests | A0 |
| A2 | API & Persistence | Api project (Program.cs, Controllers, Data), Api.Tests | Endpoints per spec, EF Core SQLite, machine registry, ProblemDetails, Swagger, /healthz, integration tests | A1 |
| A3 | Frontend/UI | Client project | Machine UI per spec, animations, sounds, keyboard, a11y, responsive, ledger+service panels | A1 contracts; A2 for live testing |
| A4 | QA & Hardening | none (touches all, fixes only) | Adversarial pass: races, refresh mid-brew, exact change, mobile, reduced motion; bug reports → fixes | A2, A3 |
| A5 | DevOps & Hosting | render.yaml, docs/DEPLOYMENT.md, CI deploy job, Dockerfile finalize | Blueprint config, deploy wiring, health check, badges, keep-warm, deploy runbook | A0–A4 |
| A6 | Docs & Submission | README.md, ADR.md, LICENSE, demo GIF, cover-email draft | Polish submission artifacts | A4, A5 |

**Phases (speed-optimized):**
1. A0 (foreground)
2. A1 ∥ A5-pre (render.yaml only) — then A2 ∥ A3
3. A4 QA loop
4. A5-finalize ∥ A6
5. Orchestrator final review vs brief, demo GIF, live link + repo handed to user.

## 7. Risks & mitigations
- SDK missing ⇒ `dotnet-install.sh` to `~/.dotnet` (no sudo).
- Docker missing ⇒ skip local image build; verify on Render; fallback native runtime.
- `gh` not authed ⇒ pause and ask user to `gh auth login`.
- Render cold start ⇒ keep-warm + GIF.
- WASM load size ⇒ publish trimming, loading splash.
- Audio autoplay ⇒ gesture unlock + mute.
- Scope creep ⇒ deferred features list ("Future ideas").
