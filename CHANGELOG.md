# Changelog

All notable changes to this package are documented in this file.

## 0.2.0 - 2026-09-13

### Changed

- Replaced automatic layout inference with an explicit developer/Codex Recipe workflow.
- Added a compact final preview that draws the real Sprite texture region.
- Kept transactional Build, independent Validate, rollback, and Ctrl+S Profile synchronization.
- Made Profile-external Tiles outside Recipe target cells silent and preserved.
- Prevented Build-owned saves from triggering Ctrl+S synchronization.
- Persisted reused Tile references back into the Profile during Build.

### Removed

- Removed visual-model providers, API configuration, HTTP/proxy code, layout candidates, and inference templates.
- Removed the obsolete layout-template sample.

## 0.1.0 - 2026-09-10

### Added

- Visual-first Sprite grouping and two-dimensional Tile layout analysis.
- Automatic request batching with a maximum of 36 Sprites per request.
- Strict validation for missing, unknown, duplicate, and colliding Sprite IDs.
- One-time local GLM API Key entry.
- Safe Tile creation, placement, movement, validation, and rollback.
- Automatic Profile synchronization after manual Palette edits are saved.
- Multiple Profile and Palette support.
- Editor tests and batch self-validation.
