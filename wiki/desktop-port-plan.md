# Posit.Dt Dashboard Port Plan

Goal: port the Shepherd.Web Blazor Server dashboard framework into `src/Posit.Dt/` and adapt its data layer to the Posit schema.

## Current State

- **Shepherd.Web** (`/c/Users/goldf/orch/src/Shepherd.Web/`) is a working Blazor Server dashboard with the pages listed below, a `DashboardRepository` (raw Npgsql), and a Bootstrap + scoped-CSS layout.
- **Posit.Dt** (`/c/Users/goldf/Posit/src/Posit.Dt/`) is a minimal Blazor Server app that builds cleanly. It currently has:
  - `Program.cs` with `UseHttpsRedirection` gated to non-Development.
  - A simple `MainLayout` with no sidebar/nav and no CSS framework.
  - One page: `Home.razor`, which lists `posit_artifacts.artifacts` session IDs via `ArtifactRepository`.
  - No `_Imports.razor` aliases for data/render namespaces beyond basics.
  - No dark theme, no navigation menu, no dashboard repository.
  - Already references `Posit.Data`, `Posit.Core`, and `Posit.Contracts`.

The Posit DB is the same Postgres host/port/database used by Shepherd (`Host=localhost;Port=5434;Database=shepherd;Username=shepherd;Password=shepherd`).

## 1. Page/Component Mapping

| Shepherd.Web page | Purpose | Posit.Dt target | Schema adaptation |
|---|---|---|---|
| `Home.razor` (`/`) | Redirect to `/sessions` | Keep `/` as redirect to `/sessions` | No data change |
| `Sessions.razor` (`/sessions`) | Session list table | `Components/Pages/Sessions.razor` | Read from `posit_state.sessions` instead of `shepherd_state.sessions`; derive description/phase/cost from `state_json` |
| `PipelineView.razor` (`/pipeline/{sessionId}`) | 10-phase visual flow + details | `Components/Pages/PipelineView.razor` | Map to Posit 11-phase list (add `dafny-contracts`, `dafny-implementation`, `csharp-implementation`, drop `implementation` as a single phase, remap `qa`) |
| `PromptHarvest.razor` (`/prompts`) | Prompt/response table + expand detail | `Components/Pages/PromptHarvest.razor` | Read from `posit_qa.prompts_log` instead of `shepherd_qa.prompts_log` (schemas nearly identical) |
| `ImplTrace.razor` (`/impl-trace`) | Implementation trace table | **Table** | Shepherd uses `shepherd_qa.implementation_trace`. Posit equivalent: `posit_qa.dafny_results` for Dafny phase, and source-code build failures can be sourced from `posit_qa.prompts_log`/`posit_audit.events` + artifact payload parse status. Implement as thin `PositImplTraceRepository` over `dafny_results` + `prompts_log`. |
| `DesignReviews.razor` (`/design-reviews`) | Design review log | **Table** | Shepherd uses `shepherd_qa.design_review_log`. Posit has no direct equivalent. The `DesignReview` artifact is stored in `posit_artifacts.artifacts` (`kind = 'DesignReview'`). Implement a repository that reads those artifacts, or add `posit_qa.design_review_log` table. Recommendation: for this port, **omit DesignReviews page** and add it later after a migration. |
| `Trials.razor` (`/trials`) | Trial launcher + phase drill-down | `Components/Pages/Trials.razor` | Reuse `Sessions`/`PromptHarvest`/`ImplTrace` repositories; the launch button stays stubbed (no actual runner yet) |
| `UserGuide.razor` (`/guide`) | Static help page | `Components/Pages/UserGuide.razor` | Rewrite content for Posit phase names and data sources |
| `Error.razor` (`/Error`) | Error display | `Components/Pages/Error.razor` | Copy with namespace/usings update |
| `NotFound.razor` (`/not-found`) | 404 | `Components/Pages/NotFound.razor` | Already implicitly handled by `Routes.razor`; can create explicit page |
| `Login.razor` / `Locked.razor` / auth components | Shepherd security layer | **Omit for now** | Posit.Dt has no auth subsystem. Defer until `Posit.Security` project exists. |
| `Layout/MainLayout.razor` | Top-level layout | Replace/extend existing `MainLayout.razor` | Add dark theme + `NavMenu` |
| `Layout/NavMenu.razor` | Sidebar navigation | Create `Components/Layout/NavMenu.razor` | Posit branding/links |
| `ReconnectModal.razor` | Blazor reconnect UI | **Optional** | Copy if interactive server reconnect UX is desired; otherwise rely on framework default |

## 2. Posit-Specific Repository/Service Layer

