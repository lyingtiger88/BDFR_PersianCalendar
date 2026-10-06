# picture

Runtime picture assets for BDFR Persian Calendar.

## Structure

- `themes/`
  - `zara-pastel/`
    - `theme.json`
- `theme/`
  - `season backgrounds/`
    - `Spring_16x9.svg`
    - `Summer_16x9.svg`
    - `Autumn_16x9.svg`
    - `Winter_16x9.svg`
    - `spring.svg` / `summer.svg` / `autumn.svg` / `winter.svg` (generic fallbacks)
- `event picture/`
  - `default.svg`
  - `birthday.svg`
  - `nowruz.svg`
  - `yalda.svg`
  - `religious.svg`
  - `national.svg`
  - `personal.svg`

## Elena Mode seasonal naming

Elena Mode is independent from visual themes such as Zara Pastel.

Seasonal files may be SVG, PNG, JPG, JPEG, or WEBP. The preferred naming convention is:

- `Spring_16x9.jpg`
- `Spring_16x10.jpg`
- `Spring_21x9.jpg`
- `Spring_4x3.jpg`

Use the same pattern for `Summer`, `Autumn`, and `Winter`.

At runtime the app reads the current Windows display work-area aspect ratio, chooses the closest matching seasonal variant, and falls back to the generic season file if no ratio-specific file exists.

User-selected backgrounds are copied at runtime to:
`%LOCALAPPDATA%\BDFR\PersianCalendar\picture\user background`.
