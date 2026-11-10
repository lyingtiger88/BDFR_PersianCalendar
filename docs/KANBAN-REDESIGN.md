# Zurvan personal Kanban — implementation and verification

This is an **independent** implementation of a PLANKA-style Kanban workflow. No PLANKA source code or artwork has been copied. PLANKA Community is subject to its separate Fair Use license; do not vendor PLANKA Community/Pro code into a commercial Zurvan distribution without the corresponding legal rights.

## Entry points

- Android: `ZurvanPersonalKanban.kt`, invoked by `PlansScreen` in `MainActivity.kt`
- Android model/state: `ZurvanKanbanStore.kt`
- Android attachment backup: `ZurvanKanbanBackup.kt`, plus a scoped Android FileProvider
- Windows: `MainWindow.Kanban.cs`, invoked by `MainWindow.Modules.cs` for `برنامه‌ها`
- The existing planning data and collaboration implementation have **not** been deleted; only the previous default Plans UI was replaced.

## Intended interaction

1. Empty Programs landing page: private/personal and shared/team sections, each with Add.
2. Model chooser has three slots; **only the first model (Kanban) has been specified by the user**.
3. Creating a private Kanban project opens an initially empty board.
4. User creates columns and cards as needed; no lists or records are auto-created.

## Current Android functionality

- Multiple personal boards, columns and cards; rename/delete
- Touch long-press horizontal moves across columns and vertical card ordering
- Column order controls; card details, Markdown-ready plain-text descriptions, multiple labels, priority, due date, checklist, comments, archive and search/filtering
- Local private app storage; validated card/column relationships on restore; previous-good snapshot
- File picker with app-private copied files, scoped FileProvider opening and deletion
- Full ZIP export/import (JSON metadata + attached file bytes); zip path and size validations

## Current Windows functionality

- Separate private/team landing sections; multi-board Kanban, lists and cards
- Editable title, description, labels, priority, ISO due date, checklist, comments, archive, card activity, attachment file actions, search and list order
- Native Windows drag-and-drop to move cards between columns
- Local JSON persistence under the user's LocalApplicationData/Zurvan folder, with last-good .bak recovery
- Team area is a placeholder until the team-model specifications are finalized

## Verification gates (NOT YET COMPLETED)

The source changes use `[no-build]`; GitHub Actions compilers and app packaging were intentionally skipped. **Do not label the release production-ready until the user explicitly approves a build and these checks pass.**

- Android: Gradle assembleDebug; install on small/large screens; launch and create first project
- Windows: WinUI compile, installer, portable version, first-run and restart
- Board CRUD, fresh install, empty board, multiple boards, delete confirmations, reorder columns/cards, long press drag, archive/restore
- Lists/cards with Persian text and emoji, checklist completion, priority/date filtering, overdue styling
- Android backup round trip with zero/one/multiple files; corrupted zip, oversized zip, path traversal, duplicate names and partial writes
- Windows FilePicker attach/open/delete and permissions; verify no unexpected loss when write fails
- Refresh/restart durability; compatibility with older JSON formats
- Keyboard accessibility, RTL, screen reader labels, high contrast, responsive layout and touch targets
- End-to-end parity and interactive visual review against the approved PLANKA reference

## Known limits

- These changes are **not** yet device-tested or compile-tested after the last Kanban edits.
- This is a functional reconstruction, not an exact pixel-for-pixel PLANKA replica.
- Cross-platform private-board synchronization is not implemented. Android and Windows use separate local stores.
- The team collaboration model, and the second/third model choices, await user specification.
- PLANKA-exclusive Pro features, multi-user real-time collaboration, stopwatch/time tracking, rich Markdown rendering and enterprise integration are not part of this pass.
- The current Windows backup is an on-disk .bak of metadata; a user-portable ZIP round trip is implemented on Android only.

## Safety

- Never automatically migrate or delete legacy plans from the previous application.
- Do not silently create Kanban boards before the user chooses Add.
- Never run builds without explicit user approval.