Replace `Shepherd.Web.Data.DashboardRepository` with a set of small, Posit-specific read-only repositories under a new namespace `Posit.Dt.Data`.

### Proposed files

| File | Responsibility | Key query sources |
|---|---|---|
| `src/Posit.Dt/Data/PositDashboardRepository.cs` | Aggregate/sessions + cost rollup | `posit_state.sessions`, `posit_artifacts.artifacts`, `posit_qa.prompts_log` |
| `src/Posit.Dt/Data/PositPromptRepository.cs` | Prompt list + detail | `posit_qa.prompts_log` |
| `src/Posit.Dt/Data/PositImplTraceRepository.cs` | Dafny/build trace rows | `posit_qa.dafny_results`, `posit_qa.prompts_log` |
| `src/Posit.Dt/Data/PositSessionSummary.cs` | DTO for session row | Maps from `SessionState` JSON |
| `src/Posit.Dt/Data/PositPromptEntry.cs` | DTO for prompt row | Mirrors `posit_qa.prompts_log` |
| `src/Posit.Dt/Data/PositImplTraceEntry.cs` | DTO for trace row | Combines `dafny_results` + prompt parse status |

### Key schema mapping details

#### `posit_state.sessions`
- `session_id` maps directly.
- `state_json` contains `SessionState` serialized JSON.
- Fields needed by the UI: `Status`, `CurrentPhaseId`, `CurrentPhaseStatus`, `CurrentAttempt`, `CompletedPhases`, `RunningCosts`, `StartedAt`, `LastAdvancedAt`, `InitialRequest.Description`.
- The repository should deserialize `state_json` into a lightweight DTO (`PositSessionSummary`) or project fields with `jsonb` operators (e.g., `state_json->>'status'`).
- Posit `SessionStatus` enum values: `Idle`, `Planning`, `Active`, `Validating`, `Retry`, `CheckpointRollback`, `Recovery`, `ReviewGate`, `Paused`, `Completed`, `Aborted`, `Abandoned`. Status badge styles need to be extended.

#### Cost/tokens
- `RunningCosts` inside `state_json` provides `InputTokens`, `OutputTokens`, `AmountUsd`.
- Prompt-level costs can also be summed from `posit_qa.prompts_log` as a fallback.

#### Phases
- Shepherd has 10 phases (`ideation`..`documentation`) with a single `implementation` phase.
- Posit has 11 phases: add `dafny-contracts`, `dafny-implementation`, `csharp-implementation`, and keep `qa`, `deployment`, `observability`, `documentation`.
- UI constants in `PipelineView`/`Trials` must use `KnownPhases.AllPhases` from `Posit.Core.State` (or duplicate a dashboard-specific ordered list).

#### `posit_qa.prompts_log`
- Schema matches Shepherd's `prompts_log` closely. Direct port is possible.
- Posit has no `implementation_trace` or `design_review_log` tables, so those pages need adaptation.

#### `posit_artifacts.artifacts`
- Can be used for session existence/session list fallback and for reading `DesignReview` payloads if the DesignReviews page is reintroduced.

## 3. Layout & Routing Changes

### `Program.cs`
- Add `builder.Services.AddSingleton<NpgsqlDataSource>(DbConnectionProvider.CreateDataSource())`.
- Register new repositories as scoped services.
- Keep `UseHttpsRedirection` gated (already correct).
- Add `UseStatusCodePagesWithReExecute("/not-found")` (optional) if a `NotFound` page is created.
- Remove auth services (not yet applicable).

### `Components/App.razor`
- Add `<link rel="stylesheet" href="bootstrap/bootstrap.min.css" />` (if Bootstrap is copied/bundled) OR inline a minimal CSS reset/dark theme.
- Add `Posit.Dt.styles.css` scoped bundle link.
- Add `HeadOutlet` (already present).

### `Components/Routes.razor`
- Current router uses `NotFound` layout view with `Layout.MainLayout`.
- No auth-aware routing needed yet.
- Keep simple, but ensure `RouteView DefaultLayout` points to the new `MainLayout`.

### `Components/Layout/MainLayout.razor`
- Add a dark-theme wrapper (`background:#1e1e2e`, `color:#cdd6f4`).
- Add sidebar `<NavMenu />`.
- Keep `@Body` content area.
- Add scoped CSS `MainLayout.razor.css` to restore layout styling.

### `Components/Layout/NavMenu.razor`
- Port from Shepherd, replacing brand name to `Posit` and removing auth-dependent links.
- Links: Sessions, Prompt Harvest, Impl Trace, Trials, User Guide.
- Add scoped CSS `NavMenu.razor.css`.

