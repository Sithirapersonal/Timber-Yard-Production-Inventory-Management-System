-- ===== LogIntakeDB: one stock batch (species x 10 ft) with 3 InStock logs per test =====
USE LogIntakeDB;

INSERT IGNORE INTO Species (Name) VALUES
  ('E2E-SawA'),('E2E-SawB'),('E2E-SawC'),('E2E-SawD'),('E2E-SawE'),('E2E-SawF');

INSERT IGNORE INTO Stock (SpeciesId, LengthId)
SELECT s.SpeciesId, l.LengthId
FROM Species s JOIN LogLengths l ON l.LengthFt = 10.00
WHERE s.Name LIKE 'E2E-Saw%';

-- reset: remove logs/delivery from any previous seed run
DELETE FROM Logs WHERE StockId IN (
  SELECT st.StockId FROM Stock st JOIN Species sp ON sp.SpeciesId = st.SpeciesId
  WHERE sp.Name LIKE 'E2E-Saw%');
DELETE FROM Deliveries WHERE Notes = 'E2E-SAW-SEED';

INSERT INTO Deliveries (SupplierId, Species, VolumeM3, VehicleNumber, ReceivedBy, LogCount, Notes)
VALUES (1, 'E2E-Saw', 13.50, 'E2E-0001', 1, 18, 'E2E-SAW-SEED');
SET @d = LAST_INSERT_ID();

-- 3 logs per stock: 0.5 + 0.75 + 1.0 = 2.25 m3
INSERT INTO Logs (DeliveryId, StockId, GirthFt, VolumeM3)
SELECT @d, st.StockId, 4.50, v.vol
FROM Stock st
JOIN Species sp ON sp.SpeciesId = st.SpeciesId
JOIN (SELECT 0.5000 AS vol UNION ALL SELECT 0.7500 UNION ALL SELECT 1.0000) v
WHERE sp.Name LIKE 'E2E-Saw%';

-- ===== SawmillDB: dedicated machines + clean job history for them =====
USE SawmillDB;

DELETE FROM SawJobLogs    WHERE SawJobId IN (SELECT SawJobId FROM SawJobs WHERE MachineCode LIKE 'E2E-M%');
DELETE FROM SawJobWorkers WHERE SawJobId IN (SELECT SawJobId FROM SawJobs WHERE MachineCode LIKE 'E2E-M%');
DELETE FROM SawJobs       WHERE MachineCode LIKE 'E2E-M%';

INSERT INTO Machines (MachineCode, Name, Status) VALUES
  ('E2E-M1','E2E Saw Rig 1','Available'),
  ('E2E-M2','E2E Saw Rig 2','Available'),
  ('E2E-M3','E2E Saw Rig 3','Available'),
  ('E2E-M4','E2E Saw Rig 4','Available'),
  ('E2E-M5','E2E Saw Rig 5','Available')
ON DUPLICATE KEY UPDATE Status = 'Available';