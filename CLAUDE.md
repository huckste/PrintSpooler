# PrintSpooler — Project Reference

## Goal

Portfolio piece demonstrating C#/ASP.NET Core/Azure to employers (author left
Hachette Book Group warehouse/print-ops work, self-taught into programming —
prior tools: PrintFlow, PrintFlow_v2, LabelGen). Product pitch: a
print-spooler-replacement — REST API + Blazor dashboard that accepts print
jobs, queues them, delivers to a real printer via IPP, tracks status, logs
everything. Genericized, not tied to any employer's systems. Built in ~1 week,
pushed to GitHub via `gh`.

The app is feature-complete for its purpose. Remaining work is a fixed,
closed list — see **Definition of done** at the bottom of this file. Do not
propose work outside it; the failure mode for this project is scope growth,
not missing features.

`README.md` already exists and is good — architecture diagram, dependency
direction, job-flow diagram, honest out-of-scope section. It needs a demo GIF
and a CI badge, not a rewrite.

## Current build status: SOLUTION BUILDS CLEAN; NO TEST PROJECT

`PrintSpooler.Core` / `Infrastructure` / `Api` / `Web` all build clean, and
they are the whole solution — the first `PrintSpooler.Tests` attempt was
deleted rather than repaired, to restart from a decision about *what* is worth
testing instead of from whatever was easiest to assert. See **Tests**.

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
- `IppJobStatus` — `Id`, `State` (`JobStatus?`), `Message?`. One printer-reported
  job snapshot from `IPrinterDispatcher.GetPrinterJobsAsync` (maps IPP's
  `job-state` onto `JobStatus`). `State` is null when the printer reports an
  IPP state we don't model — `PrinterWatch` logs it and skips, so an unmodeled
  state can never corrupt `Job.Status`.
- `WatchedJob` — `JobId`, `State` (`JobStatus`). `PrinterWatch`'s own
  bookkeeping record — one per in-flight IPP job it's polling, cached in
  `PrinterWatch`'s `ConcurrentDictionary<int, WatchedJob>` keyed by IPP job ID.
- `LogQueryParams` — filter/sort/page params for `GET /Logs`
  (`SearchTerms`, `JobId?`, `DateFrom?`/`DateTo?` as `DateOnly?`,
  `ActionFilter?`, `PerformedBy?`, `OrderByField`, `SortDirection`, `Page`,
  `PageSize`).
- `PagedResult<T>` — `Items`, `TotalCount`, `Page`, `PageSize`,
  computed `TotalPages`.

**Enums:** `JobStatus` (`Queued`/`Submitting`/`Processing`/`Completed`/
`Cancelled`/`Failed` — shared by `Job.Status` and by `IppJobStatus.State?`, the
poller's mapping of IPP `job-state`. Deliberately has no `Staged` (Web-only
concept — see `QueueRow.IsStaged`) and no `Unknown` (that was a null wearing an
enum member's clothes; unmappable IPP states are now `null`). `Submitting`
covers both our send-in-progress window and the printer's IPP
`pending`/`pending-held` — in all of them the bytes have left us and nothing
has hit paper) ·
`PrinterStatus` (`Online`/`Offline`/`Unknown`/`Stopped`/`Idle`/`Processing` —
granular states from IPP `printer-state`, added in migration
`AddPrinterStateGranularity`; Web treats `Idle`/`Processing`/`Stopped` as
"online" via `PrinterWebService.IsOnline`) · `JobAction`
(`Created`/`Cancelled`/`Completed`/`Failed`/`Retried`) · `ByWho`
(`System`/`User`) · `OrderByField` (`JobAction`/`ByWho`/`Timestamp`/`Details`) ·
`SortDirection` (`Asc`/`Desc`).

## Core — Service interfaces (`PrintSpooler.Core/Services`)

