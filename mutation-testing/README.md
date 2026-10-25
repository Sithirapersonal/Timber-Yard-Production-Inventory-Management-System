# Mutation testing with Stryker.NET

This folder holds the Stryker.NET configuration, the local run instructions and the
baseline results for the three unit-test suites in this repository:

| Service | Test project | Test cases reported by xUnit |
| --- | --- | --- |
| LogIntakeService | `LogIntakeService.Tests` | 40 |
| SawmillService | `SawmillService.Tests` | 32 |
| TreatmentService | `TreatmentService.Tests` | 42 |

The counts above are what `dotnet test` printed locally. Some tests are theories, so the
number of executed cases is larger than the number of `[Fact]` methods.

## What mutation testing is

A normal test run only tells you that the tests pass. It does not tell you whether the
tests would notice if the code were wrong.

Mutation testing checks that directly. Stryker makes many small, deliberate changes
("mutants") to the production code, one at a time, and re-runs the tests for each change.
Examples: `>` becomes `>=`, `&&` becomes `||`, a `return` value is replaced, a whole block
is removed, or a string literal is replaced with `""`. If the tests fail, the mutant is
**killed** - the tests noticed the bug. If the tests still pass, the mutant **survived** -
the suite would not have caught that bug.

### Result statuses

- **Killed** - at least one test failed against the mutant. This is the desired outcome.
- **Survived** - the mutant compiled and all tests still passed. The suite has a blind spot.
- **Timeout** - the mutant made the tests run longer than the allowed time (often an
  infinite loop). Treated as detected by Stryker, but it is not a clean kill.
- **No coverage** - no test executed the mutated line at all, so Stryker did not run tests
  for it. This is worse than a survivor: the code is simply untested.
- **Ignored** - Stryker generated the mutant but did not test it, because a configured
  filter or its "block already covered" optimisation removed it.
- **Compile error** - the mutation produced code that does not compile. Stryker discards
  it; it is not evidence either way.

**Mutation score** = killed / (killed + survived + timeout + no coverage) x 100.
Ignored and compile-error mutants are not in the denominator. Stryker therefore counts
no-coverage mutants as "not caught", which is why the headline score is much lower than
the share of *tested* mutants that were killed.

## Scope: what is mutated and what is not

Each config lives in this folder and is passed to Stryker from the matching test project
directory. Only business logic that the existing suites are meant to verify is mutated.

### Included

**LogIntakeService** (`logintake.stryker-config.json`)

- `Controllers/LogIntakeController.cs`
- `Repositories/LogIntakeRepository.cs` (the supplier tests exercise it)
- `Services/LogsConsumedConsumer.cs`
- `Services/RawStockReversedConsumer.cs`
- `Utils/LogVolumeCalculator.cs`

**SawmillService** (`sawmill.stryker-config.json`)

- `Controllers/SawmillController.cs`
- `Utils/SawJobCalculator.cs`

**TreatmentService** (`treatment.stryker-config.json`)

- `Controllers/TanksController.cs`
- `Controllers/TreatmentBatchesController.cs`
- `Controllers/TreatmentStockController.cs`
- `Repositories/TreatmentStockRepository.cs` (the batch tests exercise it)
- `Services/SawnStockCreditedConsumer.cs`

### Excluded, and why

| Pattern | Reason |
| --- | --- |
| `**/Program.cs` | Application startup, DI wiring, host and middleware configuration. No business rules. |
| `**/DTOs/**` | Request/response data carriers. Properties with no logic; mutating them only tests property syntax. |
| `**/Models/**` | Plain persistence/data classes. |
| `**/Events/**` | Plain Kafka event contracts (data only). |
| `**/Migrations/**` | Not present as C# in these projects (schema lives in `.sql` files); excluded for portability. Database migration code is generated/plumbing and not unit-tested. |
| `**/Repositories/I*.cs` | Interfaces; there is no executable code to mutate. |
| Sawmill `**/Repositories/**` | `SawmillRepository.cs` is SQL data access and no unit test targets it. Mutating it would only produce no-coverage noise. |
| Sawmill `**/Services/**` | Kafka producer/consumer wiring, the HTTP client and the producer guard. Integration plumbing that the unit suite does not exercise. |
| Treatment `**/Repositories/I*.cs` | Interface only. |

