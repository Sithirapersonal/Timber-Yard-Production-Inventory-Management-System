## UI / E2E Testing (Selenium)

Automated browser tests that drive the React frontend and exercise the Auth, LogIntake, Sawmill and Treatment services against real MySQL databases.

**Stack:** C# · xUnit · Selenium WebDriver · Page Object pattern · explicit waits · GitHub Actions

| Suite | Tests | Covers |
|---|---|---|
| AuthTests | AUTH-01..08 | Login, invalid/injection input, route protection, logout, session persistence |
| DashboardNavigationTests | NAV-01..07 | Dashboard, sidebar, role-based menus |
| UserManagementTests | UM-01..08 | Admin user lifecycle, duplicates, audit history |
| LogIntakeTests | LI-01..12 | Suppliers, delivery form validation, recording a delivery |
| SawingTests | SAW-01..06 | Tabs, role-based report access, job form |
| TreatmentTests | TRT-01..08 | Tabs, validation, tank schedule |
| TreatmentWorkflowTests | TRT-09..14 | Create/start/complete/cancel batch, stock deduction, tank capacity |

**Run locally**
1. Start MySQL, the 4 services and the frontend (`npm run dev`, http://localhost:5173).
2. Create the three test users (e2e_admin, e2e_manager, e2e_supervisor) and seed sawn stock.
3. `dotnet test UiTests` (set `E2E_HEADLESS=false` to watch the browser).

**CI:** `.github/workflows/e2e-tests.yml` starts MySQL, builds the databases, seeds users and stock, starts all services and the frontend, runs the suite headless in Chrome and uploads the results and service logs.

Configuration is through environment variables: `E2E_BASE_URL`, `E2E_PASSWORD`, `E2E_ADMIN_USER`, `E2E_MANAGER_USER`, `E2E_SUPERVISOR_USER`, `E2E_HEADLESS`.

Test cases: see the test-case table in the project report.
Owner: <your name> (UI / E2E Testing).
