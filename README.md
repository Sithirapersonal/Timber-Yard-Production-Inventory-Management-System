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

## CI/CD Pipeline

Built with **GitHub Actions**, active from Sprint 1 through final evaluation:

1. **Push / Pull Request** triggers the workflow
2. **Build** each service independently
3. **Run unit tests** (xUnit)
4. **Deploy** to the corresponding Azure App Service instance

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
