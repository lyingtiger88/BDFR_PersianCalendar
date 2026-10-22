# Zurvan Android

Status: **active scaffold**.

The Android client uses Kotlin + Jetpack Compose + Material 3. It is intentionally organized around the same feature IDs as `platform/feature-parity.json`.

## UI direction

- Full-width monthly calendar
- Configurable first/second/third Persian/Hijri/Gregorian date priority
- Bottom navigation: Calendar, Plans, Reminders, Companion, Settings
- Day details as bottom sheets
- Floating add action for event/note/task/occasion
- Companion pairing through QR or six-digit code
- Local-first bidirectional sync with the Windows client

The protocol data classes in `CompanionModels.kt` mirror the versioned contracts in the shared .NET Core. Network transport is intentionally separated from UI and domain models.
