# Zurvan Desk — E2EE, Recovery and Account Security

Zurvan Desk stores encrypted user data on S3-compatible Object Storage. The initial provider is ArvanCloud Object Storage.

## Security goal

The storage provider must not receive calendar plaintext, note text, reminder text, private occasion contents, or the Zurvan Vault Key.

Encryption and decryption happen only on trusted Zurvan clients:

- Windows
- Android
- iOS

The provider stores ciphertext and transport-visible metadata such as object path, object size and access timing.

## Keys

### Vault Key

Each Zurvan Vault has one random 256-bit Vault Key.

The Vault Key is never stored in normal app settings.

Platform storage:

- Windows: PasswordVault / protected credential storage
- Android: data wrapped by Android Keystore
- iOS: Keychain

The Vault Key encrypts user data indirectly. Each cloud object receives a derived object key:

```text
ObjectKey = HMAC-SHA256(
  key = VaultKey,
  data = "zurvan-cloud-v1|VaultId|ObjectId"
)
```

Each object is encrypted with AES-256-GCM and a fresh 96-bit nonce.

## Recovery phrase

Zurvan uses a 24-word BIP39 English mnemonic for account recovery.

Why 24 words:

- 256 bits of random entropy
- checksum built into the mnemonic
- standard and easy to write down
- supported consistently across Windows, Android and iOS

The recovery phrase is generated locally and is never uploaded.

The phrase is decoded back to its original entropy. A dedicated recovery wrapping key is derived:

```text
RecoveryKey = HMAC-SHA256(
  key = BIP39 entropy,
  data = "zurvan-recovery-v1|VaultId"
)
```

That key does not directly encrypt user calendar data. It only wraps the 256-bit Vault Key using AES-256-GCM.

The cloud stores only a Recovery Envelope:

- protocol version
- Vault ID
- word count
- non-secret phrase fingerprint
- nonce
- wrapped Vault Key ciphertext
- authentication tag

The 24 words are not stored in the envelope.

## Recovery flow

### New vault

1. Client creates a random Vault Key.
2. Client creates a 24-word recovery phrase.
3. Client derives the Recovery Key from the phrase.
4. Client encrypts/wraps the Vault Key locally.
5. Only the Recovery Envelope is uploaded to the cloud.
6. User writes the 24 words down offline.

### App deleted / device lost / password forgotten

1. User signs in to the Zurvan account.
2. Account service returns only non-secret vault routing information and authorized Object Storage access.
3. User enters the 24-word phrase locally.
4. Client downloads the Recovery Envelope.
5. Client validates the BIP39 checksum.
6. Client derives the Recovery Key locally.
7. Client unwraps the Vault Key locally.
8. Vault Key is saved into the new device's secure storage.
9. Encrypted cloud objects can now be decrypted.

## Phone/email account takeover

Phone number and email are identity/reachability factors only.

They must never become encryption keys.

If an attacker gains access to the Zurvan account through:

- SIM swap
- stolen email session
- password reset abuse
- compromised recovery email

the attacker may be able to obtain encrypted objects and the Recovery Envelope, but cannot decrypt the Vault without the 24-word phrase or an already trusted device containing the Vault Key.

This separation is intentional.

## Recovery phrase rotation

Generating a new recovery phrase creates a new Recovery Envelope for the same Vault Key and replaces the current master recovery envelope.

After successful rotation:

- the new phrase becomes active;
- the previous phrase no longer opens the current recovery envelope;
- encrypted user data does not need to be re-encrypted.

## What happens if the phrase is lost

If all trusted devices are lost and the user also loses the recovery phrase, Zurvan cannot reconstruct the Vault Key.

This is a security property, not a recoverable server-side password reset.

Support staff, ArvanCloud, email providers and phone-number verification must not have a hidden bypass.

## What happens if the phrase is stolen

Anyone who has:

1. the correct recovery phrase, and
2. access to the user's recovery envelope / account storage

can recover the Vault Key.

Users should keep the phrase offline and should not store it in:

- email drafts
- cloud notes
- screenshots synced to photo cloud
- chat applications
- password reset forms

## Server-side visibility

Zurvan Desk is content-encrypted but is not intended to claim perfect traffic-analysis anonymity.

ArvanCloud can still observe normal storage-layer metadata such as:

- bucket access
- object sizes
- timestamps
- request IP addresses
- encrypted object paths / Vault ID routing

The content itself and protected object metadata remain encrypted by the client.

## Current implementation status

Windows:

- Arvan S3 SigV4 transport
- secure credential storage
- random Vault Key
- encrypted probe upload/download
- 24-word phrase generation
- Recovery Envelope upload
- phrase-based Vault restore

Android:

- Arvan S3 SigV4 transport foundation
- Android Keystore secure store
- cloud AES-GCM primitives
- 24-word BIP39 recovery implementation

iOS:

- Arvan S3 SigV4 transport foundation
- Keychain secure store
- CryptoKit cloud primitives
- 24-word BIP39 recovery implementation

The future Zurvan Account service must only broker identity, Vault ID discovery and short-lived storage authorization. It must never receive or escrow the recovery phrase or Vault Key.
