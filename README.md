# PrintSpooler

A practice project, not a product — no users, no production intent. A resume piece exercising the full .NET stack: API, background workers, Azure SQL, live updates. Printing was the domain because it forces the interesting problems (async queue, background work, status polling, external hardware).

Submit a job from a dashboard → queued → delivered to a real network printer over IPP → status tracked → audit logged.

![PrintSpooler demo](docs/hero.gif)

## Architecture

Four projects, one dependency direction:

```
Api ──┐
      ├──> Core          # domain models + interfaces, no I/O
Infra ┘

Web ──> Core             # DTOs only; talks to Api over HTTP
```

| Project | Role |
|---|---|
| **Core** | Domain models, enums, service interfaces. No I/O. |
| **Infrastructure** | EF Core (SQL Server), IPP (SharpIppNext), mDNS (Zeroconf), background workers. |
| **Api** | Web API: controllers, SignalR hub, DI. Uses Infrastructure via Core interfaces. |
| **Web** | Blazor Server dashboard; `ApiClient` over `HttpClient` + SignalR. |

Core doesn't know EF Core exists; Infrastructure doesn't know ASP.NET exists. Swapping the printer tech touches Infrastructure only.

### Job flow

```
Dashboard: Send → POST /PrintJob
  → JobService: write Job + JobData + AuditLog
  → enqueue (JobId, PrinterId) into Channel<JobRef>
  → PrinterManager routes to that printer's PrinterMonitor
      → PrinterMonitor dispatches from its own per-printer queue
          ├─ success → track the job, wait for it to reach a real terminal
          │            state on the device before sending this printer's next
          └─ failure → mark Failed (retry is manual)
      → IJobNotifier → SignalR broadcast → Dashboard patches its cache
```

The HTTP request returns when the job is queued; the print happens in the background and status arrives over SignalR.

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

Dispatch is serial per printer, concurrent across printers. Each printer's
`PrinterMonitor` owns a private queue and won't hand off its next job until
the current one reaches a real terminal state on the device — not just
"accepted" — so a printer with no internal queue of its own can never have
two jobs in flight at once. A slow job on one printer never blocks another
printer's queue.
