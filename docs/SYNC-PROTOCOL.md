# Sync protocol v1 — implemented in 0.2

The TypeScript API and Unity client implement this protocol. Core/HTTP tests use injected memory
storage; Google production adapters are implemented but have not been exercised against a cloud project.

## Snapshot and local persistence

The JSON snapshot is the `Town.Domain.World` object: schemaVersion/contentVersion/simulationVersion
(all 1), worldId, name, width, height, seed, money, speed, tick, paused, sandbox, buildings, families,
and claimedGoals. Enum kinds are stable numeric IDs 0–11. It is not a Unity Scene serialization.
The actual C# serializer output is a fixture accepted by the TypeScript validator.

Network bytes are a full gzip snapshot: maximum 8 MiB compressed, 16 MiB decoded. SHA-256 is over
the exact compressed bytes. Supported size/field/entity limits are enforced before committing.
Only schema 1 is supported; no migrations are currently needed or implemented. Unsupported versions
fail without replacing the existing world. There is no offline economy catch-up.

Local `worldId.town`: ASCII `TOWN1\n`, 64 lowercase SHA-256 hex characters, newline, then gzip of a
LocalWorld envelope. Header is 71 bytes. The envelope carries ownerUid, lastSyncedRevisionId,
conflictRevisionId, dirty, generation, world and an optional pending upload with exact gzip bytes
encoded as base64. Maximum local file size is 24 MiB; maximum decoded envelope is 48 MiB.

LocalRepository writes and flushes a temp file on the same filesystem, then File.Replace with `.bak`
(or first File.Move). Sequence numbers stop an old background write from replacing a newer one.
Load checks checksum and schema, falling back to `.bak` on corruption. Import creates a new identity
and unbinds cloud ownership. This protects against interrupted writes; it does not protect against
loss of the device or deletion of the whole app. Keep cloud/file exports for that case.

## Authentication and storage

All `/v1` routes require an Identity Platform ID token. The production adapter verifies signature,
issuer/audience/expiry via Admin SDK, checks revocation/disabled user, then an explicit tester UID
allowlist. UID is never taken from the body. No service-account key is shipped to Unity.
The client refresh token lives in Keychain; an unavailable native bridge falls back to session memory.
`/healthz` is public. Cloud Run permits transport; application authentication protects data.

Firestore paths use URI-encoded verified UIDs:

- `users/{uid}`: active world count and daily upload counter.
- `users/{uid}/worlds/{worldId}`: ownerUid, name, headRevisionId, headSequence, createdAt, updatedAt, deletedAt.
- `.../revisions/{revisionId}`: parentRevisionId, blobPath, blobGeneration, sha256, bytes, tick, createdAt, state.
- `.../requests/{key}`: requestHash, stable revisionId, createdAt, nullable result receipt.

GCS objects: `users/{uid}/worlds/{worldId}/revisions/{revisionId}.json.gz`. Runtime can create/read/list,
but cannot overwrite/delete existing snapshots. Reads pin the recorded object generation and verify SHA.
Direct client Firestore access is denied by rules; GCS is private. Ownership misses return 404.

## HTTP endpoints

| Method | Path | Result |
|---|---|---|
| POST | `/v1/worlds` | Body `{worldId,name}`; idempotent create, maximum 10 active worlds per UID |
| GET | `/v1/worlds?cursor=...&limit=20` | Owned worlds including tombstones |
| GET | `/v1/worlds/{id}` | Metadata, ETag, If-None-Match support |
| POST | `/v1/worlds/{id}/revisions` | Full gzip upload with CAS base and durable receipt |
| GET | `/v1/worlds/{id}/requests/{key}` | `{status:"pending"}` or final stored receipt |
| GET | `/v1/worlds/{id}/revisions?cursor=...` | Revision metadata, including conflict versions |
| GET | `/v1/worlds/{id}/revisions/{rev}/snapshot` | Exact gzip bytes and X-Snapshot-SHA256 |
| POST | `/v1/worlds/{id}/restore` | Body `{sourceRevisionId}`; copies snapshot into new CAS revision |
| POST | `/v1/worlds/{id}/resolve` | Same primitive as restore; source must belong to this world |
| DELETE | `/v1/worlds/{id}` | CAS tombstone; no hard deletion |

