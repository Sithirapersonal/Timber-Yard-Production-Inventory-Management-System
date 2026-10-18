-- Bulk, realistic-sized data so list and report endpoints are measured against
-- tables that look like a few months of production, not empty ones.
-- Idempotent: every block is guarded by a 'perf-seed' marker, so re-running adds nothing.
-- Prerequisite: each service's schema.sql has been applied (see perf/README.md).
-- Requires MySQL 8 (recursive CTEs, window functions).

SET SESSION cte_max_recursion_depth = 10000;

-- ============================================================================
-- LogIntakeDB : 500 deliveries, 3,000 logs
-- ============================================================================
USE LogIntakeDB;

-- every Species x LogLength combination must exist as a Stock row
INSERT IGNORE INTO Stock (SpeciesId, LengthId)
SELECT s.SpeciesId, l.LengthId FROM Species s CROSS JOIN LogLengths l;

INSERT INTO Deliveries (SupplierId, VehicleNumber, ReceivedBy, ReceivedAt, LogCount, Notes)
WITH RECURSIVE seq (n) AS (
    SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 500
)
SELECT ((n - 1) % 3) + 1,
       CONCAT('PERF-', LPAD(n, 4, '0')),
       1,
       UTC_TIMESTAMP() - INTERVAL (n % 90) DAY - INTERVAL (n % 24) HOUR,
       6,
       'perf-seed'
FROM seq
WHERE NOT EXISTS (SELECT 1 FROM Deliveries WHERE Notes = 'perf-seed');

INSERT INTO Logs (DeliveryId, StockId, GirthFt, VolumeM3, Status, CreatedAt)
WITH RECURSIVE k (i) AS (
    SELECT 1 UNION ALL SELECT i + 1 FROM k WHERE i < 6
),
numbered_stock AS (
    SELECT StockId, ROW_NUMBER() OVER (ORDER BY StockId) AS rn FROM Stock
),
stock_count AS (
    SELECT COUNT(*) AS c FROM Stock
)
SELECT d.DeliveryId,
       ns.StockId,
       3.00 + (k.i * 0.25),
       0.50 + (k.i * 0.10),
       IF((d.DeliveryId + k.i) % 10 < 3, 'Consumed', 'InStock'),
       d.ReceivedAt
FROM Deliveries d
JOIN k
JOIN stock_count sc
JOIN numbered_stock ns ON ns.rn = ((d.DeliveryId + k.i) % sc.c) + 1
WHERE d.Notes = 'perf-seed'
  AND NOT EXISTS (SELECT 1 FROM Logs lg JOIN Deliveries dd ON dd.DeliveryId = lg.DeliveryId WHERE dd.Notes = 'perf-seed');

-- ============================================================================
-- SawmillDB : 1,500 finished saw jobs (Completed and Cancelled)
-- ============================================================================
USE SawmillDB;

INSERT INTO SawJobs (JobCode, StockId, SpeciesName, LengthFt, TotalVolumeM3, Notes, Status,
                     StartedBy, StartedAt, MachineId, MachineCode, MachineName,
                     OutputVolumeM3, WastageM3, CompletedAt)
WITH RECURSIVE seq (n) AS (
    SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 1500
)
SELECT CONCAT('PERF-', LPAD(n, 5, '0')),
       (n % 15) + 1,
       ELT((n % 3) + 1, 'Teak', 'Mahogany', 'Jak'),
       ELT((n % 5) + 1, 8.00, 10.00, 12.00, 14.00, 16.00),
       5.0000 + (n % 20),
       'perf-seed',
       IF(n % 10 = 0, 'Cancelled', 'Completed'),
       1,
       UTC_TIMESTAMP() - INTERVAL (n % 180) DAY - INTERVAL 10 HOUR,
       m.MachineId, m.MachineCode, m.Name,
       IF(n % 10 = 0, NULL, (5.0000 + (n % 20)) * 0.6),
       IF(n % 10 = 0, NULL, (5.0000 + (n % 20)) * 0.4),
       IF(n % 10 = 0, NULL, UTC_TIMESTAMP() - INTERVAL (n % 180) DAY - INTERVAL 2 HOUR)
FROM seq
JOIN Machines m ON m.MachineCode = ELT((n % 3) + 1, 'MCH-01', 'MCH-02', 'MCH-04')
WHERE NOT EXISTS (SELECT 1 FROM SawJobs WHERE Notes = 'perf-seed');

-- ============================================================================
-- TreatmentDB : 3,000 treatment batches + stock rows
-- ============================================================================
USE TreatmentDB;

INSERT INTO TreatmentBatches (BatchCode, Species, Dimensions, ChemicalType, QuantityM3, Tank, TankId,
                              TreatedM3, RejectedM3, Status, StartedAt, CompletedAt, CreatedAt)
WITH RECURSIVE seq (n) AS (
    SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 3000
),
batch AS (
    SELECT n,
           CASE WHEN n % 20 = 0 THEN 'Pending'
                WHEN n % 20 = 1 THEN 'InTreatment'
                WHEN n % 10 = 2 THEN 'Cancelled'
                ELSE 'Completed' END AS st,
           UTC_TIMESTAMP() - INTERVAL (n % 240) DAY - INTERVAL 96 HOUR AS started
    FROM seq
)
SELECT CONCAT('PERF-B-', LPAD(n, 5, '0')),
       ELT((n % 3) + 1, 'Teak', 'Mahogany', 'Jak'),
       ELT((n % 4) + 1, '2x4', '2x6', '4x4', '1x6'),
       ELT((n % 3) + 1, 'CCA', 'ACQ', 'Borate'),
       1.0000 + (n % 5),
       NULL, NULL,
       IF(st = 'Completed', 1.0000 + (n % 5) - 0.1, NULL),
       IF(st = 'Completed', 0.1000, NULL),
       st,
       IF(st IN ('Pending', 'Cancelled'), NULL, started),
       IF(st = 'Completed', started + INTERVAL (24 + (n % 48)) HOUR, NULL),
       started - INTERVAL 1 HOUR
FROM batch
WHERE NOT EXISTS (SELECT 1 FROM TreatmentBatches WHERE BatchCode LIKE 'PERF-B-%');

INSERT IGNORE INTO SawnStock (Species, Dimensions, VolumeM3)
SELECT sp.s, d.d, 50.0000
FROM (SELECT 'Teak' AS s UNION ALL SELECT 'Mahogany' UNION ALL SELECT 'Jak') sp
CROSS JOIN (SELECT '2x4' AS d UNION ALL SELECT '2x6' UNION ALL SELECT '4x4' UNION ALL SELECT '1x6') d;

INSERT IGNORE INTO TreatedStock (Species, Dimensions, ChemicalType, VolumeM3)
SELECT sp.s, d.d, c.c, 25.0000
FROM (SELECT 'Teak' AS s UNION ALL SELECT 'Mahogany' UNION ALL SELECT 'Jak') sp
CROSS JOIN (SELECT '2x4' AS d UNION ALL SELECT '2x6' UNION ALL SELECT '4x4' UNION ALL SELECT '1x6') d
CROSS JOIN (SELECT 'CCA' AS c UNION ALL SELECT 'ACQ' UNION ALL SELECT 'Borate') c;
