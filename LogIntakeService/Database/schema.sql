CREATE DATABASE IF NOT EXISTS LogIntakeDB;
USE LogIntakeDB;

-- 1. Suppliers Table
CREATE TABLE IF NOT EXISTS Suppliers (
                                         SupplierId INT AUTO_INCREMENT PRIMARY KEY,
                                         SupplierName VARCHAR(100) NOT NULL,
    ContactNumber VARCHAR(20) NULL,
    Email VARCHAR(100) NULL,
    IsActive BOOLEAN DEFAULT TRUE,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
    );

-- 2. Raw Stock Table
CREATE TABLE IF NOT EXISTS RawStock (
                                        StockId INT AUTO_INCREMENT PRIMARY KEY,
                                        Species VARCHAR(50) NOT NULL,
    Grade VARCHAR(10) NOT NULL,
    CurrentVolumeM3 DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    LowStockThreshold DECIMAL(10,2) NOT NULL DEFAULT 10.00,
    LastUpdated DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT uq_species_grade UNIQUE (Species, Grade)
    );

-- 3. Deliveries Table
CREATE TABLE IF NOT EXISTS Deliveries (
                                          DeliveryId INT AUTO_INCREMENT PRIMARY KEY,
                                          SupplierId INT NOT NULL,
                                          Species VARCHAR(50) NOT NULL,
    Grade VARCHAR(10) NOT NULL,
    VolumeM3 DECIMAL(10,2) NOT NULL,
    VehicleNumber VARCHAR(20) NULL,
    ReceivedBy INT NOT NULL,
    ReceivedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_deliveries_supplier FOREIGN KEY (SupplierId) REFERENCES Suppliers(SupplierId)
    );

-- 4. Stock Adjustments Table
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
INSERT INTO Suppliers (SupplierName, ContactNumber, Email, IsActive) VALUES
                                                                         ('Lanka Timber Corporation', '+94112345678', 'info@lankatimber.lk', TRUE),
                                                                         ('Southern Forestry Suppliers', '+94912233445', 'sales@southernforestry.com', TRUE),
                                                                         ('Highland Logs Ltd', '+94522233444', 'contact@highlandlogs.lk', TRUE);

INSERT INTO RawStock (Species, Grade, CurrentVolumeM3, LowStockThreshold) VALUES
                                                                              ('Teak', 'A', 25.50, 10.00),
                                                                              ('Mahogany', 'B', 8.00, 12.00),
                                                                              ('Jak', 'A', 15.00, 5.00);