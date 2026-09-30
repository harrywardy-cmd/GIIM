# GIIM: IT Joiner/Mover/Leaver and Asset Platform

Internal tool for the IT Service Desk. It links ServiceDesk Plus tickets to:

- **Onboarding checklists** generated from the new starter's department profile
- **Offboarding checklists** generated from what the leaver actually holds
- **A hardware and app register** with a tracked lifecycle for every device

It integrates with ServiceDesk Plus Cloud (AU), Microsoft Entra ID (staff sign in from My Apps), on-prem Active Directory,
hybrid Exchange and Microsoft Intune.

> Status: **scaffold**. Structure, domain model and test data only. No integrations are live yet.
> See [docs/roadmap.md](docs/roadmap.md) for the build plan, [docs/architecture.md](docs/architecture.md) for the design, and
> [docs/lifecycle-gap-analysis.md](docs/lifecycle-gap-analysis.md) for how the asset lifecycle brief maps onto GIIM.

## Tech stack

| Area | Technology |
|---|---|
| **Backend** | .NET 10 (C#), ASP.NET Core minimal APIs (`Giim.Api`), a hosted background worker (`Giim.Workers`) |
| **Data** | SQL Server (Docker locally, Azure SQL in Azure), Entity Framework Core 10 with code-first migrations |
| **Web UI** | React 19, TypeScript 6, Vite 8, lucide-react icons, oxlint. Served by the API as static files in production |
| **Sign-in and roles** | Microsoft Entra ID (OpenID Connect, authorization code + PKCE) handled by the API, launched from the GIIM tile in My Apps; HTTP-only session cookie; roles from Entra app roles; no client secret in Azure (managed identity as a federated credential) |
| **Integrations** | Microsoft Graph via Azure.Identity: Intune device sync, and email from a Microsoft 365 mailbox. ServiceDesk Plus Cloud and the on-premises AD/Exchange agent are planned (see the roadmap) |
| **Documents and exports** | ClosedXML (Excel import and export), CSV export, QRCoder (asset QR codes and label sheets) |
| **Hosting** | Azure, Australia East: App Service (Linux), Azure SQL, Key Vault, Blob Storage, private networking, all accessed with managed identities |
| **Infrastructure as code** | Bicep templates and PowerShell scripts in `infra/` |
| **CI/CD** | GitHub Actions: build, test and lint on every change; gated deployment to test, then production |
| **Monitoring** | Application Insights and Log Analytics through OpenTelemetry (Azure Monitor), plus alert rules |
| **Testing** | xUnit, ASP.NET Core `WebApplicationFactory` for API tests; all tests run without a database |
| **Code quality** | .NET analysers at `latest-recommended` with warnings treated as errors; oxlint and the TypeScript compiler for the UI |

## Repository layout

| Path | What it is |
|---|---|
| `src/Giim.Domain` | Business entities and rules (asset lifecycle, checklist generation). No external dependencies |
| `src/Giim.Infrastructure` | EF Core `GiimDbContext`, migrations, Azure SQL |
| `src/Giim.Connectors` | Integrations: Intune and email (Microsoft Graph), the staff directory, and contracts for ServiceDesk Plus |
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
| `Auth:Mode`, `Auth:Entra:*` | `Entra` (Microsoft sign-in) everywhere except a developer PC, where `Development` gives a pick-a-role sign-in. See [docs/entra-setup.md](docs/entra-setup.md) |
| `Intune:Source` | `File` (sample export) or `Graph` (live), see [docs/intune-app-registration.md](docs/intune-app-registration.md) |
| `People:FilePath` | Staff directory export used by People sync until the Active Directory source exists |
| `DataProtection:BlobUri`, `DataProtection:KeyUri` | Where the sign-in cookie keys are kept in Azure (Blob Storage, wrapped by a Key Vault key) so every instance shares them. Set by the deployment; leave empty locally |
| `ServiceDesk:*` | ServiceDesk Plus connection: `Mode` (`None`, `File` stand-in, `Api`), Zoho client, webhook secret; ticket field mapping in `config/servicedesk.json`. See [docs/servicedesk-setup.md](docs/servicedesk-setup.md) |
| `Email:Mode`, `Email:FromMailbox` | How the workers send request emails: `File` (written to `artifacts/mail` on a developer PC), `Graph` (Microsoft 365, in Azure) or `None`. See [docs/email-notifications.md](docs/email-notifications.md) |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Sends logs and telemetry to Application Insights. Set by the deployment; leave empty locally |

Health checks: `/health/live` (the app is running) and `/health` (also reaches the database).

## Ground rules

- **Secrets never go in appsettings or git.** Locally use `dotnet user-secrets`; in Azure use Key Vault + Managed Identity.
  The only exception is the throwaway SA password for the local Docker SQL container, which works only on your own machine.
- **No real staff data in the repo.** Use `samples/private/` for real exports.
- Every automated action that disables, removes or wipes something requires a named approver and is written to the audit log.
