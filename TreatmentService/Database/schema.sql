CREATE DATABASE IF NOT EXISTS TreatmentDB;
USE TreatmentDB;

-- 1. SawnStock Table
-- Holds available sawn timber inventory credited from SawmillService via Kafka
-- stock-updates events. Used to check stock availability before running chemical treatment.
CREATE TABLE IF NOT EXISTS SawnStock (
    StockId      INT AUTO_INCREMENT PRIMARY KEY,
    Species      VARCHAR(50) NOT NULL,
    Dimensions   VARCHAR(50) NOT NULL,
    VolumeM3     DECIMAL(10,4) NOT NULL DEFAULT 0.0000,
    LastUpdated  DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT uq_sawnstock_species_dimensions UNIQUE (Species, Dimensions)
);

-- 2. ProcessedEvents Table
-- Enforces idempotent consumption. If a Kafka event with EventId has already been
-- processed, redeliveries are recognized and ignored.
CREATE TABLE IF NOT EXISTS ProcessedEvents (
    EventId      VARCHAR(100) PRIMARY KEY,
    EventType    VARCHAR(50) NOT NULL,
    JobId        INT NULL,
    ProcessedAt  DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 3. TreatmentBatches Table (Foundation for treatment lifecycle)
CREATE TABLE IF NOT EXISTS TreatmentBatches (
    BatchId      INT AUTO_INCREMENT PRIMARY KEY,
    BatchCode    VARCHAR(20) UNIQUE NOT NULL,
    ChemicalType VARCHAR(50) NOT NULL,
    Status       ENUM('Pending','InTreatment','Completed','Cancelled') NOT NULL DEFAULT 'Pending',
    StartedAt    DATETIME NULL,
    CompletedAt  DATETIME NULL,
    CreatedAt    DATETIME DEFAULT CURRENT_TIMESTAMP
);
