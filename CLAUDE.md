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
  `JobData`), `IppJobId?` (printer-assigned, written by `SetIppJobId` after a
  successful send), `SubmittedBy`, `FileName`, `ContentType`, `FileSizeBytes`,
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
- `WatchedJob` — `JobId`, `MissedPolls`. `PrinterWatch`'s own bookkeeping —
  one per in-flight IPP job it's polling, in a
  `ConcurrentDictionary<int, WatchedJob>` keyed by IPP job ID. Deliberately
  holds **no** status: the DB is the only record of that, and a second copy
  could only drift from it. `MissedPolls` is the inverse — consecutive polls in
  which the printer answered but said nothing about this job, which the DB
  cannot know.
- `LogQueryParams` — filter/sort/page params for `GET /Logs`
  (`SearchTerms`, `JobId?`, `DateFrom?`/`DateTo?` as `DateOnly?`,
  `ActionFilter?`, `PerformedBy?`, `OrderByField`, `SortDirection`, `Page`,
  `PageSize`).
- `PagedResult<T>` — `Items`, `TotalCount`, `Page`, `PageSize`,
  computed `TotalPages`.

**Enums:** `JobStatus` (`Queued`/`Submitting`/`Processing`/`Cancelling`/
`Completed`/`Cancelled`/`Failed` — shared by `Job.Status` and by `IppJobStatus.State?`, the
poller's mapping of IPP `job-state`. Deliberately has no `Staged` (Web-only
concept — see `QueueRow.IsStaged`) and no `Unknown` (that was a null wearing an
enum member's clothes; unmappable IPP states are now `null`). `Submitting`
covers both our send-in-progress window and the printer's IPP
`pending`/`pending-held` — in all of them the bytes have left us and nothing
has hit paper. `Cancelling` = we sent IPP `Cancel-Job` and are waiting for the
printer to confirm; `PrinterWatch` owns the move to `Cancelled`) ·
`PrinterStatus` (`Online`/`Offline`/`Unknown`/`Stopped`/`Idle`/`Processing` —
granular states from IPP `printer-state`, added in migration
`AddPrinterStateGranularity`; Web treats `Idle`/`Processing`/`Stopped` as
"online" via `PrinterWebService.IsOnline`) · `JobAction`
(`Created`/`CancelRequested`/`Cancelled`/`Completed`/`Failed`/`Retried` —
`CancelRequested` is written by `JobService` when the user cancels a job that
is already at the printer; the matching `Cancelled` comes later from
`PrinterWatch`, so a `Cancelled` with no `CancelRequested` before it was
cancelled at the printer itself) · `ByWho`
(`System`/`User`) · `OrderByField` (`JobAction`/`ByWho`/`Timestamp`/`Details`) ·
`SortDirection` (`Asc`/`Desc`).

## Core — Service interfaces (`PrintSpooler.Core/Services`)

- `IJobService` — `CreateJob(JobCreationData)`, `GetJob(Guid)`,
  `GetAllActiveJobs() -> Task<List<Job>>` (not `ErrorOr` — an empty queue is a
  valid answer, not a failure), `CancelJob(Guid)`,
  `RetryJob(Guid)`, `SetIppJobId(Guid, int, ct)`, `GetPendingJobs() -> ErrorOr<List<Job>>` (Queued),
  `GetInFlightJobs() -> ErrorOr<List<Job>>` (Submitting/Processing),
  `GetJobs(Guid[] ids, ct) -> Task<List<Job>>` (one round trip for a set —
  `Contains` translates to SQL `IN`), `GetJobData(Guid, ct) -> ErrorOr<JobData>`, `UpdateJob(JobUpdate, ct) ->
  ErrorOr<Success>`, `RemoveJobData(Guid, ct) -> ErrorOr<Success>`.
- `IPrinterService` — `GetPrinter`, `CreatePrinter`, `GetPrinters`,
  `DeletePrinter -> ErrorOr<Success>` (guards against active jobs via
  `JobPolicies.IsActive`), `UpdatePrinter(printer, ct)` (SaveChanges + notify).
