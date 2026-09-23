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

-- 2. Machines Table (physical saw rigs — exactly one allocated per SawJob)
CREATE TABLE IF NOT EXISTS Machines (
    MachineId   INT AUTO_INCREMENT PRIMARY KEY,
    MachineCode VARCHAR(20) UNIQUE NOT NULL,   -- e.g. MCH-01
    Name        VARCHAR(100) NOT NULL,
    Status      ENUM('Available','InUse','UnderMaintenance') NOT NULL DEFAULT 'Available',
    CreatedAt   DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 3. SawJobs Table
--    StockId is a value-only reference to LogIntakeService's Stock.StockId (no cross-DB FK).
--    SpeciesName / LengthFt are denormalized snapshots taken at job-creation time.
--    MachineCode / MachineName are denormalized snapshots of the allocated machine.
--    TotalVolumeM3 is computed server-side (sum of allocated logs' VolumeM3).
--    CompletedAt records when the job was marked Completed (UTC); NULL until then.
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
    StartedAt    DATETIME DEFAULT CURRENT_TIMESTAMP,
    MachineId    INT NOT NULL,
    MachineCode  VARCHAR(20) NOT NULL,
    MachineName  VARCHAR(100) NOT NULL,
    OutputVolumeM3 DECIMAL(10,4) NULL,   -- total sawn board volume, computed server-side, set only on Complete
    WastageM3    DECIMAL(10,4) NULL,   -- TotalVolumeM3 - OutputVolumeM3, computed server-side, set only on Complete
    CompletedAt  DATETIME NULL,        -- when the job was marked Completed (UTC), set only on Complete, cleared on revert
    CONSTRAINT fk_sawjobs_machine FOREIGN KEY (MachineId) REFERENCES Machines(MachineId)
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

-- Seed Data: machines
INSERT INTO Machines (MachineCode, Name, Status) VALUES
    ('MCH-01', 'Circular Saw Rig A', 'Available'),
    ('MCH-02', 'Band Saw B',         'Available'),
    ('MCH-03', 'Circular Saw Rig C', 'UnderMaintenance'),
    ('MCH-04', 'Edger Saw D',        'Available'),
    ('MCH-05', 'Band Saw E',         'InUse')
ON DUPLICATE KEY UPDATE MachineCode = VALUES(MachineCode);
