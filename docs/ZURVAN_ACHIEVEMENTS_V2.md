# Zurvan Achievements v2 — Windows and Android

The six achievement badges are **not XP levels, license privileges, or referral rewards**.
The legacy database badge IDs `xp_100`, `xp_500` and `xp_1500` are retained
for backward compatibility only; their new meaning is **not** an XP threshold.

| Server badge_key | Persian name | Idempotent condition |
|---|---|---|
| `first_step` | قدم اول | First authenticated app visit |
| `xp_100` | ستاره آغاز | A successfully saved non-empty note or event |
| `xp_500` | کوشای زروان | Explore three distinct recognized sections |
| `xp_1500` | استاد زمان | Visit all twelve Persian months of one year |
| `streak_7` | هفت روز پیوسته | Seven consecutive server-confirmed UTC visit days |
| `streak_30` | یک ماه پایدار | Thirty consecutive server-confirmed UTC visit days |

## Architecture

1. Local calendar, SQLite notes and reminders are authoritative for their own data;
   achievements **cannot block or undo local writes**.
2. Windows `ZurvanAchievementOutbox` queues immutable, account-scoped events
   in `%LOCALAPPDATA%/BDFR/PersianCalendar/achievements-pending.json`.
   File/network work occurs in a background task and exceptions are logged.
3. Android `ZurvanAchievementTracker` uses a bounded SharedPreferences queue,
   background I/O, a Mutex, account-scoped replay, and a guest event slot until login.
4. Both platforms send only a small JSON payload to
   `POST /functions/v1/zurvan-achievements` with action `record`, or query
   badges with action `status`.
5. The dedicated Edge Function checks the user's verified, active account against
   Supabase Auth, derives the account ID from JWT, and calls an internal SQL function.
   The client never supplies an account ID or XP amount.
6. The SQL function serializes concurrent activity claims per account, deduplicates
   activities via a unique constraint and badge awards via a separate unique constraint.
   Network retries, repeated navigation and UI rerenders do not create extra awards.
7. XP accounting (`zurvan-user-progress`) and license checks are independent.
   Windows and Android fetch badge status separately from XP/referral summaries.

## Important boundaries and recovery

- The persisted pending-event queue must not be cleared when signing out.
  Another account cannot claim those account-scoped events.
- If offline, note/event and navigation evidence is queued for later.
  **Visit streaks count server-confirmed UTC days**. Offline days cannot be
  reliably backdated from an untrusted device clock.
- No migration deletes existing notes, XP balances, licenses or badge awards.
- A broken achievement request should not crash or block application startup,
  calendar rendering, note writes or other controls.
- A successful build is not proof of native WinUI stability on a user's Windows
  installation. Investigate native `combase.dll` crashes independently if they persist.

## Regression test matrix

- App open signed out, then sign in; first step.
- Save an empty note (no badge); save non-empty note or event (one badge only).
- Explore one section repeatedly (no badge); three distinct sections (one badge).
- Visit 12 months in one year, including random order and backtracking.
- Check in for 7, 29 and 30 UTC calendar days and verify unlock thresholds.
- Re-login and switch accounts without cross-account awards.
- Disable internet during a note save; verify note persists and no crash;
  reconnect and verify badge replay.
- Open and close settings while badge status refreshes; no UI exception.
- Verify XP, Silver and referrals do not change when achievement events are recorded.
- Verify build output for both Windows and Android, then test on real devices.