- `IJobService` — `CreateJob(JobCreationData)`, `GetJob(Guid)`,
  `GetAllActiveJobs() -> Task<List<Job>>` (not `ErrorOr` — an empty queue is a
  valid answer, not a failure), `CancelJob(Guid)`,
  `RetryJob(Guid)`, `GetPendingJobs() -> ErrorOr<List<Job>>` (Queued),
  `GetInFlightJobs() -> ErrorOr<List<Job>>` (Submitting/Processing),
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
- `JobPolicies` (static) — the single source of truth for job state, for both
  Api and Web. Status **arrays** are the vocabulary; every guard and predicate
  reads from them, so a policy is written down exactly once:
  - `Terminal` = Completed|Cancelled — JobData deleted on these
  - `Pending` = Queued (ours, not yet dispatched)
  - `InFlight` = Submitting|Processing (at the printer)
  - `Retryable` = Failed
  - `Active` = **derived**, `Enum.GetValues<JobStatus>().Except(Terminal)`, so
    `Active` and `Terminal` can never drift and a newly added status defaults
    to active (visible on dashboard, blocks printer delete)
  - `DefaultStatus` = Queued (field, not a method)

  Predicates `IsPending`/`IsTerminal`/`IsActive`/`IsInFlight` are `Contains`
  over those arrays. Guards return `ErrorOr<Job>` and use **positive**
  membership, never set complement, so an unclassified status fails closed:
  `CanCancel` (currently `Active` — deliberately wide, see below),
  `CanRetry` (`Retryable`), `CanDispatch` (`Pending`).

  The arrays are also what EF queries use (`JobPolicies.Active.Contains(...)`
  translates to SQL `IN`; a predicate method call does not).

  **Open:** `CanCancel` allows in-flight jobs, but `JobService.CancelJob` only
  writes the DB — it does not send IPP `Cancel-Job`. Cancelling a printing job
  currently marks it Cancelled, deletes JobData, and lets `PrinterWatch`
  overwrite the status when the printer reports Completed. Intentional
  placeholder: IPP cancel is being added. When it lands, split a `Cancellable`
  array (`Pending ∪ Retryable`) out of `Active` if cancel should narrow again.

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
  jobs from the DB, then marks any in-flight (`Submitting`/`Processing`) jobs as
  `Failed` + audit + notify (crash recovery — the printer-assigned IPP job id
  lives only in `PrinterWatch`'s memory, so a restart genuinely loses track of
  those jobs). Consumes
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
  Verified end-to-end against a real HP ENVY Inspire 7200 — direct IPP to the
  printer, mDNS for discovery only. No CUPS in the path.
  Content type must already be printer-native (no transcoding).
  `GetPrinterJobsAsync` maps IPP `job-state` to `JobStatus?`:
  `pending`/`pending-held` → `Submitting`, `processing` → `Processing`,
  `processing-stopped`/`aborted` → `Failed`, `canceled` → `Cancelled`,
  `completed` → `Completed`, anything else → `null`.
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
    WatchedJob>` of in-flight IPP jobs, keyed by IPP job ID; `AddJob` seeds
    each at `Submitting`, which is true at hand-off time and makes the
    printer's first `pending` report a no-change (no DB write, no SignalR
    push). A `PeriodicTimer` polls `IPrinterDispatcher.GetPrinterJobsAsync` —
    60s idle, drops to 5s while any job is tracked. A `null` state is logged
    and skipped. On a state change: updates
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
  - `JobWebService` (static) — `CountByStatus(JobStatus?, ...)` (null counts
    staged rows, same convention as `QueueRow.Status`), `ToList` (flatten `Dictionary`
    cache w/ optional `RowActions` + `printerId` + `rowIds` filter),
    `CanDoAction(action, row)` delegating to `RowPolicies`, `TargetedRow`.
  - `PrinterWebService` (static) — `IsOnline(status)`, `PrintersOnlineCount`,
    `PrintersOfflineCount`.
  - `RowPolicies` (static) — `CanSend(row)` (`row.IsStaged`), `CanRetry(row)`
    and `CanCancel(row)`, both of which read Core's `JobPolicies.Retryable` /
    `IsActive` directly. **Not** a mirror of Core's rules — Web owns only the
    staged case (a row with no `Job` yet) and defers every real-job decision to
    Core, so the button set can't drift from what the API will accept.
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
  jobs — `FromJob`/`ApplyJob` sync it from a real `Job`). `Status` is
  `JobStatus?`: null means staged, since a staged row is a picked file, not a
  job. `IsStaged` (`JobId is null`) is the discriminator;
  `LabelFor(JobStatus?)` / `StatusLabel` render "Staged" for null, and the
  dashboard/card stat tiles are plain `JobStatus?[]` on the same convention.
  A failed submit leaves the row staged with
  `ErrorText` set and the bytes still local — it is not marked `Failed`,
  because no `Job` exists to have failed. · `RowActions` enum
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
  -> per tracked job: state unchanged or null (unmodeled IPP state) -> skip
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

No test project. A first attempt (xunit + FluentAssertions) was deleted — its
`JobPolicies` tests enumerated every `JobStatus` by hand in `[InlineData]`
rows, which restated the guard bodies and then broke the moment the enum
changed. Rebuilding from scratch as a deliberate learning exercise; the author
writes every line, do NOT write test code for them. Mode: ask a guiding
question, let them attempt, correct with reasoning.

**What counts as a test worth writing here** (settled by the author, applies to
everything below):

- A test that restates the function body tests nothing. If you can read the
  assertion off the implementation at a glance, skip it.
- Hardcoding a value is only bad when the code under test is that value's only
  source of truth. Three cases: *arbitrary internal policy* (`Retryable =
  [Failed]`) — write it down once, in the array, don't re-assert it;
  *external standard* (HTTP 400, IPP `job-state` numbers) — hardcode freely,
  that IS the contract; *relation between two independently-written policies* —
  the valuable case.
- Prefer tests whose answer you cannot predict by reading one function: EF
  query translation, HTTP status mapping, crash recovery, cross-layer
  invariants.
- Use SQLite in-memory, NOT the EF `InMemory` provider — `InMemory` silently
  client-evaluates and hides exactly the translation bugs worth catching.
- Mock-heavy tests that assert a fake was called mostly test the fake.

**Scope: 8–12 tests, not a suite.** Selection rule the author set — test what
is hard to stage against real hardware. A failed submit, a crash mid-print, a
printer that reports a state we don't model: all painful to reproduce live,
all cheap to assert. Anything you can just click through in the dashboard
doesn't need a test here.

Candidates, in rough priority:
- `Terminal`/`Pending`/`InFlight`/`Retryable` partition `JobStatus`
  exhaustively and pairwise disjointly (fires when a new status is added and
  left unclassified)
- `Retryable ∩ Terminal = ∅` — bytes must survive for retry to work
- fail a job, retry it, assert `JobData` still exists and `FailureReason` is
  cleared
- startup recovery with seeded `Queued`/`Submitting` rows
- `ErrorOr` → HTTP status mapping via `WebApplicationFactory` (404 for a
  missing job, 409 for cancelling a completed one)
- `DeletePrinter` against a real provider — a predicate method inside a
  `Where` throws only at runtime

Known bugs that were parked for tests to catch. Tests are now scoped smaller
and #1 is visible in the demo, so both are on the **Definition of done** list
to fix directly:
1. `JobService.UpdateJob` — `FailureReason` assigned only when status is
   `Failed`, never cleared, so a retried job keeps a stale reason.
2. `PrinterService.CreatePrinter` — dup check ORs `p.Host == printer.Host`; EF
   preserves C# null semantics, so two host-less printers read as duplicates.

(Two earlier planted bugs are now fixed as a side effect of the `JobPolicies`
rework: the inverted `IsActive` pattern match, and `DeletePrinter` calling a
predicate method inside an EF query instead of `Active.Contains`.)

## Definition of done

This is the finish line, agreed 2026-08-28. Goal is a project that reads well
to a company hiring for full-stack C# — not a product. Nothing outside this
list gets built; when these nine ship, the project is **complete**.

Ordered by value per hour of work:

1. **`ErrorOr` → HTTP status mapping.** Every controller currently returns
   `statusCode: 400` for every error, so a missing job is a 400 instead of 404
   and cancelling a completed job is a 400 instead of 409. `ErrorOr` already
   carries `ErrorType`; it's discarded at the boundary. One extension method,
   used by all four controllers. Most-noticed thing in an API review.
2. **Cancel tells the truth.** The dashboard offers Cancel on a printing job
   and it doesn't stop the printer — `CancelJob` only writes the DB. Either
   send IPP `Cancel-Job`, or narrow `CanCancel` to a `Cancellable` array
   (`Pending ∪ Retryable`). Author's preference is the real cancel. A demo
   that lies is worse than a missing button.
3. **Stale `FailureReason`.** Bug #1 in **Tests** — a retried job shows its old
   error in the dashboard. Visible in the demo recording, so fix it directly.
4. **Tests + CI together.** 8–12 tests per **Tests** above, plus a GitHub
   Actions workflow (restore / build / test on push) and a badge in the
   README. No `.github/` exists today. CI is what makes the tests count for a
   reviewer who won't run them.
5. **Persist the IPP job id on `Job`.** Lives only in `PrinterWatch`'s
   in-memory dictionary, so a restart marks still-printing jobs `Failed`.
   Column + migration + rehydrate `PrinterWatch` on startup. This is the
   strongest distributed-systems story in the project — reconciling against a
   device you don't control, across a crash.
6. **Auth, minimal.** JWT + one login endpoint + `[Authorize]`. No ASP.NET
   Identity, no roles, no refresh tokens. Seed a demo user and put the
   credentials in the README so the demo stays clickable.
7. **`docker-compose` with `mssql/server`.** Makes the project runnable by
   someone with no Azure account. Deliberately NOT a SQLite provider switch —
   EF migrations are provider-specific, so that means maintaining a second
   migration set forever plus silent behavioural drift. Same outcome, a tenth
   of the work.
8. **XML doc comments** on Core interfaces and `JobPolicies` — practice
   knowing where they earn their place. Skip obvious private methods;
   over-commenting reads as badly as under-commenting.
9. **Demo GIF at the top of the README.** Real file, real printer, dashboard
   moving live. Record last, once everything above is final. This is the asset
   a reviewer actually consumes — nobody clones a portfolio repo.

## Explicitly out of scope

Decided, not pending. These belong in the README's limitations section, where
a stated boundary reads as judgment rather than as a gap.

- File transcoding (text/PDF → raster). Only printer-native content types
  print. Guard it at `CreateJob` using `Printer.SupportedContentTypes` (today
  discovered by mDNS, stored, and never read) and say so in the README.
- A fake `IPrinterDispatcher` for demo purposes — considered and rejected.
  The README already documents that everything except the print itself works
  without a printer, and faking the one part that talks to real hardware
  throws away the most interesting thing the project does.
- Reading IPP `job-state-reasons` (would separate "held" from "warming up").
- Multi-printer batch submission UI (backend already supports it).
- Print settings (copies/paper size/duplex) via IPP job attributes.
- `Printer.FailoverPrinterId` — in the model and 6 migrations, zero logic.
  Leave it or delete it; do not build failover.
- Deployment to Azure App Service. Optional stretch after the nine above.
- DB retention/purge for terminal `Job` rows — table grows unbounded.
- `GetPendingJobs`/`GetInFlightJobs` returning `Error.NotFound` on empty
  (an empty queue is a valid answer, not a failure). Cosmetic; fix only if
  touching that code anyway.

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
