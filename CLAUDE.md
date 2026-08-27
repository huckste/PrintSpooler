# PrintSpooler — Project Reference

## Goal

Portfolio piece demonstrating C#/ASP.NET Core/Azure to employers (author left
Hachette Book Group warehouse/print-ops work, self-taught into programming —
prior tools: PrintFlow, PrintFlow_v2, LabelGen). Product pitch: a
print-spooler-replacement — REST API + Blazor dashboard that accepts print
jobs, queues them, delivers to a real printer via IPP, tracks status, logs
everything. Genericized, not tied to any employer's systems. Built in ~1 week,
pushed to GitHub via `gh`.

## Current build status: APP BUILDS CLEAN; NO TESTS YET

`PrintSpooler.Core` / `Infrastructure` / `Api` / `Web` all build clean.
No test project exists — one is being built from scratch, step by step, as a
learning exercise. See **Tests**.

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
  `JobService.UpdateJob` deletes it whenever `JobPolicies.IsTerminal`
  (`Cancelled`/`Completed`) via `RemoveJobData`. `Failed` is intentionally
  NOT terminal — bytes are kept so the job can be retried.
- `JobCreationData` — Core-side "data needed to create a job," parallel to
  Api's `CreateJobRequest` DTO (Infrastructure can't reference Api).
- `Printer` — `Id`, `Name`, `IpAddress`, `Status` (`PrinterStatus`),
  `FailoverPrinterId?`, `LastHeartbeat?` (written by `PrinterHeartbeat` poll),
  `Host?`, `SupportedContentTypes?`, `PrinterUuid?` (last three added
  for mDNS discovery).
- `AuditLog` — `Id`, `JobId`, `Action` (`JobAction`), `PerformedBy` (`ByWho`),
  `Timestamp`, `Details?`. Static factory `AuditLog.For(jobId, action, by, details?)`.
  Scoped to job lifecycle only (printer CRUD not audited).
- `JobUpdate` — intent/description object: `JobId`, `Status`, `Retry`/`Notify`/
  `Write` flags, `AuditLog?`, `FailureReason?`. Fluent setters:
  `NotifyDashboard()` (Notify), `RetryJob()` (Retry), `WriteToChannel()` (Write),
  `Log(action, by, reason)` (sets AuditLog + FailureReason). Consumed by
  `IJobService.UpdateJob` — replaces the old scattered save/audit/notify/enqueue
  steps with one declarative update.
- `IppJobRef` — `PrinterId`, `JobId`, `IppId`. Returned by
  `IPrinterDispatcher.SendAsync` on a successful submit — the printer-assigned
  IPP job ID paired back to our `Job.Id`, handed to `PrinterPoller` over
  `Channel<IppJobRef>` so it knows which IPP job to start watching.
- `IppJobStatus` — `Id`, `State` (`JobStatus`), `Message?`. One printer-reported
  job snapshot from `IPrinterDispatcher.GetPrinterJobsAsync` (maps IPP's
  `job-state` onto `JobStatus`).
- `WatchedJob` — `JobId`, `State` (`JobStatus`). `PrinterWatch`'s own
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
`PrinterStatus` (`Online`/`Offline`/`Unknown`/`Stopped`/`Idle`/`Processing` —
granular states from IPP `printer-state`, added in migration
`AddPrinterStateGranularity`; Web treats `Idle`/`Processing`/`Stopped` as
"online" via `PrinterWebService.IsOnline`) · `JobAction`
(`Created`/`Cancelled`/`Completed`/`Failed`/`Retried`) · `ByWho`
(`System`/`User`) · `OrderByField` (`JobAction`/`ByWho`/`Timestamp`/`Details`) ·
`SortDirection` (`Asc`/`Desc`).

## Core — Service interfaces (`PrintSpooler.Core/Services`)

- `IJobService` — `CreateJob(JobCreationData)`, `GetJob(Guid)`,
  `GetAllActiveJobs() -> Task<List<Job>>` (not `ErrorOr`), `CancelJob(Guid)`,
  `RetryJob(Guid)`, `GetPendingJobs() -> ErrorOr<List<Job>>` (Queued),
  `GetMiaJobs() -> ErrorOr<List<Job>>` (Submitting/Processing),
  `GetJobData(Guid, ct) -> ErrorOr<JobData>`, `UpdateJob(JobUpdate, ct) ->
  ErrorOr<Success>`, `RemoveJobData(Guid, ct) -> ErrorOr<Success>`.