`Database/*.sql` is not compiled, so Stryker cannot mutate it. DbContext/seed/Swagger
code does not exist in these services (they use raw ADO.NET and SQL files).

## How to run it locally

Prerequisites: .NET 10 SDK and a local MySQL instance for the integration-style
LogIntake and Treatment tests (see the gap section below).

```powershell
# From the repository root
dotnet tool restore

# LogIntake
cd LogIntakeService.Tests
dotnet stryker --config-file ../mutation-testing/logintake.stryker-config.json --output ../mutation-testing/output/logintake
cd ..

# Sawmill
cd SawmillService.Tests
dotnet stryker --config-file ../mutation-testing/sawmill.stryker-config.json --output ../mutation-testing/output/sawmill
cd ..

# Treatment
cd TreatmentService.Tests
dotnet stryker --config-file ../mutation-testing/treatment.stryker-config.json --output ../mutation-testing/output/treatment
```

`output` is a command-line option in Stryker.NET 5.0.0; it is not read from the JSON
config file. Reports are written to
`mutation-testing/output/<service>/reports/mutation-report.html` and `.json`. The
`mutation-testing/.gitignore` ignores `output/`, so generated reports are never committed.

Run the three services one at a time. They share a machine and the LogIntake/Treatment
tests touch a real database.

## Baseline results

- **Run date:** 2026-10-25, 18:30-18:44 local time
- **Machine:** ASUS laptop, AMD Ryzen 5 3500U (8 logical processors), 5.9 GB RAM,
  Windows 11 Home 10.0.26200
- **Stryker.NET:** 5.0.0 (local tool, pinned in `.config/dotnet-tools.json`)
- **.NET SDK:** 10.0.401
- **Configuration:** `mutation-level: Standard`, thresholds high 80 / low 60 / break 0
- **Runs:** one run per service, executed sequentially

| Service | Mutation score | Killed | Survived | Timeout | No coverage | Ignored | Compile errors | Mutants tested | Run time |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| LogIntakeService | **26.40 %** | 113 | 50 | 0 | 265 | 189 | 44 | 163 | 3m 24s |
| SawmillService | **31.44 %** | 61 | 18 | 0 | 115 | 629 | 110 | 79 | 2m 07s |
| TreatmentService | **65.05 %** | 430 | 54 | 0 | 177 | 206 | 32 | 484 | 7m 59s |

"Mutants created" was 661 for LogIntake, 933 for Sawmill and 899 for Treatment. Ignored
counts are the sum of Stryker's "already covered block" and "mutate filter" removals.

The same score expressed against **tested** mutants only (killed / (killed + survived)):

| Service | Tested-mutant kill rate |
| --- | ---: |
| LogIntakeService | 69.33 % |
| SawmillService | 77.22 % |
| TreatmentService | 88.84 % |

### Per-file scores

Only files selected by the `mutate` filters are listed. Scores use the same formula as
above; ignored and compile-error mutants are excluded.

