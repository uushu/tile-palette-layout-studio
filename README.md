# Tile Palette Layout Studio

[![Unity][unity-badge]][unity] [![Package Version][version-badge]][package] [![License][license-badge]][license] [![GitHub Stars][stars-badge]][stargazers]

**Reconstruct structured Unity Tile Palettes from loose Sprite assets.**

Tile Palette Layout Studio is a Unity Editor extension that identifies tiles that visually belong together, infers their relative grid positions, and rebuilds them into organized multi-tile groups — even when the source Sprites are imported from separate texture files.

Instead of dragging dozens of independent Tiles back into walls, cliffs, buildings, transitions, or other multi-tile patterns by hand, select a Sprite folder, analyze the layout, review the result, and build it into a reusable Unity Tile Palette.

> Layout reconstruction is inference-based. The Studio validates structure and asset identity, but the preview remains the final review step before writing to the Palette.

## Overview

```text
Loose Sprite Assets
        ↓
Analyze Layout
        ↓
Visual Grouping + Spatial Reconstruction
        ↓
2D Layout Preview
        ↓
Build / Update Palette
        ↓
Manual Edits → Ctrl+S → Profile Sync
```

The tool is designed for tile art whose useful visual structure has been lost at import time — especially asset packs where related tiles arrive as separate PNGs or otherwise no longer share a usable source layout.

It reconstructs that structure at the **Tile Palette level**:

- related Sprites are grouped into visual objects or tile patterns;
- each Sprite receives a relative grid position inside its group;
- groups are written into a structured Tile Palette layout;
- existing compatible Tile assets are reused where possible;
- later manual Palette edits can be synchronized back to the Profile.

## Features

- **Loose Sprite reconstruction** — works with Sprites discovered across multiple Texture assets, not only a single spritesheet.
- **Visual grouping** — identifies Sprites that appear to belong to the same multi-tile object or pattern.
- **Spatial layout inference** — reconstructs relative cell positions instead of merely sorting files by name.
- **2D preview before build** — inspect the inferred layout before the Palette is modified.
- **Stable Sprite identity** — Unity asset GUID and local file ID are used as permanent identity; filenames are only optional hints.
- **Safe incremental builds** — compatible Tiles are reused, correct placements are preserved, and only missing ordinary Tiles are created.
- **Transactional rollback** — the Profile and Palette are backed up before writes and restored if a build fails.
- **Manual edit synchronization** — moving or deleting managed Tiles and saving the Palette updates the Profile.
- **Multiple Profiles** — different Sprite sources and Palette prefabs can be managed independently.

## Requirements

- Unity **2022.3** or newer.
- Unity **2D Tilemap**.
- A source folder containing textures imported as Sprite.
- A folder for generated Tile assets.
- A Palette prefab containing exactly one Tilemap.
- A Zhipu AI API Key for the included visual-analysis provider.

## Installation

### Unity Package Manager

Open **Window → Package Manager**, select **Add package from git URL...**, and enter:

```text
https://github.com/uushu/tile-palette-layout-studio.git
```

### Local development

Clone this repository, then select **Add package from disk...** in Package Manager and choose `package.json`.

## Quick Start

1. Open **Tools → Tile Palette → Layout Studio**.
2. Create or select a **Profile**.
3. Assign the **Source Folder**, **Tile Output Folder**, and **Palette Prefab**.
4. Click **Analyze Layout**.
5. On first use, enter your GLM API Key and save it locally.
6. Review the generated **Layout Preview**.
7. Click **Build / Update Palette**.
8. Use **Validate** whenever you want to verify the Profile, Tile assets, and Palette cells.

## How Layout Reconstruction Works

The source scanner recursively discovers every Sprite under the selected folder. A Sprite may come from its own Texture file or be one of multiple Sprites imported from the same Texture.

For analysis, the Studio:

1. reads Sprite pixels through Unity's GPU texture path without changing `Read/Write` import settings;
2. assigns every Sprite a stable analysis ID backed by its Unity GUID and local file ID;
3. uses filenames only as optional grouping hints;
4. creates numbered contact sheets in batches of at most **36 Sprites**;
5. asks the configured vision provider to divide the Sprites into visual groups and infer a 2D grid position for every Sprite;
6. rejects missing, unknown, duplicate, or colliding Sprite IDs;
7. merges valid batches;
8. locally checks neighboring tile edges as an additional confidence signal;
9. presents one final two-dimensional layout for review.

