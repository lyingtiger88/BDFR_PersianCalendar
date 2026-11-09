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

## Build authorization
On 2026-11-09, the user explicitly requested test builds. PR #24 was labeled `build-approved` and marked ready for review to enable CI compilation. This does **not** authorize a merge or production release. Test APKs and simulator builds remain non-production artifacts.


## Layout correction — 2026-11-09 (supersedes earlier placement instructions)
- Desktop weather card is **only** in the physical left day-details sidebar: immediately after the Gregorian date and before occasions. No weather card appears above the calendar.
- The sidebar contains the current condition plus a horizontally scrollable **previous 24 hours** of hourly temperatures and weather icons, not a forward forecast list. Missing historical hours are not fabricated.
- Each corresponding calendar day tile shows daily weather and high/low temperatures from **the seven previous days, today, and the seven coming days** (15 dates). Historical days are identified as archived forecasts, not direct local observations.
- Open-Meteo Forecast API uses `past_days=7&forecast_days=8` for the 15-date window. Separate daily forecast panels and the former five-day weekly weather button were removed from the sidebar.
- **Settings → General → Weather** holds a master enable/disable switch. When off, its location and temperature-unit submenu is hidden; when on, it is shown. Search returns multiple city matches for the user to choose, rather than silently using the first result. Location is selected manually; there is no GPS access.
- Removed the duplicate toggle from the Windows Appearance settings.
- Android and iOS data requests were also aligned to 24 historical hourly values and the 15-date daily span.
- No build was initiated for this correction; layout and native runtime behavior still require approval and visual validation.
