-- Migration: add Machines support to an EXISTING SawmillDB
-- (Use this instead of re-running schema.sql if you already have a SawmillDB
--  with data in it — e.g. you already started one or more saw jobs before
--  this feature existed. schema.sql's CREATE TABLE IF NOT EXISTS won't alter
--  a SawJobs table that's already there, so this does it explicitly.)
--
-- Run with:
--   mysql -u root -p SawmillDB < SawmillService/Database/migration_add_machines.sql
-- or from PowerShell:
--   Get-Content SawmillService/Database/migration_add_machines.sql | mysql -u root -p SawmillDB

USE SawmillDB;

-- 1. Create the Machines table if it doesn't exist yet.
CREATE TABLE IF NOT EXISTS Machines (
    MachineId   INT AUTO_INCREMENT PRIMARY KEY,
    MachineCode VARCHAR(20) UNIQUE NOT NULL,
    Name        VARCHAR(100) NOT NULL,
    Status      ENUM('Available','InUse','UnderMaintenance') NOT NULL DEFAULT 'Available',
    CreatedAt   DATETIME DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO Machines (MachineCode, Name, Status) VALUES
    ('MCH-01', 'Circular Saw Rig A', 'Available'),
    ('MCH-02', 'Band Saw B',         'Available'),
    ('MCH-03', 'Circular Saw Rig C', 'UnderMaintenance'),
    ('MCH-04', 'Edger Saw D',        'Available'),
    ('MCH-05', 'Band Saw E',         'InUse')
ON DUPLICATE KEY UPDATE MachineCode = VALUES(MachineCode);

-- 2. Add the machine columns to SawJobs, nullable so existing rows (started
--    before this feature existed) don't break. Every NEW job going forward
--    always gets a real MachineId — enforced by the application, not the DB.
--    Guarded with a check so re-running this script is safe.
SET @col_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'SawmillDB' AND TABLE_NAME = 'SawJobs' AND COLUMN_NAME = 'MachineId'
);

SET @sql := IF(@col_exists = 0,
    'ALTER TABLE SawJobs
        ADD COLUMN MachineId INT NULL AFTER StartedAt,
        ADD COLUMN MachineCode VARCHAR(20) NULL AFTER MachineId,
        ADD COLUMN MachineName VARCHAR(100) NULL AFTER MachineCode,
        ADD CONSTRAINT fk_sawjobs_machine FOREIGN KEY (MachineId) REFERENCES Machines(MachineId)',
    'SELECT "MachineId column already exists on SawJobs — skipping."'
);

PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
