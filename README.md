# PrintSpooler

A practice project, not a product — no users, no production intent. A resume piece exercising the full .NET stack: API, background workers, Azure SQL, live updates. Printing was the domain because it forces the interesting problems (async queue, background work, status polling, external hardware).

Submit a job from a dashboard → queued → delivered to a real network printer over IPP → status tracked → audit logged.

![PrintSpooler demo](docs/hero.gif)

## What it does

A print job comes in from the Blazor dashboard, gets written to the database, and is handed off to a background queue. From there it's dispatched to a real network printer over IPP (no CUPS, no print-server in between), and its status is polled directly off the device until it reaches a real terminal state — printing, completed, failed, whatever the printer actually reports. Every step is audit-logged, and status changes push back to the dashboard live over SignalR instead of the browser polling for them.

## Architecture

Four projects, one dependency direction: **Api** and **Infrastructure** both depend on **Core**, but Core depends on nothing — no EF Core, no ASP.NET, just domain models, enums, and service interfaces. **Web** only depends on Core for its DTOs and talks to Api over plain HTTP plus SignalR; it has no project reference to Api or Infrastructure at all.

| Project | Role |
|---|---|
| **Core** | Domain models, enums, service interfaces. No I/O. |
| **Infrastructure** | EF Core (SQL Server), IPP (SharpIppNext), mDNS (Zeroconf), background workers. |
| **Api** | Web API: controllers, SignalR hub, DI. Uses Infrastructure via Core interfaces. |
| **Web** | Blazor Server dashboard; `ApiClient` over `HttpClient` + SignalR. |

Keeping Core this empty means swapping the printer technology, or the database, only touches Infrastructure — Core's interfaces don't change, so nothing depending on them has to either.

### Job flow

Sending a job from the dashboard is a `POST /PrintJob` that writes the job, its data, and an audit log entry, then queues a reference and returns immediately — the actual printing happens in the background. Each printer has its own dedicated monitor with a private queue: it dispatches one job at a time, waits for that job to reach a real terminal state on the device before pulling the next one, and reports status changes back over SignalR as they happen. A printer can never end up with two jobs in flight at once, but a slow job on one printer never blocks any other printer — each one runs its own independent loop.

## A few design choices worth calling out

- **Core has no I/O dependencies.** It only takes on `ErrorOr` (for representing expected failures without exceptions) — not EF Core, not ASP.NET. Interfaces live in Core; Infrastructure implements them. That's what lets Infrastructure change without Core, or anything depending on Core, having to change too.
- **Dispatch is per-printer, not global.** Instead of one worker racing to serve every printer, each printer gets its own monitor and its own queue, so printers can't interfere with each other and one slow device doesn't stall the rest.
- **No fake printer dispatcher for demoing without hardware.** It would have been easy to add one, but faking the one part of the project that talks to real hardware would have defeated the point — status here is genuinely polled off a real network printer over IPP, not simulated.
- **Unused fields got deleted, not left in.** A printer-failover field existed early on and was removed once that feature wasn't going to be built, rather than left in the model unused.

## Known limitations

This is a demo, so these are noted rather than fixed:

- **IPP only.** No CUPS, no raw/LPR printing, no driver-based printers — anything that doesn't speak IPP directly isn't supported.
- **Tested against a single real printer** with no internal queue of its own. The per-printer dispatch loop assumes the device just accepts and works one job at a time; behavior against a printer that queues jobs itself hasn't been verified.
- **Missed-poll failure detection is a heuristic** — three consecutive unresponsive polls marks a job Failed, not an explicit failure signal from the device.
- **No automatic retry.** A failed job stays failed; resubmitting is manual.

## Stack

- .NET 10 / C# 13
- ASP.NET Core Web API, DI, health checks, OpenAPI
- EF Core 10 + Azure SQL (migrations, conn string in `user-secrets`)
- SignalR, Blazor Server
- SharpIppNext (IPP), Zeroconf (mDNS), ErrorOr

## Run

```bash
conn string via dotnet user-secrets (Key:.ConnectionStrings:PrintSpoolerDb),
then:

dotnet run --project PrintSpooler.Api
dotnet run --project PrintSpooler.Web   # set ApiBaseAddress to the Api's URL
```

Print needs a real network printer on the API's LAN. Everything else (UI, queue, logs, SignalR) works without one — the job just lands as `Failed`.

## Layout

```
Core/          models, interfaces, enums
Infrastructure/ DbContext + migrations, workers, IPP, mDNS
Api/           controllers, SignalR hub, DTOs, DI
Web/           Blazor pages (Dashboard, Printers, Logs), ApiClient
```

## Out of scope (deliberately)

Auth, deployment, file transcoding, multi-printer batch UI, tests.
