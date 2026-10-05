# BDFR Persian Calendar

A native, Persian-first calendar, planner, occasions and reminder application for Windows 11.

## Current development build

The current `main` branch contains a working WinUI 3 foundation with:

- Native Persian (Jalali) month calendar, Saturday-first layout and RTL UI
- Gregorian date conversion
- Offline-first SQLite database
- Daily notes
- Timed events
- Tasks with completion state and optional due time
- Multi-stage reminder model
- Native Windows app notifications
- Windows scheduled reminders for future delivery
- Personal annual occasions such as birthdays and anniversaries
- Multiple advance reminders for personal occasions (for example 30, 7, 1 and 0 days)
- Persian Quick Add input, e.g. `فردا ساعت 16:30 جلسه تیم`
- time.ir occasions provider with holiday detection
- Local activity log
- Mica / WinUI 3 Windows 11 desktop shell
- Reusable Core and Infrastructure layers for future BDFR Lock integration

## Architecture

```text
BDFR.PersianCalendar.Core
  Persian date model
  Calendar grid
  Planner domain models
  Reminder engine
  Persian Quick Add parser
  Platform-independent contracts

BDFR.PersianCalendar.Infrastructure
  SQLite repository
  time.ir occasion provider
  Planner service
  Special occasion service
  Reminder fallback service

BDFR.PersianCalendar.Desktop
  WinUI 3 UI
  Windows App SDK notifications
  Windows scheduled reminder adapter

BDFR.PersianCalendar.Smoke
  Date conversion checks
  Quick Add checks
  Month grid checks
  Offline time.ir HTML parser fixture
```

## Local data

Application data is stored under:

```text
%LOCALAPPDATA%\BDFR\PersianCalendar\calendar.db
```

The calendar remains usable offline. A failed occasions synchronization does not delete the last locally stored data.

## Build

Requirements:

- Windows 11
- .NET 10 SDK for local development

Build the desktop app:

```powershell
dotnet build src/BDFR.PersianCalendar.Desktop/BDFR.PersianCalendar.Desktop.csproj -c Release -p:Platform=x64
```

Run smoke checks:

```powershell
dotnet run --project tools/BDFR.PersianCalendar.Smoke/BDFR.PersianCalendar.Smoke.csproj
```

Every push to `main` runs GitHub Actions checks for the Core/Infrastructure and the Windows desktop application. The Windows job also publishes an x64 test build as the `BDFR_PersianCalendar-win-x64` workflow artifact.

## time.ir data

`TimeIrOccasionSource` is isolated behind `IOccasionSource`. This intentionally keeps the application independent from the site's HTML structure. Parser failures preserve the local cache instead of corrupting calendar data.

## BDFR Lock

The Core/Infrastructure projects do not depend on the WinUI shell. This allows BDFR Lock to consume the same date, occasion, task and reminder data without duplicating calendar logic.

## Status

**Development / local-test milestone.** Installer, tray flyout, richer notification actions, automatic annual source refresh and final BDFR Lock bridge are planned for subsequent milestones.