- `IPrinterService` — `GetPrinter`, `CreatePrinter`, `GetPrinters`,
  `DeletePrinter -> ErrorOr<Success>` (guards against active jobs via
  `JobPolicies.IsActive`), `UpdatePrinter(printer, ct)` (SaveChanges + notify).
- `IPrinterDispatcher` — `SendAsync(job, bytes, ct) -> ErrorOr<IppJobRef>` ·
  `GetPrinterJobsAsync(printer, int[] ids, ct) -> ErrorOr<List<IppJobStatus>>`
  · `GetPrinterStatusAsync(printer, ct) -> ErrorOr<PrinterStatus>`.
- `IPrinterDiscoveryService` — `ProbeForNetworkPrinters()` → mDNS scan.
- `IJobNotifier` — `JobUpdateAsync(job, ct)` → pushes a job update out (Api
  implements via SignalR).
- `IPrinterNotifier` — `PrinterUpdateAsync(printer, ct)` → same for printers.
- `JobPolicies` (static) — guards + helpers for the whole pipeline:
  `CanCancel(job)` (Queued|Failed), `CanRetry(job)` (Failed),
  `CanDispatch(job)` (Queued), `IsPending(status)` (Queued),
  `DefaultStatus() -> Queued`, `IsMia(status)` (Submitting|Processing),
  `IsTerminal(status)` (Cancelled|Completed — JobData deleted on these),
  `IsActive(status)`, plus status arrays `Mia`/`Pending`/`Active` used in the
  `GetMiaJobs`/`GetPendingJobs`/`GetAllActiveJobs` queries and the printer
  delete guard.

## Infrastructure (`PrintSpooler.Infrastructure`)

- `AppDbContext` — `DbSet<Job/JobData/Printer/AuditLog>`. Enums stored as
  strings (`HasConversion<string>()`). `Job`↔`JobData` is a required 1:1 on
  `JobData.JobId`.
- `JobService : IJobService` — wraps every state change through the
  `JobUpdate`/`UpdateJob` path (save + optional audit + optional notify +
  optional `Channel<Guid>` write; deletes `JobData` on terminal states).
  `CreateJob` validates the printer exists, saves `Job` + `JobData`, then
  `UpdateJob` with `.Log(Created).WriteToChannel()`. `CancelJob`/`RetryJob`
  guard via `JobPolicies.CanCancel`/`CanRetry` then `UpdateJob` (Retry adds
  `.RetryJob().WriteToChannel()`). `RemoveJobData` does a direct
  `ExecuteDeleteAsync` — used by `UpdateJob`'s terminal-state cleanup.
- `PrintJobWorker : BackgroundService` — on startup, re-enqueues any `Queued`
  jobs from the DB, then marks any MIA (`Submitting`/`Processing`) jobs as
  `Failed` + audit + notify (crash recovery for in-flight jobs). Consumes
  `jobChannel` indefinitely. Per job: `JobPolicies.CanDispatch` (already
  `Cancelled`? stop) → marks `Submitting` → `GetJobData` → `SendAsync`.
  On dispatch failure: marks `Failed` + audit (retry is manual via
  `JobService.RetryJob`, not automatic here). On dispatch success: hands the
  returned `IppJobRef` to `PrinterPoller` over `Channel<IppJobRef>` —
  `PrinterPoller` owns `Completed`/`Failed` detection from that point via IPP
  polling, not this worker. Fresh DB scope per job via
  `IServiceScopeFactory`.
- `PrinterDispatcher : IPrinterDispatcher` — real IPP delivery via
  SharpIppNext (`SharpIppClient`), targets `ipp://{printer.Host}:631/ipp/print`.
  Verified end-to-end against a real HP ENVY Inspire 7200 over CUPS/mDNS.
  Content type must already be printer-native (no transcoding).
- `PrinterDiscoveryService : IPrinterDiscoveryService` — mDNS scan via
  `Zeroconf` for `_ipp._tcp.local.`, parses TXT records into `Printer` objects
  (not yet persisted — caller has to `POST /Printer` separately).
