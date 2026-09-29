# GIIM: IT Joiner/Mover/Leaver and Asset Platform

Internal tool for the IT Service Desk. It links ServiceDesk Plus tickets to:

- **Onboarding checklists** generated from the new starter's department profile
- **Offboarding checklists** generated from what the leaver actually holds
- **A hardware and app register** with a tracked lifecycle for every device

It integrates with ServiceDesk Plus Cloud (AU), Okta, on-prem Active Directory, hybrid Exchange and Microsoft Intune.

> Status: **scaffold**. Structure, domain model and test data only. No integrations are live yet.
> See [docs/roadmap.md](docs/roadmap.md) for the build plan, [docs/architecture.md](docs/architecture.md) for the design, and
> [docs/lifecycle-gap-analysis.md](docs/lifecycle-gap-analysis.md) for how the asset lifecycle brief maps onto GIIM.

## Repository layout

| Path | What it is |
|---|---|
| `src/Giim.Domain` | Business entities and rules (asset lifecycle, checklist generation). No external dependencies |
| `src/Giim.Infrastructure` | EF Core `GiimDbContext`, migrations, Azure SQL |
| `src/Giim.Connectors` | Contracts for ServiceDesk Plus, Intune (Graph) and Okta |
| `src/Giim.Api` | ASP.NET Core API used by the web UI and SDP webhooks |
| `src/Giim.Workers` | Background sync and automation jobs (Intune sync) |
| `src/Giim.Web` | React + TypeScript UI (Vite) |
| `tests/` | xUnit tests |
| `tools/SampleData` | Fake test-data generator |
| `samples/` | Generated fake data. **Real exports go in `samples/private/` (git-ignored)** |
| `infra/` | Azure infrastructure (Bicep) and deployment runbook: see [infra/README.md](infra/README.md) |
| `.github/workflows/` | CI (build, tests, lint, templates) and deployment to test and prod |

## Prerequisites

- .NET SDK 10.0
- Node.js 24+
- Docker Desktop (WSL 2 backend) for the local SQL Server

## Common commands

```bash
docker compose up -d                   # start local SQL Server (localhost,1433)

dotnet build Giim.slnx                 # build everything
dotnet test Giim.slnx                  # run tests

dotnet tool restore                    # installs dotnet-ef (local tool)
dotnet ef database update --project src/Giim.Infrastructure   # create/upgrade the local DB

dotnet run --project src/Giim.Api      # API on http://localhost:5080
dotnet run --project src/Giim.Workers  # scheduled Intune sync (optional locally), health on http://localhost:5081
cd src/Giim.Web && npm install && npm run dev                 # UI on http://localhost:5173

dotnet run tools/SampleData/generate-sample-data.cs           # regenerate fake test data
```

## Key settings

| Setting | Purpose |
|---|---|
| `ConnectionStrings:Giim` | SQL Server connection (local Docker in development) |
| `Giim:PublicBaseUrl` | The address staff use to open GIIM, e.g. `https://giim.company.com.au`. **QR labels link here**, so set it before printing labels; a label printed with the wrong address has to be reprinted. Development: `http://localhost:5173` |
| `Auth:Mode` | `Okta` in production; `Development` (pick-a-role sign-in) only on a developer PC. See [docs/okta-setup.md](docs/okta-setup.md) |
| `Intune:Source` | `File` (sample export) or `Graph` (live), see [docs/intune-app-registration.md](docs/intune-app-registration.md) |
| `People:FilePath` | Staff directory export used by People sync until the Active Directory source exists |
| `DataProtection:BlobUri`, `DataProtection:KeyUri` | Where the sign-in cookie keys are kept in Azure (Blob Storage, wrapped by a Key Vault key) so every instance shares them. Set by the deployment; leave empty locally |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Sends logs and telemetry to Application Insights. Set by the deployment; leave empty locally |

Health checks: `/health/live` (the app is running) and `/health` (also reaches the database).

## Ground rules

- **Secrets never go in appsettings or git.** Locally use `dotnet user-secrets`; in Azure use Key Vault + Managed Identity.
  The only exception is the throwaway SA password for the local Docker SQL container, which works only on your own machine.
- **No real staff data in the repo.** Use `samples/private/` for real exports.
- Every automated action that disables, removes or wipes something requires a named approver and is written to the audit log.
