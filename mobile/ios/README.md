# Zurvan iOS

Status: **active scaffold**.

The iOS client uses SwiftUI. The Xcode project is described through `project.yml` so it can be generated with XcodeGen.

## UI direction

- Monthly calendar as the primary screen
- Configurable Persian/Hijri/Gregorian priority
- Native SwiftUI tab navigation for Calendar, Plans, Reminders, Companion, Settings
- Day details in sheets
- Native notification/reminder integration
- Companion pairing over QR or six-digit code
- Local-first bidirectional sync with Windows and Android

The Swift models in `CompanionModels.swift` mirror the versioned sync protocol in the shared Core.
