# Zurvan Collaboration — zero-cost Supabase deployment

**Status (2026-11-09): committed to GitHub; SQL has NOT been applied to the live project. No build has been run.**

## Architecture decision

Zurvan Windows and Android Desk clients both point, by default, to:

`https://yztithydgdvydaktuxlw.supabase.co/functions/v1/zurvan-desk-api`

The .NET 10 Desk API under `server/Zurvan.Desk.Api` is an alternative
self-hosted implementation, not the runtime of that Supabase Edge Function.
Updating the .NET API does **not** change the deployed Supabase function.

For the existing Supabase project and zero recurring software cost, prefer:

- Supabase Auth for verifiable user identity (or a securely implemented
  bridge from Zurvan's existing account/activation identity).
- PostgreSQL tables and Row Level Security for groups, membership,
  invitations, permissions, tasks, chat, reports and the event feed.
- Supabase Realtime **Postgres Changes** on `collab_events` for live updates.
- Clients retrieve the full changed object using authenticated requests;
  live feed contains only minimal event metadata.
- A monotonically increasing event cursor supports reconnect and missed updates.

SQL source: `supabase/migrations/20261109_000001_zurvan_collaboration.sql`.

## Important identity boundary

The current `zurvan-desk-api` Edge Function implements an account API
that is not automatically the same as Supabase Auth user identity.
The PostgreSQL migration uses `auth.uid()` and references `auth.users`.
**Do not deploy UI features relying on these RLS policies until the clients
authenticate with Supabase Auth or an audited identity-bridge exchanges the
existing verified account identity for a Supabase-authenticated session.**
The arbitrary local profile identifier from the old Android TeamPlanner
does not prove ownership of a Zurvan account.

The precise linkage to Zurvan's existing license system remains an open
integration item. Do not copy license secrets or a service-role key into apps.

## Applying the migration (only when authorized and connection supports SQL)

1. Verify the target Supabase project ID and backup strategy.
2. Review the migration and its grants/policies with the target schema.
3. Apply the SQL in the Supabase SQL Editor or connected SQL action using
   a privileged migration role. The migration is wrapped in a transaction.
4. Verify tables `collab_groups`, `collab_members`, `collab_invitations`,
   `collab_items`, `collab_messages`, `collab_reports`,
   `collab_events` have RLS enabled.
5. Verify the `supabase_realtime` publication includes `collab_events`.
6. Create **two non-production test users in Supabase Auth** and test
   invitation/acceptance with member UUIDs.
7. Verify that an uninvited account receives zero group or message rows.
8. Grant `can_chat=false` and `can_report=false`; verify writes are denied.
9. Verify non-managers cannot edit items or assign assistant roles.
10. Confirm the report author and managers are the only users allowed to
    read private report events and report content.
11. Subscribe to `collab_events` from two authenticated test clients;
    verify creating/editing an item sends a change event to authorized
    receivers. After disconnecting a client, use `seq > last_seq` to
    catch up and check for duplicates.
12. Confirm removed members lose SELECT access and receive no further
    authorized Postgres Changes.
13. Measure the limits of the current free plan (database storage,
    concurrent Realtime connections, message volume and project inactivity).

## Authenticated client subscription model

Pseudocode only — no client implementation or runtime deployment yet:

```text
sign in with verified Supabase Auth credentials
subscribe to postgres_changes on public.collab_events, filtered by group_id
on INSERT:
   if seq <= last_seen_seq: ignore
   refetch authorized item/message/report/group resource
   persist seq
on reconnect:
   query collab_events where group_id = G and seq > last_seen_seq order by seq
   apply authorized missed events then resume live subscription
```

With Realtime Postgres Changes, group RLS is evaluated for the subscribing
user. Do not enable broadcast of full chat/report/grade payloads without
per-recipient authorization. Normal Postgres Changes subscriptions are
suitable for a small prototype but are not an unlimited delivery guarantee.

## Optional ASP.NET Core backend

A separate prototype has been added to the existing .NET Desk server at:
- `server/Zurvan.Desk.Api/Collaboration.cs`
- `server/Zurvan.Desk.Api/Program.cs`

It contains a verified *Desk-account* group/invitation/permission model,
API for shared work items, messages, reports, SignalR notifications,
version checks and cursor-based reconciliation. It is not deployed.
Its JSON file persistence and in-process SignalR connections are
single-server development infrastructure, not a scalable or durable
production hosting arrangement.

**Do not connect the .NET prototype and Supabase collaboration data as if
they were the same database.** Choose one authoritative backend for any
given deployment. The free Supabase path is currently preferred.

## Remaining work before launch

- Run and review the migration on the actual project.
- Create or verify a secure Supabase Auth identity bridge with Zurvan IDs.
- Implement Windows/Android authenticated Realtime clients and optimistic
  synchronization, as well as permission-aware UI.
- Implement full Kanban boards/columns, academic entities and attachments.
- Offline command queue, push notifications, structured audit tests.
- End-to-end authorization/revocation tests on real accounts and devices.
- Confirm free tier limits and accept potential pauses/quotas.
- No GitHub Actions build, production publish or live migration until authorized.

## Implementation update — 2026-11-09

The migration has now been applied to the existing project
`yztithydgdvydaktuxlw`. The collaboration Edge Function
`zurvan-collaboration` is deployed and active (version 4).
It validates the existing opaque Desk session against
`zurvan_desk_sessions` and resolves the account through
`zurvan_desk_accounts`; it does not require a separate Supabase Auth login.
The Edge Function source is tracked at
`supabase/functions/zurvan-collaboration/index.ts`.

Implemented authenticated routes include:
- group create/list/detail
- invitations by verified Desk identifier and explicit acceptance
- member permission changes and member removal
- shared Agenda/Kanban/School generic items
- group chat and permission-gated private reports
- event cursor endpoint for reconnect/reconciliation

Windows and Android now have API clients and initial UI for online group
creation, invitations, shared task creation, chat, and polling. Both clients
poll the event cursor every three seconds while the collaboration screen is
open and refresh shared items/messages after new events. This is **near-live
polling**, not a persistent WebSocket or push channel. Android also has
invitation acceptance; Windows can accept an invitation from its invitation
prompt.

Database smoke checks ran inside a rolled-back transaction: owner membership
trigger, adding a second member, item insert, message insert and event triggers
all passed. No test accounts or business data were retained. The current
Desk accounts table has zero rows, so end-to-end API testing with two real
accounts has not yet been possible.

Security advisors still report informational "RLS enabled, no policies" on
collaboration tables. This is intentional: direct anon/authenticated table
access is revoked and the service-role Edge Function is the sole access path.
The exposed security-definer trigger functions have had EXECUTE revoked from
public/anon/authenticated. Existing unrelated project findings (including
leaked-password protection configuration) are outside this migration.

Not yet complete: direct WebSocket subscription, offline write queue,
push notifications, full timetable/homework APIs and UI, granular audit UI,
real two-account end-to-end test, and cross-platform iOS client.
Do not represent these items as finished.

Build validation requested after wiring the Windows and Android clients. The
GitHub Actions workflow is triggered with the `[pc-android-only]` marker to
compile Core, the Windows desktop app and the Android debug APK; iOS and
Theme Studio jobs are intentionally excluded from that targeted run.

Android first compile attempt found and fixed a missing closing brace in the
new Compose group panel. The corrected source is committed; the targeted
Android/Windows build is being rerun.

Final targeted build after the Windows invitation payload adjustment: rebuild
Core, Windows desktop and Android from the latest main commit. This is the
release candidate build for the collaboration client changes.
