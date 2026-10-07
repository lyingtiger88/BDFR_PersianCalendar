# Anahita -> BDFR LogonUI Bridge

Anahita can publish a small, privacy-tagged lock-screen snapshot to BDFR LogonUI.

## Important boundary

BDFR LogonUI does **not** open Anahita's SQLite database.

Anahita reads its own repository and publishes only the bounded data needed by the lock experience through the local pipe:

`BDFR.LogonUI.Broker.v1`

Provider id:

`bdfr.anahita`

## Data included

- current Persian date;
- current Gregorian date;
- current Hijri date;
- official occasions for today;
- personal occasions for today;
- user events for today;
- incomplete tasks for today;
- pending reminders for the next 24 hours.

## Privacy defaults

- official/non-personal occasions: Public;
- personal occasions: Private;
- events: Private;
- tasks: Private;
- reminders: Private.

The LogonUI broker masks Private titles before the locked UI can read them and drops Secret items.

## Refresh and failure behavior

The bridge publishes locally about every 30 seconds.

If the LogonUI broker is unavailable, publish attempts fail quietly and retry later. Anahita remains fully usable.

The bridge does not use:

- Windows notification APIs;
- network access;
- time.ir;
- credentials.

This is intentional because the current stability build keeps optional Windows notification and network background services quarantined.

## Future work

Anahita should eventually expose explicit per-item Public / Private / Secret lock-screen visibility instead of relying only on the current safe defaults.
