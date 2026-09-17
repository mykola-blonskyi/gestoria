# ADR-0009: In-process `Channel<T>` job queue for OCR extraction in v1

**Status:** Accepted · **Date:** 2026-09-17

## Context
Document extraction takes seconds (up to ~8–10 s for a phone photo on CPU), so uploads must be accepted immediately and processed in the background. The queue can live in the API process (.NET `System.Threading.Channels`) or in an external broker (RabbitMQ, Azure Service Bus, Kafka).

## Decision
v1 uses an in-process bounded `Channel<ExtractJob>` consumed by a `BackgroundService` inside `GestorIA.Api`, behind an `IExtractJobQueue` port. PostgreSQL is the source of truth for job state (`SourceDocument.ExtractionStatus: Pending → Processing → Done | Failed`); on startup the service re-enqueues every `Pending`/`Processing` document, so a restart loses no work.

## Alternatives
- **RabbitMQ from the start**: persistent queue, independent workers (e.g. OCR worker on a GPU box), retry/dead-letter built in. Cost: a fourth container to run and monitor, a consumer/ack model to learn alongside two new languages, and no current need — load is one user uploading a batch a few times a year.
- **No queue (synchronous HTTP)**: simplest, but ties up browser requests for seconds and fails on batches.

## Consequences
- Upload endpoint returns `202 { documentId, jobId }` immediately; the SPA polls `GET /documents/{id}` (SSE later if needed).
- Concurrency is bounded (default 2 parallel extractions) to protect the CPU-only OCR service.
- Polly retry + circuit breaker live in the consumer, not in the endpoint.
- Migration path: implement `IExtractJobQueue` over RabbitMQ when OCR moves to separate hardware or when a second worker process is needed. Business logic and the API contract stay unchanged.
