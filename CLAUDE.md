# PrintSpooler — Project Status

## What this project is

A portfolio piece built to demonstrate C#, ASP.NET Core, and Azure skills to
employers after leaving Hachette Book Group (previously did warehouse/print
operations work there, self-taught into programming by building production
tools — see PrintFlow/PrintFlow_v2, LabelGen).

**The pitch:** a print-spooler-replacement product — a REST API + Blazor
dashboard that accepts print jobs, queues them, delivers them to a real
printer, tracks status, and logs everything. Framed generically (not tied to
any specific employer's systems), reusing architectural lessons learned from
building PrintFlow (event-driven design, concurrency handling, printer
failover) without carrying over anything Hachette-specific.

**Goal:** build it in about a week. Repo is on GitHub, pushed via `gh` CLI.

## Where the author is, skill-wise

Read this section before assuming background knowledge:

- Strong in C#, LINQ, and general programming fundamentals (built PrintFlow_v2,
  ~1500 lines, single project, used ErrorOr for railway-style error handling,
  built a `SafelyRun` try/catch wrapper). Comfortable with Neovim/LazyVim,
  git, Arch-based Linux (CachyOS), Docker, Sybase ASE SQL.
- **Genuinely new to:** ASP.NET Core web APIs, Entity Framework Core,
  dependency injection, Azure (any of it — CLI, SQL provisioning, resource
  groups), REST API design, DTOs, multi-project .NET solution architecture,
  IPP/printing protocols outside of PrintFlow's Bartender-delegated approach.
- Has been walked through **why** each architectural decision was made via
  Socratic questioning, not just handed code — explicitly asked to understand
  and defend every design choice rather than follow along passively. Prefers
  this teaching style: guided questions, own reasoning first, correction
  after a genuine attempt — not code dumped without explanation.
- Knows and can explain: why the 4-project split (Core has zero dependencies,
  Infrastructure implements Core's interfaces, Api wires everything together
  via DI), what DbContext/DbSet/migrations are and why they're needed, DI
  lifetimes (Singleton vs Scoped vs Transient) and the reasoning for each,
  what a foreign key constraint does and why `Job.PrinterId` needed one,
  navigation properties and why they're nullable (not lazy loading — EF just
  doesn't fetch related data unless `.Include()` is used), the producing vs.
  consuming sides of ErrorOr, DTOs vs domain models and why they're separate,
  `.Match()` vs `.Switch()` in ErrorOr, basic SQL transactions
  (BEGIN TRAN/COMMIT/ROLLBACK) and isolation.
- Still shaky / hasn't been tested on: auth (not started), any deployment step
  (App Service, CI/CD).
- Now understands: `BackgroundService`/`IHostedService`, `Channel<T>` as a
  producer/consumer queue, `IServiceScopeFactory` for creating DB scopes inside
  a Singleton, DI lifetime rules (Scoped can depend on Singleton, not vice versa),
  `await foreach` with `ReadAllAsync` for blocking channel consumption. Blazor:
  CSS isolation only scopes a component's own root element into a parent's
  isolated stylesheet — elements nested *inside* a child component's own markup
  need the `::deep` combinator to reach at all, and CSS custom properties are
  the one thing that still inherits through a child component regardless of
  scoping, since it's normal DOM-tree inheritance, not selector matching. JS
  interop (`IJSRuntime.InvokeVoidAsync`) for the one thing Blazor Server has no
  built-in API for — reading real element geometry — called from
  `OnAfterRenderAsync`, not the event handler that triggered the state change,
  since the element being measured doesn't exist in the DOM until after that
  render actually happens.

## Architecture

Four .NET projects, dependency direction enforced by project references:

```
Api → Core
Api → Infrastructure
Infrastructure → Core
Core → nothing
Web (Blazor) → Core (planned; not wired yet)
```

