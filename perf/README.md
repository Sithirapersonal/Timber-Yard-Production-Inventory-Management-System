# Performance & Load Testing (JMeter)

| File / folder | What it is |
| --- | --- |
| `jmeter/timber-yard-load.jmx` | **The load-test plan** for all four services (one plan, many scenarios) |
| `jmeter/profiles/*.properties` | Scenario definitions: smoke, load, stress, spike, soak |
| `jmeter/check_thresholds.py` | Pass/fail verdict from the results file (JMeter has no built-in thresholds) |
| `jmeter/build_plan.py` | Generates the `.jmx` (XML is unpleasant to edit by hand - change this, re-run it) |
| `seed/` | Test users + realistic bulk data, cleanup script |
| `run-load-tests.ps1` / `.sh` | One-command local runners |
| `treatment-duration-report.jmx` | Original single-endpoint plan (Treat.7) - kept, see bottom |
| `../.github/workflows/load-performance-tests.yml` | CI pipeline that boots the stack and runs the suite |

## What it tests

Realistic traffic across Auth, LogIntake, Sawmill and Treatment using seeded Supervisor / Manager roles.
In `setUp`, one login per role stores the JWT as a JMeter property (all four services share the same JWT, so no
per-request login). Each simulated user then loops over a weighted random action:

| Action (weight) | Endpoints | Label prefix |
| --- | --- | --- |
| Browse screens (40%) | LogIntake stock/species/lengths/suppliers/logs; Sawmill stock (proxy to LogIntake), machines, workers, jobs; Treatment stock, tanks, batches | `[read]` |
| Manager reports (20%) | Treatment duration report, Sawmill wastage-yield + job history, low-stock alerts, full delivery list | `[report]` |
| Record data (20%) | `POST` supplier, `POST` delivery (real DB transactions, tagged `perf-load`) | `[write]` |
| Auth + access control (20%) | `POST /auth/login` (bcrypt), no token -> 401, Supervisor on Manager report -> 403, bad password -> 401 | `[login]` / `[read]` |

Every request asserts its HTTP status; the duration report also asserts `completedBatches` is in the body.

## Scenarios (`jmeter/profiles/`)

| Profile | Shape | Purpose | Length |
| --- | --- | --- | --- |
| `smoke` | 1 user | Everything works, seed/accounts OK. **CI gate - runs first.** | 30 s |
| `load` | ramp to 20 users, hold | Expected traffic; **the pass/fail gate** | ~4.5 min (CI: 10 users, 2.5 min) |
| `stress` | +25 / +25 / +50 / +50 / +50 users each minute -> 200, little think time | Find the breaking point | 6 min |
| `spike` | 5 users, +145 within 10 s at t=30 s for ~70 s, then back | Survive a surge and recover | 3.3 min |
| `soak` | 15 users | Leaks, connection-pool exhaustion, drift | 30 min (manual) |

Only "wave" thread groups differ between profiles: `waveN.threads / .ramp / .delay / .duration` (and `think.min/.range`)
are properties, so any value can be overridden: `-Jwave1.threads=50 -Jwave1.duration=600`.

## Pass/fail criteria (`check_thresholds.py`)

| Request type | Criterion |
| --- | --- |
| all | failed requests <= 1 % |
| `[read]` | p95 <= 500 ms |
| `[report]` | p95 <= 2000 ms (same limit as the Treat.7 plan) |
| `[write]` | p95 <= 800 ms |
| `[login]` | p95 <= 1000 ms |

A breach exits non-zero (fails the CI job / the runner script). `stress` is informational: it reports the **breaking point**
(first minute with > 10 % errors or p95 > 5 s). Every run also prints the slowest endpoints and a per-minute timeline
(users, requests, errors %, p95) - the timeline is how you read the spike/recovery and stress results.
Limits can be overridden with `SLO_READ_P95`, `SLO_REPORT_P95`, `SLO_WRITE_P95`, `SLO_LOGIN_P95`, `SLO_ERROR_RATE`.

## Running it

**GitHub Actions:** nothing to do. The workflow starts MySQL, applies the schemas, seeds data, publishes and starts the four
services, runs smoke then load, shows the tables in the job summary and uploads the JMeter HTML dashboards, raw `.jtl` files and
service logs as the `jmeter-results` artifact. *Actions -> load-performance-tests -> Run workflow* lets you pick another scenario.

**Locally** (services on 5030 / 5201 / 5210 / 5000, MySQL on localhost; Apache JMeter 5.6+ and Python 3 installed):

```powershell
.\perf\run-load-tests.ps1 -Seed                         # seed DBs, then smoke -> load
.\perf\run-load-tests.ps1 -Scenario stress
.\perf\run-load-tests.ps1 -Scenario load -JMeterArgs '-Jwave1.threads=50','-Jwave1.duration=600'
```

