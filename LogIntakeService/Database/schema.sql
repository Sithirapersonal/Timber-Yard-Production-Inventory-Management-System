CREATE DATABASE IF NOT EXISTS LogIntakeDB;
USE LogIntakeDB;

-- 1. Suppliers Table
CREATE TABLE IF NOT EXISTS Suppliers (
    SupplierId INT AUTO_INCREMENT PRIMARY KEY,
    SupplierName VARCHAR(100) NOT NULL,
    ContactNumber VARCHAR(20) NULL,
    Email VARCHAR(100) NULL,
    Address VARCHAR(255) NULL,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 2. Master Table: Species
CREATE TABLE IF NOT EXISTS Species (
    SpeciesId INT AUTO_INCREMENT PRIMARY KEY,
    Name VARCHAR(50) UNIQUE NOT NULL
);

-- 3. Master Table: LogLengths
CREATE TABLE IF NOT EXISTS LogLengths (
    LengthId INT AUTO_INCREMENT PRIMARY KEY,
    LengthFt DECIMAL(5,2) UNIQUE NOT NULL
);

-- 4. Deliveries Table
CREATE TABLE IF NOT EXISTS Deliveries (
    DeliveryId INT AUTO_INCREMENT PRIMARY KEY,
    SupplierId INT NOT NULL,
    Species VARCHAR(50) NULL,
    VolumeM3 DECIMAL(10,2) NULL,
    VehicleNumber VARCHAR(20) NULL,
    ReceivedBy INT NOT NULL,
    ReceivedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    LogCount INT NULL,
    Notes VARCHAR(500) NULL,
    CONSTRAINT fk_deliveries_supplier FOREIGN KEY (SupplierId) REFERENCES Suppliers(SupplierId)
);

-- 5. Stock Table (Species + Length combination defines stock batch)
CREATE TABLE IF NOT EXISTS Stock (
    StockId INT AUTO_INCREMENT PRIMARY KEY,
    SpeciesId INT NOT NULL,
    LengthId INT NOT NULL,
    UNIQUE KEY uq_stock_species_length (SpeciesId, LengthId),
    CONSTRAINT fk_stock_species FOREIGN KEY (SpeciesId) REFERENCES Species(SpeciesId),
    CONSTRAINT fk_stock_length FOREIGN KEY (LengthId) REFERENCES LogLengths(LengthId)
);

-- 6. Logs Table (Per individual log)
CREATE TABLE IF NOT EXISTS Logs (
    LogId INT AUTO_INCREMENT PRIMARY KEY,
    DeliveryId INT NOT NULL,
    StockId INT NOT NULL,
    GirthFt DECIMAL(6,2) NOT NULL,
    VolumeM3 DECIMAL(10,4) NOT NULL,
    Status ENUM('InStock','Consumed','Removed') NOT NULL DEFAULT 'InStock',
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    RemovalReason VARCHAR(255) NULL,
    RemovedAt DATETIME NULL,
    RemovedBy INT NULL,
    CONSTRAINT fk_logs_delivery FOREIGN KEY (DeliveryId) REFERENCES Deliveries(DeliveryId),
    CONSTRAINT fk_logs_stock FOREIGN KEY (StockId) REFERENCES Stock(StockId)
);

-- 7. Stock Thresholds Table (Configurable threshold per stock item)
CREATE TABLE IF NOT EXISTS StockThresholds (
    StockId INT NOT NULL PRIMARY KEY,
    LowStockThreshold DECIMAL(10,2) NOT NULL DEFAULT 10.00,
    CONSTRAINT fk_threshold_stock FOREIGN KEY (StockId) REFERENCES Stock(StockId)
);

-- 8. [DEPRECATED] Raw Stock Table (Kept for backwards compatibility, do not drop)
CREATE TABLE IF NOT EXISTS RawStock (
    StockId INT AUTO_INCREMENT PRIMARY KEY,
    Species VARCHAR(50) NOT NULL,
    Grade VARCHAR(10) NOT NULL,
    CurrentVolumeM3 DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    LowStockThreshold DECIMAL(10,2) NOT NULL DEFAULT 10.00,
    LastUpdated DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT uq_species_grade UNIQUE (Species, Grade)
);

-- 9. [DEPRECATED] Stock Adjustments Table (Kept for backwards compatibility)
CREATE TABLE IF NOT EXISTS StockAdjustments (
    AdjustmentId INT AUTO_INCREMENT PRIMARY KEY,
    Species VARCHAR(50) NOT NULL,
    Grade VARCHAR(10) NOT NULL,
    AdjustedVolumeM3 DECIMAL(10,2) NOT NULL,
    Reason VARCHAR(255) NOT NULL,
    AdjustedBy INT NOT NULL,
    AdjustedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Seed Data
INSERT INTO Suppliers (SupplierName, ContactNumber, Email, Address, IsActive) 
VALUES
    ('Lanka Timber Corporation', '+94112345678', 'info@lankatimber.lk', 'Colombo, Western Province', TRUE),
    ('Southern Forestry Suppliers', '+94912233445', 'sales@southernforestry.com', 'Galle, Southern Province', TRUE),
    ('Highland Logs Ltd', '+94522233444', 'contact@highlandlogs.lk', 'Kandy, Central Province', TRUE)
ON DUPLICATE KEY UPDATE SupplierName = VALUES(SupplierName);

INSERT INTO Species (Name) VALUES
    ('Teak'),
    ('Mahogany'),
    ('Jak')
ON DUPLICATE KEY UPDATE Name = VALUES(Name);

INSERT INTO LogLengths (LengthFt) VALUES
    (8.00),
    (10.00),
    (12.00),
    (14.00),
    (16.00)
ON DUPLICATE KEY UPDATE LengthFt = VALUES(LengthFt);