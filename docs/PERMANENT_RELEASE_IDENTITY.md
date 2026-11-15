# Permanent release identity — Zurvan

## Android upgrade contract

- Permanent Android applicationId: `com.bdfr.zurvan`.
- Stable **production/release** certificate SHA-256: `29:43:4E:DE:B1:22:73:48:B4:DA:7F:B8:F1:CD:A4:D6:07:E4:33:A8:00:DB:8E:49:8E:39:78:42:68:60:FB:E8`.
- The release key is generated once and preserved offline. Never generate a new private key on the GitHub runner or commit a keystore, password, or credential JSON.
- Android allows an in-place APK update only when the package ID is identical, signing certificates match (or a supported authenticated signing lineage exists), and `versionCode` does not decrease.
- A new debug APK signed with a different runner's debug key is **not** an update of a release APK, even with the same package ID.
- Releases are generated through **Actions → Zurvan Android signed upgrade APK → Run workflow**; the workflow fails without secrets and verifies the public certificate fingerprint.
- Release version codes increment using the GitHub workflow run number with a reserved 10000 offset; never decrease codes when changing CI workflows.

## Required GitHub Actions repository secrets

In Settings → Secrets and variables → Actions → Repository secrets, add:

- `ZURVAN_ANDROID_KEYSTORE_B64` — base64 of the exact `zurvan-release.jks` file from the secure signing vault. Base64 is transport encoding **not encryption**, so treat as a secret.
- `ZURVAN_ANDROID_STORE_PASSWORD` — store password.
- `ZURVAN_ANDROID_KEY_ALIAS` — the keystore alias.
- `ZURVAN_ANDROID_KEY_PASSWORD` — key password.

The encrypted Kaliia signing vault contains each JKS and a `signing_credentials.json` record with the corresponding values. Do not paste the password into source, issues, pull requests, README, code review, or Actions logs.

**One-time activation:** the first signed release needs these secrets in GitHub. I cannot configure encrypted GitHub Actions secrets from the currently available repository-file API. Until they are installed, do not advertise DEBUG artifacts as seamless upgrades.

## Windows upgrade contract

Inno Setup installer/BDFR_PersianCalendar.iss has permanent AppId {5E8E4792-25B8-4C11-A87E-84687ED84E42}.
Use the same Inno Setup `AppId` and product-specific installation path on every future build. New setup EXEs can replace the installed app without uninstalling; leave user data in AppData, not the application binaries directory. A portable EXE by itself is not an installer/auto-updater.

A stable Windows installer identity is separate from Authenticode code signing. For a publicly trusted publisher signature, obtain a code-signing certificate from a trusted CA or an approved signing service; a new self-signed PFX will not establish SmartScreen trust.

## Initial migration warning

**Do not uninstall current APKs containing irreplaceable data.** If a device already has a DEBUG-signed APK or different package ID, these newly created production identities might require a one-time migration. Inventory existing installed packages' signatures with `apksigner verify --print-certs old.apk` and compare against the above SHA-256; then export/backup app data before any reinstall. Android deletes app-private data when the app is uninstalled.

Once the first production-signed APK is accepted on devices, **never rotate or lose the signing key**. Keep two encrypted offline backups of the private vault and credentials. Android App Signing via Google Play uses an additional managed signing key model; confirm Play enrollment before your first Play release.
