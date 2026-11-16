# Zurvan offline activation — implementation status

The offline activation issuer lives at `tools/Zurvan.LicenseIssuer`; shared RSA-PSS verification lives in `src/BDFR.PersianCalendar.Core/ZurvanLicense.cs`. Android has a device-code and activation-code field under Settings.

## Issuer calculator

Use a secure, private computer for the issuer. The signing private key must **never** be committed to GitHub, embedded in any application, or uploaded as an artifact.

```sh
dotnet run --project tools/Zurvan.LicenseIssuer -- keygen ./zurvan-issuer-private.pem ./zurvan-issuer-public.pem
dotnet run --project tools/Zurvan.LicenseIssuer -- issue ./zurvan-issuer-private.pem ZRV-0123456789ABCDEF01234567 standard 2028-12-31T23:59:59Z
dotnet run --project tools/Zurvan.LicenseIssuer -- verify ./zurvan-issuer-public.pem ZRV-0123456789ABCDEF01234567 ZRV1.<payload>.<signature>
```

The device code is a one-way SHA-256-derived identifier. Android uses `Settings.Secure.ANDROID_ID` scoped to the application/device; it is not a guarantee of permanent hardware identity across resets, profile changes or signing changes.

For Android release builds, convert **only the public PEM** to a base64 UTF-8 value and provide Gradle property `ZURVAN_LICENSE_PUBLIC_KEY_B64`. Without it, Android rejects all activation codes (fail closed).

## Security and rollout rules

- RSA-3072 / SHA-256 / RSASSA-PSS, signed `ZRV1` payload.
- Clients verify product, exact device code, signature and optional expiry.
- A pasted string without a valid signature cannot activate the application.
- Store an accepted code only after verification; re-verify on future launches.
- The issuer private key belongs only on an administrator-controlled machine.
- Do not change the existing application data directory or delete existing user data.
- License enforcement policy and the actual enable/disable experience are **not yet implemented**.
- iOS and Windows activation UI and trusted public key provisioning are **not yet implemented**.
- Full hardware binding must account for platform privacy and device changes.

This is a foundation, not a deployed production licensing system.


## Trial and read-only enforcement (2026-11-15)

The hosted Supabase project `zurvan-license-server` now has:
- A server-side, one-time 45-day trial per verified Zurvan account and product.
- Trial issuance through the authenticated `zurvan-license-start-trial` Edge Function.
- Entitlement status through `zurvan-license-status`, including `can_write`, `mode`, `expires_at`, `days_remaining`, and `trial_available`.
- Expired or revoked licenses return a non-writable entitlement. Existing data is retained.
- The trial RPC is executable only by `service_role`; clients cannot call it directly.

Windows now requests the server entitlement at startup and after account sign-in. When no license exists, it requests the trial and re-reads status. Non-settings workspace controls are disabled when the server reports read-only or verification is unavailable.

Android now starts a 45-day local device-bound trial on first launch, shows the remaining days, preserves workspace navigation after expiry, and guards calendar data, Kanban and Desk file mutations in the data/view-model layer. The local Android trial is a rollout fallback, not server-grade anti-tamper protection: clearing app data or reinstalling can reset local state. Android account purchase synchronization and a provisioned release public key are still required before paid activation is production-ready.

The 45-day trial is account-scoped in the hosted database. A trial cannot be reissued for the same account/product after expiry because the license row is retained and the account/product pair is unique. The optional device fingerprint parameter can attach a registered device activation; current Windows account flow starts the account trial without a device fingerprint.

**Not yet complete:** payment-provider checkout/webhook, Windows/Android end-to-end build and runtime tests, secure Android server-account sign-in/entitlement refresh, and full cross-device transfer verification. Do not describe licensing as production-complete until these are tested.


## Manual bank transfer checkout (2026-11-15)

The website source is web/manual-payment/index.html and the backend is supabase/functions/zurvan-manual-payment/index.ts.

1. Customer signs up with Supabase Auth and confirms their email.
2. On first signed-in website visit, the init action links the account and issues the 45-day trial if eligible.
3. Customer views the merchant's bank card and account-holder name, enters full name and phone number, and uploads a JPG, PNG or PDF receipt (maximum 5 MiB).
4. Request is stored as pending. It does not activate an annual subscription automatically.
5. A superadmin independently verifies the real bank transaction, accesses the private receipt with a short-lived signed URL, then approves or rejects.
6. Approval atomically changes the license to an annual subscription and records the reviewer. Repeated approval fails. Rejection does not modify the license.
7. Windows revalidates the subscription on startup and periodically.

Merchant bank number and cardholder name are not yet configured. No number has been invented. An actual superadmin must enter the bank details in the web admin panel before real submissions are enabled.

Security: Edge Functions validate JWTs; private receipt bucket and RLS-protected request records; direct client table access denied; only superadmins can list or approve requests. Service role key never goes to the static website.

Deployment: the HTML was committed to GitHub but is not yet deployed to the live kaliia.ir hosting. Deploy it through the hosting provider and ensure the actual HTTPS site origin is allowed by the Edge Function. Confirm data privacy disclosure and customer support contact before going live.

Outstanding: runtime testing and Android server-account subscription sync. The Android local trial and signed offline code are not yet automatically unlocked by a manually approved online subscription. Account-based trials alone are not robust against multiple accounts on one physical device.


## Android repository write guard (2026-11-15)
The Android local repository now checks the current write entitlement at its private persistence boundaries for plans, occasions, media tags, notes and reminders. UI preference changes remain available in read-only mode. Automatic expiry cleanup now stops when the product is read-only, preventing license expiry from deleting existing calendar records. The Android trial authority is still the existing local device policy; this change is a defense-in-depth repository guard, not yet the Supabase account-entitlement sync. The generic Kaliia endpoint is available, but Android Supabase sign-in/token storage and periodic refresh must be completed before cloud entitlement becomes authoritative on Android.


## Restricted historical/Pahlavi occasion feed (Android)

Android Settings > Calendar & Occasions now supports hiding all events or selecting
solar/Persian, lunar/religious, and Gregorian/international occasions independently.
Personal entries remain stored when a feed is hidden. An additional Pahlavi
historical feed and optional imperial-era year label are **hidden by default**.

Access is checked at render time and when saving the preference. It is granted only by:
- An unexpired, device-bound, RSA-PSS signed offline activation code with Edition
  `pahlavi` or `special` (not by `standard`).
- The existing hard-coded preview codes, **only in DEBUG builds**, exactly as the
  existing activation validator permits. Ordinary 45-day trials do not grant access.

An issuer holding the private signing key can grant access to a specific device:

```bash
dotnet run --project tools/Zurvan.LicenseIssuer -- issue ./issuer-private.pem ZRV-0123456789ABCDEF01234567 pahlavi 2027-12-31T23:59:59Z
```

To revoke an offline grant, allow its expiry or implement a server-synced
revocation check; an unexpired offline signed license cannot be remotely revoked
by the current Android client. Never place the private key in the app or repository.
The initial curated Pahlavi records are **nonofficial historical observances**, not
legal holidays. Production rollout requires build/device acceptance testing.