### `_Imports.razor`
- Add `@using Posit.Dt.Data`, `@using Posit.Core.State`, `@using Posit.Contracts.Core`.
- Keep existing Posit namespaces.

## 4. Files to Create/Modify

### Modify

| File | Change |
|---|---|
| `src/Posit.Dt/Program.cs` | Register `NpgsqlDataSource`, `PositDashboardRepository`, `PositPromptRepository`, `PositImplTraceRepository` |
| `src/Posit.Dt/Components/_Imports.razor` | Add data/core namespaces |
| `src/Posit.Dt/Components/App.razor` | Add stylesheet links for dark theme / scoped styles |
| `src/Posit.Dt/Components/Routes.razor` | No major change unless explicit `NotFound` page is added |
| `src/Posit.Dt/Components/Layout/MainLayout.razor` | Add dark theme wrapper, NavMenu, error UI |
| `src/Posit.Dt/Components/Pages/Home.razor` | Keep redirect to `/sessions` |
| `src/Posit.Dt/wwwroot/app.css` | Add dark-theme CSS variables / reset (or copy Shepherd's `app.css` base) |

### Create

| File | Content sketch |
|---|---|
| `src/Posit.Dt/Components/Layout/NavMenu.razor` | Sidebar with links to `/sessions`, `/prompts`, `/impl-trace`, `/trials`, `/guide` |
| `src/Posit.Dt/Components/Layout/NavMenu.razor.css` | Dark sidebar styles |
| `src/Posit.Dt/Components/Layout/MainLayout.razor.css` | Two-column layout, error UI |
| `src/Posit.Dt/Components/Pages/Sessions.razor` | Port of Shepherd `Sessions.razor`; inject `PositDashboardRepository`; use `PositSessionSummary`; derive status badge color for Posit `SessionStatus` |
| `src/Posit.Dt/Components/Pages/PipelineView.razor` | Port of Shepherd `PipelineView.razor`; use 11 Posit phases; read session via `PositDashboardRepository` |
| `src/Posit.Dt/Components/Pages/PromptHarvest.razor` | Port of Shepherd `PromptHarvest.razor`; inject `PositPromptRepository` |
| `src/Posit.Dt/Components/Pages/ImplTrace.razor` | Adapted from Shepherd; read `PositImplTraceEntry` rows from `posit_qa.dafny_results` + `posit_qa.prompts_log` |
| `src/Posit.Dt/Components/Pages/Trials.razor` | Port of Shepherd `Trials.razor`; keep launch stub; use Posit repositories and phases |
| `src/Posit.Dt/Components/Pages/UserGuide.razor` | Posit-branded static help; list Posit phases and data sources |
| `src/Posit.Dt/Components/Pages/Error.razor` | Standard Blazor error page |
| `src/Posit.Dt/Components/Pages/NotFound.razor` | 404 page (optional) |
| `src/Posit.Dt/Data/PositDashboardRepository.cs` | `GetSessionsAsync()`, `GetSessionSummaryAsync(sessionId)` queries over `posit_state.sessions` |
| `src/Posit.Dt/Data/PositPromptRepository.cs` | `GetPromptsAsync(...)`, `GetPromptDetailAsync(id)` over `posit_qa.prompts_log` |
| `src/Posit.Dt/Data/PositImplTraceRepository.cs` | `GetImplTracesAsync(sessionId)` combining `posit_qa.dafny_results` and prompt parse info |
| `src/Posit.Dt/Data/PositSessionSummary.cs` | DTO: `SessionId`, `Status`, `CurrentPhaseId`, `CurrentPhaseStatus`, `CurrentAttempt`, `CompletedPhases[]`, `InputTokens`, `OutputTokens`, `CostUsd`, `StartedAt`, `LastAdvancedAt`, `Description` |
| `src/Posit.Dt/Data/PositPromptEntry.cs` | DTO mirroring `prompts_log` columns |
| `src/Posit.Dt/Data/PositImplTraceEntry.cs` | DTO: `PhaseAttempt`, `ModuleName`, `ParseStatus`, `PromptLength`, `ResponseLength`, `CompilerErrors?`, `CreatedAt`, `IsDafny` |
| `src/Posit.Dt/Data/ProjectClassifier.cs` | Port/adapt `KnownProjects.Classify` to group sessions by description/project type |

### Optional/Deferred

| File | Reason |
|---|---|
| `Components/Pages/DesignReviews.razor` | No `posit_qa.design_review_log` table; requires migration or artifact parsing. |
| Auth pages (Login, Locked, etc.) | Posit has no auth project. |
| `ReconnectModal.razor` | Nice-to-have; framework provides default. |

## 5. Broken Shepherd Assumptions to Fix

1. **Schema names** — every `shepherd_state.*`/`shepherd_qa.*` reference must become `posit_state.*`/`posit_qa.*`.
2. **Table names** — `shepherd_qa.implementation_trace` does not exist in Posit; replace with `posit_qa.dafny_results` plus `prompts_log` parse status.
3. **Table names** — `shepherd_qa.design_review_log` does not exist in Posit; defer or add migration.
4. **Phase list** — Shepherd's 10-phase list is wrong for Posit. Use Posit's 11 phases (`KnownPhases.AllPhases`).
5. **Session cost source** — Shepherd reads cost from `running_costs_json`. Posit stores the same data inside `state_json` (`RunningCosts`), so queries must extract it via JSON operators or deserialize.
6. **Status values** — Shepherd maps string status values; Posit uses `SessionStatus` enum names (`Active`, `Completed`, `Paused`, etc.). Update badge color logic.
7. **Completed phases** — Shepherd stores `completed_phases` as a `text[]` DB column. Posit stores it in `state_json`. Use JSON projection or deserialize.
8. **Project classifier** — `KnownProjects.Classify` is Shepherd-specific. Port it to `ProjectClassifier` or replace with a simple description substring matcher.
9. **Brand/links** — update page titles and nav links from `Shepherd` to `Posit`.
10. **Bootstrap assets** — Shepherd ships Bootstrap under `wwwroot/lib/bootstrap`. Posit.Dt currently has only `app.css`. Either copy the Bootstrap files or rewrite inline styles to not require Bootstrap.
11. **CSS bundle reference** — `App.razor` references `Posit.Dt.styles.css` (the scoped-style bundle) and `app.css`. Ensure the bundle name is correct.
12. **Auth in `Routes.razor`** — Shepherd's `Routes.razor` branches on `AuthService`. Posit should remove this and use a plain `RouteView` until auth is added.
13. **Repository injection** — pages currently use `@inject Shepherd.Web.Data.DashboardRepository Repo`; replace with the appropriate Posit repository(s).
14. **Deterministic phases badge** — Shepherd marks `api-definition`, `pseudocode`, `design-review`, `deployment`, `observability`, `documentation` as deterministic. In Posit, deterministic phases include `api-definition`, `pseudocode`, `dafny-contracts`, `deployment`, `observability`, `documentation` (and arguably Dafny phases are Z3-verified, not model-generated). Update badges accordingly.

## 6. Observations on Posit.Dt Today

- **Layout:** `MainLayout.razor` is a bare shell with no navigation. It renders, but there is no sidebar, no dark theme, and no styling framework.
- **Home page:** Renders a simple list of sessions from `posit_artifacts.artifacts` using `ArtifactRepository.ListSessionsAsync`. It works but only shows raw session IDs and a mis-linked "View artifacts" anchor (`/sessions/{id}` does not exist).
- **No routing beyond `/`:** Navigating to `/sessions`, `/pipeline/{id}`, etc. would currently 404.
- **No dashboard data layer:** All data access is currently through `ArtifactRepository`, which is artifact-centric, not session-centric.
- **No interactive server mode on Home:** The existing page does not set `@rendermode InteractiveServer`, so interactivity is limited; the ported pages should add it.
- **Build:** Clean (`dotnet build src/Posit.Dt/Posit.Dt.csproj` succeeds with 0 warnings/errors).

## 7. Suggested Implementation Order

1. Add `Posit.Dt.Data` repositories and DTOs.
2. Replace `Program.cs` DI to register the new repositories and a shared `NpgsqlDataSource`.
3. Create the dark-themed `MainLayout` + `NavMenu`.
4. Port `Sessions.razor` (most useful first page).
5. Port `PipelineView.razor` with Posit phases.
6. Port `PromptHarvest.razor`.
7. Port/adapt `ImplTrace.razor` using `dafny_results`.
8. Port `Trials.razor` (stub launcher + phase drill-down).
9. Add `UserGuide.razor`, `Error.razor`, optional `NotFound.razor`.
10. Verify `dotnet build`, then run and validate pages render against the shared DB.

## 8. Out of Scope for This Port

- Shepherd's auth subsystem (Login/Locked/AuthService/ShepherdAuthStateProvider).
- Shepherd's `auto_merge_events` and `skeleton_inference_log` views.
- Real-time Blazor Circuit reconnection UX (can be added later).
- Actual trial launching from the web UI (the button remains a stub, matching Shepherd's current state).
