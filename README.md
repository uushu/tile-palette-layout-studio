# Tile Palette Layout Studio

Tile Palette Layout Studio is a Unity Editor package for arranging individually imported Sprite assets into a readable Tile Palette without dragging every Tile by hand.

## Requirements

- Unity 2022.3 or newer.
- Unity 2D Tilemap.
- A source folder containing textures imported as Sprite.
- A Tile output folder.
- A Palette prefab containing exactly one Tilemap.
- A personal Zhipu AI API Key for visual analysis.

Names are optional hints rather than strict identifiers. Unity asset GUID and local file ID values remain the permanent identity of every Sprite.

## Installation

Open Package Manager, select Add package from git URL, and enter:

    https://github.com/uushu/tile-palette-layout-studio.git

For local development, select the package.json file from this repository.

## First Use

1. Open Tools > Tile Palette > Layout Studio.
2. Create or select a Profile.
3. Assign Source Folder, Tile Output Folder, and Palette Prefab.
4. Click Analyze Layout.
5. Enter your GLM API Key in the one-field password window and click Save.
6. Review the final two-dimensional layout.
7. Click Build / Update Palette.

The Key is stored only in the consuming Unity project's local file:

    Library/TilePaletteLayoutStudio/.env.local

It is not written to Assets, Profiles, package files, logs, or Git. The key can also be supplied through the ZHIPUAI_API_KEY environment variable.

After the first save, Analyze Layout uses the local Key automatically and does not show configuration again.

## Analysis

The analyzer:

1. Recursively discovers every Sprite under the selected source folder.
2. Reads pixels through Unity's GPU texture path without changing Read/Write import settings.
3. Uses names only as optional grouping hints.
4. Packs complete hint groups into requests of no more than 36 Sprites.
5. Creates one numbered PNG contact sheet per request.
6. Uses Zhipu GLM-4.6V-Flash to produce one strict JSON layout per batch.
7. Rejects missing, unknown, duplicate, or colliding Sprite IDs.
8. Safely merges all batches and locally verifies adjacent edges.
9. Displays one final two-dimensional preview.

The package does not expose Row, Column, Grid, or candidate selection. Optional TileLayoutTemplate assets can provide project-specific hints without adding project rules to source code.

## Build and Synchronization

The Profile is the authoritative layout. The builder:

- Reuses compatible existing Tile assets.
- Creates only missing ordinary Tile assets.
- Keeps correctly placed Tiles.
- Moves known Tiles to their Profile cells.
- Refuses ambiguous custom TileBase assets and coordinate conflicts.
- Backs up the Palette prefab and Profile before writing.
- Restores both and removes newly created Tile assets if a build fails.

Moving or deleting managed Tiles in the Palette and pressing Ctrl+S automatically synchronizes the Profile:

- A uniform group movement updates the group origin.
- An individual movement creates a per-Tile manual override.
- A deleted managed Tile becomes disabled.
- Duplicate or ambiguous Tiles produce a warning.
- Synchronization failures restore the Profile and Palette.

Multiple Profiles and Palette prefabs are supported.

## Vision Provider

The included provider uses:

- Provider: Zhipu AI.
- Model: glm-4.6v-flash.
- Endpoint: https://open.bigmodel.cn/api/paas/v4/chat/completions.
- Local key name: ZHIPUAI_API_KEY.

Provider classes implement ITilePaletteVisionProvider and are discovered through Unity TypeCache. Each provider owns its endpoint, model, request JSON, authentication headers, and response parsing.

Do not place API Keys in source code, package assets, Profiles, logs, or Git.

## Editor Architecture

- TilePaletteLayoutStudioWindow: Profile selection, actions, and preview.
- TilePaletteSourceScanner: Sprite discovery and local pixel features.
- TilePaletteVisionAnalyzer: batching, prompts, merge, and strict validation.
- TilePaletteContactSheetRenderer: numbered PNG contact sheets.
- TilePaletteVisionHttpClient: UnityWebRequest, timeout, proxy detection, and bounded retry.
- ITilePaletteVisionProvider: provider-specific transport format.
- TilePaletteBuilder: plans, transactional writes, rollback, and validation.
- TilePaletteAutoSync: save-time manual layout synchronization.

## Tests

The package contains EditMode tests for:

- Optional naming hints.
- Missing Sprite rejection.
- Coordinate collision rejection.
- GLM image request structure.
- Request batch limits and group preservation.
- Exact managed-cell behavior for layout holes.
- Provider discovery.

The batch self-validation entry point is:

    -executeMethod TilePaletteLayoutStudio.TilePaletteLayoutStudioValidation.RunAll

Reports are written to:

    Library/TilePaletteLayoutStudio/Reports/latest.txt

## License

MIT
