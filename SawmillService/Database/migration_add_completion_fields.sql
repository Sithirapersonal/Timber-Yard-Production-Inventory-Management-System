-- Migration: add OutputVolumeM3 and WastageM3 completion fields to an EXISTING SawmillDB
-- (Use this instead of re-running schema.sql if you already have a SawmillDB
--  with data in it. schema.sql's CREATE TABLE IF NOT EXISTS won't alter
--  a SawJobs table that's already there, so this does it explicitly.)
--
-- Run with:
--   mysql -u root -p SawmillDB < SawmillService/Database/migration_add_completion_fields.sql
-- or from PowerShell:
--   Get-Content SawmillService/Database/migration_add_completion_fields.sql | mysql -u root -p SawmillDB

USE SawmillDB;

SET @col_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'SawmillDB' AND TABLE_NAME = 'SawJobs' AND COLUMN_NAME = 'OutputVolumeM3'
);

SET @sql := IF(@col_exists = 0,
    'ALTER TABLE SawJobs
        ADD COLUMN OutputVolumeM3 DECIMAL(10,4) NULL AFTER MachineName,
        ADD COLUMN WastageM3 DECIMAL(10,4) NULL AFTER OutputVolumeM3',
    'SELECT "OutputVolumeM3 column already exists on SawJobs — skipping."'
);

PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
