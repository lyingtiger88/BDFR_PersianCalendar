# Zurvan User XP / badges / ranking

## What ships

The general user progress system is separate from the administrator-issued
`zurvan_special_ranks` system. No amount of XP can grant Emperor, Empress
or Special Advisor privileges, and no badge has licensing authority.

Production Supabase objects:
- `zurvan_user_scores`: current XP, current/best streak, last rewarded UTC day,
  default-private leaderboard opt-in
- `zurvan_xp_events`: immutable server-only event ledger with unique
  `(account_id,event_key)`; clients cannot insert XP
- `zurvan_badge_catalog`: the displayed thresholds and descriptions
- `zurvan_user_badges`: permanent award records, keyed by user and badge
- `zurvan_claim_daily_xp(uuid)`: the only initial automatic-scoring rule
- `zurvan_set_score_visibility(uuid,boolean)`: authenticated opt-in preference
- `zurvan-user-progress` Edge Function (JWT verified): `status`,
  `claim_daily`, `set_visibility` actions

The Edge Function gets the authenticated user from `auth.getUser(token)`;
it never accepts an `account_id`, XP amount, badge ID or special-rank value
supplied by the client.

## Rules

Users explicitly press **Register today's activity** to earn XP, rather than
silently earning points from page refreshes or opening the app.
One claim per confirmed, active account per **UTC calendar day**:

| Streak | Reward |
| --- | ---: |
| First day | 20 XP |
| Second day | 25 XP |
| Third day | 30 XP |
| Fourth day | 35 XP |
| Fifth day | 40 XP |
| Sixth day | 45 XP |
| Seventh and later consecutive days | 50 XP |

If a day is skipped, the next claim starts a new streak. Previously earned XP
and badges are never deleted when a streak breaks. Concurrent claims and
repeated requests are serialized and deduplicated by Postgres.

| Level | Name (FA) | Minimum total XP |
| ---: | --- | ---: |
| 1 | تازه‌وارد | 0 |
| 2 | رهرو زمان | 100 |
| 3 | همراه زروان | 250 |
| 4 | کوشای زروان | 500 |
| 5 | زمان‌بان | 900 |
| 6 | استاد زمان | 1500 |
| 7 | فرزانه زمان | 2500 |
| 8 | اسطوره زروان | 4000 |

The first six earned badges are First Step (20 XP and a 1-day streak),
100 XP, 500 XP, 1500 XP, 7 consecutive days and 30 consecutive days.
Badges are only awarded by the transaction, never by modifying local settings.

## UI availability

- Windows: Settings > حساب کاربری > امتیاز و سطح کاربر. Special rank
  remains a visually distinct badge above the XP block.
- Android: Settings > امتیاز و نشان‌های من. The separate founder/editorial
  badge appears if the account was granted such a rank.
- iOS: Settings > امتیاز و نشان‌های من; expired-license activation view
  also links to achievements for authenticated users.

All three UI implementations query the same Edge Function and expose the
badge list, progress, history, daily claim, and leaderboard opt-in.
The leaderboard publishes only optional, explicitly enabled accounts'
usernames (or generic alias) and XP; it never publishes email addresses.

## Safety, testing and rollout

- All four tables have RLS enabled. `anon` and `authenticated` roles have
  no direct table writes; the trusted server uses the service role.
- History writes are append-only through the award function; all totals are
  calculated inside Postgres and must match the event ledger.
- Existing accounts start with 0 XP, with new account trigger
  `zurvan_init_user_score` creating new profiles automatically.
- Read-only licensed users can view their accumulated progress.
- Account deactivation prevents new claims and profile access.
- Data retrieval/network errors do not invent XP or local badges.
- Existing Emperor/Empress/Special Advisor grants are not modified.

**Not yet included:** points for offline Kanban completions, reminders,
event creation or learning milestones. Those require a trusted server-side
action log and deduplication; a local event notification alone must not
be accepted as proof of an action.

**Acceptance tests still required:** two concurrent claims; repeat daily
claim; UTC rollover; 7-/30-day streak; password-session expiry; private
leaderboard default; opt-out; account suspended; long-term badge retention;
end-to-end UI checks on real Windows, Android and iOS devices.

CI builds are intentionally not requested with this change set.
