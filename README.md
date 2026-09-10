<div align="center">

# Tile Palette Layout Studio

**Rebuild structured Unity Tile Palettes from loose Sprite assets.**

An AI-assisted Unity Editor extension that groups related tiles, infers their relative grid placement, previews the reconstructed layout, and safely builds it into a reusable Tile Palette.

[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-000000?logo=unity&logoColor=white)](https://unity.com/releases/editor/archive)
[![Package](https://img.shields.io/badge/package-v0.1.0-2ea44f)](https://github.com/uushu/tile-palette-layout-studio/blob/main/package.json)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.md)
[![GitHub stars](https://img.shields.io/github/stars/uushu/tile-palette-layout-studio?style=flat)](https://github.com/uushu/tile-palette-layout-studio/stargazers)

</div>

---

## What is Tile Palette Layout Studio?

Tile Palette Layout Studio is a Unity Editor tool for **reconstructing meaningful Tile Palette structure from loose Sprite assets**.

A tileset does not always arrive as one clean spritesheet. Related pieces may be imported as separate PNGs, split across multiple textures, or otherwise lose the spatial arrangement that made the original object readable.

The Studio analyzes those Sprites, determines which ones visually belong together, infers their relative cell positions, and rebuilds them as organized multi-tile structures inside Unity's Tile Palette.

It is not a file sorter.

It is a **Tile Palette layout reconstruction tool**.

```text
Loose Sprite assets
        ↓
Visual grouping
        ↓
Spatial layout inference
        ↓
Structured 2D preview
        ↓
Build / Update Tile Palette
```

> Layout reconstruction is inference-based. The tool validates Sprite identity, completeness, collisions, and local edge consistency, while the preview remains the final review step before writing to the Palette.

## Why?

When a multi-tile object is split into independent Sprites, Unity still knows how to render each Sprite, but it no longer knows how those pieces were meant to relate spatially.

A structure such as this:

```text
        ┌───┐
        │ A │
    ┌───┼───┼───┐
    │ B │ C │ D │
    └───┼───┼───┘
        │ E │
        └───┘
```

may arrive in the project as nothing more than:

```text
A.png   B.png   C.png   D.png   E.png
```

Rebuilding walls, cliffs, buildings, terrain transitions, props, or other multi-tile patterns by hand is repetitive and error-prone.

Tile Palette Layout Studio reconstructs the **grouping** and **relative spatial relationships** so those pieces become readable and reusable again in the Palette.

## Features

- **Layout reconstruction** — rebuilds structured Tile Palette layouts rather than merely sorting Sprite files.
- **Loose Sprite support** — related Sprites may come from separate Texture assets or from the same spritesheet.
- **Visual grouping** — identifies tiles that appear to belong to the same object, terrain set, transition, or pattern.
- **Spatial inference** — assigns relative grid coordinates to each Sprite inside its reconstructed group.
- **2D layout preview** — review the inferred structure before changing the Palette.
- **Stable Sprite identity** — Unity GUID + local file ID are used as persistent identity; filenames are only optional hints.
- **Safe incremental builds** — reuses compatible Tile assets and creates only what is missing.
- **Conflict validation** — rejects missing, unknown, duplicate, or colliding Sprite placements.
- **Transactional rollback** — restores the Profile and Palette if a build fails.
- **Manual edit sync** — move or remove managed Tiles in the Palette, save, and synchronize those edits back to the Profile.
- **Multiple Profiles** — manage different Sprite sources and Palette prefabs independently.

## Requirements

- Unity **2022.3** or newer
- Unity **2D Tilemap**
- A source folder containing assets imported as `Sprite`
- A folder for generated `Tile` assets
- A Tile Palette prefab containing exactly one `Tilemap`
- A Zhipu AI API Key for the included vision provider

## Installation

### Unity Package Manager

Open **Window → Package Manager**.

Select **+ → Add package from git URL...** and enter:

```text
https://github.com/uushu/tile-palette-layout-studio.git
```

### Local development

Clone the repository, then use:

```text
Package Manager
→ +
→ Add package from disk...
→ package.json
```

## Getting Started

1. Open **Tools → Tile Palette → Layout Studio**.
2. Create or select a **Profile**.
3. Assign the **Source Folder**.
4. Assign the **Tile Output Folder**.
5. Assign the target **Palette Prefab**.
6. Click **Analyze Layout**.
7. Enter your GLM API Key on first use.
8. Review the generated **Layout Preview**.
9. Click **Build / Update Palette**.

The normal workflow is intentionally short:

```text
Select source
   ↓
Analyze
   ↓
Review
   ↓
Build
```

## How it works

### 1. Discover Sprites

The source scanner recursively discovers every Sprite under the selected folder.

Sprites can come from:

- individual PNG files;
- multiple Texture assets;
- a spritesheet containing multiple Sprite sub-assets;
- a mixture of the above.

The tool does not require every related tile to live in the same Texture.

### 2. Preserve stable identity

Every Sprite is tracked using its Unity asset GUID and local file ID.

Names such as `wall_01`, `wall_left`, or `cliff_corner` are treated as optional semantic hints, not permanent identity.

### 3. Build visual contact sheets

Sprites are rendered into numbered contact sheets for visual analysis without changing their `Read/Write` import setting.

Large inputs are automatically divided into batches of up to **36 Sprites**.

### 4. Reconstruct the layout

The vision provider analyzes every Sprite and returns:

- a visual group;
- an optional subgroup;
- a relative `(x, y)` grid position for each Sprite;
- an overall confidence value.

The important distinction is:

```text
Sorting
A B C D E

vs.

Reconstruction
    A
  B C D
    E
```

Tile Palette Layout Studio performs the second operation.

### 5. Validate the result

Before a layout is accepted, the Studio rejects:

- missing Sprite IDs;
- unknown Sprite IDs;
- duplicate Sprite IDs;
- duplicate coordinates within a group;
- invalid managed-cell conflicts.

Adjacent tile edges are also compared locally and used as an additional confidence signal.

### 6. Preview before writing

The reconstructed 2D layout is displayed inside the editor before the Tile Palette is modified.

This keeps the inference visible instead of silently treating it as ground truth.

## Build & Update

The Profile is the authoritative description of a managed layout.

When **Build / Update Palette** runs, the Studio:

- reuses compatible existing Tile assets;
- creates only missing ordinary Tile assets;
- preserves Tiles already in the correct cells;
- moves known Tiles to their Profile positions;
- rejects ambiguous custom `TileBase` assets;
- rejects coordinate conflicts;
- backs up the Profile and Palette before writing.

If the operation fails, the previous valid state is restored and newly created Tile assets are removed.

## Manual Editing & Synchronization

The generated Palette is still editable by hand.

After modifying managed Tiles in Unity and pressing `Ctrl + S`:

- moving a complete group updates the group origin;
- moving one Tile creates a per-Tile manual override;
- deleting a managed Tile marks it as disabled;
- duplicate or ambiguous Tiles produce a warning;
- synchronization failures restore the previous valid state.

This makes reconstruction a starting point, not a lock-in.

## API Key & Vision Provider

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

Provider integrations implement `ITilePaletteVisionProvider` and are discovered with Unity `TypeCache`, keeping provider-specific request and authentication logic separate from layout reconstruction.

<details>
<summary><strong>Editor architecture</strong></summary>

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
<summary><strong>Validation & tests</strong></summary>

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

For layout reconstruction issues, please include the Unity version, expected structure, generated preview, and a minimal description of the source Sprite set when possible.

## License

Tile Palette Layout Studio is released under the [MIT License](LICENSE.md).
