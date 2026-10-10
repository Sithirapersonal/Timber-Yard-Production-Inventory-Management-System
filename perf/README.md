# Performance Tests

## Treat.7 — Treatment Duration Report (`treatment-duration-report.jmx`)

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
