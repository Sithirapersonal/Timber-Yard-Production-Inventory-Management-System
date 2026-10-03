# TreatmentService Events

## SawnStockCreditedEvent

Consumed from Kafka topic: `stock-updates`.

### Background
When a saw job finishes at the Sawmill, `SawmillService` publishes a `sawn-stock-credited` event to the `stock-updates` topic. `TreatmentService` consumes this event to autonomously credit its own `SawnStock` inventory without direct API calls or database sharing.

### Payload Schema
```json
{
  "eventId": "string (UUID/GUID, required)",
  "eventType": "sawn-stock-credited (required)",
  "jobId": 123,
  "species": "Teak",
  "dimensions": "2x4x10",
  "volumeM3": 1.2500,
  "occurredAt": "2026-10-03T10:00:00Z"
}
```

### Guarantees
- **Idempotency**: Handled via `ProcessedEvents` table using `eventId`. Duplicate deliveries are safely skipped.
- **Fault-Tolerance**: Malformed JSON or unhandled `eventType` values are logged and ignored without stopping the consumer loop.
- **Resilience**: Consumer automatically retries connection with backoff up to 30 seconds if Kafka broker is unavailable.