```bash
SEED=1 ./perf/run-load-tests.sh        # Linux / macOS / Git Bash
./perf/run-load-tests.sh spike
```

Plain JMeter: `jmeter -n -t perf/jmeter/timber-yard-load.jmx -q perf/jmeter/profiles/load.properties -l load.jtl -e -o load-report`
(open the plan in the GUI to inspect or debug it; use `-Jhost=...`, `-Jauth.port=...` etc. for other hosts/ports).
After runs that wrote data: `mysql < perf/seed/cleanup.sql`.

Output goes to `perf/jmeter/results/` (git-ignored): `<profile>.jtl`, `<profile>-report/index.html`, `<profile>-summary.md`.

## Seed data (`seed/`)

Empty tables make every endpoint look fast, so `seed-data.sql` (idempotent, MySQL 8) adds ~3 months of plausible production:
**500 deliveries / 3,000 logs, 1,500 saw jobs, 3,000 treatment batches** plus stock rows. `seed-auth.sql` creates `load_supervisor`,
`load_manager`, `load_admin` (and `jmeter_mgr` for the Treat.7 plan), all with password `LoadTest123!`.

## Design notes / viva talking points

- **Login is separate from the measured load.** bcrypt makes `/auth/login` CPU-heavy by design; logging in every iteration would
  measure bcrypt, not the APIs. Tokens are made once in `setUp`; the "Auth" action measures login on its own.
- **Kafka is not required** for these tests; the services' Kafka consumers retry with backoff and never block HTTP. Sawmill job
  start/complete and the Treatment batch lifecycle are not in the mix (they publish/consume Kafka events and change multi-service
  state) - a good follow-up with a full stack.
- **Likely bottleneck to look at first:** `TreatmentStockRepository.GetDurationReportAsync` loads *all* batches into memory and
  filters/aggregates in C#, so its cost grows with table size; `GET /api/LogIntake/deliveries` has no paging. The 3,000-batch seed
  and the `[report]` limit are there to expose this. Sawmill `stock` calls LogIntake over HTTP, so it includes the cross-service hop.
- Spike/stress "waves" are plain Thread Groups with a startup delay, so no JMeter plugins are needed in CI.

## Results

All runs below were executed for real on **2026-10-17** (local Windows machine, JMeter 5.6.3 / Java 21,
MySQL 8 on localhost). They were **not re-seeded between scenarios**: smoke and load ran against the
seeded baseline (about 508 and 522 deliveries), and because the "record data" actions add rows, stress
ran on about 1,255 deliveries and spike on about 3,747 — so the unpaged LogIntake list endpoints were
slightly more loaded in those two runs. The bulk seed tables (3,012 logs / 1,523 saw jobs / 3,004
treatment batches) were unchanged throughout. `seed/cleanup.sql` resets the data the suite wrote, and
re-running each scenario after cleanup would give directly comparable numbers. Raw `.jtl`s, HTML
dashboards and per-run summaries are in `jmeter/results/`.

| Date | Profile | Users | Requests | Failed | read p95 | report p95 | write p95 | login p95 | Result |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-10-17 | smoke | 1 | 121 | 0 (0.00%) | 49 ms | 95 ms | 31 ms | 303 ms | **PASSED ✅** |
| 2026-10-17 | load | 20 | 7,308 | 0 (0.00%) | 91 ms | 144 ms | 48 ms | 256 ms | **PASSED ✅** |
| 2026-10-17 | stress | 200 | 26,295 | 631 (2.40%) | 5,766 ms | 5,629 ms | 3,861 ms | 12,639 ms | Informational: breaking point minute 2 (~111 users) |
| 2026-10-17 | spike | 150 | 6,322 | 97 (1.53%) | 5,473 ms | 4,655 ms | 6,052 ms | 9,965 ms | **FAILED ❌** – every limit breached during the surge |

"Users" = peak concurrent users of the profile (stress ramps 25 → 200; spike jumps 5 → 150 for ~70 s).
`stress` is informational by design; `spike` is held to the normal limits, and it breached all five
(error rate + the four p95s).

## Findings

### Slowest endpoints

At **load** (the pass/fail gate) everything is comfortable: slowest p95s are
`[read] Treatment batches (Completed)` 286 ms, `[login] auth login bad password` 264 ms,
`auth login` 248 ms, `[report] Treatment duration report` 183 ms — all far inside their limits.
Under **stress** the ordering is dominated by (p95 / errors):

