# Zurvan test build request — 2026-10-24

Build target: main at prior commit `91c980becd0b61e79b6cd441d0dde807b62d1c56`.

Requested checks: Core, Windows desktop, Android debug APK, iOS simulator ZIP, Windows License Calculator.

Notes:
- This commit initiates a new CI test build after run #393 had an iOS cancellation.
- The Season Vibe and Elena Mode moodboard/mockup images were supplied in conversation, but the image packs have not yet been integrated into app resources. The current build does not include those new wallpapers.
- Launcher app icon integration and full licensing across all clients remain pending.
- Do not treat a green build as proof of feature parity or functional validation on physical devices.
