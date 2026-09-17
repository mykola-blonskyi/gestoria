# ADR-0005: REST + JSON Schema between API and OCR service; gRPC deferred

**Status:** Accepted · **Date:** 2026-09-17

## Context
The C# API must call the Python OCR service and receive structured extraction results. Options: REST/JSON, gRPC/protobuf, message queue.

## Decision
Synchronous REST (`POST /extract`, multipart file + `document_type`) returning JSON validated on both sides against `services/ocr/schema/extraction-result.schema.json` (also used to generate the C# DTOs via NJsonSchema and the Pydantic models). Asynchrony is handled on the C# side with a background job queue (in-process `Channel<T>` first); the OCR service stays stateless and request/response.

## Alternatives
- gRPC/protobuf: stricter contract and smaller payloads, but adds toolchain on both sides while payloads are small and call volume is low.
- Message queue (RabbitMQ): decoupled and resilient, but a third runtime to operate for a solo developer at this stage.

## Consequences
- One schema file is the contract; a change requires bumping `schemaVersion` and updating both generated models in the same PR.
- Timeouts (60 s default), Polly retry (3×, exponential) and circuit breaker in `OcrClient`.
- Revisit: move to gRPC if payload size or call volume makes JSON a bottleneck, or to a queue if OCR moves to separate hardware.