| Service | File | Score | Killed | Survived | No coverage |
| --- | --- | ---: | ---: | ---: | ---: |
| LogIntake | `Controllers/LogIntakeController.cs` | 15.22 % | 7 | 10 | 29 |
| LogIntake | `Repositories/LogIntakeRepository.cs` | 28.13 % | 81 | 23 | 184 |
| LogIntake | `Services/LogsConsumedConsumer.cs` | 19.05 % | 8 | 8 | 26 |
| LogIntake | `Services/RawStockReversedConsumer.cs` | 19.51 % | 8 | 7 | 26 |
| LogIntake | `Utils/LogVolumeCalculator.cs` | 81.82 % | 9 | 2 | 0 |
| Sawmill | `Controllers/SawmillController.cs` | 29.03 % | 54 | 17 | 115 |
| Sawmill | `Utils/SawJobCalculator.cs` | 87.50 % | 7 | 1 | 0 |
| Treatment | `Controllers/TanksController.cs` | 0.00 % | 0 | 0 | 2 |
| Treatment | `Controllers/TreatmentBatchesController.cs` | 0.00 % | 0 | 0 | 61 |
| Treatment | `Controllers/TreatmentStockController.cs` | 26.32 % | 10 | 3 | 25 |
| Treatment | `Repositories/TreatmentStockRepository.cs` | 81.09 % | 386 | 30 | 60 |
| Treatment | `Services/SawnStockCreditedConsumer.cs` | 40.48 % | 34 | 21 | 29 |

## Survived mutants: analysis

The lists below are the most meaningful survivors, not every survivor. String-literal
survivors that only change log or response wording are mostly omitted unless the wording
is the only thing a caller can observe.

### LogIntakeService

1. **`Repositories/LogIntakeRepository.cs:38`** - `existingId != null && existingId != DBNull.Value` became `||` (and the equality operands were flipped).
   *Why missed:* the supplier tests insert one delivery and check supplier history, but never record two deliveries for the same species/length and assert that the existing `StockId` is reused. **Untested condition / missing assertion.**
2. **`Repositories/LogIntakeRepository.cs:111`** - `await logCmd.ExecuteNonQueryAsync();` became `;` (the individual log row is never inserted).
   *Why missed:* no test reads back the `Logs` rows after `RecordDeliveryAsync`; the delivery-history test only checks that the supplier cannot then be deleted. **Missing assertion.**
3. **`Repositories/LogIntakeRepository.cs:343` and `:355`** - `return await cmd.ExecuteNonQueryAsync() > 0;` became `>= 0`.
   *Why missed:* deactivate/reactivate are only called with IDs that exist, so the unknown-ID `false` path is never asserted. **Untested boundary/condition.**
4. **`Repositories/LogIntakeRepository.cs:373`** - `if (result is null || result is DBNull)` became `&&`.
   *Why missed:* `DeleteSupplierAsync` is never called with a non-existent supplier ID, so the null-result guard never runs. **Untested error path.**
5. **`Repositories/LogIntakeRepository.cs:390`** - `if (count > 0)` became `count >= 0`, which would reject every delete.
   *Why missed:* the only delete test covers the "has delivery history" rejection; deleting a supplier with no history is never tested. **Untested condition.**
6. **`Controllers/LogIntakeController.cs:127`** - the `StockAdjustment` object initializer was replaced with `new StockAdjustment {}`, dropping the trimmed species/grade/reason values.
   *Why missed:* the controller test mocks the repository and only checks the HTTP status; it never captures the argument and asserts the values that were passed on. **Missing assertion.**
7. **`Controllers/LogIntakeController.cs:139`, `:142`, `:204`, `:207`, `:217`, `:220`, `:232`, `:235`** - response message strings were replaced with `""`.
   *Why missed:* tests assert status codes and object identity, never the response message. **Missing assertion.**
8. **`Services/LogsConsumedConsumer.cs:34-35`** and **`Services/RawStockReversedConsumer.cs:33-34`** - the Kafka bootstrap/topic fallbacks (`?? "localhost:9092"`, `?? "logs-consumed"`, `?? "raw-stock-reversed"`) were removed or their strings emptied.
   *Why missed:* tests construct the consumers but never assert the resolved configuration values. **Missing assertion.**
