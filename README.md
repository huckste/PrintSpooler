# PrintSpooler

A practice project, not a product. No users, no production intent, just a resume piece meant to exercise the full .NET stack: API, background workers, Azure SQL, live updates. Printing was the domain because it forces the interesting problems (async queue, background work, status polling, external hardware).

Submit a job from a dashboard → queued → delivered to a real network printer over IPP → status tracked → audit logged.

![PrintSpooler demo](docs/hero.gif)

## What it does

A print job comes in from the Blazor dashboard, gets written to the database, and is handed off to a background queue. From there it's dispatched to a real network printer over IPP (no CUPS, no print-server in between). Its status gets polled directly off the device until it reaches a real terminal state: printing, completed, failed, whatever the printer actually reports. Every step is audit-logged, and status changes push back to the dashboard live over SignalR instead of the browser polling for them.

## Architecture

Four projects, one dependency direction. **Api** and **Infrastructure** both depend on **Core**, but Core depends on nothing: no EF Core, no ASP.NET, just domain models, enums, and service interfaces. **Web** only depends on Core for its DTOs and talks to Api over plain HTTP plus SignalR; it has no project reference to Api or Infrastructure at all.

| Project | Role |
|---|---|
| **Core** | Domain models, enums, service interfaces. No I/O. |
| **Infrastructure** | EF Core (SQL Server), IPP (SharpIppNext), mDNS (Zeroconf), background workers. |
| **Api** | Web API: controllers, SignalR hub, DI. Uses Infrastructure via Core interfaces. |
| **Web** | Blazor Server dashboard; `ApiClient` over `HttpClient` + SignalR. |

Keeping Core this empty means swapping the printer technology, or the database, only touches Infrastructure. Core's interfaces don't change, so nothing depending on them has to either.

### Job flow

Sending a job from the dashboard is a `POST /PrintJob` that writes the job, its data, and an audit log entry, then queues a reference and returns immediately. The actual printing happens in the background. Each printer has its own dedicated monitor with a private queue: it dispatches one job at a time, waits for that job to reach a real terminal state on the device before pulling the next one, and reports status changes back over SignalR as they happen. A printer can never end up with two jobs in flight at once, but a slow job on one printer never blocks any other printer, since each one runs its own independent loop.

## Design notes

Core doesn't reference EF Core or ASP.NET at all, only `ErrorOr` for modeling expected failures. Interfaces live there; Infrastructure implements them, so swapping the database or the print protocol never touches Core.

Each printer runs its own monitor with its own queue instead of one worker trying to serve all of them. That's what keeps a stuck or slow printer from holding up jobs meant for a different device.

There's no fake dispatcher for demoing without hardware, even though it would have made the project runnable anywhere. The whole point was proving this talks to a real printer over IPP, and faking that part would have missed it entirely.

A printer failover field got cut mid-build once that feature wasn't going to happen. Leaving it in would have meant a column nothing reads.

## Known limitations

Only IPP is supported right now. CUPS, raw/LPR printing, and driver-based printers are all out. The dispatch loop also assumes a printer has no internal queue of its own; it's only been run against one real device that fits that shape, so anything printer-side that queues its own jobs is untested territory. Failure detection after three missed polls is a heuristic guess, not a confirmed signal from the device. And retries aren't automatic; a failed job sits there until someone resends it.

## Stack

Built on .NET 10 and C# 13: ASP.NET Core Web API with DI, health checks, and OpenAPI; EF Core 10 against Azure SQL (migrations, connection string in `user-secrets`); SignalR and Blazor Server for the live dashboard; SharpIppNext for IPP, Zeroconf for mDNS discovery, and ErrorOr for result handling.

## Run

```bash
conn string via dotnet user-secrets (Key:.ConnectionStrings:PrintSpoolerDb),
then:

dotnet run --project PrintSpooler.Api
dotnet run --project PrintSpooler.Web   # set ApiBaseAddress to the Api's URL
```

Print needs a real network printer on the API's LAN. Everything else (UI, queue, logs, SignalR) works without one; the job just lands as `Failed`.

## Layout

```
Core/          models, interfaces, enums
Infrastructure/ DbContext + migrations, workers, IPP, mDNS
Api/           controllers, SignalR hub, DTOs, DI
Web/           Blazor pages (Dashboard, Printers, Logs), ApiClient
```

## Out of scope (deliberately)

Auth, deployment, file transcoding, multi-printer batch UI, tests.
