# Zurvan Companion and Sync Architecture

Protocol version: **1**

Zurvan is local-first. Windows, Android and iOS keep their own local data and exchange changes through a versioned companion protocol. A device can continue working offline and synchronize later.

## Product rule

Every product feature must be represented in `platform/feature-parity.json` for all three clients:

- Windows
- Android
- iOS

A feature may temporarily be marked `planned` while a mobile client is being implemented, but it may not be omitted. CI validates this contract so platform drift is visible immediately.

## Pairing flow

1. The Windows or mobile host creates a short-lived pairing session.
2. The host advertises `_zurvan-sync._tcp` on the local network.
3. The host shows:
   - a QR pairing payload, and
   - a six-digit fallback code.
4. The joining device identifies itself with a persistent Device ID.
5. Both devices verify the protocol version and the short-lived session.
6. The device is stored as a trusted companion only after user confirmation.
7. A user can revoke a trusted device at any time.

The network transport is deliberately separated from calendar domain logic through `ICompanionTransport`.

## Security direction

The transport implementation must use:

- ephemeral ECDH P-256 during pairing;
- a user-confirmed six-digit short authentication string;
- a per-pair derived symmetric key;
- AES-256-GCM authenticated encryption for sync frames;
- key storage through the platform secure store:
  - Windows Credential/DPAPI-backed storage;
  - Android Keystore;
  - iOS Keychain.

Private calendar item text must never be placed in an unauthenticated discovery packet or pairing advertisement.

## Synchronization model

Changes are transferred as `SyncEnvelope` objects.

Each envelope contains:

- entity kind;
- entity ID;
- mutation type (`Upsert` or `Delete`);
- monotonic local revision;
- UTC modification timestamp;
- origin device ID;
- JSON payload;
- optional content hash.

Deletes are synchronized as tombstones. A device that was offline cannot accidentally resurrect an item deleted on another device.

Batches are acknowledged with a checkpoint. The sender only advances its peer checkpoint after receiving `SyncAck`.

## Conflict policy

The shared `SyncMergePolicy` is deterministic:

1. larger revision wins;
2. if revisions match, newer UTC modification time wins;
3. at the same revision/time, delete wins over an upsert;
4. identical hashes are treated as already synchronized;
5. if two devices independently edited the same revision/time with different content, keep a conflict copy instead of silently losing data.

UI clients should show unresolved conflict copies in Activity Center / Sync history.

## Data scope

Initial sync scope:

- events;
- tasks;
- notes;
- personal/special occasions;
- reminder definitions;
- syncable user settings;
- occasion picture mappings.

Not synced by default:

- activity-log diagnostics;
- local cache generated from time.ir;
- platform-specific notification IDs;
- temporary files;
- local crash diagnostics.

## Transport phases

### Phase 1 — Local companion

- mDNS/Bonjour discovery
- LAN direct connection
- QR / six-digit pairing
- encrypted two-way sync
- manual Sync Now and automatic sync while both devices are reachable

### Phase 2 — Background companion

- Android foreground/background work constraints
- iOS background refresh limitations
- Windows tray/background host
- resumable checkpoints

### Phase 3 — Optional relay

A cloud relay may be added later for devices not on the same network. The relay must carry encrypted envelopes and must not require plaintext calendar contents.

## Platform UI

Windows, Android and iOS expose a Companion screen containing:

- trusted devices;
- Pair New Device;
- Sync Now;
- last successful sync;
- pending changes;
- conflict count;
- revoke device.

The Android and iOS scaffolds already include the Companion navigation destination. Windows will use the same Core protocol rather than a separate sync format.
