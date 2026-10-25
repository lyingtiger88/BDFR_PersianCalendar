# Zurvan team scheduling implementation status

## Included in Android (offline foundation)
- Plans tab contains a pastel-colored, horizontally scrollable seven-day schedule, plus local group and profile tabs.
- A device-local profile has a display name, a user-chosen identifier, and a photo stored in the app's private files.
- Group owner can create a locally stored group name, add member identifiers and assign roles of member or assistant manager. These roles are **display-only until a secure service is connected**.
- Reminder lead-time preference can be selected (10/15/30/60 minutes). This selection is persisted but **does not yet schedule team notifications**.
- Day details now merge personal occasions with a small built-in offline list. It is not a complete Iranian holiday source and observed lunar holidays need authoritative annual verification.

## Required for real cross-device teams
1. Authenticated accounts; user IDs issued and verified by the server.
2. Server-side Group, Membership, Role, SharedSchedule, Invitation and Notification tables.
3. Authorization rules: owner manages group and assistant roles; owner and assistant may create/edit shared schedule; members have read-only access.
4. An invite/accept handshake; entering an arbitrary ID must never grant access.
5. Secure event synchronization with offline caching and conflict handling.
6. Device notifications fired at a configurable offset with timezone and daylight-saving handling; remote update pushes.
7. Activity audit and handling of removed members.

Without that backend, app copies on different phones **do not** automatically exchange schedules. Do not describe the current UI as cloud/team sharing.

## Build policy
Changes were committed with [skip ci]; build when requested.
