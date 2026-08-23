# Timber Yard Production & Inventory Management System

A standalone, microservice-based system for tracking timber through its full production lifecycle — from raw log intake, through sawmill processing, to chemical treatment — built alongside (not replacing) the client's existing Point-of-Sale system.

---

## Table of Contents

- [Business Context](#business-context)
- [Solution & Scope](#solution--scope)
- [System Architecture](#system-architecture)
- [Tech Stack](#tech-stack)
- [Repository Structure](#repository-structure)
- [Services](#services)
- [User Roles & Access Control](#user-roles--access-control)
- [Getting Started](#getting-started)
- [CI/CD Pipeline](#cicd-pipeline)
- [Testing Strategy](#testing-strategy)
- [Team](#team)
- [Role Rotation](#role-rotation)

---

## Business Context

The client is a timberyard business supplying timber in three forms: **raw logs**, **sawn timber**, and **treated timber**. They already run a POS system that handles customer sales and checkout — the point at which finished timber leaves the yard.

**What's missing today:**
- No visibility into current stock across raw, sawn, and treated timber
- No structured tracking of sawmill jobs, treatment batches, or wastage
- Stock discrepancies (damage, loss, miscounts) have no proper record or audit trail
- No role-based control over who can create, view, or delete records
- The POS only records what was *sold* — not how stock got there in the first place

## Solution & Scope

A standalone **Timber Yard Production & Inventory Management System**, separate from the POS.

**In scope:**
- Structured logging of deliveries, saw jobs, and treatment batches
- Stock, wastage, and duration reports per production stage
- Role-based access control (Supervisor / Manager / Admin)
- Full audit trail for every stock change

**Out of scope:**
- Sales / checkout (remains fully handled by the existing POS)
- Customer payment processing
- POS integration (noted as future scope)

## System Architecture

```
                     ┌─────────────────┐
                     │  React Frontend  │
                     └────────┬─────────┘
                              │
                     ┌────────▼─────────┐
                     │   API Gateway     │
                     │  (verifies token) │
                     └────────┬─────────┘
                              │
      ┌───────────┬───────────┼───────────┬───────────┐
      │           │           │           │
┌─────▼─────┐┌────▼─────┐┌────▼─────┐┌────▼──────┐
│Auth Service││Log Intake││ Sawmill  ││ Treatment │
│            ││ Service  ││ Service  ││ Service   │
└─────┬─────┘└────┬─────┘└────┬─────┘└────┬──────┘
      │           │           │           │
┌─────▼─────┐┌────▼─────┐┌────▼─────┐┌────▼──────┐
│ MySQL DB  ││ MySQL DB ││ MySQL DB ││ MySQL DB  │
└───────────┘└──────────┘└──────────┘└───────────┘
```

The **Auth Service** issues and verifies role-based JWT tokens (Supervisor / Manager / Admin) consumed by the other three services. Each production service (Log Intake, Sawmill, Treatment) owns its own stock, low-stock alerts, movement log, and report — fully independent, database-per-service.

## Tech Stack

| Layer | Technology | Notes |
|---|---|---|
| Frontend | React.js | One shared app; each service's page owned by its developer |
| Backend | ASP.NET (Web API) + ADO.NET | 4 independent Web API projects, direct SQL access |
| Database | MySQL | Database-per-service — 4 separate schemas |
| Version Control | GitHub | Single repo, folder per service + shared frontend |
| Deployment | Azure App Service | Each microservice deployed as its own App Service instance |
| Testing | Selenium · JMeter · xUnit | E2E, load/performance, and business-logic coverage |
| Monitoring | Apache Kafka | Centralized logging & observability across services |
| Auth | JWT | Issued by Auth Service, verified at the API Gateway |

## Repository Structure

```
/
├── frontend/                  # Shared React app
├── AuthService/                # ASP.NET Web API — users, login, roles
├── LogIntakeService/           # ASP.NET Web API — raw log deliveries & stock
├── SawmillService/             # ASP.NET Web API — saw jobs, wastage
├── TreatmentService/           # ASP.NET Web API — treatment batches
├── .github/
│   └── workflows/              # CI/CD pipeline definitions
└── README.md
```

## Services

| Service | Owner | Responsibility |
|---|---|---|
| **Auth Service** | IT24103469 | User accounts, secure login, role-based access control for all other services |
| **Log Intake Service** | IT24103435 | Raw log deliveries, supplier tracking, raw stock adjustments |
| **Sawmill Service** | IT24610782 | Saw jobs, sawn stock output, wastage tracking |
| **Treatment Service** | IT24103438 | Treatment batches, treated stock, turnaround tracking |

Each service owns its own database schema and exposes its own REST endpoints, secured by the JWT issued from the Auth Service and enforced via ASP.NET's `[Authorize(Roles="...")]` attribute.

## User Roles & Access Control

| Role | Permissions |
|---|---|
| **Supervisor** | Create & Update — records deliveries, starts/completes saw jobs & treatment batches, performs manual stock adjustments |
| **Manager** | Everything Supervisor can do, plus Read (reports & logs) — views stock, reports, and movement logs across all services |
| **Admin** | Everything Manager can do, plus Delete & User Management — cancels/deletes records and manages employee accounts via the Auth Service |

Every write action across Log Intake, Sawmill, and Treatment is restricted by role via the Auth Service's token.

## Getting Started

> ⚠️ Prerequisites: .NET SDK, Node.js, MySQL Server, Azure CLI (for deployment)

```bash
# Clone the repo
git clone <repo-url>
cd <repo-name>

# Backend — run any service (example: Auth Service)
cd AuthService
dotnet restore
dotnet run

# Frontend
cd frontend
npm install
npm start
```

Each service connects to its own MySQL schema — set connection strings via `appsettings.Development.json` locally, and via GitHub Actions Secrets / Azure App Settings in CI/CD.

## Infrastructure & Deployment

All shared infrastructure runs on Azure, under a single resource group. This section is the reference for anyone doing DevOps work in future sprints.

### Azure Resources (resource group: `timber-yard-rg`, region: Southeast Asia)

| Resource | Name | Purpose |
|---|---|---|
| App Service Plan | `timber-yard-plan` | Linux, Free (F1) tier — hosts all 4 microservice App Services |
| MySQL Flexible Server | `timber-yard-mysql` | Burstable B1ms — one server, 4 separate databases |
| MySQL Databases | `auth_db`, `sawmill_db`, `treatment_db`, `log_intake_db` | One per service |
| Container Registry | `timberyardacr` | Private registry — hosts the Kafka image (avoids Docker Hub rate limits) |
| Container Instance | `timber-yard-kafka` | Kafka broker, public endpoint: `timber-yard-kafka.southeastasia.azurecontainer.io:9092` |
| Application Insights | `timber-yard-insights` | Centralized logging/telemetry for all services |
| App Services (one per microservice) | `timber-yard-auth-service`, `timber-yard-sawmill-service`, `timber-yard-treatment-service`, `timber-yard-log-intake-service` | Each deployed independently via its own GitHub Actions workflow |

### Shared secrets

The following values are shared across all 4 services and must stay identical. **Ask the outgoing DevOps person for the current values** — do not regenerate them, as that will break already-deployed services:

- `Jwt__Secret` — shared JWT signing key (used by Auth Service to issue tokens, and by all others to validate them)
- MySQL admin username/password — for `timber-yard-mysql`
- `APPLICATIONINSIGHTS_CONNECTION_STRING` — from `timber-yard-insights`
- Kafka bootstrap address — `timber-yard-kafka.southeastasia.azurecontainer.io:9092`

Store these in a shared password manager or secure team doc, not just one person's notes.

### Local development (Kafka)

For day-to-day local dev, run Kafka via Docker instead of hitting Azure:

```bash
cd infra/kafka
docker compose up -d
```

This starts a single-broker Kafka instance on `localhost:9092`. Topics auto-create on first use.

### Deploying a new/updated service to Azure

Each microservice is deployed independently. Steps for a service that doesn't have an App Service yet (example: `SawmillService`):

1. **Create the App Service:**
```bash
   az webapp create --name timber-yard-sawmill-service --resource-group timber-yard-rg --plan timber-yard-plan --runtime "DOTNETCORE:10.0"
```

2. **Push app settings** (reuse the shared secrets above, swap the database name):
```bash
   az webapp config appsettings set --name timber-yard-sawmill-service --resource-group timber-yard-rg --settings Kafka__BootstrapServers="timber-yard-kafka.southeastasia.azurecontainer.io:9092" APPLICATIONINSIGHTS_CONNECTION_STRING="<app-insights-connection-string>" Jwt__Secret="<shared-jwt-secret>" ConnectionStrings__MySql="Server=timber-yard-mysql.mysql.database.azure.com;Database=sawmill_db;Uid=<admin-username>;Pwd=<admin-password>;SslMode=Required;"
```

3. **Enable Basic Auth** (required before downloading the publish profile — Azure disables this by default on new App Services):
   - Portal → App Service → Settings → Configuration → General settings → "SCM Basic Auth Publishing Credentials" → **On** → Save

4. **Get the publish profile:**
   - Portal → App Service → Overview → **Download publish profile**
   - (The `az webapp deployment list-publishing-profiles` CLI command shows redacted credentials when Basic Auth was previously off — the Portal download is the reliable method.)

5. **Add it as a GitHub secret:**
   - Repo → Settings → Secrets and variables → Actions → New repository secret
   - Name: `AZURE_<SERVICE_NAME>_PUBLISH_PROFILE` (e.g. `AZURE_SAWMILL_SERVICE_PUBLISH_PROFILE`)
   - Value: full contents of the downloaded `.PublishSettings` file

6. **Add the GitHub Actions workflow** at `.github/workflows/<service-name>-ci-cd.yml` (copy `auth-service-ci-cd.yml` as a template, swap the service name, folder path, and secret name).

7. **Deploy:** merge your feature branch → `develop` → tested → PR into `main`. Pushing to `main` triggers build + deploy automatically. Pushing to `develop` only builds/tests — it does **not** deploy (see CI/CD Pipeline below).

### Granting a new DevOps person access

Azure and GitHub access are separate — both are needed:

**Azure (resource group level, not full subscription):**
- Portal → `timber-yard-rg` → Access control (IAM) → + Add → Add role assignment → **Contributor** role → select the person by their university email → Review + assign

**GitHub:**
- Repo → Settings → Collaborators and teams → Add people → invite by GitHub username (Write access minimum, Admin if they'll manage Actions secrets)

## CI/CD Pipeline

Built with **GitHub Actions**, one workflow per microservice (in `.github/workflows/`), triggered by path — a workflow only runs when files inside its own service folder change.

1. **Push to `develop` or `main`** triggers the workflow
2. **Build** — `dotnet restore` + `dotnet build` for that service only
3. **Deploy** (main branch only) — `dotnet publish` and deploy to the service's Azure App Service via `azure/webapps-deploy`

Pushing to `develop` builds and validates code but does **not** deploy — this lets the team push work-in-progress without affecting the live environment. Deployment only happens when code is merged into `main`, which should go through a Pull Request + review rather than a direct push.

## Testing Strategy

| Type | Tool | Scope |
|---|---|---|
| Unit | xUnit | Business logic per service — stock deduction, wastage & duration calculations, validation rules |
| E2E | Selenium | Full user flows — e.g. record delivery → stock view updates |
| Performance | JMeter | Load testing on core Create/Read endpoints under concurrent requests |

## Team

| Student ID | Name | Service |
|---|---|---|
| IT24103469 | Withanage W.D.D.R | Auth Service |
| IT24610782 | Munasinghe M.A.V.E | Sawmill Service |
| IT24103435 | Srinayaka S.P.B.M | Log Intake Service |
| IT24103438 | Hettiarachchi S.S | Treatment Service |

## Role Rotation

Each member rotates through all four Scrum roles across the sprint cycle, ensuring individual accountability in every role.

| Sprint | Business Analyst | Developer | QA Engineer | DevOps |
|---|---|---|---|---|
| Sprint 1 | IT24610782 | IT24103469 | IT24610782 | IT24103435 |
| Sprint 2 | IT24103438 | IT24610782 | IT24103469 | IT24103438 |
| Sprint 3 | IT24103469 | IT24103438 | IT24103435 | IT24610782 |
| Sprint 4 | IT24103435 | IT24103435 | IT24610782 | IT24103469 |

**Role responsibilities:**
- **Business Analyst** — writes user stories & acceptance criteria for the sprint
- **Developer** — implements frontend/backend features for the sprint's focus
- **QA Engineer** — creates test cases, runs security scans
- **DevOps** — configures CI/CD, monitors deployments
