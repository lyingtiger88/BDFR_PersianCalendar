# Zurvan account, payment and device-trial rollout

## Intended policy

- Verified Supabase Auth users are enrolled automatically when the Windows app starts its first trial check.
- A **free 45-day trial is issued only once per device identity** and only once per account/product.
- The trial device is recorded by a unique server-side claim. Different free accounts cannot claim the same device. The desktop first reads the motherboard serial from WMI (`Win32_BaseBoard.SerialNumber`), with motherboard manufacturer/product as namespacing. If it is missing or a known placeholder, new free-trial issuance fails closed and directs the customer to administrator device review. BIOS serial, publisher-scoped ID and MachineGuid are NOT fallback identifiers for free trials. All values are normalized and locally SHA-256 hashed with a Zurvan domain prefix; raw hardware serials never leave the app. WMI access uses the managed `System.Management` package, borrowing the query approach (not the Node.js addon binary) from `jepnoda/hardware-identification-addon`.
- Existing paid accounts remain usable regardless of inability to read the local device identifier.
- An active trial issued with a device claim can only grant write access when the currently presented device identifier matches the claim.
- The website **does not issue** free trials without a device identity. It only prepares verified user accounts.
- Annual subscriptions are issued **only** after a superadmin manually checks and approves an actual receipt using `admin_review` / `zurvan_manual_review_payment`. Submitting a payment request does not grant a subscription.
- Emperor and Empress continue to receive server-side super entitlements. Special Advisor has no automatic unlimited entitlement, and can have an independent paid or lifetime license.

## Deploy sequence (required)

1. Back up the relevant Supabase database/schema.
2. Run `supabase/migrations/20261122_000004_zurvan_one_trial_per_device.sql` in the **target project** first. Verify `public.zurvan_trial_devices` exists and `zurvan_start_trial` was replaced.
3. Deploy updated Edge Functions `zurvan-license-start-trial`, `zurvan-license-status`, `zurvan-manual-payment` from the branch. Reconcile any direct Dashboard edits on the live functions, including environment-secret handling and database diagnostic logging, **before** deploying; GitHub and production may differ.
4. Build and install the Windows desktop client from the same branch.
5. Deploy the modified `web/manual-payment/index.html` to the actual hosting site if in use.
6. Validate using TEST accounts/devices; check Edge Function logs. Do not run a real customer through tests.

## Acceptance checks

1. New email-verified account, fresh Windows device: accounts and zurvan_accounts created automatically; one active `trial` license with 45-day expiry; one row in zurvan_trial_devices; can_write=true.
2. Different verified account, **same device**: new free trial returns HTTP 409 `device_trial_already_used`; no second license created.
3. Same trial account, **different device**: `zurvan-license-status` returns `mode=device_trial_mismatch`, can_write=false.
4. Same account and same device after re-entry: existing trial unchanged; expiry is **not extended**.
5. Approved annual payment: only signed-in superadmin can call `admin_review`, and paid `subscription` is issued for one year. A rejected or pending receipt never grants write access.
6. Paid/lifetime account: entitlement works even if fingerprint cannot be read.
7. Special Advisor: rank badge remains independent from its license.

## Limitations / security notes

- A hardware/device fingerprint is **not cryptographic device attestation**. A sufficiently motivated user can spoof Windows IDs, clone installations, patch the client, or use a virtual machine. The server-side unique claim blocks ordinary multi-account reuse; stronger anti-fraud requires TPM-backed device attestation and an operational recovery process.
- Existing legacy trial licenses issued before the migration have no device claim, so are grandfathered in status checks. This migration cannot retrospectively recover an identifier that was never collected.
- A good motherboard serial will generally survive a Windows reinstall, but changing firmware identifiers, a motherboard swap, can change the device identity. Some OEMs ship duplicate/placeholder serials; the system rejects common placeholders but cannot establish global uniqueness. Already-claimed devices issued with a Windows-only v1 fingerprint need a migration plan before production rollout. An administrator-reviewed reset/migration process is required before enforcing this at scale.
- Direct UI/Edge Function deployment and real Windows device testing are separate from committing source changes; do not call this production enabled until completed.

## Mobile implementation status (2026-11-23)

### Code introduced
- Supabase: `zurvan-mobile-trial` authenticates the user, validates Google's Play Integrity classic token / nonce / device verdict / Play-recognized APK / Google-hosted deviceRecall bitFirst (Android), or Apple DeviceCheck query/update bit0 (iOS), reserves a pending device claim, issues the one-time trial through `zurvan_start_trial`, and marks the claim verified after the upstream write.
- Supabase migration `20261123_000005_mobile_attested_trial_claims.sql`: `platform` and `verified` columns on `zurvan_trial_devices`. The status endpoint refuses free-trial write access on unverified mobile claims.
- Android: `ZurvanMobileDeviceProof` collects Android ID fingerprint and obtains classic Play Integrity token with account/fingerprint/time-bound nonce. `ZurvanAndroidOnlineLicense` signs in to Supabase, stores session in Android Keystore-encrypted data, requests online trial, and refreshes online entitlements. Compose activation and settings screens can sign in.
- iOS: `ZurvanIOSLicense` generates Apple DCDevice token, uses OS-provided IDFV fingerprint, stores Supabase refresh token securely in Keychain, signs in and requests the online trial. Activation UI has account login and startup revalidation.

