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
