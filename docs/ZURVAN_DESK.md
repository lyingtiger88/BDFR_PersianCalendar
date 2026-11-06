# Zurvan Desk — Account, E2EE, Recovery and Storage

Zurvan Desk is the personal encrypted file space shared by Zurvan clients. Its account is independent from the Zurvan License Server identity and device-activation model.

## Current hosted backend

The initial hosted backend uses the existing Supabase project in West EU (Ireland):

- Project URL: `https://yztithydgdvydaktuxlw.supabase.co`
- Edge API: `/functions/v1/zurvan-desk-api`
- Private Storage bucket: `zurvan-desk-encrypted`
- Account, session, recovery-envelope and file metadata tables use the `zurvan_desk_*` namespace.

The Desk account records are separate from Supabase Auth and the license/device tables. The Edge Function validates its own random Desk bearer sessions. The Supabase publishable key is a public gateway identifier only; it is not an account credential and it does not grant access to Desk data.

The private bucket has no client-side storage policies. Only the server-side Edge Function can access objects after validating the Desk session. The Supabase secret/service-role key is never included in desktop or mobile builds.

The initial API is hosted on Supabase Edge Functions and Storage. The API contract is provider-neutral so that the storage implementation can later move to an Iranian or another S3-compatible provider without changing the account model or client encryption protocol.

## Security goal

The storage provider must not receive file plaintext, original filenames, or the Zurvan Vault Key.

Encryption and decryption happen only on trusted Zurvan clients:

- Windows
- Android
- iOS (integration remains a separate client task)

The provider stores ciphertext and transport-visible metadata such as object size, timestamps and access timing.

## Keys

### Vault Key

Each Zurvan Vault has one random 256-bit Vault Key.

The Vault Key is never stored in normal app settings.

Platform storage:

- Windows: Windows Password Vault / protected credential storage
- Android: data protected by Android Keystore
- iOS: Keychain

Each cloud object receives a derived object key:

```text
ObjectKey = HMAC-SHA256(
  key = VaultKey,
  data = "zurvan-cloud-v1|VaultId|ObjectId"
)
```

Each object is encrypted with AES-256-GCM, a fresh 96-bit nonce and the fixed protocol AAD `zurvan-cloud-v1`. Filename metadata uses the same primitive with the object identifier suffixed by `:name`.

The file payload envelope contains the object ID, nonce, ciphertext, authentication tag and original MIME type. The original filename is separately encrypted and Base64-encoded before it is sent in the `X-Zurvan-Encrypted-Name` header.

## Recovery phrase

Zurvan uses a 24-word BIP39 English mnemonic for account recovery.

- 256 bits of random entropy
- BIP39 checksum
- shared protocol implementation across Windows and Android

The recovery phrase is generated locally and is never uploaded. A dedicated recovery wrapping key is derived:

```text
RecoveryKey = HMAC-SHA256(
  key = BIP39 entropy,
  data = "zurvan-recovery-v1|VaultId"
)
```

That key only wraps the 256-bit Vault Key using AES-256-GCM. The server stores the opaque Recovery Envelope, never the phrase, entropy or Vault Key.

## Recovery flow

### New vault

1. Client registers a Desk account.
2. Client creates a random Vault Key and a 24-word recovery phrase.
3. Client derives the Recovery Key locally and wraps the Vault Key.
4. Only the Recovery Envelope is uploaded.
5. User writes the 24 words down offline.
6. The Vault Key is stored in the device's protected credential storage.

### New device

1. User signs in to the independent Zurvan Desk account.
2. Client downloads the Recovery Envelope.
3. User enters the 24-word phrase locally.
4. Client validates the BIP39 checksum and unwraps the Vault Key.
5. The Vault Key is stored in the new device's protected credential storage.
6. Encrypted filenames and files can now be decrypted locally.

## Account and session security

- Passwords are salted and derived with PBKDF2-HMAC-SHA-256.
- Raw bearer tokens are random; only SHA-256 token hashes are stored server-side.
- Sessions expire after 30 days and can be revoked by logout.
- Authentication attempts are throttled in Postgres.
- All Desk tables have RLS enabled and forced; direct client-role access is revoked.
- File objects are private and are only accessed by the API after Desk session validation.

The Supabase publishable key is intentionally shipped in clients. The Supabase secret/service-role key must remain server-side and must never be copied into app code, app settings, logs or build artifacts.

## Free-plan demonstration limits

The initial Supabase Free deployment is for development and demonstrations, not a production uptime guarantee:

- 1 GB Storage quota
- 50 MiB maximum encrypted object size per object (approximately 37 MiB maximum original file size because the encrypted JSON envelope uses Base64 encoding)
- 5 GB egress quota
- projects can be paused after a week of inactivity

Usage must be monitored. Before real customers depend on the service, upgrade the project or migrate the storage backend to a production plan/provider with appropriate backups, availability and operational controls.

## What happens if the phrase is lost

If all trusted devices are lost and the user also loses the recovery phrase, Zurvan cannot reconstruct the Vault Key. This is an intentional security property; support staff and the storage provider have no recovery bypass.

## Current implementation status

Windows:
- Hosted Supabase Desk API client
- Login and account creation
- Windows Password Vault session and Vault Key storage
- Shared BIP39 recovery generation and restoration
- Android-compatible AES-256-GCM/HMAC-SHA256 filename and file envelopes
- File list, upload, download/decrypt, rename/re-encrypt and delete

Android:
- Same hosted Supabase Desk API and publishable gateway key
- Android Keystore-backed secure storage
- Same file and filename encryption protocol
- Recovery envelope creation and restoration
- File list, upload, download/decrypt, rename and delete

iOS:
- Existing CryptoKit and Keychain protocol foundation
- Hosted Desk API endpoint and full account/file-manager parity remain a separate integration task.

The legacy .NET `server/Zurvan.Desk.Api` remains available for local development and migration testing; the hosted clients use the Supabase Edge API.
