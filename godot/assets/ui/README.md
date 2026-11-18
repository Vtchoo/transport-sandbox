# UI icons

The SVG icons in `icons/` come from the browser game's installed `@mdi/js` Material Design Icons package. Its license is included at `icons/LICENSE`.

Regenerate them from the repository root with `node scripts/export-godot-icons.mjs`. `UiSkin.Icon` loads these shared assets for the native HUD and window controls.
