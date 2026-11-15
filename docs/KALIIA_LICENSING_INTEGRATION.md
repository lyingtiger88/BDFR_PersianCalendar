# Kaliia Soft licensing integration — Zurvan

Product ID: `zurvan`. Platforms: Windows 11 WinUI 3 (.NET 10), Android Kotlin/Compose.

## Central authority
Supabase project `yztithydgdvydaktuxlw`; authenticated Edge Function `kaliia-entitlement` (JWT required). POST `/functions/v1/kaliia-entitlement` with JSON `{"action":"status","product_id":"zurvan"}` or `{"action":"start_trial","product_id":"zurvan"}`. Send publishable key in `apikey` and user's verified access token as Bearer. Never ship service-role credentials.

## Entitlement contract
`entitlement.status`: `active`, `expired`, `revoked`, `suspended`, or `trial_available`; `can_write` boolean; `expires_at`; `days_remaining`; `license_type`. Start trial is idempotent and account/product-scoped; it cannot reset an existing expired trial.

## Enforcement
- Windows: integrate with existing `ZurvanAuthClient`; request server entitlement after verified sign-in, on startup and every 12 hours while online. Cache only a signed/validated short-lived entitlement snapshot using Windows Credential Locker/DPAPI. Do not treat a failed network request as a new trial. If no previously valid cached entitlement exists, fail closed for mutations and retain read-only access.
- Android: replace the current local-only `ZurvanTrialPolicy` as the commercial authority with server account entitlement. Keep local clock rollback/device binding only as anti-tamper signals, not as the source of trial issuance. Secure tokens with Android Keystore-backed storage.
- Apply one shared `CanWrite` guard at repository/service boundaries for task, event, reminder, note, occasion and Desk mutations. UI disabling is supplemental only. Keep reads, search, export and backup available in read-only mode.
- Revalidate before each sensitive write when online; queue no writes while read-only/offline entitlement is stale. Never delete or migrate user data due to expiry.

## Existing state and next work
The repository already has an offline RSA activation foundation, a Windows Supabase auth client, an Android local trial policy, and the existing Zurvan-specific server endpoints. Existing licensing docs state that Windows online entitlement/read-only enforcement is partially present, Android purchase sync is incomplete. The new generic endpoint is now deployed and can be used by other Kaliia products; migrate Zurvan clients only after parity tests so the current activation path is not regressed.

## Acceptance tests
45-day exact boundary; one trial per account/product; two products on one account remain independent; approved subscription overrides trial; revoked/suspended/expired => read-only; network unavailable with valid cache vs no cache; clock rollback; Windows and Android data preserved; no service-role key in binaries.
