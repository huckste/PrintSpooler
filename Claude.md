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
- Still shaky / hasn't been tested on: Blazor (not started), auth (not
  started), any deployment step (App Service, CI/CD).
- Now understands: `BackgroundService`/`IHostedService`, `Channel<T>` as a
  producer/consumer queue, `IServiceScopeFactory` for creating DB scopes inside
  a Singleton, DI lifetime rules (Scoped can depend on Singleton, not vice versa),
  `await foreach` with `ReadAllAsync` for blocking channel consumption.

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
7. `POST /Printer`, `GET /Printer/{id}` — full CRUD for printer registration
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

## What's NOT built yet (planned, in likely order)

1. ~~Wire `JobCancellationPolicy` into `DELETE /PrintJob/{id}`~~ — DONE
   - Future: IPP `Cancel-Job` for in-progress jobs (needs printer-assigned job ID stored on `Job`)
2. ~~`AuditLog`~~ — DONE. Writes on: `JobCreated` (JobService), `JobCancelled`
   (JobService), `JobCompleted`/`JobFailed` (PrintJobWorker). Enums `JobAction`
   and `ByWho` stored as strings via `HasConversion<string>()` in AppDbContext.
   Printer creation deliberately not audited — audit log scoped to job lifecycle only.
3. Blazor dashboard — project scaffolded only: queue view, submit form,
   printer status board, audit log view — none built
4. Auth (JWT + roles) — discussed early on, then explicitly descoped in favor
   of getting the core pipeline working first; revisit if time allows
5. Deployment — Azure App Service, App Insights, Key Vault for secrets in
   production (currently using User Secrets, which is dev-only) — not
   started
6. Tests — no test project exists yet; this was a stated goal (wanting to
   learn "the proper industry way" to isolate and test things, motivated by
   PrintFlow_v2 having no test setup at all)

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