UUID IDs use lowercase canonical representation. Cursors are document UUIDs, ordered by document ID;
this is stable pagination, **not chronological ordering**. The client sorts the loaded history by server
createdAt; “kolejne rewizje” loads another page. limit 1–50. Forking happens in the client as a new worldId.

Upload headers: `Content-Type: application/gzip`, `Idempotency-Key: UUID`,
`X-Base-Revision: UUID` or `none`, `X-Snapshot-SHA256: lowercase hex`.
Do not set Content-Encoding; the API owns decompression. Restore/resolve also require key/base.
DELETE requires base. JSON requests use application/json and a 32 KiB body limit.

Success receipt: `{status:"committed",revisionId,headRevisionId,headSequence,sha256}` with HTTP 201.
A concurrent head change gives 409 and a receipt with status `conflict`; the alternate revision is durable.
Reusing a request key with changed bytes/base gives a different 409 error `idempotency_key_reused`.
Other responses: 401 auth, 403 tester not allowed, 404 absent/inaccessible, 410 tombstoned,
413 size, 415 content type, 422 format/checksum/version, 429 limit, 503 transient failure.
No success response is returned before durable commit.

## Recoverable commit

1. Authenticate/authorize, validate headers and read at most 8 MiB of body. Decompress with a 16 MiB
   bound, verify hash and JSON contract. The body is memory-buffered with a fixed limit, not an unbounded stream.
2. In a Firestore transaction read request, world, counters. Reserve a new key with a stable revisionId
   and fingerprint SHA(worldId, base, compressed-hash), or return/reuse the existing matching request.
   Count new attempts against 120 uploads per UTC day per UID. Retries do not consume another attempt.
3. Write GCS object with ifGenerationMatch=0. On a retry, verify the existing object's bytes/hash.
   Object metadata stores SHA; the Firestore request stores the remaining recovery identity.
4. In a second transaction read request/world before writes. Replay any completed result.
   If deleted, reject without resurrecting. If current head differs from base, preserve a conflict
   revision and leave head unchanged. Otherwise advance head and headSequence. Store receipt atomically.
5. Return receipt. A lost response is recovered by request lookup or retry with the same exact bytes/key.

The immutable object write is outside Firestore transaction callbacks. A failure after object write
leaves a pending request recoverable by retry. A pending upload racing deletion remains rejected on retry;
there is no background reconciler marking it final. Completed receipts replay safely after deletion.

Requests/revisions are not TTL-deleted. There is no GC or age-based snapshot lifecycle rule. Orphan
objects may remain until a future reference-aware repair/GC task is implemented. Firestore metadata
backups/PITR are not configured by this prototype; this must be addressed before wider use.

## Client behavior

- Local-first play; autosave every 30 seconds and after edits, lifecycle checkpoint before suspension.
- Exact pending key/base/hash/bytes must be saved locally before any upload attempt.
- A later simulation/edit increments generation; receipt for an older generation leaves current state dirty.
- Session stamps invalidate results after logout/login changes. A bound world cannot upload under another UID;
  the player must explicitly create a copy.
- Upload coalesces unsent edits only. Once a pending request exists, its bytes stay fixed through retries.
- Auto-upload attempts every 120 seconds while active. On resume the client schedules an attempt; this is not
  guaranteed background networking. A newer remote world is explicitly opened from the cloud panel.
- Conflict UI: continue local using a fresh remote base and CAS; load cloud; or create an independent copy.
  Loading cloud first preserves a dirty local world as another local world, even if it was not currently open.
- Historical snapshots open as new local worlds. They do not silently rewind the existing world.
- A clean local save with newer cloud head reports that a newer version is available. No device-clock last-write-wins.
- Existing local worlds remain visible after logout on the same device; cloud authorization stays account-scoped.
- One identity/world can be used on two devices; simultaneous editing preserves branches, not merged roads/families.

## Verified and outstanding

Automated service tests cover concurrent commits, parallel replay, changed-key payload, storage failure,
post-object/pre-transaction failure, delete races, restore CAS, malformed saves/gzip bomb, daily quota,
world quota/pagination/ownership, HTTP authentication/upload/download/ETag and C# format compatibility.
Local C# tests cover building/undo/reward integrity, roads/families, validation and persistence recovery.

Real Unity networking, actual token expiry/revocation on GCP, native Keychain and app termination during
upload require device/cloud integration tests. Memory-backed tests do not prove IAM or Apple behavior.