- **PrintSpooler.Core** — domain models and rules only. Zero infrastructure
  dependencies (no EF Core, no Azure SDKs) — EXCEPT the `ErrorOr` package,
  which was deliberately added here too, since it's a plain in-memory data
  structure (not an I/O dependency) and both Core and Infrastructure need to
  reference the same `IJobService`/`ErrorOr<T>` contract.
  - `Models/`: `Job`, `Printer`, `AuditLog`, `JobStatus` (enum), `PrinterStatus`
    (enum), `JobCreationData` (a Core-side "raw data needed to create a job"
    type, parallel to but distinct from Api's `CreateJobRequest` DTO — exists
    because Infrastructure can't reference Api's DTOs)
  - `Services/`: `IPrinterDispatcher` (interface), `IJobService` (interface),
    `JobCancellationPolicy` (static rule: a job can only be cancelled while
    `Status == Queued`, since once bytes are on the wire to a printer's
    internal spooler, cancellation can't be reliably guaranteed — this was a
    deliberate lesson carried over from how PrintFlow observed Bartender/
    Windows spooler behavior)

- **PrintSpooler.Infrastructure** — talks to the outside world.
  - `Data/AppDbContext.cs` — EF Core DbContext, `DbSet<Job>`, `DbSet<Printer>`,
    `DbSet<AuditLog>`
  - `Services/PrinterDispatcher.cs` — implements `IPrinterDispatcher` using
    **IPP (Internet Printing Protocol)** via the `SharpIppNext` NuGet package
    (not the original `SharpIpp` — different API shape, note if
    troubleshooting). Chosen specifically because it's cross-platform
    (Windows + Linux via CUPS), unlike raw PCL/ZPL socket delivery or
    Windows-only `System.Drawing.Printing`. Verified working end-to-end
    against a real household HP ENVY Inspire 7200 printer over CUPS/mDNS
    (`ipp://HPD0AD08C445FE.local:631/ipp/print`) — got back `PrinterState:
    Idle` on a real handshake, and confirmed real print delivery works.
  - `Services/JobService.cs` — implements `IJobService`. Given a
    `JobCreationData`, fetches the matching `Printer` in one query
    (`FirstOrDefaultAsync`, not a separate exists-check + fetch), returns
    `Error.NotFound(...)` if none exists, otherwise builds and saves a `Job`
    (with the `Printer` navigation property assigned directly, not via
    `.Include()` — that's for re-reading existing rows, not building new
    ones) and returns it wrapped in `ErrorOr<Job>`.
  - `Migrations/` — EF Core migrations, currently: `InitialCreate`,
    `FixColumnTypos` (fixed `SubmitteBy`→`SubmittedBy`,
    `FailvoerPrinterId`→`FailoverPrinterId`), `AddPrinterForeignKey`

- **PrintSpooler.Api** — HTTP layer.
  - `Program.cs` — DI registrations: `AddDbContext<AppDbContext>` (SQL Server,
    connection string from **.NET User Secrets**, not `appsettings.json`, so
    it never reaches GitHub), `AddSingleton<IPrinterDispatcher,
    PrinterDispatcher>` (stateless — just takes data, sends it, no per-request
    state to leak between calls), `AddScoped`-equivalent for `IJobService`
    (registered the same pattern; verify lifetime choice was actually made —
    likely Scoped since it depends on Scoped `AppDbContext`)
  - `Contracts/CreateJobRequest.cs` — the DTO for `POST /PrintJob`. Deliberately
    narrower than `Job`: no `Id` (server-generated), no `Status`/timestamps
    (system-decided), just what a caller should actually be allowed to send
  - `Controllers/PrintJobController.cs` — `POST /PrintJob` implemented and
    fully tested both paths:
    - Bad/nonexistent `PrinterId` → clean `400 Bad Request`, RFC 9110 Problem
      Details JSON body via `Problem(detail:..., statusCode: 400)` — NOT a
      raw exception/stack trace (that was the original bug, fixed by
      validating before attempting the DB write instead of catching the FK
      violation after the fact)
    - Valid `PrinterId` → `201 Created`, real row persisted, `Location`
      header, full `Job` JSON back (with `printer: null` in the response,
      which is expected/correct — see EF Core notes below)
  - Uses `.Match()` (not `.Switch()`) to turn `ErrorOr<Job>` into
    `IActionResult` — `.Switch()` takes `Action`s and returns `void`,
    `.Match()` takes `Func`s and returns a value, which is what's needed here
  - `WeatherForecast.cs` — template leftover, not deleted yet, safe to remove