### Critical deployment dependencies
1. Back up production Supabase and apply BOTH migrations in order: `20261122_000004` then `20261123_000005`. Deploy matching `zurvan-license-start-trial`, `zurvan-license-status`, `zurvan-manual-payment`, and NEW `zurvan-mobile-trial`. Dashboard-edited live Edge Functions MUST be reconciled with GitHub changes prior to deployment.
2. For Android, link the production `com.bdfr.zurvan` package and signing certificate in Play Console. Enable Play Integrity API. **Device Recall is beta and requires approval/enablement**; without it, mobile-trial intentionally refuses to issue new Android free trials. Set secret `ZURVAN_GOOGLE_SERVICE_ACCOUNT_JSON` to the full Google Cloud service account JSON with Play Integrity API permissions. Do not commit it. Distribute a Play-recognized release; debug and sideload APKs will not be `PLAY_RECOGNIZED`.
3. For iOS, enable DeviceCheck for the app ID in Apple Developer and provision server-only secrets `ZURVAN_APPLE_TEAM_ID`, `ZURVAN_APPLE_DEVICECHECK_KEY_ID`, `ZURVAN_APPLE_DEVICECHECK_PRIVATE_KEY` (PEM), `ZURVAN_APPLE_DEVICECHECK_ENV=development|production`. NEVER put Apple .p8 key in the iOS bundle or GitHub. Test on physical signed devices.
4. Android policy flag `ZURVAN_ENFORCE_ONLINE_MOBILE_TRIAL=true` is intentionally **OFF by default**. Enable only in a separately built test/release after the live backend is configured, provisioned and verified. When on, the app uses server-authoritative online entitlement (up to 24h cached policy) or valid RSA-signed offline license, not the vulnerable local SharedPreferences 45-day trial.
5. iOS supports signed offline license plus the new online login. No new local 45-day trial should be introduced.
6. Test on real Windows machine, Play signed Android phone and Apple signed iPhone. Verify second account same device rejected, original account re-entry doesn't extend trial, different device mismatches, manual paid approval works, and vendor outages **fail closed** for new trials while paid users remain supported.

### Known limits and recovery
- New mobile claims are reserved as `verified=false` until the vendor recall bit is written. If Apple/Google or Supabase fails partway through, the device may have a pending record and the user sees read-only until a verified retry succeeds or an administrator reconciles it.
- Vendor updates and Postgres transactions cannot be made atomic across networks. DeviceCheck uses two vendor-held bits, not an exportable iPhone serial. Play Integrity Device Recall is beta and has platform/privacy constraints; manual reset must consider resale/refurbishment, mistaken blocks and compliance.
- App Attest is recommended for stronger iOS **app instance** integrity but is not yet implemented in the online trial endpoint. DeviceCheck token validates Apple-issued device state, not full anti-tamper proof of an uncompromised app.
- Windows motherboard serials and Android IDs are not cryptographically unforgeable; Google/Apple vendor-state checks mitigate common reinstallation abuse, not hardware-rooted absolute identification.
- The repo changes have NOT been deployed to live Supabase, installed on real phones, or successfully run through device builds as part of editing the repository. Do not tell customers the full enforcement is live until tests pass.

## Special calendar permissions / retired License Calculator

- Production permissions are account-scoped server entitlements, not locally minted codes.
- Apply `supabase/migrations/20261123_000006_zurvan_account_feature_grants.sql` after the device migrations. It creates `zurvan_account_features` and the immutable grant-change audit table `zurvan_feature_audit`.
- Deploy both `zurvan-license-status` (returns `features.pahlavi_occasions`) and `zurvan-manual-payment` (superadmin-only `admin_list_users`, `admin_user_features`, `admin_set_pahlavi`).
- The admin interface in `web/manual-payment/index.html` lists users and allows activating/revoking Pahlavi historical occasions per account, with an explicit confirmation step.
- Android historical-occasion settings now consult the account's last verified online grant rather than locally signed legacy codes. Android policy refresh must be deployed and the user signed in before the switch appears. The 24-hour entitlement cache means revoked grants may remain effective until refresh/cache expiry.
- Windows parses the grant and displays two curated historical (nonholiday) occasions in the occasions module when allowed. Existing Pahlavi numeral clock display is a distinct setting.
- iOS receives the grant from online license status, offers a historical-feed toggle to authorized accounts, and merges the same two curated historical nonholiday occasions into the calendar and occasions list.
- The old `tools/Zurvan.LicenseCalculator` source stays for reference/legacy offline-license compatibility only. It is NOT part of the new online entitlement or payment process. Never issue new special access with it.
- **Deployment order is mandatory**: DB migrations 000004, 000005, 000006, then new/updated Edge Functions, then web/admin frontend, then clients after CI and device validation. Deploying the status API before migration 000006 returns feature_lookup_failed.
- A generic feature toggle never auto-approves a bank transfer and never changes annual/lifetime/trial license types.
