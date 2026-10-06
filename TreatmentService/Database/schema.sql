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
    BatchId          INT AUTO_INCREMENT PRIMARY KEY,
    BatchCode        VARCHAR(20) UNIQUE NOT NULL,
    Species          VARCHAR(50) NOT NULL,
    Dimensions       VARCHAR(50) NOT NULL,
    ChemicalType     VARCHAR(50) NOT NULL,
    QuantityM3       DECIMAL(10,4) NOT NULL,
    Tank             VARCHAR(50) NULL,
    TankId           INT NULL,
    CancellationReason VARCHAR(255) NULL,
    TreatedM3        DECIMAL(10,4) NULL,
    RejectedM3       DECIMAL(10,4) NULL,
    Status           ENUM('Pending','InTreatment','Completed','Cancelled') NOT NULL DEFAULT 'Pending',
    StartedAt        DATETIME NULL,
    CompletedAt      DATETIME NULL,
    CreatedAt        DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 3b. Migration: add batch detail columns to pre-existing TreatmentBatches tables
-- (run manually once for DBs created before these columns existed)
-- ALTER TABLE TreatmentBatches
--   ADD COLUMN Species VARCHAR(50) NOT NULL DEFAULT '' AFTER BatchCode,
--   ADD COLUMN Dimensions VARCHAR(50) NOT NULL DEFAULT '' AFTER Species,
--   ADD COLUMN QuantityM3 DECIMAL(10,4) NOT NULL DEFAULT 0 AFTER ChemicalType,
--   ADD COLUMN Tank VARCHAR(50) NULL AFTER QuantityM3,
--   ADD COLUMN CancellationReason VARCHAR(255) NULL AFTER Tank;

-- 6. TreatedStock Table — treated timber ready for final use, credited on batch completion
CREATE TABLE IF NOT EXISTS TreatedStock (
    StockId      INT AUTO_INCREMENT PRIMARY KEY,
    Species      VARCHAR(50) NOT NULL,
    Dimensions   VARCHAR(50) NOT NULL,
    ChemicalType VARCHAR(50) NOT NULL,
    VolumeM3     DECIMAL(10,4) NOT NULL DEFAULT 0.0000,
    LastUpdated  DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT uq_treatedstock_species_dim_chem UNIQUE (Species, Dimensions, ChemicalType)
);

-- 5b. Migration for pre-existing TreatmentBatches without treated/rejected columns:
-- ALTER TABLE TreatmentBatches ADD COLUMN TreatedM3 DECIMAL(10,4) NULL, ADD COLUMN RejectedM3 DECIMAL(10,4) NULL;

-- 5. Tanks Table — treatment tanks with capacity; a tank can run at most one InTreatment batch at a time
CREATE TABLE IF NOT EXISTS Tanks (
    TankId     INT AUTO_INCREMENT PRIMARY KEY,
    TankCode   VARCHAR(20) UNIQUE NOT NULL,
    CapacityM3 DECIMAL(10,4) NOT NULL,
    IsActive   BOOLEAN NOT NULL DEFAULT TRUE
);

-- 5b. Migration for pre-existing TreatmentBatches with a plain Tank string column:
-- ALTER TABLE TreatmentBatches ADD COLUMN TankId INT NULL, ADD CONSTRAINT fk_batches_tank FOREIGN KEY (TankId) REFERENCES Tanks(TankId);
-- ALTER TABLE TreatmentBatches MODIFY COLUMN Tank VARCHAR(50) NULL;

-- Seed tanks for local development
INSERT INTO Tanks (TankCode, CapacityM3, IsActive) VALUES
    ('TANK-A', 10.0000, TRUE),
    ('TANK-B', 5.0000, TRUE),
    ('TANK-C', 2.5000, TRUE)
ON DUPLICATE KEY UPDATE CapacityM3 = VALUES(CapacityM3);

-- 4. StockMovements Table — audit log of every sawn-stock credit/deduction
CREATE TABLE IF NOT EXISTS StockMovements (
    MovementId   INT AUTO_INCREMENT PRIMARY KEY,
    Species      VARCHAR(50) NOT NULL,
    Dimensions   VARCHAR(50) NOT NULL,
    VolumeM3     DECIMAL(10,4) NOT NULL,
    MovementType VARCHAR(30) NOT NULL,
    BatchCode    VARCHAR(20) NULL,
    CreatedAt    DATETIME DEFAULT CURRENT_TIMESTAMP
);
