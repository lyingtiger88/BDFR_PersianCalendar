# Zurvan referral identifier and Premium Silver reward

## Rule

Every registered Zurvan account receives a random **8-character uppercase
hexadecimal referral identifier** (not a link). In the main Zurvan sign-up
form, a newly registered user may enter the inviter's identifier once.
It is passed using Supabase Auth signup's `data.zurvan_referral_code` field.
The backend captures it in a one-time `auth.users AFTER INSERT` trigger;
later edits to user metadata cannot reassign a referee.

The inviter receives **100 XP per confirmed, successfully qualified new
account**, and **15 uniquely qualified referrals make a 12-month
`premium_silver` license available for explicit redemption**.
Each additional block of 15 earns another year, with the new year's start
stacked after the previous active Silver expiry. This referral entitlement
is stored separately from the existing paid/permanent Zurvan license.
All clients receive it through `zurvan-license-status` with
`mode=premium_silver` if it is the selected active license.

A successful referral requires ALL of:
- The introduced account did not exist before the inviter identifier was issued.
- The new user entered the identifier **at original signup**.
- A different account (not self; each invitee can link to only one inviter).
- A verified email, active Zurvan account, AND either a verified claimed
  device trial or a bona fide active paid/permanent Zurvan license.
- Invitation qualification is done on the backend, not by client-submitted
  `invite_count` values.

## User flow

1. Inviter opens Settings > Account > XP / Badges / Referrals; copies the
   identifier, without generating any invite link.
2. New user selects **Create account**, enters email, password and the
   optional **Inviter identifier** in Windows, Android, iOS or the web form.
3. A read-only public endpoint checks whether the identifier exists; it does
   not reveal the inviter's identity.
4. The signup trigger records the inviter once; pending referrals show in
   inviter progress when retrieved.
5. After email and genuine-device / paid-license verification, the backend
   awards exactly 100 XP once, and updates the successful invite counter.
6. When 15 valid invites accumulate, the inviter selects **Redeem 1-year
   Premium Silver** and receives a dated server-side entitlement.
7. If the inviter already has an active subscription/lifetime license, the
   reward never replaces or downgrades that license.

## Server components

- `zurvan_invite_codes` — stable code per account
- `zurvan_referrals` — one invited account per inviter, pending/qualified state
- `zurvan_referral_silver_rewards` — reward number, fixed 1-year term,
  independent of commercial license, revocable by trusted operations
- `zurvan_capture_signup_inviter` — one-time signup attribution
- `zurvan_refresh_referrals` — verified qualification and deduplicated XP
- `zurvan_claim_referral_silver` — serialized and repeat-safe redemption
- `zurvan-referral-validate` — public validity-only API
- `zurvan-user-progress` — authenticated view, counters and `claim_silver`
- `zurvan-license-status` — paid, lifetime, Silver and trial entitlements

All tables have RLS enabled and direct client SQL writes are revoked.
A cryptographically random referral identifier does NOT itself authorize
account access or any paid functionality.

## Limitations and acceptance tests

Real-device / email-flow tests are still required. The program depends on
Play Integrity / device recall and Apple DeviceCheck being operational in
production to qualify mobile free trials; generic device IDs alone do not
prove a physical phone. Fraudulent farms of paid accounts still require
business monitoring and abuse controls.

Test with genuine signup flows: valid/invalid identifier, missing identifier,
confirmation delayed, referral attribution after metadata edits, self
referral, duplicate invitation, repeat reward request, 14 vs 15 successful
invitations, 30 referrals, Silver expiration and Silver + permanent license
coexistence. Confirm 100 XP cannot be earned twice for the same person.

No manual CI build has been requested for these commits.