- `PrinterService : IPrinterService` — duplicate check on IP/Name/Host before
  insert. `DeletePrinter` guards via `JobPolicies.IsActive` (refuses if the
  printer has active jobs), then saves + notifies. `UpdatePrinter` is the
  shared save + `IPrinterNotifier.PrinterUpdateAsync` path — `PrinterWatch`
  and `PrinterHeartbeat` route their DB writes through it, so every printer
  change (status, heartbeat, job-state side effects) fans out to the
  dashboard via SignalR.
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
  `AddPrinterDiscoveryFields`, `AddPrinterStateGranularity`.

## Api (`PrintSpooler.Api`)

- `Program.cs` DI: `AppDbContext` (SQL Server, conn string from user-secrets),
  `IPrinterDispatcher`/`IPrinterDiscoveryService`/`IJobNotifier`/
  `IPrinterNotifier` as Singleton
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
- `UpdatesHub : Hub` — SignalR hub at `/hubs/updates`, no server-invokable
  methods, purely a broadcast channel.
- `JobNotifier : IJobNotifier` — broadcasts `"JobUpdated"` with the full
  `Job` to all SignalR clients.
- `PrinterNotifier : IPrinterNotifier` — broadcasts `"PrinterUpdated"` with
  the full `Printer` to all SignalR clients.
- `Contracts/CreateJobRequest`, `CreatePrinterRequest` — narrow request DTOs
  (no server-generated fields like `Id`/`Status`).

## Web (`PrintSpooler.Web`, Blazor Server)

- `Program.cs` — `HttpClient` named `"PrintSpoolerApi"`, base address from
  config `ApiBaseAddress`; `ApiClient`, `ConnectionManager`, `PrinterApi`,
  `JobApi` registered Scoped.
- `ApiClient` — thin wrapper over `HttpClientFactory`'s named client:
  `Get<T>(url)`, `Get<T>(url, id)`, `Get<TResponse,TQuery>(url, queryParams)`
  (builds querystring via `RouteValueDictionary`), `Post<T>`, `Delete`,
  `HealthCheck`. All return `ErrorOr<T>`, never throw out to callers.
- **Service layer** (wrappers over `ApiClient` / SignalR):
  - `ConnectionManager` (Scoped, `IAsyncDisposable`) — owns the
    `HubConnection` to `{ApiBaseAddress}/hubs/updates` with automatic
    reconnect. Exposes events `JobUpdated` (Job), `PrinterUpdated` (Printer),
    `ConnectionLost` (bool), plus `StartAsync()` and `IsConnected`. Pages
    subscribe/unsubscribe in `OnInitializedAsync`/`DisposeAsync`. Replaces the
    old per-page raw `HubConnection`.
  - `JobApi` (Scoped) — `GetJobs()`, `SubmitJob(QueueRow)` (maps `PendingData`
    → `RawData`, `SubmittedBy="dashboard-user"`), `RetryJob(Guid?)`
    (`POST /PrintJob/{id}/retry`), `DeleteJob(Guid?)`.
  - `PrinterApi` (Scoped) — `GetPrinters()`, `DiscoverNetworkPrinters()`
    (`GET /PrinterDiscovery`), `AddPrinter(Printer)`, `DeletePrinter(Guid)`.
  - `JobWebService` (static) — `CountByStatus`, `ToList` (flatten `Dictionary`
    cache w/ optional `RowActions` + `printerId` + `rowIds` filter),
    `CanDoAction(action, status)` delegating to `RowPolicies`, `TargetedRow`.
  - `PrinterWebService` (static) — `IsOnline(status)`, `PrintersOnlineCount`,
    `PrintersOfflineCount`.
  - `RowPolicies` (static) — `CanCancel` (Staged|Failed), `CanRetry` (Failed),
    `CanSend` (Staged). The Web-side mirror of Core's `JobPolicies`, expressed
    over `JobStatus` for client-side row actions.