| Endpoint (stress) | p95 | Errors |
| --- | --- | --- |
| `[read] Sawmill stock logs (proxy)` | 17,915 ms | 79 |
| `[read] LogIntake logs (filtered)` | 14,937 ms | 33 |
| `[report] LogIntake deliveries (full list)` | 13,086 ms | 21 |
| `[login] auth login bad password -> 401` | 12,773 ms | 31 |
| `[login] auth login` | 12,503 ms | 25 |
| `[read] LogIntake suppliers` | 10,592 ms | 17 |
| `[report] Treatment duration report` | 4,234 ms | 22 |
| `[report] Sawmill job history` | 3,082 ms | 26 |

- **Worst endpoint class: Sawmill's stock proxy.** `/api/Sawmill/stock` and `/api/Sawmill/stock/1/logs`
  make a synchronous Sawmill → LogIntake HTTP hop, so each request queues behind two services.
  **Every single 502 in both stress (122/122) and spike (26/26) came from these two endpoints** —
  the inner call failed under saturation and Sawmill relayed it as 502.
- **Next: the unpaged LogIntake list endpoints** (`/deliveries` full list, `/logs`, `/suppliers`) —
  their cost grows with table size, exactly what the 3,000-row seed was designed to expose.
- The **Treatment duration report** (loads all batches, filters/aggregates in C#) is fine at load
  (183 ms) but reaches 4.2 s p95 at stress — same growth pattern, earlier on the curve.
- `POST /auth/login` is bcrypt-bound (~185 ms avg at load) and scales with CPU, so it degrades
  early under stress (12.5 s p95 at 200 users).

### Limits breached

- **load (the gate): none.** 0 errors; worst criterion is login p95 256 ms of 1,000 ms.
- **stress:** informational, but all four p95s exceeded their limits (5,766 / 5,629 / 3,861 /
  12,639 ms) and errors hit 2.40 % overall (worst minute 8.3 %).
- **spike: all five criteria breached** — error rate 1.53 % > 1 %, read 5,473 > 500 ms,
  report 4,655 > 2,000 ms, write 6,052 > 800 ms, login 9,965 > 1,000 ms. The surge of 150 users
  on one dev machine is simply beyond what these single-instance services + one MySQL can absorb,
  even though nothing crashed.

### Stress breaking point

Ramp: 25 → 50 → 100 → 150 → 200 users, one step per minute.

- **Minute 2 (~111 users) is the first minute with p95 > 5 s** (5,922 ms) — that is the
  reported breaking point. Latency kept climbing (minute 3: 9,711 ms).
- Errors only appear once all 200 users are loaded: minute 4 **7.7 %**, minute 5 **8.3 %**;
  overall 2.40 %. Failures were exclusively HTTP **500 (509) and 502 (122)** — no timeouts,
  no refused connections.
- The system **did not crash**: throughput plateaued at ~70 req/s (vs. 27 req/s at load), all
  200 users kept getting responses until the run ended, and no service needed a restart.

### Spike and recovery

- The 145-user wave joins at t = 30 s and stays ~70 s. During minutes 0–1 (150 users) the system
  runs at p95 6.9–8.2 s with up to 4.0 % errors in a minute (97 failed requests in total, again
  only HTTP 500/502: 55 read, 30 write, 9 report, 3 login samples failed).
- The moment the wave leaves (~t = 110 s), the 5 baseline users recover immediately:
  minute 2 p95 **166 ms**, minute 3 p95 **157 ms**, **0 % errors** — full recovery within seconds,
  no residual degradation, no restart, no warm-up needed.

### One defect worth questioning

31 of the stress failures were `POST /auth/login` with a deliberately wrong password that asserted
**401 but got HTTP 500** — the bcrypt failure path returned 500 under saturation instead of 401.
Small (31/26,295) but a real behaviour difference between "wrong credentials" and "overloaded".

---

## Original plan: Treat.7 duration report (`treatment-duration-report.jmx`)


Validates that `GET /api/TreatmentBatches/reports/duration` (Manager/Admin) is generated
within **2 seconds** under load.

### How to run

```powershell
# Prerequisites: AuthService (:5030) and TreatmentService (:5000) running,
# and a Manager account (default plan uses jmeter_mgr / LoadTest123!).
& "$env:TEMP\apache-jmeter-5.5\bin\jmeter.bat" -n -t treatment-duration-report.jmx -l results.jtl
```

### Test design

- 50 Manager threads, 10s ramp-up, 2 loops (per JMeter defaults in the plan).
- Each thread logs in once (OnceOnly) and extracts the JWT, then repeatedly requests
  the duration report with a wide date range.
- Assertions per request:
  - **DurationAssertion ≤ 2000 ms** (the DoD limit).
  - **Response contains `completedBatches`** (payload sanity).

### Result (2026-10-09, local dev machine)

| Metric | Value |
| --- | --- |
| Samples | 150 (50 logins + 100 report requests) |
| Errors | 0 (0.00%) |
| Report avg | 6.8 ms |
| Report p95 | 10 ms |
| Report max | 82 ms |
| Limit | 2000 ms ✅ |
