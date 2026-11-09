# Zurvan Desk Account API

This service sits between Zurvan clients and ArvanCloud Object Storage.

Clients never receive S3 access keys or secret keys.

## Responsibilities

- account registration
- login
- logout
- session validation
- Vault ID assignment
- opaque recovery-envelope storage
- encrypted file list metadata
- encrypted file upload
- encrypted file download
- encrypted file rename metadata
- encrypted file delete

The service does **not** decrypt user files.

## ArvanCloud configuration

Set these environment variables on the server:

```text
ZURVAN_DESK_S3_ENDPOINT=https://s3.ir-thr-at1.arvanstorage.ir
ZURVAN_DESK_S3_REGION=ir-thr-at1
ZURVAN_DESK_S3_BUCKET=<bucket-name>
ZURVAN_DESK_S3_ACCESS_KEY=<server-access-key>
ZURVAN_DESK_S3_SECRET_KEY=<server-secret-key>
```

Optional:

```text
ZURVAN_DESK_ACCOUNT_DATA_ROOT=/var/lib/zurvan-desk
```

If the S3 variables are absent, the development server falls back to local blob storage under `App_Data`. This fallback is intended for local testing only.

## Run locally

```bash
dotnet run --project server/Zurvan.Desk.Api/Zurvan.Desk.Api.csproj --urls http://0.0.0.0:5088
```

Android emulator debug builds use `http://10.0.2.2:5088` by default.

## Security boundary

- passwords are PBKDF2-SHA256 hashed with per-user salts
- sessions are random opaque bearer tokens; only token hashes are stored server-side
- recovery phrase is never sent to the server
- Vault Key is never sent to the server
- filenames are client-encrypted before upload
- file contents are client-encrypted before upload
- Arvan credentials remain server-side only

## Collaboration API — server foundation (not deployed)

This repository now contains a **development-stage** group collaboration backend in
`Collaboration.cs`, hosted by the **same ASP.NET Core process** as Zurvan Desk.
No additional hosting subscription, Redis instance, managed database or paid service
is required to run a **single-instance test server**. This does not imply that the
existing Desk API is deployed or that a permanently available free host exists.

### Prerequisites

- .NET 10 SDK/runtime and a reachable HTTPS endpoint (for tests from other devices).
- A writable persistent directory, set with `ZURVAN_DESK_ACCOUNT_DATA_ROOT`.
- Existing Desk accounts, issued by `POST /api/v1/auth/register` or login.
- A bearer token from the **Desk API account system**. License-server/Supabase
  account identifiers are not yet federated with Desk accounts. Do not assume
  the identifiers are interchangeable.

### Routes

Authenticated HTTP endpoints, prefix `/api/v1/collaboration`:

| Method | Route | Purpose |
|---|---|---|
| GET | `/groups` | Groups of authenticated user |
| POST | `/groups` | Create a group; `{ "name":"Team", "agenda":true, "kanban":true, "school":false }` |
| GET | `/groups/{groupId}` | Group profile and members |
| POST | `/groups/{groupId}/invites` | Owner/assistant invites verified Desk ID; `{ "identifier":"alice", "role":"member" }` |
| GET | `/invitations` | Pending invitations for signed-in account |
| POST | `/invitations/{inviteId}/accept` | Target explicitly accepts an invitation |
| PUT | `/groups/{groupId}/members/{memberId}/permissions` | Set `canChat`, `canReport`, `canEdit` |
| DELETE | `/groups/{groupId}/members/{memberId}` | Remove member / leave group (owner protected) |
| GET | `/groups/{groupId}/items` | Shared Agenda/Kanban/School *generic work items* |
| POST | `/groups/{groupId}/items` | Create item: `title`, `mode`, `state`, `dueAt` |
| PUT | `/groups/{groupId}/items/{itemId}` | Edit using `expectedVersion` optimistic lock |
| GET | `/groups/{groupId}/messages` | Fetch recent group chat |
| POST | `/groups/{groupId}/messages` | Send message if permitted |
| GET | `/groups/{groupId}/reports` | Owner/assistant sees reports; members see only their own |
| POST | `/groups/{groupId}/reports` | Submit report if permitted; only relevant users notified |
| GET | `/groups/{groupId}/changes?after=0` | Catch up on missed events (cursor pagination) |

All above routes require `Authorization: Bearer <desk-session-token>`.
Only authenticated users with an accepted group membership can retrieve
group resources. A text identifier entered in Android's old *local* TeamPlanner
is **not** a real group invitation.

### Realtime connection

SignalR endpoint: `/api/v1/collaboration/live`.

The SignalR client must send the Desk session token through the **Authorization
header** (not a token in a URL query string). After successful negotiation,
the server adds the connection to an authenticated **per-user channel**.
Clients listen for `collaborationChanged`, then fetch updated objects with
their authorized HTTP endpoints; this event contains identifiers only, not
chat/report contents. `collaborationAccessRevoked` notifies removed members.

Events are dispatched only to **currently active member IDs**; sensitive
report change notices are sent only to the reporter and current managers.
Clients should save the last cursor and call `/changes?after=<cursor>` whenever
connecting/reconnecting. Concurrent item updates require `expectedVersion`.

### Hosting and security limitations

- This first implementation persists a single-instance JSON database to
  `collaboration.json` alongside `accounts.json`. It is **not** a production
  Postgres deployment and is unsuitable for concurrent server replicas or
  large groups. Protect and back up this directory; **messages are stored in
  cleartext** on the server, unlike encrypted Zurvan Desk files.
- The committed change log enables reconnect recovery, but a transactionally
  durable messaging outbox and background retry dispatcher are **not yet
  implemented**. Live delivery can be missed during a process crash; clients
  must reconcile after reconnect.
- There is no verified always-on public host, DNS or TLS setup supplied by the
  repository. A free test run may be done on a local machine, but for members
  on distinct networks an accessible host with persistent storage and HTTPS
  is required. Hosting quotas, uptime and any charges must be checked before
  choosing a provider.
- This is the **server foundation only**. Windows and Android clients have
  not yet been wired to the new endpoints. Push notifications, media files,
  a full drag-and-drop Kanban model, academic school/grade records, robust
  multi-tenant throttling and end-to-end security testing remain open.
- The current Desk account identity is separate from the earlier Zurvan
  license/Supabase identity. A verified account-link flow is needed before
  inviting by the canonical Zurvan ID.
- These commits carry `[skip ci]` at the user's request. They have **not**
  been compiled, built, deployed or live-tested.

### Development-only acceptance checklist

1. Create and authenticate two Desk test accounts.
2. Create a group with both Agenda and Kanban enabled.
3. Invite the second account by its exact Desk identifier.
4. Confirm it is not a member until it accepts the invitation.
5. Connect two authenticated SignalR clients. Create and update an item.
6. Confirm the second client receives `collaborationChanged` and can read it.
7. Set `canChat=false`; verify sending a message is forbidden while authorized
   reading still works.
8. Set `canReport=false`; verify report submission is forbidden.
9. Restore reporting; verify report content is visible only to the author/manager.
10. Update an item with a stale version; expect HTTP 409.
11. Remove a member; verify group reads/writes are forbidden and no later group
    events are dispatched to their user channel.
12. Reconnect using a saved change cursor; verify missed authorized changes
    are recovered. Restart the server and confirm JSON data persists.
