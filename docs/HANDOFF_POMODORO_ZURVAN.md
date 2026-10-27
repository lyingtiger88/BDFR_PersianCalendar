# Handoff: Pomodoro / Focus Sessions for Zurvan

**Date:** 2026-10-27  
**Repository:** https://github.com/lyingtiger88/BDFR_PersianCalendar  
**Purpose:** Give the implementation agent a single source of truth for planning and implementing a Pomodoro/focus-session feature without confusing it with existing reminders or task tracking.

## Current understanding

Zurvan is a Persian-first calendar and planner. The repository describes a Windows desktop app built with WinUI 3, C#/.NET 10 and SQLite, plus an Android companion implemented with Kotlin and Jetpack Compose. The Core project is intended to hold platform-independent domain logic/contracts; Infrastructure contains SQLite-backed services; platform UI and notification scheduling are platform-specific.

Relevant areas identified during repository review:

- `src/BDFR.PersianCalendar.Core/Models.cs`: planner domain models.
- `src/BDFR.PersianCalendar.Core/ReminderEngine.cs`: reminder logic.
- `src/BDFR.PersianCalendar.Infrastructure/PlannerService.cs`: planner operations.
- `src/BDFR.PersianCalendar.Infrastructure/ReminderActionService.cs`: reminder actions and activity logging.
- `src/BDFR.PersianCalendar.Infrastructure/ReminderPollingService.cs`: desktop reminder polling behavior.
- `src/BDFR.PersianCalendar.Desktop/App.xaml.cs` and `MainWindow.xaml.cs`: desktop service wiring and planner UI.
- `mobile/android/app/src/main/java/com/bdfr/zurvan/ZurvanViewModel.kt`: Android local planner state and actions.
- `tools/BDFR.PersianCalendar.Smoke/Program.cs`: existing Core smoke-test entry point.

The current Android README documents local tasks/events, reminders, notes and local persistence, but does not document Pomodoro/focus-session support. Existing reminders are not a substitute for focus sessions. Do not claim feature parity or successful tests without running them.

## Goal

Implement Pomodoro as a planner feature associated with a task when applicable, not as an isolated timer disconnected from Zurvan's calendar and task model.

## Scope for the first implementation

1. **Platform-independent domain model**
   - Add a focus-session model in Core, using the repository's existing naming and conventions.
   - Record a stable ID, optional linked task ID, session type (focus or break), planned duration, actual start/end timestamps, status, and creation/update timestamps as appropriate.
   - Follow existing date/time and ID conventions after inspecting the actual model and repository interfaces. Do not duplicate existing fields or invent a parallel task model.
   - Keep persisted timestamps unambiguous (UTC or the convention already used by the repository); render them in local time in the UI.

2. **Core/service behavior**
   - Add a service/API for starting, pausing/resuming if supported by the current app architecture, completing, and cancelling a session.
   - Store enough state to recover after app restart. Do not rely only on an in-memory countdown.
   - Compute remaining time from persisted timestamps/state, rather than assuming a timer callback runs continuously.
   - Make state transitions explicit and validate invalid transitions.
   - Link a session to an existing task only when a valid task ID is supplied. A focus session must also work without a task.
   - Do not automatically mark a task completed when a Pomodoro finishes.

3. **Persistence**
   - Extend the existing SQLite repository/migration strategy instead of creating a second database.
   - Add repository methods and schema migration safely; preserve existing user data.
   - Inspect the actual repository interfaces/schema version before editing. Do not guess their method signatures.

4. **UI integration**
   - First implement the shared/domain and persistence layer, then wire platform UIs according to their existing patterns.
   - Desktop: offer a way to start a focus session for a selected task and a general session without a task.
   - Android: follow existing ViewModel/Compose patterns. Avoid pretending the Android companion has full desktop feature parity.
   - Show the current session state and remaining time; support completing/cancelling the session.
   - Use configurable defaults (suggested initial defaults: 25 minutes focus, 5 minutes short break, 15 minutes long break after 4 completed focus sessions), but keep these configurable and avoid hard-coding the durations throughout the codebase.
   - Do not add sound, complex statistics, cloud sync, group planning, or a new backend in this first pass unless the existing architecture makes a small part trivial.

5. **History and calendar**
   - Persist completed/cancelled sessions as history, with enough data to distinguish actual focused time from planned duration.
   - Make sessions available to daily/weekly summaries only if the existing calendar/activity architecture can support this cleanly.
   - Avoid creating fake calendar events or overloading reminder entities just to display focus sessions.

## Tests and acceptance criteria

Add tests using the repository's existing test/smoke conventions. At minimum verify:

- starting a session creates a persisted active session;
- remaining time is derived correctly from the stored state/time;
- pause/resume and complete/cancel transitions behave correctly if implemented;
- a session can be linked to a valid task or run without a task;
- invalid transitions and missing task IDs are handled safely;
- SQLite migration preserves existing planner data;
- existing date/parser/reminder smoke tests still pass.

Run the relevant build and tests for every platform touched. Report exact commands and results. If Android SDK, Windows build environment, or another required tool is unavailable, state that explicitly and do not report the unrun test as passing.

## Working rules

- Read the current source and interfaces before making changes; the paths above are starting points, not a complete dependency map.
- Preserve the repository's existing architecture, Persian-first UX, offline-first behavior, naming, formatting, and persistence patterns.
- Do not refactor unrelated features.
- Do not write directly to `main`; work on a feature branch and open a pull request for review.
- Do not overwrite user changes or silently change database semantics.
- Keep the implementation scoped. Prefer a complete, testable Core + persistence slice before adding UI.
- In the final report, list changed files, schema/migration changes, supported platforms, tests actually run, tests not run and why, and any remaining gaps.

## Recommended order

1. Inspect Core models/contracts, SQLite repository and migration/versioning, desktop DI, Android repository, and current test projects.
2. Propose a short implementation plan based on the real interfaces.
3. Implement Core model/state rules and tests.
4. Add SQLite persistence/migration and persistence tests.
5. Wire desktop UI and validate Windows build/tests.
6. Wire Android UI only after checking the Android repository/storage architecture; validate Android build/tests.
7. Open a PR with a clear summary and test evidence. Do not merge without review.

**Important:** This document is a handoff/specification only. It does not mean Pomodoro has already been implemented.