- **PrintSpooler.Web** — Blazor Server (chosen over separate JS frontend to
  stay in C#, deploy alongside Api with no CORS/extra-Azure-resource
  headache). Scaffolded only — `dotnet new blazor --interactivity Server`
  — no actual pages/wiring built yet.

## Key EF Core concepts already covered (don't re-teach from scratch)

- `DbContext`/`DbSet<T>`, `DbContextOptions<T>`, migrations
  (`dotnet ef migrations add`, `dotnet ef database update`, both need
  `--project ../PrintSpooler.Infrastructure --startup-project .` run from
  the Api folder, since AppDbContext lives in Infrastructure but config
  lives in Api)
- Foreign keys via navigation properties (`public Printer? Printer { get;
  set; }` on `Job` → EF generates the FK constraint automatically)
- Why `Printer` navigation property is nullable: **not lazy loading** (that's
  a specific, different, unconfigured EF feature) — it's just "never loaded
  unless you `.Include()` it," a distinct mechanism that looks similar
- `.Include()` is for re-reading existing rows with their relations attached;
  building a brand new entity just needs direct property assignment instead
- Migrations do smart diffs, not blind rewrites — confirmed firsthand when a
  column-name fix generated `sp_rename` instead of drop-and-recreate
- Adding a FK constraint to a table with existing bad data fails loudly
  (Error 547) until the bad rows are cleared — this happened for real with a
  leftover all-zero test `PrinterId`, cleared via a `BEGIN TRAN` /
  `SELECT COUNT` / `COMMIT` transaction in vim-dadbod-ui

## Infra actually provisioned (real, not simulated)

- Azure subscription: free trial. **Region gotcha**: SQL Server creation
  failed with `RegionDoesNotAllowProvisioning` in `eastus`, `eastus2`, and
  `westus2` — known free-trial-subscription limitation, not user error.
  `centralus` worked. If provisioning more Azure SQL resources, default to
  `centralus` first.
- Resource group: `PrintSpooler-RG` (centralus)
- SQL Server: `printspooler-sql` (centralus), admin user `printspoolerAdmin`
- SQL Database: `PrintSpoolerDb` — downgraded from Standard S0 (auto-selected,
  costly) to **Basic tier, 2GB max size** to control cost
- Firewall rule allowing the dev machine's IP through
- Connection string stored via `dotnet user-secrets`, NOT committed to git
- Local dev tooling: `az` CLI (Arch/pacman, had to `-Syy` refresh mirrors once),
  `go-sqlcmd`, vim-dadbod + vim-dadbod-ui (connected via
  `sqlserver://user:pass@host:1433/db` URL), roslyn.nvim (needed to manually
  pick the target `.slnx` file the first time — `No solution selected` in
  `:checkhealth` was the symptom), kulala.nvim (via LazyVim's `util.rest`
  extra) for testing endpoints from `.http` files instead of raw curl —
  request file lives at `requests/print-job.http` in the repo root

## What's built and verified working end-to-end

1. Full data layer: models, DbContext, migrations, real Azure SQL database
2. `IPrinterDispatcher`/`PrinterDispatcher` — real IPP printing via SharpIppNext,
   `try/catch` around `PrintJobAsync` returns `ErrorOr<Success>` on both success
   and failure (including `IppResponseException` with `StatusCode` in the message).
   Content type must be printer-native (`image/jpeg` confirmed working on HP ENVY
   Inspire 7200) — spooler does not transcode, caller is responsible for send-ready data.
3. `IJobService`/`JobService` — validates printer exists, creates job, writes to
   `Channel<Job>` after DB save to hand off to background worker
4. `IPrinterService`/`PrinterService` — creates printers (duplicate check by IP and
   name), fetches by ID
