# PrintSpooler — Project Reference

## Goal

Portfolio piece demonstrating C#/ASP.NET Core/Azure to employers (author left
Hachette Book Group warehouse/print-ops work, self-taught into programming —
prior tools: PrintFlow, PrintFlow_v2, LabelGen). Product pitch: a
print-spooler-replacement — REST API + Blazor dashboard that accepts print
jobs, queues them, delivers to a real printer via IPP, tracks status, logs
everything. Genericized, not tied to any employer's systems. Built in ~1 week,
pushed to GitHub via `gh`.

## Current build status: BUILDS CLEAN

`PrinterPoller` (printer heartbeat/status polling + IPP job-state polling,
mirrors `PrintJobWorker`'s shape) is wired in and compiles. Runs three loops
per printer via `Task.WhenAll`: `PrinterWatch` (polls `Get-Job-Attributes` for
jobs handed to it), `PrinterHeartbeat` (polls `Get-Printer-Attributes` on a
30s interval, writes `Printer.Status`/`LastHeartbeat`), and a `RouteJobs` loop
that reads `Channel<IppJobRef>` (written by `PrintJobWorker` after a
successful `SendAsync`) and hands each job to the right `PrinterWatch`.

## Architecture — project references

```
Api ─────┬──> Core
         └──> Infrastructure ──> Core
Web ────────> Core (DTOs only, via ApiClient over HTTP — no project ref to Api/Infrastructure)
```

- **Core** — domain models, interfaces, pure rules. Zero infra deps except
  `ErrorOr` (in-memory data structure, not I/O).
- **Infrastructure** — EF Core (SQL Server), IPP printing (SharpIppNext), mDNS
  discovery (Zeroconf). Implements Core's interfaces.
- **Api** — controllers, SignalR hub, DI wiring. Talks to Infrastructure via
  Core interfaces only.
- **Web** — Blazor Server. Talks to Api over HTTP (`ApiClient`) + SignalR
  (`HubConnection`), not a project reference to Api.

## Core — Models (`PrintSpooler.Core/Models`)

- `Job` — one print job. `Id`, `Printer?`/`PrinterId`, `Data?` (nav to
  `JobData`), `SubmittedBy`, `FileName`, `ContentType`, `FileSizeBytes`,
  `Status` (`JobStatus`), `RetryCount`/`MaxRetries`, `SubmittedAt`,
  `CompletedAt?`, `FailureReason?`.
- `JobData` — job's raw file bytes, 1:1 with `Job` on `JobId`. Meant to be
  deleted once a job reaches a terminal state (keeps `Jobs` table light) —
  currently only actually happens on `PrintJobWorker`'s `Cancelled` path,
  see **Not built yet**.
- `JobCreationData` — Core-side "data needed to create a job," parallel to
  Api's `CreateJobRequest` DTO (Infrastructure can't reference Api).
- `Printer` — `Id`, `Name`, `IpAddress`, `Status` (`PrinterStatus`),
  `FailoverPrinterId?`, `LastHeartbeat?` (column exists, nothing writes to it
  yet), `Host?`, `SupportedContentTypes?`, `PrinterUuid?` (last three added
  for mDNS discovery).
- `AuditLog` — `Id`, `JobId`, `Action` (`JobAction`), `PerformedBy` (`ByWho`),
  `Timestamp`, `Details?`. Scoped to job lifecycle only (printer CRUD not
  audited).
- `IppJobRef` — `PrinterId`, `JobId`, `IppId`. Returned by
  `IPrinterDispatcher.SendAsync` on a successful submit — the printer-assigned
  IPP job ID paired back to our `Job.Id`, handed to `PrinterPoller` over
  `Channel<IppJobRef>` so it knows which IPP job to start watching.
- `IppJobStatus` — `Id`, `State` (`JobStatus`), `Message?`. One printer-reported
  job snapshot from `IPrinterDispatcher.GetPrinterJobsAsync` (maps IPP's
  `job-state` onto `JobStatus`).
- `WatchedJob` — `JobId`, `State` (`JobStatus`). `PrinterPoller`'s own
  bookkeeping record — one per in-flight IPP job it's polling, cached in
  `PrinterWatch`'s `ConcurrentDictionary<int, WatchedJob>` keyed by IPP job ID.
