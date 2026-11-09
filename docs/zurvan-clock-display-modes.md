# Selectable analog and digital Zurvan clock (source-only)
Date: 2026-11-09

## Choices
- Mode: analog (default) or digital, stored in local platform preferences.
- Digital styles: classic gold, modern turquoise, dotted purple, flip-style slate.
- Time format: 24-hour (default) or 12-hour.
- Seconds: visible by default, may be hidden.
- Clock updates once per second from device local time.

## Implemented wiring
- Android: Settings > Appearance > Zurvan clock stores choices in SharedPreferences `zurvan_clock`. The existing secret Zurvan title (seven taps) opens the same Easter egg; its clock illustration dynamically selects the live analog 12-point vector dial or digital time. A preferences listener applies changes immediately.
- Windows: Settings > Calendar and Time displays a live analog/digital preview, persists user choice in AppSettings, and reveals digital controls only in digital mode. The preview is a simplified twelve-point dial pending visual alignment with the repository's vector emblem; this Windows setting is not yet wired to a standalone calendar-face clock.
- iOS: no new clock control has been wired in this pass.

## Not part of this pass
The Pahlavi, Khwarazmi and cuneiform numeral sets shown in the concept mockup are **not** validated or implemented. Mockup labels are conceptual and are not to be mistaken for historical accuracy. No files were compiled or built in this pass.
