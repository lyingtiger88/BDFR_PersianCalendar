# Zurvan Theme Studio (Windows)

A standalone Windows Forms GUI for authoring Android Zurvan color palettes.

## Run locally

Requires .NET 9 SDK on Windows.

```powershell
dotnet run --project tools/Zurvan.ThemeStudio/Zurvan.ThemeStudio.csproj
```

This tool does not write to Android files automatically. It exports a portable JSON theme file or a ZIP containing `theme.json`. Send that file for integration into the Android app's palette presets.

## Format

```json
{
  "schemaVersion": 1,
  "format": "zurvan-android-theme",
  "name": "My theme",
  "themeId": "windows-light",
  "colors": {
    "background": "#F5F6F8",
    "surface": "#FFFFFF",
    "card": "#FFFFFF",
    "ink": "#202631",
    "accent": "#9DC7F5",
    "holiday": "#D34E5E"
  }
}
```

Fields map directly to the Android `ZurvanThemes` palette keys. The GUI includes a live calendar preview and highlights the element under edit. Import and export support `.json` and `.zip`.

Note: user-provided themes are not automatically installed by the mobile app; integration is a separate repository change. No CI build is triggered by this tool's creation.