5. `POST /PrintJob` — 400 on bad printer, 201 on success
6. `GET /PrintJob/{id}` — returns job with `Printer` populated via `.Include()`
7. `POST /Printer`, `GET /Printer/{id}`, `GET /Printer` (list all) — full CRUD for printer registration
8. `PrintJobWorker` (`BackgroundService`) — on startup seeds from DB (any
   `Queued` jobs), then consumes `Channel<Job>` indefinitely; dispatches via
   `IPrinterDispatcher`, updates `Status` (`Completed`/`Failed`/retry), `FailureReason`,
   `CompletedAt`, and `RetryCount` in DB per job using a fresh `IServiceScopeFactory`
   scope per job. Retry logic: on failure, if `RetryCount < MaxRetries` increment and
   re-enqueue; otherwise mark `Failed`. No backoff delay currently — future improvement.
9. `DELETE /PrintJob/{id}` — cancel endpoint, enforces `JobCancellationPolicy`
   (only `Queued` jobs can be cancelled; once dispatched, bytes are on the wire).
   Future: IPP `Cancel-Job` operation could cancel `Processing` jobs — needs
   printer-assigned IPP job ID stored on `Job` model.
10. Local testing workflow via kulala.nvim `.http` files (`Requests/print-job.http`)
11. `ILogsService`/`LogsService` + `GET /Logs` — backend complete, verified end-to-end.
    `LogQueryParams` (`SearchTerms`, `DateFrom`/`DateTo` as `DateOnly?`, `ActionFilter`,
    `PerformedBy`, `Page`, `PageSize`) bound via `[FromQuery]` on the controller action —
    GET has no body, so complex-type params need that attribute explicitly (`[ApiController]`
    default inference is `[FromBody]`, wrong for GET). `LogsService.GetLogs` builds an
    `IQueryable<AuditLog>` by reassigning through each optional filter (`Where` doesn't
    mutate — each call returns a new queryable wrapping the expression tree, so the
    reassignment is what accumulates filters), calls `CountAsync()` on the filtered-but-unpaged
    query for `TotalCount` (must happen before `Skip`/`Take` narrows it to one page), then
    `Skip((Page - 1) * PageSize).Take(PageSize)` for the actual page, wrapped in `PagedResult<T>`.
    `DateFrom`/`DateTo` filter independently (not both-required), so "from X onward" with no
    end date works. Whole DB block wrapped in try/catch → `Error.Unexpected` on failure
    (EF's `ToListAsync()` never returns `null` — that's not a valid failure signal, an
    exception is). Frontend (Audit Log dashboard page) not started — see item 4 below.

## What's NOT built yet (planned, in likely order)

1. ~~Wire `JobCancellationPolicy` into `DELETE /PrintJob/{id}`~~ — DONE
   - Future: IPP `Cancel-Job` for in-progress jobs (needs printer-assigned job ID stored on `Job`)
2. ~~`AuditLog`~~ — DONE. Writes on: `JobCreated` (JobService), `JobCancelled`
   (JobService), `JobCompleted`/`JobFailed` (PrintJobWorker). Enums `JobAction`
   and `ByWho` stored as strings via `HasConversion<string>()` in AppDbContext.
   Printer creation deliberately not audited — audit log scoped to job lifecycle only.