9. **`Utils/LogVolumeCalculator.cs:17`** - `lengthFt <= 0 || girthFt <= 0` became `< 0` for each operand.
   *Why missed:* negative inputs are tested, but zero is not, so a zero dimension would flow into the calculation unnoticed. **Untested boundary.**

### SawmillService

1. **`Controllers/SawmillController.cs:132`** - `dto.LogIds is null || dto.LogIds.Count == 0` became `&&`.
   *Why missed:* there is no test that sends a null or an empty `LogIds` list to `StartSawJob`. **Untested condition.**
2. **`Controllers/SawmillController.cs:135`** - the equivalent `WorkerIds` guard became `&&`.
   *Why missed:* same gap for workers: empty/null worker assignment is not tested. **Untested condition.**
3. **`Controllers/SawmillController.cs:138`** - `dto.MachineId <= 0` became `< 0`.
   *Why missed:* `MachineId = 0` is a boundary that no test exercises. **Untested boundary.**
4. **`Controllers/SawmillController.cs:152`** - `allStock.FirstOrDefault(...)` became `First(...)`.
   *Why missed:* no test asks for a `StockId` that is absent from LogIntakeService's stock list; the intended 400 response becomes an exception. **Untested error path.**
5. **`Controllers/SawmillController.cs:165`** - `.Where(l => l.Status == "InStock" && l.StockId == dto.StockId)` became `||`.
   *Why missed:* all test logs are `InStock` and belong to the requested stock, so a wrong-status or wrong-stock log is never in the data. **Untested condition.**
6. **`Controllers/SawmillController.cs:203`** - `logAllocations.Sum(...)` became `Max(...)`.
   *Why missed:* the start-job test allocates a single log, where sum and max are identical; a multi-log allocation is never asserted. **Missing assertion.**
7. **`Controllers/SawmillController.cs:267`** - `AssignedWorkerNames = workers.Select(w => $"{w.FullName} ({w.EmployeeCode})")` became an empty string per worker.
   *Why missed:* the response's `AssignedWorkerNames` contents/format are never asserted. **Missing assertion.**
8. **`Controllers/SawmillController.cs:578-580`** - the Bearer-header condition `string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", ...)` became `&&`, the literal `"Bearer "` became `""`, and the slicing return became `""`.
   *Why missed:* every test supplies a valid `Bearer` header; missing header, wrong scheme, different casing and whitespace handling are not covered. **Untested error path/condition.**

The single `SawJobCalculator` survivor is the exception message at line 42; tests assert the exception type, not the message. This is low risk.

### TreatmentService

1. **`Repositories/TreatmentStockRepository.cs:100`** - `result != null && result != DBNull.Value ? Convert.ToDecimal(result) : 0m` became `||` and the ternary was forced to `true`.
   *Why missed:* `GetStockBalanceAsync` is only called for species/dimensions that exist; the "no row, return 0m" path is never tested. **Untested condition.**
2. **`Repositories/TreatmentStockRepository.cs:171`** - `await transaction.RollbackAsync();` became `;` in the over-allocation path.
   *Why missed:* the test asserts the exception and the unchanged balance, but not that the transaction was actually rolled back. **Untested error-path side effect.**
3. **`Repositories/TreatmentStockRepository.cs:268`** - `return batches.FirstOrDefault();` became `First()`.
   *Why missed:* `GetBatchByIdAsync` is never called with an unknown batch ID. **Untested error path.**
4. **`Repositories/TreatmentStockRepository.cs:283`** - `b.CompletedAt.Value >= fromUtc.Value` became `>`.
   *Why missed:* the duration report is not tested with `fromUtc` exactly equal to a batch's `CompletedAt`. **Untested boundary.**
5. **`Repositories/TreatmentStockRepository.cs:288`** - `b.Status == "InTreatment" && b.StartedAt.HasValue` became `!= "InTreatment"`.
   *Why missed:* the report tests use completed batches only, so `ActiveCycles` is never asserted against an actual in-treatment batch. **Untested condition.**
