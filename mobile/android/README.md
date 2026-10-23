# Zurvan Android

Status: **active implementation**.

The Android client uses Kotlin + Jetpack Compose + Material 3 and is tracked against `platform/feature-parity.json`.

## Implemented modules

- Real Persian month calendar with Gregorian and Hijri equivalents
- Configurable first / second / third calendar priority
- Jump to date with separate year / month / day fields
- Three-calendar date converter
- Day details
- Public/private daily notes
- Events and tasks with local persistence
- Personal annual occasions
- Reminders with repeat count / repeat interval
- Native Android notifications with private-content redaction
- Adaptive navigation:
  - phones: bottom navigation
  - tablets / wide screens: side Navigation Rail
- Configurable Navigation Rail side
- Configurable corner radius
- Zurvan Desk account UI
- Zurvan Desk sign-up / login / logout
- 24-word recovery phrase flow
- Client-side encrypted file upload / download / rename / delete
- Android Keystore-protected local secrets
- Companion protocol UI and cryptographic foundation

## Zurvan Desk UX rule

The Android user does **not** configure S3, Arvan Object Storage, buckets, regions, access keys, or secret keys.

The app only exposes:

- Create account
- Sign in
- Sign out
- Recovery phrase entry when needed
- My files
- Upload
- Download
- Rename
- Delete
- Refresh

The Android client talks to the Zurvan Desk Account API. The API owns the Arvan Object Storage credentials and stores only client-encrypted payloads.

## Development API

Debug builds default to:

```text
http://10.0.2.2:5088
```

This is only for the Android emulator talking to a locally running Zurvan Desk API.

For a deployed build, pass:

```text
-PZURVAN_DESK_API_BASE_URL=https://your-desk-api.example
```

The endpoint is compiled into BuildConfig and is never shown in the normal user UI.

## Remaining parity gaps

The Android client still has some explicit parity gaps tracked in `platform/feature-parity.json`, including:

- occasion picture mapping/rendering
- Season Carousel / Elena seasonal backgrounds
- live LAN Companion transport
- automatic cross-device sync execution

These are not silently marked complete.
