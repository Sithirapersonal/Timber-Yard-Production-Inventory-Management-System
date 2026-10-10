# Event Contract: Sawn-Stock-Credited

## Overview
- **Event Name**: `sawn-stock-credited`
- **Topic**: `stock-updates`
- **Publisher**: `SawmillService` (upon completion of a saw job)
- **Subscriber**: `TreatmentService` (consumer group: `treatment-stock-updates`)
- **Delivery Guarantees**: At-least-once delivery with idempotent consumption by `TreatmentService` via unique `eventId`.

## Purpose
Establishes the event-driven link between `SawmillService` and `TreatmentService`. When a saw job completes at the sawmill, the finished sawn timber is credited to the treatment inventory without direct database sharing or point-to-point HTTP coupling. `TreatmentService` maintains its own independent `TreatmentDB` to track sawn stock available for chemical treatment.

---

## Schema Definition

| Field Name   | Type      | Required | Description |
|:-------------|:----------|:---------|:------------|
| `eventId`    | `string`  | Yes      | Globally unique identifier (UUID/GUID) for the event instance. Used for idempotency/deduplication. |
| `eventType`  | `string`  | Yes      | Discriminator string, must equal `"sawn-stock-credited"`. Other types on `stock-updates` are skipped. |
| `jobId`      | `integer` | Yes      | Identifier of the completed `SawJob` in SawmillService. |
| `species`    | `string`  | Yes      | Name of the timber species (e.g., `"Teak"`, `"Mahogany"`, `"Pine"`). |
| `dimensions` | `string`  | Yes      | Cross-section and length specification of the sawn timber (e.g., `"2x4x10"`, `"1x6x12"`). |
| `volumeM3`   | `decimal` | Yes      | Sawn timber volume in cubic meters to be added to stock balance (must be > 0). |
| `occurredAt` | `string`  | Yes      | ISO-8601 UTC timestamp indicating when the job was marked completed. |

---

## Example Payload

```json
{
  "eventId": "a7b3c4d5-e6f7-4890-abcd-1234567890ef",
  "eventType": "sawn-stock-credited",
  "jobId": 14,
  "species": "Teak",
  "dimensions": "2x4x10",
  "volumeM3": 1.2500,
  "occurredAt": "2026-10-03T10:30:00Z"
}
```

---

## Idempotency & Error Handling
1. **Deduplication**: `TreatmentService` tracks processed `eventId` values in `ProcessedEvents`. If a duplicate message arrives, the event is acknowledged and logged as duplicate without double-crediting stock.
2. **Tolerance**: If a message payload is malformed (invalid JSON or missing required fields) or contains an unknown `eventType`, it is logged with `Warning` level and skipped. The consumer does not crash or block subsequent messages.
3. **Resilience**: If the Kafka broker is unreachable, `TreatmentService` retries connecting with exponential backoff (starting at 1 second, capped at 30 seconds) rather than terminating the application.