6. **`Repositories/TreatmentStockRepository.cs:313`** - `.OrderByDescending(c => c.AverageDurationHours)` became `.OrderBy(...)`.
   *Why missed:* the chemical-type rows are compared by value, not by order. **Missing assertion.**
7. **`Repositories/TreatmentStockRepository.cs:319`** - `.Where(h => h >= 0)` became `h > 0`.
   *Why missed:* a zero-duration (same start and completion time) batch is not part of the fixture. **Untested boundary.**
8. **`Repositories/TreatmentStockRepository.cs:326-327`** - `durations.Count > 0 ? durations.Min()/Max() : 0` became `>= 0` and `Max()` became `Min()`.
   *Why missed:* every chemical-type group has at least one duration, and each group appears to contain a single batch, so min and max are the same value and the empty-group branch never runs. **Untested condition / missing multi-row fixture.**
9. **`Repositories/TreatmentStockRepository.cs:450`** - the `ArgumentException` message for a blank cancellation reason became `""`.
   *Why missed:* the test asserts the exception type, not the message. **Missing assertion.**
10. **`Repositories/TreatmentStockRepository.cs:807`** - `Status = hasBatch ? "Busy" : "Idle"` became always `"Idle"`.
    *Why missed:* the tank-availability test has no tank occupied by an in-treatment batch. **Untested condition.**
11. **`Controllers/TreatmentBatchesController.cs`** - 61 mutants, all no coverage.
    *Why missed:* the batch lifecycle is tested through the repository directly; the controller endpoints are never exercised. **Untested file.**

The `TreatmentStockController` survivors are only response strings (`:42`, `:117`); status codes are asserted but message/health payload contents are not.

### Grouped findings

- **Missing assertions:** response messages, the `AssignedWorkerNames` format, the `StockAdjustment` object passed to the repository, chemical-type report ordering, exception messages, resolved Kafka settings, and table/row counts.
- **Untested boundaries and conditions:** zero dimensions, `MachineId = 0`, `fromUtc` equality, zero-duration batches, null/empty ID lists, `MachineId` and supplier-ID `0`, multi-log sums, multiple batches per chemical type, occupied tanks.
- **Untested error paths:** unknown supplier/batch/stock IDs, failed HTTP publish paths, invalid Bearer headers, transactions that should roll back, null query results.
- **Untested files:** `TreatmentBatchesController` (61 no-coverage mutants) and `TanksController` (2), the Sawmill repository and Kafka/HTTP service layer (excluded from mutation), and most of `LogIntakeRepository` beyond the supplier methods (184 no-coverage mutants).

### Proposed tests (not implemented)

These are suggestions only. No test or application code was changed in this work.

**LogIntakeService**

- Record two deliveries for the same species/length and assert the same `StockId` is reused (one `Stock` row created).
- After `RecordDeliveryAsync`, read back the `Logs` rows and assert one row per requested log with the expected volume.
- Deactivate/reactivate an unknown supplier ID and assert `false`.
- Delete a deactivated supplier with no delivery history and assert success; keep the existing history-rejection test.
- Capture the `StockAdjustment` argument in the controller test and assert trimmed species, upper-cased grade, reason and adjusted-by values.
- Assert the response message for the 404/200 branches.
- Assert consumer bootstrap server, topic and group ID, both when configuration is present and when it falls back.
- Add zero and negative cases for both dimensions in `LogVolumeCalculator`.

**SawmillService**

- Separate tests for `LogIds = null`, `LogIds = []`, `WorkerIds = null`, `WorkerIds = []`, and `MachineId = 0`.
- Unknown `StockId` returns 400 rather than throwing.
- Include a log that is not `InStock` and a log belonging to another stock; assert both are rejected.
- Start a job with two logs and assert `TotalVolumeM3` equals their sum.
- Assert `AssignedWorkerNames` exactly (name and employee code format).
- Auth tests for missing header, non-Bearer scheme, lower-case `bearer`, and extra whitespace.

