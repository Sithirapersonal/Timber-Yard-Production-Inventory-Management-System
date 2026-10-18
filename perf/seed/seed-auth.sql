-- Load-test accounts for AuthDB (idempotent).
-- All three accounts share the password:  LoadTest123!
-- (bcrypt cost 10, same algorithm AuthService verifies with BCrypt.Net).
-- Prerequisite: AuthService/Database/001_*.sql and 002_*.sql already applied.
USE AuthDB;

INSERT INTO Users (Username, PasswordHash, Role) VALUES
    ('load_supervisor', '$2b$10$PaiEQTzjAZZPLSQ9Nb2CYO8HTPYRTSZ2evnnVQWtcO.slg3MqI6FK', 'Supervisor'),
    ('load_manager',    '$2b$10$PaiEQTzjAZZPLSQ9Nb2CYO8HTPYRTSZ2evnnVQWtcO.slg3MqI6FK', 'Manager'),
    ('load_admin',      '$2b$10$PaiEQTzjAZZPLSQ9Nb2CYO8HTPYRTSZ2evnnVQWtcO.slg3MqI6FK', 'Admin')
ON DUPLICATE KEY UPDATE PasswordHash = VALUES(PasswordHash), Role = VALUES(Role);

-- Matches the account the existing JMeter plan (treatment-duration-report.jmx) logs in with.
INSERT INTO Users (Username, PasswordHash, Role) VALUES
    ('jmeter_mgr', '$2b$10$PaiEQTzjAZZPLSQ9Nb2CYO8HTPYRTSZ2evnnVQWtcO.slg3MqI6FK', 'Manager')
ON DUPLICATE KEY UPDATE PasswordHash = VALUES(PasswordHash), Role = VALUES(Role);
