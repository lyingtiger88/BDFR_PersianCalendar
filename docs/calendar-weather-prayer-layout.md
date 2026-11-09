# Calendar weather and prayer layout — 2026-11-09

## Placement and behavior
- **Windows**: estimated current-day prayer times above the calendar; the current weather card immediately below them with the next 24 hourly temperature forecasts in a horizontally scrollable strip. Clicking the month heading opens a Persian month/year picker. The calendar remains vertically scrollable on smaller desktop windows.
- **Android**: weather appears directly underneath the existing prayer-times card. The 24-hour strip scrolls horizontally within its own card and uses the coordinates configured for prayer times.
- **iOS**: adds an estimated prayer-times strip and a matching 24-hour weather card above the monthly calendar. Appearance settings allow four predefined cities (Tehran, Zanjan, Frankfurt and Seattle).
- **All three**: weather icon and forecast high/low appear directly on the corresponding date cards for the seven days actually returned by the forecast service. No forecast is invented for historical or distant calendar months.
- **Appearance settings**: users can toggle all calendar weather displays on/off. The preference is persisted locally.
- **Source**: Open-Meteo Forecast API for current/hourly/daily data; credit with an opening link is shown in the weather card.
- **Refresh**: a 30-minute cache and refresh cadence; unavailable network/data results are presented as unavailable instead of made up. Actual devices have not yet been tested.

## Privacy
No new operating-system GPS permission is requested. Windows weather defaults to the sample Seattle location from the screenshot and supports manual city selection. Android uses the location already chosen for prayer times. iOS uses the city selected in its weather/appearance section.

## Production licensing gate
The free api.open-meteo.com endpoint is for non-commercial use. The Zurvan project may have paid subscriptions, so **do not ship the free endpoint in a commercial release**. Before release, arrange a commercial Open-Meteo plan with customer-api.open-meteo.com access or replace it with a licensed provider, keeping credentials server-side. See https://open-meteo.com/en/pricing and https://open-meteo.com/en/docs. Weather data attribution (CC BY 4.0) remains mandatory.

## Build/test gate
No build was started for this feature change. The source still needs native Windows, Android and iOS compilation, UI visual validation, city/time-zone edge-case checks, 24-hour refresh tests, and verification on narrow screens. Draft PR #24 is intentionally unmerged.