- `LogQueryParams` — filter/sort/page params for `GET /Logs`
  (`SearchTerms`, `JobId?`, `DateFrom?`/`DateTo?` as `DateOnly?`,
  `ActionFilter?`, `PerformedBy?`, `OrderByField`, `SortDirection`, `Page`,
  `PageSize`).
- `PagedResult<T>` — `Items`, `TotalCount`, `Page`, `PageSize`,
  computed `TotalPages`.

**Enums:** `JobStatus` (`Staged`/`Queued`/`Submitting`/`Processing`/
`Cancelled`/`Completed`/`Failed`/`Unknown` — shared by `Job.Status` and by
`IppJobStatus.State`, the poller's mapping of IPP `job-state`) ·
`PrinterStatus` (`Online`/`Offline`/`Unknown`) · `JobAction`
(`Created`/`Cancelled`/`Completed`/`Failed`/`Retried`) · `ByWho`
(`System`/`User`) · `OrderByField` (`JobAction`/`ByWho`/`Timestamp`/`Details`) ·
`SortDirection` (`Asc`/`Desc`).

## Core — Service interfaces (`PrintSpooler.Core/Services`)

- `IJobService` — `CreateJob`, `GetJob`, `GetAllActiveJobs`, `CancelJob`,
  `RetryJob`. All `ErrorOr<T>` except the list method.
- `IPrinterService` — `GetPrinter`, `CreatePrinter`, `GetPrinters`,
  `DeletePrinter`.
- `IPrinterDispatcher` — `SendAsync(job, bytes, ct)` → sends print data to a
  real printer.
- `IPrinterDiscoveryService` — `ProbeForNetworkPrinters()` → mDNS scan.
- `IJobNotifier` — `JobUpdateAsync(job, ct)` → pushes a job update out (Api
  implements via SignalR).
- `JobCancellationPolicy` (static) — `CanCancel(status)`: true for `Queued`
  or `Failed` only. Once bytes are on the wire to a printer, cancellation
  isn't reliable.

## Infrastructure (`PrintSpooler.Infrastructure`)

- `AppDbContext` — `DbSet<Job/JobData/Printer/AuditLog>`. Enums stored as
  strings (`HasConversion<string>()`). `Job`↔`JobData` is a required 1:1 on
  `JobData.JobId`.
- `JobService : IJobService` — validates printer exists before creating a
  job; on create, writes `Job` + `JobData` + an audit row, then pushes
  `job.Id` onto `Channel<Guid> jobChannel` for `PrintJobWorker` to pick up.
  Cancel/Retry follow the same save→audit→notify→re-enqueue pattern.
- `PrintJobWorker : BackgroundService` — on startup, re-enqueues any
  `Queued`/`Submitting` jobs from the DB, then consumes `jobChannel`
  indefinitely. Per job: if already `Cancelled` (set by `JobService.CancelJob`
  before the worker picked it up), audits + deletes `JobData` and stops.
  Otherwise marks `Submitting`, dispatches via `IPrinterDispatcher.SendAsync`.
  On dispatch failure: marks `Failed`, audits (retry is manual via
  `JobService.RetryJob`, not automatic here). On dispatch success: hands the
  returned `IppJobRef` to `PrinterPoller` over `Channel<IppJobRef>` —
  `PrinterPoller` owns `Completed`/`Failed` detection from that point via IPP
  polling, not this worker. Pushes updates via `IJobNotifier` after each DB
  write. Fresh DB scope per job via `IServiceScopeFactory`.
- `PrinterDispatcher : IPrinterDispatcher` — real IPP delivery via
  SharpIppNext (`SharpIppClient`), targets `ipp://{printer.Host}:631/ipp/print`.
  Verified end-to-end against a real HP ENVY Inspire 7200 over CUPS/mDNS.
  Content type must already be printer-native (no transcoding).
- `PrinterDiscoveryService : IPrinterDiscoveryService` — mDNS scan via
  `Zeroconf` for `_ipp._tcp.local.`, parses TXT records into `Printer` objects
  (not yet persisted — caller has to `POST /Printer` separately).
- `PrinterService : IPrinterService` — duplicate check on IP/Name/Host before
  insert.
- `LogsService : ILogsService` — builds an `IQueryable<AuditLog>` by
  reassigning through each optional filter, counts before paging, returns
  `PagedResult<AuditLog>`.
- `PrinterPoller : BackgroundService` — seeds one `PrinterWatch` + one
  `PrinterHeartbeat` per printer on startup, runs all of them plus a
  `RouteJobs` loop concurrently via `Task.WhenAll`.
  - `PrinterWatch` — per printer. Holds `ConcurrentDictionary<int,
    WatchedJob>` of in-flight IPP jobs, keyed by IPP job ID. A
    `PeriodicTimer` polls `IPrinterDispatcher.GetPrinterJobsAsync` — 60s
    idle, drops to 5s while any job is tracked. On a state change: updates
    `Job.Status`/`FailureReason` in the DB, writes an `AuditLog` row for
    terminal states (`Failed`/`Cancelled`/`Completed`), pushes via
    `IJobNotifier`, and stops tracking the job once terminal.
  - `PrinterHeartbeat` — per printer. `PeriodicTimer` polls
    `IPrinterDispatcher.GetPrinterStatusAsync` every 30s, writes
    `Printer.Status`/`LastHeartbeat`.
  - `RouteJobs` — reads `Channel<IppJobRef>` (written by `PrintJobWorker`
    after a successful `SendAsync`), hands each ref to the matching
    printer's `PrinterWatch.AddJob`.
- `Migrations/` — `InitialCreate`, `FixColumnTypos`, `AddPrinterForeignKey`,
  `EnumToStringConversions`, `AddJobData`, `AddFileSizeToJob`,
  `AddPrinterDiscoveryFields`.

## Api (`PrintSpooler.Api`)

- `Program.cs` DI: `AppDbContext` (SQL Server, conn string from user-secrets),
  `IPrinterDispatcher`/`IPrinterDiscoveryService`/`IJobNotifier` as Singleton
  (stateless), `IJobService`/`IPrinterService`/`ILogsService` as Scoped
  (depend on Scoped `AppDbContext`), `PrintJobWorker` + `PrinterPoller` as
  hosted services, `Channel<Guid>` singleton (job queue, `PrintJobWorker`),
  `Channel<IppJobRef>` singleton (submitted-job hand-off, `PrintJobWorker`
  writes → `PrinterPoller` reads), SignalR.
- `PrintJobController` — `POST /PrintJob` (201 + body / 400 Problem Details),
  `GET /PrintJob/{id}`, `GET /PrintJob` (all active), `DELETE /PrintJob/{id}`
  (cancel), `POST /PrintJob/{id}/retry`.
- `PrinterController` — `POST /Printer`, `GET /Printer/{id}`, `GET /Printer`,
  `DELETE /Printer/{id}`.
- `PrinterDiscoveryController` — `GET /PrinterDiscovery` → runs mDNS probe.
- `LogsController` — `GET /Logs` (`[FromQuery] LogQueryParams` — GET has no
  body, so `[ApiController]`'s default `[FromBody]` inference must be
  overridden explicitly for complex types).
- `JobHub : Hub` — SignalR hub at `/hubs/jobs`, no server-invokable methods,
  purely a broadcast channel.
- `JobNotifier : IJobNotifier` — broadcasts `"JobUpdated"` with the full
  `Job` to all SignalR clients.
- `Contracts/CreateJobRequest`, `CreatePrinterRequest` — narrow request DTOs
  (no server-generated fields like `Id`/`Status`).

## Web (`PrintSpooler.Web`, Blazor Server)

- `Program.cs` — `HttpClient` named `"PrintSpoolerApi"`, base address from
  config `ApiBaseAddress`; `ApiClient` registered Scoped.
- `ApiClient` — thin wrapper over `HttpClientFactory`'s named client:
  `Get<T>(url)`, `Get<T>(url, id)`, `Get<TResponse,TQuery>(url, queryParams)`
  (builds querystring via `RouteValueDictionary`), `Post<T>`, `Delete`,
  `HealthCheck`. All return `ErrorOr<T>`, never throw out to callers.
- **Pages:**
  - `Dashboard.razor` (`/dashboard`) — home page. Opens a `HubConnection` to
    `/hubs/jobs`, loads printers + active jobs into `Dictionary<Guid,
    List<QueueRow>> cache` (keyed by printer), renders one `PrinterCard` per
    printer. Live job updates patch `cache` in place via `HandleJobUpdate`.
    Staged files (picked but not yet submitted) live client-side only as
    `QueueRow`s with `PendingData` bytes until sent.
  - `Printers.razor` (`/printers`) — printer CRUD + mDNS "Scan Network" (via
    `PrinterDiscoveryController`) with one-click add from scan results, plus
    a manual add form (form fields present, submit handler not yet wired —
    `Register Printer` button has no `@onclick`).
  - `Logs.razor` (`/logs`) — paginated/filterable audit log table. Debounced
    search (350ms), debounced loading spinner (250ms delay / 300ms minimum
    shown), row-hover (600ms dwell) or job-ID click opens `JobDetailCard`
    with that job's detail, fetched and cached in `jobCache`.
- **Shared components:**
  - `PrinterCard.razor` — one printer's expandable card on the Dashboard:
    stat counts, multi-select rows with a contextual action tray
    (Delete/Send/Retry, shown only for actions that apply to the current
    selection's states), file drop via `InputFile`.
  - `JobDetailCard.razor` — floating detail panel (used by Logs; Dashboard
    does not use it) showing one job's id/content-type/submitted/printer/
    failure reason.
  - `SpSelect.razor` (`@typeparam TValue`) — custom dropdown replacing native
    `<select>`, click-outside-to-close backdrop, `@bind-Value` support.
  - `SpSelectOption<TValue>` — `Value`/`Label` pair for `SpSelect`.
- **Models:** `QueueRow` (client-side view of a `Job`, plus `PendingData`/
  `IsSending` for not-yet-submitted files — `FromJob`/`ApplyJob` sync it from
  a real `Job`) · `RowActions` enum (`Delete`/`Send`/`Retry`) · `ActionArgs`
  (bundles selected row ids + action + printer for the Dashboard's action
  handler) · `PrinterLoadState` enum (`Ready`/`Empty`/`Failed`/`Retry` —
  currently unused by any page).
- **Formatters:** `FormatBytes.Short` (B/KB/MB) · `FormatTime.Short`/`Full`/
  `Ago` (relative vs. absolute timestamps).
- `wwwroot/js/interop.js` — `scrollElementToTop` (Logs, on filter/page
  change). Earlier `positionDetailCard` JS-interop popover-clamping logic
  referenced in prior notes is not present in the current Dashboard/Logs flow
  — `JobDetailCard` is now positioned by normal layout flow, not JS.

## Request flows

**Job submission → delivery:**
```
Dashboard (file picked)
  -> staged client-side as QueueRow (PendingData bytes, not yet a Job)
  -> user hits Send -> ApiClient.Post("/PrintJob", ...)
  -> PrintJobController.Post -> IJobService.CreateJob
  -> JobService: validate PrinterId exists -> save Job+JobData+AuditLog(Created)
  -> Channel<Guid> jobChannel.Write(job.Id)
  -> PrintJobWorker reads channel -> mark Submitting -> IPrinterDispatcher.SendAsync (IPP)
  -> send failure: mark Failed, FailureReason set, AuditLog(Failed)  [no auto-retry — RetryJob is manual, user-triggered]
     send success: Channel<IppJobRef>.Write -> PrinterPoller.RouteJobs -> PrinterWatch tracks it
  -> IJobNotifier.JobUpdateAsync (on every status change, including the initial Submitting)
     -> SignalR "JobUpdated" -> Dashboard.HandleJobUpdate patches cache
```

**Printer job-state polling (completion detection):**
```
PrinterWatch (per printer, PeriodicTimer 5s active / 60s idle)
  -> IPrinterDispatcher.GetPrinterJobsAsync (IPP Get-Job-Attributes) -> List<IppJobStatus>
  -> per tracked job: state unchanged or Unknown -> skip
     state changed -> Job.Status/FailureReason updated in DB
       -> terminal (Failed/Cancelled/Completed): AuditLog row, JobData NOT
          deleted anywhere for this path — see **Not built yet**
          -> stop tracking (remove from WatchedJob cache)
       -> non-terminal: WatchedJob cache entry updated, keep tracking
  -> IJobNotifier.JobUpdateAsync -> SignalR "JobUpdated" -> Dashboard patches cache
```

**Printer discovery → registration:**
```
Printers page "Scan Network"
  -> GET /PrinterDiscovery -> PrinterDiscoveryService.ProbeForNetworkPrinters (Zeroconf mDNS)
  -> unsaved Printer objects returned to page
  -> user clicks "+ Add" -> POST /Printer -> PrinterService.CreatePrinter (dup check) -> saved
```

**Logs query:**
```
Logs page filters/search/page change
  -> ApiClient.Get<PagedResult<AuditLog>, LogQueryParams>("/Logs", queryParams)
  -> LogsController.Get([FromQuery]) -> LogsService.GetLogs
  -> IQueryable built up through optional Where clauses -> CountAsync (pre-paging) -> Skip/Take
  -> PagedResult<AuditLog> back to page
```

## Infra provisioned (real Azure, not simulated)

- Resource group `PrintSpooler-RG`, region **centralus** (free-trial
  subscription blocks SQL provisioning in eastus/eastus2/westus2 — known
  limitation, default to centralus for any new resources).
- SQL Server `printspooler-sql`, admin `printspoolerAdmin`; DB
  `PrintSpoolerDb`, Basic tier / 2GB cap (downgraded from auto-selected
  Standard S0 to control cost).
- Firewall rule for dev machine IP. Connection string via
  `dotnet user-secrets`, never committed.
- Local tooling: `az` CLI, `go-sqlcmd`, vim-dadbod(-ui), roslyn.nvim,
  kulala.nvim (`.http` requests at `Requests/print-job.http`,
  `PrintSpooler.Api/PrintSpooler.Api.http`).

## Not built yet

- `JobData` cleanup for jobs completed via `PrinterPoller` — `PrintJobWorker`
  only deletes `JobData` on its own `Cancelled` path (`RemoveJobData`).
  Jobs that reach `Completed`/`Failed` through `PrinterWatch` polling never
  get their `JobData` row deleted, unlike the old pre-poller flow. Likely a
  gap introduced when completion detection moved from `PrintJobWorker` to
  `PrinterPoller` — revisit.
- `Printers.razor` manual "Register Printer" button — form exists, not wired.
- File transcoding (text/PDF/image → PCL raster) — only printer-native
  content types (e.g. JPEG) work today. SkiaSharp planned for rasterizing.
- Multi-printer batch submission UI (backend already supports it — one
  `CreateJob` call per printer, no new domain logic needed).
- Print settings (copies/paper size/duplex) via IPP job attributes — would
  need `Get-Printer-Attributes` to discover what each printer actually
  supports before exposing options.
- Auth (JWT + roles) — descoped, revisit if time allows.
- Deployment (Azure App Service, App Insights, Key Vault).
- Tests — no test project yet.
- DB retention/purge policy for terminal `Job` rows — table grows unbounded.

## Working style / how to help this person

- Prefers being taught, not handed finished code — ask guiding questions for
  new-to-them concepts (ASP.NET/EF/Azure/DI), let them attempt it, correct
  with explanation. Wants to be able to defend every architectural decision
  in an interview — "can you explain why" is the real success criterion, not
  just "does it compile."
- Direct/fast answers are fine for pure syntax/tooling issues (typo, missing
  semicolon, CLI flag) — that's not a teaching moment.
- Explain *why*, not just *what* (e.g. why Core can't reference EF Core but
  can reference ErrorOr, why Scoped vs Singleton for a given class).
- Strong already in C#/LINQ/general fundamentals, Neovim, git, Arch Linux,
  Docker, Sybase ASE SQL. Newer to ASP.NET Core, EF Core, DI, Azure, REST API
  design, multi-project .NET architecture, IPP outside PrintFlow's
  Bartender-delegated approach. Auth and deployment are untested territory.
- Always use caveman mode (compressed, low-filler text) for prose responses,
  even mid-Socratic-teaching — code/commits/PRs/security warnings still get
  written out normally.

## Claude.md maintenance

Update this file whenever something is completed or the architecture changes
— it should stay a map of what exists and how it connects, not a changelog.
Keep the class/enum lists and flow diagrams accurate over prose history.
