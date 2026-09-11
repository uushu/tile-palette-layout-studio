# Changelog

All notable changes to this package are documented in this file.

## 0.1.1 - 2026-09-11

### Fixed

- Preserved the last successful layout preview when a new analysis fails or is cancelled.
- Added visible batch progress and cancellation for long-running visual analysis.
- Reduced automatic request retries and the default request timeout so transient failures do not block the Editor for several minutes.
- Respected `Retry-After` for HTTP 429 responses when provided by the vision service.
- Kept large named Sprite groups in a shared coordinate system across multiple requests by carrying validated anchor Sprites into continuation batches.
- Rejected continuation batches when an anchor is renamed, moved, or omitted instead of silently merging inconsistent coordinates.
- Composited transparent Sprite pixels onto the contact-sheet background before upload so the vision model receives an opaque, deterministic image.
- Increased contact-sheet label readability with fixed two-digit, high-contrast labels.
- Preserved Sprite aspect ratio when rendering contact-sheet previews.
- Added regression tests for continuation batching, anchor validation, continuation merging, and alpha compositing.

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
