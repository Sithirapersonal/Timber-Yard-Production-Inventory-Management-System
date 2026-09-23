-- Migration: add CompletedAt (completion timestamp) to an EXISTING SawmillDB
-- (Use this instead of re-running schema.sql if you already have a SawmillDB
--  with data in it. schema.sql's CREATE TABLE IF NOT EXISTS won't alter
--  a SawJobs table that's already there, so this does it explicitly.)
--
-- Run with:
--   mysql -u root -p SawmillDB < SawmillService/Database/migration_add_completed_at.sql
-- or from PowerShell:
--   Get-Content SawmillService/Database/migration_add_completed_at.sql | mysql -u root -p SawmillDB

USE SawmillDB;

-- 1. Add the CompletedAt column (guarded so re-running is a no-op, not an error).
SET @col_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'SawmillDB' AND TABLE_NAME = 'SawJobs' AND COLUMN_NAME = 'CompletedAt'
);

SET @sql := IF(@col_exists = 0,
    'ALTER TABLE SawJobs
        ADD COLUMN CompletedAt DATETIME NULL AFTER WastageM3',
    'SELECT "CompletedAt column already exists on SawJobs — skipping."'
);

PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 2. Backfill APPROXIMATION for pre-existing Completed rows that predate this
--    column: we don't know when they were actually completed, so use StartedAt
--    as a reasonable approximation. This only ever touches OLD rows — any job
--    completed after this migration landed gets a real CompletedAt written by
--    the CompleteSawJob UPDATE, so it can never be NULL while Status = 'Completed'.
--    Naturally idempotent: a second run finds nothing left to backfill.
UPDATE SawJobs
SET CompletedAt = StartedAt
WHERE Status = 'Completed' AND CompletedAt IS NULL;