3. Blazor dashboard — Web project running with `@rendermode InteractiveServer`
   required on every interactive page (without it, `@onclick` is dead static HTML).
   `HttpClient` registered in `Program.cs` as named client `"PrintSpoolerApi"`
   pointed at `http://localhost:5164` — injected via `IHttpClientFactory`, not
   `HttpClient` directly. Razor LSP working in Neovim via roslyn.nvim cohosting.
   Use `dotnet watch --project PrintSpooler.Web` for hot reload during development.
   - `SubmitJob.razor` (`/submit-job`) — working, feature-complete for multi-file
     submission. Key behaviours:
     - Multi-file selection via `<InputFile multiple>` — files appear in a stacked
       list below Browse; each can be removed individually with fade animation
     - Parallel submission via `Task.WhenAll` + `InvokeAsync(StateHasChanged)` per
       entry for live UI updates across thread pool threads
     - Per-entry status: spinner (Submitting) → ✓ fade-out (Success) → `view error
       (400)`/`view error (refused)` toggle (Failed)
     - Failed entries show expandable error message + full-width remove button;
       footer switches from "Send to Queue" to `retry failed` / `clear failed` pair
     - Validation: clicking Send while printer unselected or no files shakes the
       invalid fields red; button only hard-disabled while jobs are in-flight
     - `FadeAndRemove` collapses open error before fading — shared by all removal paths
     - `submittedBy` is a `const`; `client` initialized as `default!` (safe: only
       called after `OnInitializedAsync`); `IsInFlight` property gates disabled state
     - Follow-up (not tonight): `SubmittedBy` should eventually capture the
       originating machine, not just a username (real print-ops need "which
       computer sent this"). Real nuance: this is Blazor **Server**, not WASM —
       the browser request lands on the Web project, which then makes a
       *separate* server-to-server call to Api. Capturing identity on the Api
       side (`HttpContext.Connection.RemoteIpAddress`) would only ever see the
       Web server's own address, never the real submitter. Must capture on the
       Web side (where the browser connection actually lands) and pass it
       explicitly as a field on `CreateJobRequest`. An IP address is gettable
       this way without much work; an actual computer *name* needs
       domain-authenticated identity (Kerberos/NTLM) — same bucket as item 7,
       Auth, below, not a quick add on its own.
     - Printer dropdown has four load states via `PrinterLoadState` enum: `Loading`
       (muted spinner on init), `Ready` (dropdown), `Empty` (no printers found +
       retry), `Failed` (failed to load + retry), `Retry` (spinner during retry).
       Retrying from `Empty` or `Failed` shows spinner then resolves to correct state.
     - Still needs: file type enforcement, real `SubmittedBy` identity

## Blazor Shared Components

- `Components/Shared/SpSelect.razor` — generic custom dropdown (`@typeparam TValue`).
  Avoids native `<select>` OS styling. Takes `List<SpSelectOption<TValue>>` and
  supports `@bind-Value`. Backdrop div handles click-outside-to-close. Usage:
  ```razor
  <SpSelect TValue="Guid" @bind-Value="selectedId" Options="options" Placeholder="-- select --" />
  ```
- `Components/Shared/SpSelectOption.cs` — `SpSelectOption<TValue>` with `Value` and `Label`.

## CSS Design System

All custom classes prefixed `sp-` to avoid Bootstrap collisions. Never use Bootstrap's
`.card`, `.alert`, `.form-group`, `.nav-link` etc. in new pages — use the `sp-` equivalents.

**File structure** (split from a single `app.css` once it grew past ~870 lines):
- `wwwroot/css/base.css` — variables, reset, html/body, scrollbar, Blazor error UI
- `wwwroot/css/layout.css` — app shell only (sidebar, nav, top bar, content area) — not page content
- `wwwroot/css/components.css` — real shared design-system primitives (cards, tags, buttons,
  alerts, forms, dropdown, spinner, stat-grid, section-title, data table) — anything meant
  for reuse across current or future pages goes here. This includes the `.sp-table*` rules:
  Queue and Logs both render a `<table class="sp-table">` directly in the page (columns/thead
  differ too much per page to be worth a shared `<SpTable>` component yet), but the row/cell
  styling itself is identical, so it lives here instead of being duplicated per page. Generic
  cell text classes are `.sp-table-primary` (main text color) / `.sp-table-muted` (secondary,
  e.g. timestamps) / `.sp-table-muted-sm` (secondary + smaller, e.g. printer name) — named for
  what they style, not which page uses them, since Queue and Logs both do.
- `wwwroot/css/animations.css` — every `@keyframes` block, referenced by name from wherever
  needed. Keyframe definitions aren't subject to Blazor CSS isolation scoping (only selectors
  are), so isolated page files can reference these global keyframes with no leakage risk
- `Component.razor.css` next to its `.razor` file — **Blazor CSS isolation**: Blazor adds a
  `[b-xxxxxxxx]` attribute to every selector at build time, so it physically cannot leak to or
  be affected by any other component, and auto-bundles into `PrintSpooler.Web.styles.css`
  (already referenced in `App.razor`, no extra `<link>` needed per new page/component). Applies
  to page-specific one-off styles (`SubmitJob.razor.css`) AND shared-component styles that
  only that component renders (`Components/Shared/JobDetailCard.razor.css` — the floating job
  detail popover, used by both Queue and Logs via the `JobDetailCard` component itself, not
  duplicated markup, so its CSS only needs to exist once, isolated to that component).
  Logs.razor.css holds the toolbar/filters/pagination styling that's genuinely one-off to that
  page; Queue.razor has none of its own right now.