- `IPrinterDispatcher` — `SendAsync(job, bytes, ct) -> ErrorOr<IppJobRef>` ·
  `CancelPrinterJob(printer, ippId, ct) -> ErrorOr<Success>` (IPP `Cancel-Job`) ·
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
  - `InFlight` = Submitting|Processing|Cancelling (at the printer)
  - `Retryable` = Failed
  - `Cancellable` = `Pending ∪ Retryable ∪ (InFlight \ Cancelling)` — a
    positive union with one exclusion rather than `Active.Except(Cancelling)`,
    so an unclassified new status still fails closed
  - `Active` = **derived**, `Enum.GetValues<JobStatus>().Except(Terminal)`, so
    `Active` and `Terminal` can never drift and a newly added status defaults
    to active (visible on dashboard, blocks printer delete)
  - `DefaultStatus` = Queued (field, not a method)

  Predicates `IsPending`/`IsTerminal`/`IsActive`/`IsInFlight`/`IsCancellable`
  are `Contains`
  over those arrays. Guards return `ErrorOr<Job>` and use **positive**
  membership, never set complement, so an unclassified status fails closed:
  `CanRetry` (`Retryable`), `CanDispatch` (`Pending`).

  `CanCancel` is the exception to the status-only rule: it takes a whole `Job`,
  because `Submitting` covers two different situations. With an `IppJobId` the
  job is sitting in the printer's IPP queue and is cancellable; without one we
  are mid-`SendAsync`, the printer has no id to cancel by, and accepting the
  request would mark the job `Cancelled` and print it anyway — so that case
  returns `Error.Conflict("Job.Sending")`.

  The arrays are also what EF queries use (`JobPolicies.Active.Contains(...)`
  translates to SQL `IN`; a predicate method call does not).

## Infrastructure (`PrintSpooler.Infrastructure`)

- `AppDbContext` — `DbSet<Job/JobData/Printer/AuditLog>`. Enums stored as
  strings (`HasConversion<string>()`). `Job`↔`JobData` is a required 1:1 on
  `JobData.JobId`.
