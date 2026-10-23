# Zurvan Desk Account API

This service sits between Zurvan clients and ArvanCloud Object Storage.

Clients never receive S3 access keys or secret keys.

## Responsibilities

- account registration
- login
- logout
- session validation
- Vault ID assignment
- opaque recovery-envelope storage
- encrypted file list metadata
- encrypted file upload
- encrypted file download
- encrypted file rename metadata
- encrypted file delete

The service does **not** decrypt user files.

## ArvanCloud configuration

Set these environment variables on the server:

```text
ZURVAN_DESK_S3_ENDPOINT=https://s3.ir-thr-at1.arvanstorage.ir
ZURVAN_DESK_S3_REGION=ir-thr-at1
ZURVAN_DESK_S3_BUCKET=<bucket-name>
ZURVAN_DESK_S3_ACCESS_KEY=<server-access-key>
ZURVAN_DESK_S3_SECRET_KEY=<server-secret-key>
```

Optional:

```text
ZURVAN_DESK_ACCOUNT_DATA_ROOT=/var/lib/zurvan-desk
```

If the S3 variables are absent, the development server falls back to local blob storage under `App_Data`. This fallback is intended for local testing only.

## Run locally

```bash
dotnet run --project server/Zurvan.Desk.Api/Zurvan.Desk.Api.csproj --urls http://0.0.0.0:5088
```

Android emulator debug builds use `http://10.0.2.2:5088` by default.

## Security boundary

- passwords are PBKDF2-SHA256 hashed with per-user salts
- sessions are random opaque bearer tokens; only token hashes are stored server-side
- recovery phrase is never sent to the server
- Vault Key is never sent to the server
- filenames are client-encrypted before upload
- file contents are client-encrypted before upload
- Arvan credentials remain server-side only