- DONE: `.sp-table-wrapper`, `.sp-job-detail-card`, `.submit-card` now compose with `.sp-panel`
  via multi-class markup (`class="sp-panel sp-table-wrapper"`, etc.) instead of hand-repeating
  its four base declarations. `.sp-card`/`.sp-log` still redeclare them directly since they're
  full standalone primitives, not layout wrappers around other content — acceptable as-is.

**Variables** (`--sp-*`):
- `--sp-bg` `#0e100f` / `--sp-panel` `#151816` / `--sp-panel-alt` `#19271f`
- `--sp-border` `#1c693b` / `--sp-border-subtle` `#1e2b22`
- `--sp-green` `#4afa8a` / `--sp-green-dim` `#42d477`
- `--sp-text` `#e5eae6` / `--sp-muted` `#67736a`
- `--sp-yellow` `#ffc857` / `--sp-red` `#ff5c5c`
- `--sp-mono` Courier New / `--sp-radius` 3px

**Building blocks for new pages:**
- `sp-section-title` — green label + extending horizontal rule
- `sp-card` — padded panel (background + subtle left accent border)
- `sp-log` — monospace event log panel
- `sp-table` + `sp-table-wrapper` (fixed `height`, not `max-height` — a short/partial page
  shouldn't shrink the box and drag pagination controls with it) — shared data table (Queue,
  Logs); `sp-table-status`/`sp-table-num`/`sp-table-action` for centered columns,
  `sp-table-primary`/`sp-table-muted`/`sp-table-muted-sm` for cell text weight,
  `sp-row-clickable`/`sp-row-updated`/`sp-row-fading`/`sp-row-active`/`sp-empty-row` for row
  states
- `JobDetailCard` (`Components/Shared/JobDetailCard.razor`) — a floating popover (not an inline
  accordion row — that was the original design, replaced once it became clear an expanding row
  needs the table to auto-scroll to reveal it, while a popover just overlays whatever's already
  visible) shown when a Queue/Logs row is clicked. Takes `Job? Job` (renders "No data found"
  when null — stale `AuditLog.JobId` rows from before a job existed to look up), `string CardId`,
  and `EventCallback OnClose`. Only one open at a time per page (`Guid? expandedJobId` /
  `expandedLogId`, not a `HashSet`). Positioned via `wwwroot/js/interop.js`'s
  `positionDetailCard(cardId, rowId, wrapperId)` — first JS interop in the project — called from
  `OnAfterRenderAsync` (via a `pendingPosition*Id` flag set in the click handler, consumed once
  the card has actually rendered and has a real height to clamp against) since Blazor Server has
  no built-in element-geometry API. Clamps its own top position inside the scrollable wrapper so
  it never renders past the visible (already-scrolled) area, which is the reason it replaced the
  accordion in the first place. `interop.js` also has `scrollElementToTop(el)`, used by Logs to
  reset table scroll on every filter/search/page change (unrelated to the card).