- `JobService : IJobService` — wraps every state change through the
  `JobUpdate`/`UpdateJob` path (save + optional audit + optional notify +
  optional `Channel<Guid>` write; deletes `JobData` on terminal states).
  `CreateJob` validates the printer exists, saves `Job` + `JobData`, then
  `UpdateJob` with `.Log(Created).WriteToChannel()`. `RetryJob`
  guards via `JobPolicies.CanRetry` then `UpdateJob` (adds
  `.RetryJob().WriteToChannel()`). `CancelJob` guards via
  `JobPolicies.CanCancel`, then branches on `IppJobId` (**not** status): null
  means the job never reached the printer, so it goes straight to `Cancelled`;
  otherwise it sends IPP `Cancel-Job` and, on success, writes `Cancelling` +
  `CancelRequested`/`User` and lets `PrinterWatch` confirm the terminal
  `Cancelled`/`System`. The four cancel-ish outcomes stay distinguishable in
  the audit log: `Cancelled`/`User` alone (never reached the printer),
  `CancelRequested` then `Cancelled` (we asked, printer complied), `Cancelled`
  alone (cancelled at the printer's own panel), `Failed` (printer aborted). A refused cancel leaves
  the status untouched, records `FailureReason`, and returns the error — the
  job is still printing, so saying otherwise would lie. `RemoveJobData` and
  `SetIppJobId` are direct `ExecuteDeleteAsync` / `ExecuteUpdateAsync` calls
  that bypass `UpdateJob` — neither is a status change, so neither audits,
  notifies, nor writes to the channel.
- `PrintJobWorker : BackgroundService` — on startup, re-enqueues any `Queued`
  jobs from the DB, then marks every `JobPolicies.InFlight` job
  (`Submitting`/`Processing`/`Cancelling`) as `Failed` + audit + notify (crash
  recovery — `Job.IppJobId` survives the restart but `PrinterWatch`'s tracking
  does not, so nothing is watching those jobs any more). Consumes
  `jobChannel` indefinitely. Per job: `JobPolicies.CanDispatch` (already
  `Cancelled`? stop) → marks `Submitting` → `GetJobData` → `SendAsync`.
  On dispatch failure: marks `Failed` + audit (retry is manual via
  `JobService.RetryJob`, not automatic here). On dispatch success: persists the IPP id via
  `SetIppJobId`, then hands the returned `IppJobRef` to `PrinterPoller` over `Channel<IppJobRef>` —
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
  `completed` → `Completed`, anything else → `null`. Asks by
  `job-ids` and deliberately sends **no** `which-jobs` — the two are mutually
  exclusive (PWG 5100.7) and a printer given both rejects the request. `job-ids`
  returns the named jobs in any state, which is what completion detection and
  startup rehydration both need. A printer that answers but knows none of the
  ids returns an **empty list**, not an error, so `PrinterWatch` can tell "job
  is gone" from "printer unreachable".
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
  - `PrinterWatch` — per printer. Tracks in-flight IPP jobs in a
    `ConcurrentDictionary<int, WatchedJob>` keyed by IPP job ID. A
    `PeriodicTimer` polls `IPrinterDispatcher.GetPrinterJobsAsync` — 60s idle,
    5s while any job is tracked; each poll is bounded by a 10s linked-token
    deadline so an unresponsive printer can't stall the loop or hold up
    shutdown. Each poll reads every tracked job in **one** `GetJobs` query and
    compares the printer's report against `Job.Status` in the DB, so the watch
    cannot drift from the job's real status. Per job:
    - reported, state unchanged, or `null` (unmodelled IPP state) → skip
    - reported in-flight while the DB says `Cancelling` → skip; we are waiting
      on the cancel and the printer's "still working" report is stale news
    - reported, state changed → update `Job.Status`, `AuditLog` row for
      `Failed`/`Cancelled`/`Completed`, notify; stop tracking once the reported
      state is no longer `InFlight`
    - **not** reported for `MaxMissedPolls` (3) consecutive answered polls →
      the printer has purged it, so resolve it and stop tracking. `Cancelling`
      resolves to `Cancelled` (the disappearance *is* the cancel confirming —
      many printers drop a cancelled job instead of reporting `canceled`);
      anything else resolves to `Failed`. Only successful responses count as
      misses, so an unreachable printer can never resolve a live job.
  - `PrinterHeartbeat` — per printer. `PeriodicTimer` polls
    `IPrinterDispatcher.GetPrinterStatusAsync` every 30s, writes
    `Printer.Status`/`LastHeartbeat`.
  - `RouteJobs` — reads `Channel<IppJobRef>` (written by `PrintJobWorker`
    after a successful `SendAsync`, and by its startup rehydration sweep),
    hands each ref to the matching printer's `PrinterWatch.AddJob`. A ref for a
    printer with no watch is marked `Failed` rather than dropped — nothing else
    would ever resolve it.
- `Migrations/` — `InitialCreate`, `FixColumnTypos`, `AddPrinterForeignKey`,
  `EnumToStringConversions`, `AddJobData`, `AddFileSizeToJob`,
  `AddPrinterDiscoveryFields`, `AddPrinterStateGranularity`, `AddIppJobIdToJob`.
  Adding a `JobStatus` member needs no migration — enums persist as strings
  with no check constraint.

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
  `GET /PrintJob/{id}`, `GET /PrintJob` (all active), `POST /PrintJob/{id}/cancel`,
  `POST /PrintJob/{id}/retry`. Cancel is a POST, not a DELETE — it does not
  remove the job row.
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
    (`POST /PrintJob/{id}/retry`), `CancelJob(Guid?)`
    (`POST /PrintJob/{id}/cancel`).
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
    Core, so the button set can't drift from what the API will accept —
    including Core's `Submitting`-with-no-`IppJobId` exclusion, which is why
    `QueueRow` carries `IppJobId`.
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
  `IsBusy`/`ErrorText`/`JobId?`/`IppJobId?` for not-yet-submitted files and submitted
  jobs — `FromJob`/`ApplyJob` sync it from a real `Job`). `Status` is
  `JobStatus?`: null means staged, since a staged row is a picked file, not a
  job. `IsStaged` (`JobId is null`) is the discriminator;
  `LabelFor(JobStatus?)` / `StatusLabel` render "Staged" for null, and the
  dashboard/card stat tiles are plain `JobStatus?[]` on the same convention.
  A failed submit leaves the row staged with
  `ErrorText` set and the bytes still local — it is not marked `Failed`,
  because no `Job` exists to have failed. `IsBusy` means an API call for that
  row is outstanding: it drives the row spinner and makes
  `JobWebService.CanDoAction` return false, so a slow request can't be fired
  twice. · `RowActions` enum (`Cancel`/`Send`/`Retry`) · `ActionArgs` (bundles selected row ids + action
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

One known bug still parked for tests to catch (the stale-`FailureReason` one
is fixed):
1. `PrinterService.CreatePrinter` — dup check ORs `p.Host == printer.Host`; EF
   preserves C# null semantics, so two host-less printers read as duplicates.

(Two earlier planted bugs are now fixed as a side effect of the `JobPolicies`
rework: the inverted `IsActive` pattern match, and `DeletePrinter` calling a
predicate method inside an EF query instead of `Active.Contains`.)

## Known problems (real bugs, not scheduled)

Distinct from **Explicitly out of scope** below: those are decisions, these are
defects. Neither is on the **Definition of done** list, so neither blocks
finishing — but neither should be written up as a design choice either.

### 1. `PrinterPoller` never reconciles the printer list

`ExecuteAsync` reads `GetPrinters()` exactly once at startup and builds one
`PrinterWatch` + one `PrinterHeartbeat` per printer. Nothing re-reads it, so
both printer-management actions on the Printers page leave the poller stale:

- **Printer added after startup** — no watch, no heartbeat, so its status never
  updates. Worse, any job dispatched to it reaches `RouteJobs` with no matching
  watch and is marked `Failed`.
- **Printer deleted after startup** — its `PrinterHeartbeat` keeps the stale
  `Printer` snapshot and keeps polling. `HandleStatusUpdate` then calls
  `GetPrinter(printer.Id)`, gets `NotFound`, and logs an error every 30s
  forever, against a printer we no longer manage. This is the one a reviewer
  would notice, because it fills the console.

Three fixes, increasing cost: (a) document "restart the API after adding or
removing a printer" and accept the delete-case log spam; (b) let a watch or
heartbeat retire itself when `GetPrinter` returns `NotFound` — a handful of
lines, kills the spam, leaves the add case needing a restart; (c) a periodic
reconcile loop in `PrinterPoller`, correct for both but real concurrency work
(safe add/remove against a live `Task.WhenAll`, plus disposal ordering).

### 2. Dispatch ignores whether the printer is already busy

`PrintJobWorker` sends every job the moment the channel yields it, with no
regard for what the printer is doing. The HP ENVY Inspire 7200 used for testing
handles exactly one job at a time, so submitting two back to back is wrong for
it: the correct behaviour is send → wait for that job to reach a terminal state
→ send the next, with never more than one job in operation per printer. Other
printers queue internally and would be fine, so this is a per-device property,
not a universal rule.

Note this is **not** the same as the serial-dispatch limitation recorded in the
README. That one is about our own throughput (job N waits for job N-1's upload).
This one is about exceeding what the printer itself will accept.

The awkward part of any fix is the wait. Gating dispatch on "does this printer
have an in-flight job" is easy (`JobPolicies.InFlight` over `PrinterId`); doing
it without a busy-spin is not, since re-enqueuing onto `Channel<Guid>`
immediately just spins. It wants a signal from `PrinterWatch` when a printer
goes idle, or a delayed re-enqueue.

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
2. ~~**Cancel tells the truth.**~~ **Done.** Real IPP `Cancel-Job`, a
   `Cancelling` status the printer has to confirm, `Cancellable` split out of
   `Active`, and a `Submitting`-with-no-IPP-id guard for the send race. Cancel
   moved from `DELETE /PrintJob/{id}` to `POST /PrintJob/{id}/cancel`, and the
   dashboard shows a spinner for any row with an outstanding request.
3. ~~**Stale `FailureReason`.**~~ **Done.** `UpdateJob` assigns
   `FailureReason` on every write instead of only on `Failed`, so a retried job
   no longer carries its old error.
4. **Tests + CI together.** 8–12 tests per **Tests** above, plus a GitHub
   Actions workflow (restore / build / test on push) and a badge in the
   README. No `.github/` exists today. CI is what makes the tests count for a
   reviewer who won't run them.
5. ~~**Persist the IPP job id and rehydrate on startup.**~~ **Done.**
   `Job.IppJobId` is a persisted column (`AddIppJobIdToJob`) and
   `PrintJobWorker.Init` re-routes in-flight jobs through `Channel<IppJobRef>`
   so `PrinterWatch` picks them back up; only jobs with no IPP id are failed. This is the
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
   moving live. Record last, once everything above is final. Toolchain and
   capture notes live in `docs/recording-demo.md`. This is the asset
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
- Concurrent dispatch. `PrintJobWorker` is one serial loop: a job stays
  `Queued` until the one ahead of it has been read out of SQL and pushed over
  IPP, both of which scale with file size. FIFO per instance is what a spooler
  is, and `Queued` is the honest label for that wait.
- A cancel timeout. A printer that accepts `Cancel-Job` but never reports
  `canceled` leaves the job `Cancelling` and pinned in `PrinterWatch._jobs`.
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