- **Pages:**
  - `Dashboard.razor` (`/dashboard`) — home page. Injects `ConnectionManager`,
    subscribes `hub.JobUpdated`/`hub.PrinterUpdated`; loads printers
    (`PrinterApi.GetPrinters`) + active jobs (`JobApi.GetJobs`) into
    `Dictionary<Guid, List<QueueRow>> cache` (keyed by printer), renders one
    `PrinterCard` per printer. Live `Job`/`Printer` events patch the cache +
    `printers` in place (`ApplyJob`). Staged files (picked but not yet
    submitted) live client-side only as `QueueRow`s with `PendingData` bytes
    until sent via `SubmitEntry`. On a terminal update a row is faded and
    removed after a delay (`FadeRowAfterDelay`); delete/retry/send via
    `OnRowAction` (`RowActions`).
  - `Printers.razor` (`/printers`) — printer overview (total/online/offline
    stats), "Discover on Network" mDNS scan (`PrinterApi.
    DiscoverNetworkPrinters`) with one-click "+ Add" from scan results
    (`PrinterApi.AddPrinter`), and a registered-printers table with delete.
    Live `PrinterUpdated` events patch `printers` in place; a 1s ticker
    refreshes "last heartbeat" relative time. No manual add form (add is
    scan-driven only).
  - `Logs.razor` (`/logs`) — paginated/filterable audit log table (injects
    `ApiClient` directly + JS interop). Debounced search (350ms), debounced
    loading spinner (250ms delay / 300ms minimum shown), row-hover (600ms
    dwell) or job-ID click opens `JobDetailCard` with that job's detail,
    fetched and cached in `jobCache`.
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
  `IsSending`/`ErrorText`/`JobId?` for not-yet-submitted files and submitted
  jobs — `FromJob`/`ApplyJob` sync it from a real `Job`) · `RowActions` enum
  (`Delete`/`Send`/`Retry`) · `ActionArgs` (bundles selected row ids + action
  + printer for the Dashboard's action handler) · `HealthState` enum
  (`Unknown`/`Online`/`Offline`) + `Label()`/`CssClass()` extensions.
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
      -> SignalR "JobUpdated" -> Dashboard.OnJobUpdated patches cache
```

**Printer job-state polling (completion detection):**
```
PrinterWatch (per printer, PeriodicTimer 5s active / 60s idle)
  -> IPrinterDispatcher.GetPrinterJobsAsync (IPP Get-Job-Attributes) -> List<IppJobStatus>
  -> per tracked job: state unchanged or Unknown -> skip
      state changed -> Job.Status/FailureReason updated in DB
        -> terminal (Cancelled/Completed): AuditLog row if terminal,
           JobData deleted via UpdateJob->RemoveJobData, stop tracking
        -> Failed: AuditLog row, JobData KEPT (retryable)
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

## Tests

No test project yet. Being built from scratch as a deliberate learning
exercise — the author writes every line; do NOT write test code for them.
Mode: ask a guiding question, let them attempt, correct with reasoning.

Known bugs deliberately left unfixed so their tests can catch them (do not
fix these unprompted — finding them is the exercise):
1. `JobPolicies.IsActive` — `is not Cancelled and Completed` parses as
   `(not Cancelled) and (== Completed)`; true only for `Completed`. Inverted.
2. `PrinterService.DeletePrinter` — calls `JobPolicies.IsActive(...)` inside an
   EF query; a method call can't translate to SQL, so it throws at runtime.
   `JobPolicies.Active.Contains(...)` is the translatable form.
3. `JobService.UpdateJob` — `FailureReason` assigned only when status is
   `Failed`, never cleared, so a retried job keeps a stale reason.
4. `PrinterService.CreatePrinter` — dup check ORs `p.Host == printer.Host`; EF
   preserves C# null semantics, so two host-less printers read as duplicates.

Reference implementation (a full 4-layer suite, written then parked) lives in
the session scratchpad at `reference-tests/` — for the assistant to consult,
not to paste back in.

## Not built yet

- File transcoding (text/PDF/image → PCL raster) — only printer-native
  content types (e.g. JPEG) work today. SkiaSharp planned for rasterizing.
- Multi-printer batch submission UI (backend already supports it — one
  `CreateJob` call per printer, no new domain logic needed).
- Print settings (copies/paper size/duplex) via IPP job attributes — would
  need `Get-Printer-Attributes` to discover what each printer actually
  supports before exposing options.
- Auth (JWT + roles) — descoped, revisit if time allows.
- Deployment (Azure App Service, App Insights, Key Vault).
- Tests — see **Tests** above. Nothing written yet.
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