- `sp-stat-grid` + `sp-stat-label` + `sp-stat-value` — 4-col stat cards
- `sp-tag` + `sp-tag-processing/queued/completed/failed/cancelled` — status badges
- `sp-progress` + `sp-progress-bar` — 2px progress line
- `sp-btn` + `sp-btn-primary/secondary/danger` — outlined buttons, no Bootstrap
- `sp-btn:disabled` — muted/dimmed, `cursor: not-allowed`
- `sp-btn-group` — flex row of buttons that each `flex: 1` (equal width, centered text)
- `sp-alert` + `sp-alert-success/danger/muted` — inline status messages
- `sp-form-group` / `sp-label` / `sp-input` — form elements
- `sp-dropdown*` — used internally by `SpSelect` component
- `sp-file-row` — flex row for browse button area
- `sp-file-list` — stacked column of file entries
- `sp-file-entry` — single file row (column flex: row + optional error block)
- `sp-file-entry-row` — inner flex row: name left, status icon/button right
- `sp-file-entry-name` — truncated filename
- `sp-file-entry-icon` + `.pending`/`.success` — status text indicator
- `sp-file-entry-remove` — subtle `×` button (middle-right of entry row)
- `sp-file-entry-err` — underlined red text toggle for error details
- `sp-file-entry-error` — expanded error block with red left-border accent
- `sp-file-entry-error-actions` — wrapper above the remove button inside error block
- `sp-file-entry-error-remove` — full-width outlined red remove button inside error
- `sp-file-entry.fading` — triggers `sp-entry-out` collapse animation on removal
- `sp-spinner` — CSS-only rotating border spinner (used for Submitting state)
- `sp-invalid` — shake animation + red border on child `.sp-dropdown-trigger`/`.sp-btn`/`.sp-file-list`
- `sp-nav-item` / `sp-nav-link` — sidebar nav (replaces Bootstrap nav classes)
- `submit-card` / `submit-card-header/body/footer/title/id` — submit form card layout

**Design reference:** matches solomonhuckstep.us aesthetic — monospace throughout,
green section labels with extending rule, outlined cards with 2px left accent border,
transparent outlined buttons/tags, no filled backgrounds on interactive elements.

