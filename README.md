# GIIM: IT Joiner/Mover/Leaver and Asset Platform

Internal tool for the IT Service Desk. It links ServiceDesk Plus tickets to:

- **Onboarding checklists** generated from the new starter's department profile
- **Offboarding checklists** generated from what the leaver actually holds
- **A hardware and app register** with a tracked lifecycle for every device

It integrates with ServiceDesk Plus Cloud (AU), Okta, on-prem Active Directory, hybrid Exchange and Microsoft Intune.

> Status: **scaffold**. Structure, domain model and test data only. No integrations are live yet.
> See [docs/roadmap.md](docs/roadmap.md) for the build plan and [docs/architecture.md](docs/architecture.md) for the design.

## Repository layout

| Path | What it is |
|---|---|
| `src/Giim.Domain` | Business entities and rules (asset lifecycle, checklist generation). No external dependencies |
| `src/Giim.Infrastructure` | EF Core `GiimDbContext`, migrations, Azure SQL |
| `src/Giim.Connectors` | Contracts for ServiceDesk Plus, Intune (Graph) and Okta |
| `src/Giim.Api` | ASP.NET Core API used by the web UI and SDP webhooks |
| `src/Giim.Workers` | Background sync and automation jobs |
| `src/Giim.Web` | React + TypeScript UI (Vite) |
| `tests/` | xUnit tests |
| `tools/SampleData` | Fake test-data generator |
| `samples/` | Generated fake data. **Real exports go in `samples/private/` (git-ignored)** |
| `infra/` | Azure infrastructure (to be added in Phase 1) |

## Prerequisites

- .NET SDK 10.0
- Node.js 24+
- SQL Server for local dev: LocalDB (installed with Visual Studio), SQL Server Developer Edition, or a Docker container

## Common commands

```bash
dotnet build Giim.slnx                 # build everything
dotnet test Giim.slnx                  # run tests

dotnet tool restore                    # installs dotnet-ef (local tool)
dotnet ef database update --project src/Giim.Infrastructure   # create/upgrade the local DB

dotnet run --project src/Giim.Api      # API on http://localhost:5080
cd src/Giim.Web && npm install && npm run dev                 # UI on http://localhost:5173

dotnet run tools/SampleData/generate-sample-data.cs           # regenerate fake test data
```

## Ground rules

- **Secrets never go in appsettings or git.** Locally use `dotnet user-secrets`; in Azure use Key Vault + Managed Identity.
- **No real staff data in the repo.** Use `samples/private/` for real exports.
- Every automated action that disables, removes or wipes something requires a named approver and is written to the audit log.
