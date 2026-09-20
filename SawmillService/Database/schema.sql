CREATE DATABASE IF NOT EXISTS SawmillDB;
USE SawmillDB;

-- 1. Workers Table (shop-floor sawyers/machine operators, NOT system login accounts)
CREATE TABLE IF NOT EXISTS Workers (
    WorkerId    INT AUTO_INCREMENT PRIMARY KEY,
    EmployeeCode VARCHAR(20) UNIQUE NOT NULL,   -- e.g. EMP-014
    FullName    VARCHAR(100) NOT NULL,
    JobRole     VARCHAR(50)  NOT NULL,
    IsActive    BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt   DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 2. SawJobs Table
--    StockId is a value-only reference to LogIntakeService's Stock.StockId (no cross-DB FK).
--    SpeciesName / LengthFt are denormalized snapshots taken at job-creation time.
--    TotalVolumeM3 is computed server-side (sum of allocated logs' VolumeM3).
--    JobCode is generated server-side as SAW-001, SAW-002, ...
CREATE TABLE IF NOT EXISTS SawJobs (
    SawJobId     INT AUTO_INCREMENT PRIMARY KEY,
    JobCode      VARCHAR(20) UNIQUE NOT NULL,
    StockId      INT NOT NULL,
    SpeciesName  VARCHAR(50) NOT NULL,
    LengthFt     DECIMAL(5,2) NOT NULL,
    TotalVolumeM3 DECIMAL(10,4) NOT NULL,
    Notes        VARCHAR(500) NULL,
    Status       ENUM('InProgress','Completed','Cancelled') NOT NULL DEFAULT 'InProgress',
    StartedBy    INT NOT NULL,       -- userId claim from JWT
    StartedAt    DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 3. SawJobLogs Table (which LogIntakeService logs were cut in this job)
--    LogId is a value-only reference to LogIntakeService's Logs.LogId (no cross-DB FK).
--    VolumeM3 is a snapshot of that log's volume at allocation time.
CREATE TABLE IF NOT EXISTS SawJobLogs (
    SawJobLogId INT AUTO_INCREMENT PRIMARY KEY,
    SawJobId    INT NOT NULL,
    LogId       INT NOT NULL,       -- value-only ref to LogIntakeService.Logs.LogId
    VolumeM3    DECIMAL(10,4) NOT NULL,
    CONSTRAINT fk_sawjoblogs_job FOREIGN KEY (SawJobId) REFERENCES SawJobs(SawJobId)
);

-- 4. SawJobWorkers Table (many-to-many: which workers were on each saw job)
CREATE TABLE IF NOT EXISTS SawJobWorkers (
    SawJobWorkerId INT AUTO_INCREMENT PRIMARY KEY,
    SawJobId       INT NOT NULL,
    WorkerId       INT NOT NULL,
    CONSTRAINT fk_sawjobworkers_job    FOREIGN KEY (SawJobId)  REFERENCES SawJobs(SawJobId),
    CONSTRAINT fk_sawjobworkers_worker FOREIGN KEY (WorkerId)  REFERENCES Workers(WorkerId)
);

-- Seed Data: at least 5 workers
INSERT INTO Workers (EmployeeCode, FullName, JobRole, IsActive) VALUES
    ('EMP-011', 'Kamal Perera',      'Head Sawyer',       TRUE),
    ('EMP-012', 'Saman Rathnayake',  'Machine Operator',  TRUE),
    ('EMP-013', 'Nimal Fernando',    'Sawyer',            TRUE),
    ('EMP-014', 'Roshan Silva',      'Machine Operator',  TRUE),
    ('EMP-015', 'Ajith Bandara',     'Sawyer',            TRUE),
    ('EMP-016', 'Pradeep Wijesinghe','Log Handler',       TRUE)
ON DUPLICATE KEY UPDATE EmployeeCode = VALUES(EmployeeCode);
