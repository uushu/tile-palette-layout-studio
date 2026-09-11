<div align="center">

# Tile Palette Layout Studio

**Reconstruct multi-tile structures from loose Sprite assets.**

An AI-assisted Unity Editor tool that groups related Sprites, infers their 2D arrangement, and rebuilds them into organized Tile Palette layouts — even when the pieces come from separate textures.

[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-000000?style=for-the-badge&logo=unity&logoColor=white)](https://unity.com/releases/editor/archive)
[![Package v0.1.0](https://img.shields.io/badge/Package-v0.1.0-2ea44f?style=for-the-badge)](package.json)
[![MIT License](https://img.shields.io/badge/License-MIT-blue?style=for-the-badge)](LICENSE.md)

[Installation](#installation) · [Quick Start](#quick-start) · [How It Works](#how-it-works) · [License](#license)

</div>

---

## Overview

Unity can import every Sprite in a tileset, but once related pieces are split across files or textures, their **spatial relationship is no longer encoded in the assets themselves**.

Tile Palette Layout Studio reconstructs that missing organization at the **Tile Palette level**. It identifies Sprites that appear to belong to the same multi-tile structure, infers their relative grid positions, lets you review the reconstructed layout, and then builds it into a reusable Unity Tile Palette.

Typical inputs include walls, cliffs, buildings, terrain transitions, props, and other structures assembled from multiple tiles.

> **This is not a file sorter.** The Studio attempts to reconstruct visual grouping and relative 2D structure.

| Capability | What it does |
| --- | --- |
| **Reconstruct multi-tile structures** | Groups related Sprites and infers their relative grid placement. |
| **Work across textures** | Related pieces can come from separate PNGs, multiple textures, one spritesheet, or a mixture of them. |
| **Preview before writing** | Shows the inferred 2D layout before the Tile Palette is modified. |
| **Build safely** | Reuses compatible Tile assets, validates conflicts, and rolls back failed writes. |
| **Stay editable** | Manual Palette changes can be synchronized back into the layout Profile. |
| **Manage multiple palettes** | Separate Profiles can manage different Sprite sources and Palette prefabs. |

## Requirements

- Unity **2022.3** or newer
- Unity **2D Tilemap**
- A source folder containing assets imported as `Sprite`
- A folder for generated `Tile` assets
- A Tile Palette prefab containing exactly one `Tilemap`
- A Zhipu AI API Key for the included vision provider

## Installation

### Unity Package Manager

Open **Window → Package Manager**, choose **+ → Add package from git URL...**, and enter:

```text
https://github.com/uushu/tile-palette-layout-studio.git
```

### Local development

Clone the repository, then use **Package Manager → + → Add package from disk...** and select `package.json`.

## Quick Start

1. Open **Tools → Tile Palette → Layout Studio**.
2. Create or select a **Profile**.
3. Assign the **Source Folder**, **Tile Output Folder**, and **Palette Prefab**.
4. Click **Analyze Layout**.
5. Enter your GLM API Key on first use.
6. Review the generated **Layout Preview**.
7. Click **Build / Update Palette**.

```text
Loose Sprite assets
        ↓
Visual grouping
        ↓
Spatial layout inference
        ↓
2D layout preview
        ↓
Build / Update Tile Palette
        ↓
Manual edits → Save → Profile sync
```

## How It Works

### 1. Discover and identify Sprites

The Studio recursively scans the selected source folder and discovers every imported Sprite, whether it comes from an individual image, a larger spritesheet, or another Texture asset.

Each Sprite is tracked by its Unity asset GUID and local file ID. Filenames are treated only as optional semantic hints.

### 2. Infer grouping and spatial layout

Sprites are rendered into numbered contact sheets without changing their `Read/Write` import settings.

The included vision pipeline analyzes the visible tile pieces and returns:

- visual groups and optional subgroups;
- a relative `(x, y)` grid position for every Sprite;
- an overall confidence value.

Large inputs are split into batches of at most **36 Sprites** while preserving complete hint groups where possible.

### 3. Validate before accepting the layout

The Studio rejects results containing:

- missing Sprite IDs;
- unknown Sprite IDs;
- duplicate Sprite IDs;
- coordinate collisions;
- invalid managed-cell conflicts.

Adjacent tile edges are also compared locally and used as an additional confidence signal.

### 4. Preview, build, and keep editing

The reconstructed layout is shown in the editor before anything is written to the Palette.

When **Build / Update Palette** runs, the Studio reuses compatible Tile assets, creates only missing ordinary Tiles, preserves correct placements, moves known Tiles to their Profile cells, and backs up the Profile and Palette before writing.

If a build fails, the previous valid state is restored and newly created Tile assets are removed.

Afterward, the Palette remains editable. Moving or deleting managed Tiles and saving the Palette synchronizes those edits back to the Profile.

## Scope & Limitations

Layout reconstruction is **inference-based**. The Studio can validate identity, completeness, coordinate conflicts, and local visual consistency, but it cannot guarantee that the inferred arrangement is the exact original layout intended by the asset author.

The preview is therefore the final review step before writing to the Tile Palette.

Tile Palette Layout Studio is an **Editor workflow tool**. It reconstructs and manages Tile Palette layouts; it is not a runtime procedural map generator.

<details>
<summary><strong>API Key & Vision Provider</strong></summary>

<br>

The included provider currently uses:

| | |
| --- | --- |
| Provider | Zhipu AI |
| Model | `glm-4.6v-flash` |
| Environment variable | `ZHIPUAI_API_KEY` |

When entered through the Studio, the API Key is stored locally in the consuming Unity project:

```text
Library/TilePaletteLayoutStudio/.env.local
```

It is not written to `Assets`, Profiles, package files, logs, or Git.

Provider integrations implement `ITilePaletteVisionProvider` and are discovered through Unity `TypeCache`, keeping provider-specific request, authentication, and response logic separate from layout reconstruction.

</details>

<details>
<summary><strong>Editor Architecture</strong></summary>

<br>

| Component | Responsibility |
| --- | --- |
| `TilePaletteLayoutStudioWindow` | Profile selection, actions, and layout preview |
| `TilePaletteSourceScanner` | Sprite discovery and local pixel features |
| `TilePaletteContactSheetRenderer` | Numbered contact-sheet generation |
| `TilePaletteVisionAnalyzer` | Batching, grouping, spatial inference, merge, and validation |
| `TilePaletteLayoutVerifier` | Local adjacent-edge verification |
| `TilePaletteVisionHttpClient` | Vision HTTP transport, timeout, proxy detection, and retry |
| `ITilePaletteVisionProvider` | Provider-specific request and response format |
| `TilePaletteBuilder` | Build planning, transactional writes, validation, and rollback |
| `TilePaletteAutoSync` | Synchronization after manual Palette edits |

</details>

<details>
<summary><strong>Validation & Tests</strong></summary>

<br>

The package includes EditMode coverage for:

- optional naming hints;
- missing Sprite rejection;
- coordinate collision rejection;
- GLM image request structure;
- request batch limits and group preservation;
- exact managed-cell behavior for layout holes;
- provider discovery.

Batch validation entry point:

```text
-executeMethod TilePaletteLayoutStudio.TilePaletteLayoutStudioValidation.RunAll
```

Reports are written to:

```text
Library/TilePaletteLayoutStudio/Reports/latest.txt
```

</details>

## Contributing

Issues and pull requests are welcome.

For layout reconstruction issues, include the Unity version, expected structure, generated preview, and a minimal description of the source Sprite set when possible.

## License

Tile Palette Layout Studio is released under the [MIT License](LICENSE.md).
