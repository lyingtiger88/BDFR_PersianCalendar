# Shahanshahi (Pahlavi-era imperial) calendar: client parity

## Calendar arithmetic

The historical Iranian imperial calendar used the **same Solar Hijri month/day
and leap-day structure**, changing only the displayed year number. For dates
represented by Zurvan's Solar Hijri calendar:

```text
imperial year = Solar Hijri year + 1180
Solar Hijri year = imperial year - 1180
1405 SH = 2585 imperial
1355 SH = 2535 imperial (1976–1977)
```

All calendar calculations, reminders, exported timestamps, persistence keys,
and week/day labels continue to use the canonical Solar Hijri (and UTC where
applicable), not a reinterpreted historical epoch. The UI only changes the
presentation of the year. Displaying the imperial numbering for modern years
is a *retrospective extension*, not a claim that it remains an official civil
calendar.

Sources:
- Encyclopaedia Iranica, Calendars:
  https://www.iranicaonline.org/articles/calendars/
- Encyclopaedia Iranica, Chronology of Iranian History:
  https://www.iranicaonline.org/articles/chronology-of-iranian-history-part-4/

## License and settings

**Authoritative grant:** Supabase `zurvan_account_features.pahlavi_occasions`;
read through `zurvan-license-status.features.pahlavi_occasions`. A stored local
preference never grants access by itself. Grant revocations propagate with
online status refresh and limited offline cache policies.

- Desktop: Settings > Calendar & Time > Shahanshahi (Pahlavi) calendar toggle.
  Displayed primary month/year and yearly historical-occasion listing reflect
  the selection. Standard Solar Hijri dates are retained for storage and
  calculations.
- Android: Settings > Calendar & Occasions > Shahanshahi calendar card.
  Independent switches for year numbering and historical occasions.
  The year dropdown and month heading show converted years; the calculation
  engine continues to receive Solar Hijri years.
- iOS: Settings > Shahanshahi calendar contains separate switches; the month
  header shows imperial numbering, and the Occasions screen has corresponding
  switches. The existing Solar Hijri date grid and selection persist.

Unlicensed users see disabled/hidden controls and ordinary Solar Hijri dates.
The historical feed is a curated **non-holiday** dataset, not the full official
historical holiday almanac or a replacement for `time.ir`.

## Historical dataset across clients

All three clients include these annual *historical reference* records:

| Solar month/day | Description |
| --- | --- |
| Farvardin 1 | New year in imperial notation |
| Shahrivar 25 | Accession of Mohammad Reza Pahlavi in 1320 |
| Aban 4 | Birthday and the 1346 coronation of Mohammad Reza Pahlavi |
| Dey 26 | Departure from Iran, 1357 |
| Bahman 6 | White Revolution referendum, 1341 |
| Esfand 24 | 1354 adoption of imperial era numbering |

These entries are historical labels, not indications of state holidays.
Their modern annual recurrence is for lookup and display only.
Reference sources:
- Encyclopaedia Iranica, Elections:
  https://www.iranicaonline.org/articles/elections/
- Encyclopaedia Iranica, Calendars:
  https://www.iranicaonline.org/articles/calendars/
- Numismatic coronation record:
  https://leunumismatik.com/en/lot/74/5043/%5B74%5D
- Historical newspaper, departure 26 Dey 1357:
  https://commons.wikimedia.org/wiki/File:Ettelaat13571026_(page_1_crop).jpg

## Acceptance checks before shipping

1. With grant=false and preference=true, *no* imperial year or restricted feed
   must appear in any client.
2. With grant=true and preference=false, all existing dates remain Solar Hijri.
3. With grant=true and year toggle=true, Farvardin 1405 displays 2585;
   corresponding month/day and weekday must not change.
4. Year navigation (Android year picker, Windows month navigator, iOS month
   arrows) must preserve canonical Solar Hijri dates.
5. With historical toggle=true, a matching event appears as non-holiday;
   the official holiday indicator must not change.
6. Revocation should remove the feature after status revalidation or cache
   expiry, without deleting user preferences or stored occasions.
7. Regression-test paid license, trial expiry, read-only app, sign-in/out,
   unsupported device, leap Esfand, and year boundaries on real devices.

CI compilation is a separate test from full runtime acceptance.
