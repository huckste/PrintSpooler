# PrintSpooler — Project Reference

## Goal

Portfolio piece demonstrating C#/ASP.NET Core/Azure to employers (author left
Hachette Book Group warehouse/print-ops work, self-taught into programming —
prior tools: PrintFlow, PrintFlow_v2, LabelGen). Product pitch: a
print-spooler-replacement — REST API + Blazor dashboard that accepts print
jobs, queues them, delivers to a real printer via IPP, tracks status, logs
everything. Genericized, not tied to any employer's systems. Built in ~1 week,
pushed to GitHub via `gh`.

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
no CUPS). Azure resources are real, not simulated (region pinned to
**centralus** — free-trial subscription blocks SQL provisioning elsewhere).

## Explicitly out of scope (decisions, not gaps)

- File transcoding — only printer-native content types print.
- A fake `IPrinterDispatcher` for demoing without hardware — rejected; faking
  the one part that talks to real hardware defeats the point of the project.
- Cross-printer dispatch ordering — each printer serializes its own queue independently; printers don't wait on each other.
- Printer failover — `FailoverPrinterId` was removed rather than left unused; no partial field for a feature that isn't being built.
- Deployment to Azure App Service — optional stretch only.
- DB retention/purge for terminal jobs.

## Tests

No test project. A first attempt (xunit + FluentAssertions) was deleted —
its tests enumerated the policy arrays by hand and just restated the guard
bodies. Rebuilding is a deliberate learning exercise: **the author writes
every line, do not write test code for them.** Mode: ask a guiding question,
let them attempt, correct with reasoning.

Bar for a test worth writing: don't restate the implementation, don't
hardcode a value that's arbitrary internal policy (write it once, in the
policy array), prefer cases whose answer you can't predict by reading one
function (EF query translation, HTTP status mapping, crash recovery).

## Working style / how to help this person

- Prefers being taught, not handed finished code — ask guiding questions for
  new-to-them concepts (ASP.NET/EF/Azure/DI), let them attempt it, correct
  with explanation. "Can you explain why" is the real success criterion, not
  just "does it compile."
- Direct/fast answers are fine for pure syntax/tooling issues (typo, missing
  semicolon, CLI flag) — that's not a teaching moment.
- Explain *why*, not just *what* (e.g. why Core can't reference EF Core but
  can reference ErrorOr, why Scoped vs Singleton for a given class).
- Strong already in C#/LINQ/general fundamentals, Neovim, git, Arch Linux,
  Docker, Sybase ASE SQL. Newer to ASP.NET Core, EF Core, DI, Azure, REST API
  design, multi-project .NET architecture, IPP. Auth and deployment are
  untested territory.
- Always use caveman mode (compressed, low-filler text) for prose responses,
  even mid-Socratic-teaching — code/commits/PRs/security warnings still get
  written out normally.

## Claude.md maintenance

This file is a map of the project's goal and non-obvious context, not a
changelog or a class catalog — that's what the code and `git log` are for.
Before trusting anything here as current, check it against the actual code;
update or cut a note the moment it goes stale rather than let it accumulate.