4. **Blazor dashboard pages:**
   - DONE: Queue page (`/queue`) — job list with status tags, printer name, cancel action,
     live SignalR updates (row flash on update, fade-out on completion/cancellation), click a
     row to open its `JobDetailCard`
   - DONE: Logs page (`/logs`) — search box (debounced), a single "Filters" dropdown panel
     holding Action/PerformedBy/Sort/OrderBy/DateFrom/DateTo (consolidated from separate
     inline controls) + a Reset-all button, page-size selector (50/100/200), pagination with
     `IsAtFirstPage`/`IsAtLastPage` computed properties (not manually-toggled bools — those
     briefly flickered `disabled` mid-load, a real bug, not a style choice), a debounced loading
     spinner (250ms delay before showing, 300ms minimum once shown — avoids both the "flash on
     fast load" and "flash-off right after appearing" failure modes), and row-click opens the
     shared `JobDetailCard` popover
   - Job detail page — full job info, audit log entries for that job
   - Printer list / home page (`/`) — registered printers
   - Follow-up (not tonight): manual "retry a Failed job" action — needs new
     endpoint, `RetryCount` reset logic, and `JobCancellationPolicy` updated
     (currently only `Queued` jobs can be cancelled, not `Failed`)
   - Follow-up (not tonight): no unfiltered "get all jobs" endpoint —
     `GetAllActiveJobs()` (excludes Completed/Cancelled) covers the Queue
     page; terminal jobs stay reachable via Audit Log rows (`JobId`) →
     existing `GetJob(id)`, so Job Detail page doesn't need a full-table dump
   - Follow-up (not tonight): DB retention/purge policy for Completed/Cancelled
     job rows — table grows unbounded otherwise, real concern for a
     long-running spooler, deferred past the portfolio-piece timeline
   - DONE: `GetAllActiveJobs()` projects straight to `JobDisplayData`
     (`RawData` excluded at the SQL level via `JobDisplayData.Projection`,
     a shared `Expression<Func<Job, JobDisplayData>>`). The SignalR push in
     `JobNotifier.JobUpdateAsync` also converts through `JobDisplayData.FromJob`
     (compiles the same expression) before broadcasting, so job-completion
     events no longer ship the full print file over the wire either.
   - Follow-up (not tonight): file size on Queue page — do NOT implement as
     "fetch `RawData.Length` on read," that's the same bloat problem above.
     Store a dedicated `long FileSizeBytes` column instead, computed once at
     job creation time when the bytes are already in hand.
   
5. **Printer status / job lifecycle (planned):**
   - Store IPP job ID returned by printer on dispatch (needs `PrinterJobId`
     field on `Job` + migration)
   - Set `Processing` status when job is handed to printer (currently skipped)
   - Poll `Get-Job-Attributes` via SharpIppNext using stored IPP job ID to
     detect `completed`/`cancelled`/`aborted` from printer side
   - IPP is request/response only — no push, polling is correct approach
   - Follow-up (not tonight): printer heartbeat + live error state (idle, out
     of paper, etc.) for the dashboard. `Printer.LastHeartbeat` column already
     exists (migrated since day one) but nothing ever writes to it — not
     missing data, just an empty slot. Real IPP has `printer-state` +
     `printer-state-reasons` (media-empty, toner-low, door-open...), richer
     than the current `PrinterStatus` enum (`Online`/`Offline`/`Unknown`).
     Both would come from the same `Get-Printer-Attributes` IPP call
     (SharpIppNext, already in the project) — one `PrinterPoller`
     `BackgroundService` shaped like `PrintJobWorker`, pushed live via
     SignalR the same way `JobNotifier`/`JobHub` work for jobs tonight.
   
6. **File transcoding pipeline (planned):**
   - Currently only JPEG works end-to-end (printer-native)
   - Plan: text/PDF/image → render to bitmap → PCL raster wrapper → printer
   - SkiaSharp for bitmap rendering (already familiar from PrintFlow work)
   - Need paper size: either hardcode Letter/A4 or query printer via
     `Get-Printer-Attributes` IPP call for `media-default`
   - Chunks of PCL raster code exist from prior PrintFlow work

7. **Multi-printer batch submission (planned, from PrintFlow precedent):**
   - Not a backend problem — `Job` is already inherently one-file-to-one-printer,
     so fanning out to multiple printers is just submitting N single-printer
     jobs via the existing `CreateJob` path, no new domain logic needed
   - Real challenge is UI: avoid an overwhelming files×printers matrix.
     Direction: a printer multi-select as the batch's default "broadcast
     target," each file row can optionally override which printer(s) it
     specifically goes to (defaulting to the broadcast selection) — mail-merge
     style, not a full grid
8. **Print settings — copies/paper size/duplex (planned):**
   - Not a "compete with print drivers" problem — IPP already standardizes
     these as job attributes (`copies`, `media`, `sides` for duplex,
     `orientation-requested`, `print-quality`), which is the whole reason
     IPP/SharpIppNext was chosen over `System.Drawing.Printing` in the first
     place. Moderate extension of existing dispatch code, not new territory.
   - Genuinely hard part: whether a specific printer actually honors a given
     attribute varies by hardware — would need to query `Get-Printer-Attributes`
     to discover supported values before exposing options in the UI
   - Vendor-specific finishing/binding features outside IPP's standard set
     would need real per-vendor driver work — correctly out of scope

9. Auth (JWT + roles) — descoped, revisit if time allows
10. Deployment — Azure App Service, App Insights, Key Vault for secrets
11. Tests — no test project yet

## Claude.md maintenance

Update this file whenever something is completed — move it from "NOT built yet"
to "built and verified", update the architecture section if new files/patterns
were added, and update the skill level section if new concepts were covered.

## Working style / how to help this person

- Prefers being taught, not handed finished code — ask guiding questions,
  let them attempt it, correct with explanation rather than rewriting for
  them, especially for new-to-them concepts (ASP.NET/EF/Azure/DI)
- Does want direct, fast answers for pure syntax/tooling issues that aren't
  conceptual learning moments (a typo, a missing semicolon, a CLI flag)
- Appreciates being told *why*, not just *what* — e.g. why Core can't
  reference EF Core but can reference ErrorOr, why Scoped vs Singleton
  matters for a specific class based on whether it holds per-request state
- Wants to be able to defend every architectural decision in an interview —
  treat "can you explain why" as the real success criterion, not just "does
  it compile and run"
- Always use caveman mode (compressed, low-filler text) for prose responses —
  even during Socratic teaching, even when other skills/plugins are active —
  to cut context bloat. Code, commits, PRs, and security warnings still get
  written out normally (per caveman skill's own boundaries). Only drop caveman
  where it'd genuinely conflict with clarity (e.g. multi-step sequences where
  fragment order risks misread, or the user seems confused) — resume it
  right after.
