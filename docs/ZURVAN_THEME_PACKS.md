# Zurvan Theme Pack Specification (Draft v1)

Theme packs extend the existing Windows themes and the Android/iOS appearance presets.

Directory:
- `manifest.json`: theme metadata, version and colors
- `background.jpg` or `background.png`: optional user-supplied background
- `preview.jpg`: optional preview image

Example manifest:
```json
{
  "schemaVersion": 1,
  "id": "my-custom-theme",
  "name": "My Custom Theme",
  "colors": {
    "windowBackground": "#FFF5F6F8",
    "panelBackground": "#F2FFFFFF",
    "cardBackground": "#F8FFFFFF",
    "primaryText": "#FF202631",
    "secondaryText": "#FF697386",
    "accent": "#FFD9E6F8",
    "selectedDay": "#FF9DC7F5",
    "holidayText": "#FFD34E5E"
  },
  "background": {
    "file": "background.jpg",
    "fit": "cover",
    "opacity": 0.25
  }
}
```

Security requirements:
- Only local .png/.jpg image assets and JSON metadata; no executable content or remote image URLs
- Validate image format and dimensions, reject huge images, cap extracted sizes and entry counts
- Reject `../` traversal and absolute paths in packaged archives
- Store user backgrounds in app-controlled storage, never in authentication or vault-key storage
- Preserve holiday red and mourning black indicators even for user themes
- Theme images are local decoration, not automatically synchronized with private Zurvan Desk contents

Compatibility status:
- Windows: already supports JSON-based color themes; theme-pack import UI not implemented
- Android: built-in color presets and local wallpaper picker; ZIP pack import not implemented
- iOS: built-in appearance choices and Photos picker; ZIP pack import not implemented

Future image-based themes can use this structure and share color tokens across the three platforms.
