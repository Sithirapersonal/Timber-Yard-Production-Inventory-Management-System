-- Removes the rows the 'writes' load scenario created (suppliers/deliveries/logs tagged 'perf-load').
-- Safe to run any time; leaves the bulk 'perf-seed' data and real data untouched.
USE LogIntakeDB;
DELETE l FROM Logs l JOIN Deliveries d ON d.DeliveryId = l.DeliveryId WHERE d.Notes = 'perf-load';
DELETE FROM Deliveries WHERE Notes = 'perf-load';
DELETE FROM Suppliers WHERE Address = 'perf-load' AND SupplierName LIKE 'perf-load %';
