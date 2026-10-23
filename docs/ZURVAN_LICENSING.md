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