The important distinction is that the Studio does **not** merely sort Sprites into a cleaner list. It attempts to recover their **visual grouping and relative spatial layout** so that multi-tile structures become readable again inside Unity's Tile Palette.

Optional `TileLayoutTemplate` assets can provide project-specific hints without hard-coding project rules into the analyzer.

## Build & Synchronization

The **Profile** is the authoritative layout description for a managed Palette.

When **Build / Update Palette** runs, the builder:

- reuses compatible existing Tile assets;
- creates only missing ordinary Tile assets;
- keeps Tiles that are already in the correct cells;
- moves known Tiles to their Profile positions;
- rejects ambiguous custom `TileBase` assets and coordinate conflicts;
- backs up the Palette prefab and Profile before writing;
- restores both and removes newly created Tile assets if the build fails.

Manual Palette edits remain part of the workflow. After moving or deleting managed Tiles, press `Ctrl+S`:

- moving an entire group updates its group origin;
- moving one Tile creates a per-Tile manual override;
- deleting a managed Tile marks it as disabled;
- duplicate or ambiguous Tiles produce a warning;
- synchronization failures restore the previous valid Profile and Palette state.

## API Key & Vision Provider

The included provider currently uses:

- **Provider:** Zhipu AI
- **Model:** `glm-4.6v-flash`
- **Endpoint:** `https://open.bigmodel.cn/api/paas/v4/chat/completions`
- **Environment variable:** `ZHIPUAI_API_KEY`

When entered through the Studio, the key is stored only in the consuming Unity project's local file:

```text
Library/TilePaletteLayoutStudio/.env.local
```

It is not written to `Assets`, Profiles, package files, logs, or Git.

Provider implementations use `ITilePaletteVisionProvider` and are discovered through Unity `TypeCache`. Each provider owns its endpoint, model, authentication headers, request format, and response parsing.

## Editor Architecture

| Component | Responsibility |
| --- | --- |
| `TilePaletteLayoutStudioWindow` | Profile selection, actions, and 2D preview |
| `TilePaletteSourceScanner` | Sprite discovery and local pixel features |
| `TilePaletteContactSheetRenderer` | Numbered contact-sheet generation |
| `TilePaletteVisionAnalyzer` | Grouping, batching, spatial inference, merge, and validation |
| `TilePaletteVisionHttpClient` | `UnityWebRequest`, timeout, proxy detection, and bounded retry |
| `ITilePaletteVisionProvider` | Provider-specific transport and response format |
| `TilePaletteBuilder` | Build planning, transactional writes, rollback, and validation |
| `TilePaletteAutoSync` | Save-time synchronization after manual Palette edits |

## Tests

The package contains EditMode tests for:

- optional naming hints;
- missing Sprite rejection;
- coordinate collision rejection;
- GLM image request structure;
- request batch limits and group preservation;
- exact managed-cell behavior for layout holes;
- provider discovery.

The batch self-validation entry point is:

```text
-executeMethod TilePaletteLayoutStudio.TilePaletteLayoutStudioValidation.RunAll
```

Reports are written to:

```text
Library/TilePaletteLayoutStudio/Reports/latest.txt
```

## Contributing

Issues and pull requests are welcome.

For layout-related bug reports, include the Unity version, the expected structure, the generated preview, and a minimal description of the source Sprite set when possible.

## License

Tile Palette Layout Studio is available under the [MIT License](LICENSE.md).

[unity-badge]: https://img.shields.io/badge/Unity-2022.3%2B-000000?logo=unity&logoColor=white
[unity]: https://unity.com/releases/editor/archive
[version-badge]: https://img.shields.io/badge/package-v0.1.0-2ea44f
[package]: https://github.com/uushu/tile-palette-layout-studio/blob/main/package.json
[license-badge]: https://img.shields.io/badge/License-MIT-blue.svg
[license]: https://github.com/uushu/tile-palette-layout-studio/blob/main/LICENSE.md
[stars-badge]: https://img.shields.io/github/stars/uushu/tile-palette-layout-studio?style=flat
[stargazers]: https://github.com/uushu/tile-palette-layout-studio/stargazers