**TreatmentService**

- `GetStockBalanceAsync` for an unknown species/dimensions returns `0m`.
- After an over-allocation failure, assert no partial batch/movement was written (proves rollback).
- `GetBatchByIdAsync` with an unknown ID returns `null`.
- Duration report with `fromUtc` exactly equal to `CompletedAt`, a zero-duration batch, two batches of the same chemical type with different durations (assert min, max and descending order), and at least one `InTreatment` batch (assert `ActiveCycles`).
- Assert the blank-reason `ArgumentException` message.
- Tank availability with one occupied tank (assert `"Busy"` and the current batch fields).
- Controller-level tests for `TreatmentBatchesController` (create/start/complete/cancel) and the response payloads of `TreatmentStockController`.

## What this tells us about our tests

The suites are good at checking the main happy paths, but they are much weaker at proving
that wrong code would be rejected. Treatment's repository has a strong 81.09 % score and
Sawmill's calculator is 87.50 %, which shows the tests genuinely pin down those calculations
and state transitions. Overall scores are pulled down heavily by untested code: 265
no-coverage mutants in LogIntake, 115 in Sawmill and 177 in Treatment. LogIntake's
repositories and consumers are largely exercised only on the path the supplier tests need,
while Sawmill's controller has broad surface area but tests that check status codes more
than the values inside the response.

The most common pattern is "status code checked, payload not checked". That is why many
string and object-initializer mutants survive. The second pattern is missing negative
cases: unknown IDs, empty lists, zero values, wrong statuses and rollback behaviour.
Mutation testing is not saying the existing tests are bad; it is showing exactly where a
passing build would still allow a real bug through.

## Gaps and limitations

- **AuthService has no unit-test project.** It is not mutated, so its mutation score is
  unknown.
- **The frontend has no unit tests.** It is not mutated.
- **UiTests is the Selenium end-to-end suite, not a unit-test suite.** It is excluded from
  mutation testing.
- **Sawmill's repository, Kafka producers/consumers, HTTP client and producer guard are
  excluded** because no unit test targets them. Their real mutation score is unknown.
- **LogIntake and Treatment include MySQL integration-style tests** (`localhost`,
  `LogIntakeDB` / `TreatmentDB`). They passed on this machine because MySQL was running.
  They will not pass on a clean CI runner unless MySQL and the schemas are provisioned.
- **One run per service, on one machine.** Mutation scores can vary slightly with test
  ordering, timeouts, database state and hardware. These numbers are a baseline, not a
  statistically stable measurement.
- **The score counts no-coverage mutants as missed.** The tested-mutant kill rate is shown
  separately so the two effects are not confused.
- **Equivalent mutants:** a few survivors may be behaviourally identical to the original
  code. They were not manually triaged one by one.
- **Compile-error mutants** (44 / 110 / 32) are excluded from scoring. Sawmill's count is
  high because Stryker's safe mode removed mutations in excluded `LogIntakeClient` methods.
- **The test counts differ from the brief** (40 / 32 / 42 executed cases instead of
  28 / 22 / 32). The brief probably counted `[Fact]`/`[Theory]` methods; xUnit counts each
  theory case separately.

## CI

`.github/workflows/mutation-tests.yml` runs the same three configs in a matrix
(`fail-fast: false`) on `ubuntu-latest`. It restores the local tool, restores NuGet
packages, runs Stryker, uploads the HTML and JSON reports as one artifact per service and
writes the score and counts to the job summary.

Triggers:

- `workflow_dispatch` (manual)
- push to `feature/mutation-testing` - **temporary; remove this trigger before the final
  merge**
- pull requests to `develop` and `main`, limited to changes in the workflow file or
  `mutation-testing/**`, so ordinary code PRs do not start a slow mutation run

The workflow has no secrets and no deployment steps.
