# PrintSpooler — Project Reference

## Goal

Portfolio piece demonstrating C#/ASP.NET Core to employers (author is
self-taught, from warehouse/print-ops work). Product pitch: a
print-spooler-replacement — REST API + Blazor dashboard that accepts print
jobs, queues them, delivers to a real printer via IPP, tracks status, logs
everything.

Not a product — no users, no production intent. Goal is a project that reads
well to a company hiring for full-stack C#, and that the author can defend
every architectural decision on in an interview.

## Architecture

```
Api ─────┬──> Core
         └──> Infrastructure ──> Core
Web ────────> Core (DTOs only, via ApiClient over HTTP — no project ref to Api/Infrastructure)
```

- **Core** — domain models, interfaces, pure rules. No I/O deps except `ErrorOr`.
- **Infrastructure** — EF Core (SQL Server), IPP printing (SharpIppNext), mDNS
  discovery (Zeroconf), background workers.
- **Api** — controllers, SignalR hub, DI wiring.
- **Web** — Blazor Server dashboard, talks to Api over HTTP + SignalR.

Job flow in one line: dashboard submits → `JobService` persists + enqueues →
background worker dispatches over IPP → a per-printer monitor polls job/printer
state → SignalR pushes updates back to the dashboard.

Real hardware verified end-to-end against an HP ENVY Inspire 7200 (direct IPP,
no CUPS).

Database: SQL Server in Docker (`docker-compose.yml`, db service only). The
API runs on the host because every printer call needs mDNS (Zeroconf
discovery, plus resolving the saved `.local` host for IPP send/poll), and
multicast doesn't cross Docker's bridge network. Connection string lives in user-secrets
(`ConnectionStrings:PrintSpoolerDb`, `Server=localhost`, real password — .NET
never reads `.env`, only compose does). Start: `docker compose up -d --wait &&
dotnet run --project PrintSpooler.Api`. Startup `Migrate()` is Development-only
on purpose.

## Explicitly out of scope (decisions, not gaps)

- File transcoding — only printer-native content types print.
- A fake `IPrinterDispatcher` for demoing without hardware — rejected; faking
  the one part that talks to real hardware defeats the point of the project.
- Cross-printer dispatch ordering — each printer serializes its own queue independently; printers don't wait on each other.
- Printer failover — no partial fields for a feature that isn't being built.
- Cloud deployment.
- DB retention/purge for terminal jobs.

## Tests

Being rebuilt as a deliberate learning exercise: **the author writes every
line, do not write test code for them.** Mode: ask a guiding question, let
them attempt, correct with reasoning.

- `tests/PrintSpooler.Api.IntegrationTests` — xUnit v3, real app via
  `WebApplicationFactory`, real SQL Server via Testcontainers (needs Docker;
  one container per test class fixture, which is most of the ~10s runtime).

Non-obvious test seams:
- `RecordingPrinterMonitorFactory` decorates the real factory (swapped in via
  `ConfigureTestServices`) so tests can see which monitors `PrinterManager`
  created — the manager's own collection stays private on purpose.
- `PrinterMonitor.IsDisposed` exists for tests to observe the remove path.
- `CustomWebApplicationFactory.SeedAsync` runs after migrate, before the app
  starts — subclass it to test startup state (see
  `SeededPrinterWebApplicationFactory`).
- Proving something *didn't* happen on the printer channel: trigger a later
  event and wait for it — events are handled in order, so anything earlier is
  already processed.

Bar for a test worth writing: don't restate the implementation, don't
hardcode a value that's arbitrary internal policy (write it once, in the
policy array), prefer cases whose answer you can't predict by reading one
function (EF query translation, HTTP status mapping, crash recovery).

## Working style / how to help this person

- Prefers being taught, not handed finished code — ask guiding questions for
  new-to-them concepts (ASP.NET/EF/DI), let them attempt it, correct
  with explanation. "Can you explain why" is the real success criterion, not
  just "does it compile."
- Direct/fast answers are fine for pure syntax/tooling issues (typo, missing
  semicolon, CLI flag) — that's not a teaching moment.
- Strong already in C#/LINQ/general fundamentals, Neovim, git, Arch Linux,
  Docker, Sybase ASE SQL. Newer to ASP.NET Core, EF Core, DI, REST API
  design, multi-project .NET architecture, IPP. Auth and deployment are
  untested territory.
- Commit messages: the author writes them, practicing. Don't draft one —
  check their draft against `git diff --staged` and point out anything it
  misses, overstates, or describes that didn't change.
- Always use caveman mode (compressed, low-filler text) for prose responses,
  even mid-Socratic-teaching — code/commits/PRs/security warnings still get
  written out normally.

## Claude.md maintenance

This file is a map of the project's goal and non-obvious context, not a
changelog or a class catalog — that's what the code and `git log` are for.
Before trusting anything here as current, check it against the actual code;
update or cut a note the moment it goes stale rather than let it accumulate.
