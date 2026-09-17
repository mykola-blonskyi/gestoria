# ADR-0015: Documents live in MinIO on the author's VPS, encrypted client-side

**Status:** Accepted · **Date:** 2026-09-18

## Context
ADR-0013 restored the autónomo scope, so received invoices, issued invoices and bank statements are real files the author is legally required to keep for years. The tax prescription period and the Código de Comercio period differ; both need checking.

ADR-0010 put v1.0 on the author's own machine. A laptop is not a safe place for records with a multi-year retention obligation. A disk failure in year two is a more realistic threat here than an attacker.

Two candidate answers. Back up to the VPS with `restic`, which solves loss and nothing else. Or object storage the application reads and writes directly, which solves loss and gives multi-device access as well.

The author already runs MinIO on his VPS. That removes the only real argument against it, which was the cost of standing up and operating another service.

## Premises

| Premise | Stated or concluded | Source |
|---|---|---|
| MinIO is already running on the author's VPS | Stated | Author, 2026-09-18 |
| The author is still the only user | Stated | Author, ADR-0010 |
| Documents must be retained for several years | Concluded. The exact period is unverified | SPEC-013, needs checking |
| Multi-device access is wanted | Neither. Not required today, and MinIO provides it either way | — |

## Decision
Document blobs live in MinIO on the author's VPS, behind the existing `IFileStorage` port with an S3-compatible implementation.

Blobs are encrypted **client-side before upload**, AES-256-GCM with a per-document data key wrapped by a master key, exactly as SPEC-013 originally specified. MinIO never holds plaintext and never holds the master key. Server-side encryption is not the primary control, because with SSE the key sits on the same machine as the ciphertext and a VPS compromise reads your tax documents.

The bucket is private, credentials come from env, transport is TLS, and object versioning is on.

**The master key is escrowed outside both the laptop and the VPS, and this is a release blocker.** Envelope encryption with a key that exists in exactly one place turns a backup into a way to lose the data twice. The key goes in the author's password manager with a written recovery procedure, and a restore from MinIO using only the escrowed key is rehearsed **before the first real document is uploaded**.

## Alternatives
- **`restic` to the VPS.** Cheaper, with zero application code and encryption, deduplication and snapshots built in. It was the recommendation until MinIO turned out to be running already. It solves loss and gives the application nothing.
- **Local disk only.** Loses everything on a disk failure, against a multi-year retention obligation.
- **MinIO with server-side encryption.** Simpler, and it stores the key on the machine holding the ciphertext.

## Consequences
- SPEC-013's blob encryption returns as a v1.0 control. Dropping it was right for files on a FileVault disk and wrong the moment they leave it.
- The VPS enters the threat model. MinIO stays off the public internet, credentials are rotated, and the bucket is private.
- **The pure-engine boundary pays off here.** The engine never touches documents, so MinIO being unreachable can never block a calculation. Only upload and document retrieval need the network.
- `IFileStorage` was already a Phase 3 deliverable with local and S3-compatible implementations. Nothing in the architecture changes, and the local implementation stays for tests.
- ADR-0010 is untouched. This is the author's own storage, not the application hosted for other people, so there is still no second user and no data-controller role.
- The restore drill moves from Phase 6 to before the first real upload.
