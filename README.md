<div align="center">

# Tile Palette Layout Studio

**Reconstruct original multi-tile structures from loose Sprite assets.**

A Recipe-driven Unity Editor workflow for restoring reviewed Sprite relationships into reusable Tile Palette layouts — with real 2D preview, transactional builds, validation, rollback, and Ctrl+S synchronization.

[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-000000?style=for-the-badge&logo=unity&logoColor=white)](https://unity.com/releases/editor/archive)
[![Package v0.2.0](https://img.shields.io/badge/Package-v0.2.0-2ea44f?style=for-the-badge)](package.json)
[![MIT License](https://img.shields.io/badge/License-MIT-blue?style=for-the-badge)](LICENSE.md)

[Overview](#overview) · [Workflow](#workflow) · [Installation](#installation) · [Quick Start](#quick-start) · [How It Works](#how-it-works) · [Safety](#build-safety) · [License](#license)

</div>

---

## Overview

Unity can import sliced Sprites and place them into a Tile Palette, but it does not know the **original object structure** those pieces formed.

A wall may arrive as fifteen independent Sprites. A tall window may arrive as three separate pieces. Stairs, arches, columns, terrain transitions, damaged walls, props, and decorative structures lose the 2D relationships that made them meaningful.

Tile Palette Layout Studio restores that missing organization at the **Tile Palette level**.

A project-specific **Recipe** records which Sprites belong together and where each piece sits inside the reconstructed object. The Studio turns that reviewed structure into a reusable Unity workflow: it resolves real asset references, previews the final arrangement, builds or updates the Palette safely, validates the result, rolls back failures, and keeps accepted manual edits synchronized.

> **This is not a file sorter.** Its core purpose is to reconstruct and preserve original multi-tile object structure from loose or sliced Sprite assets.

| Capability | What it does |
| --- | --- |
| **Reconstruct multi-tile structures** | Restores walls, windows, stairs, arches, columns, terrain pieces, props, and other objects from loose Sprites. |
| **Work across textures** | Related pieces can come from separate PNGs, multiple textures, spritesheets, or a mixture of them. |
| **Use reviewed structure** | A developer or coding agent defines the Recipe instead of relying on filename order or automatic guessing. |
| **Preview the real result** | Renders actual Sprite texture regions in compact 2D group previews before Palette writes. |
| **Build transactionally** | Reuses Tiles, creates missing Tiles, moves known Tiles, validates writes, and restores the previous state on failure. |
| **Preserve unrelated content** | Tiles outside Profile ownership remain untouched unless they occupy a required target cell. |
| **Stay editable** | Manual moves, deletions, restorations, and whole-group offsets can synchronize back into the Profile on save. |
| **Repeat safely** | Rebuilding the same valid layout is idempotent: already-correct Tiles remain `Keep`. |

## Workflow

```text
Loose / sliced Sprite assets
            ↓
Developer or Codex reviews the real visual structure
            ↓
Project Recipe
Sprite → group / subgroup / local cell
            ↓
TilePaletteProfile
resolved Sprite / Tile references + final coordinates
            ↓
Layout Preview
            ↓
CreatePlan
Keep / Create / Place / Move / Conflict
            ↓
Build Palette
            ↓
Validate
            ↓
Manual Palette edits → Ctrl+S → Profile sync
```

The Recipe contains the project-specific visual knowledge. The package provides the reusable Unity Editor workflow around that knowledge.

## Installation

### Unity Package Manager

Open **Window → Package Manager**, choose **+ → Add package from git URL...**, and enter:

```text
https://github.com/uushu/tile-palette-layout-studio.git
```

### Local package development

Clone the repository, then use **Add package from disk...** and select `package.json`.

Do not compile both a UPM-installed copy and a duplicated `Assets/Editor` copy in the same Unity project.

## Quick Start

1. Open **Tools → Tile Palette → Layout Studio**.
2. Create or select a `TilePaletteProfile`.
3. Assign:
   - **Source Folder**
   - **Tile Output Folder**
   - **Palette Prefab**
4. Add a project-specific Recipe in the consuming project's Editor code.
5. Apply that Recipe to the Profile.
6. Inspect **Layout Preview**.
7. Click **Build Palette**.
8. Click **Validate**.
9. Use the resulting Unity Tile Palette normally.
10. If managed Tiles are adjusted manually, save with `Ctrl+S` to synchronize accepted changes back into the Profile.

## How It Works

### 1. Recipe: describe the original structure

A Recipe answers one question:

> **Where should each Sprite belong inside the reconstructed object?**

Each Recipe group stores:

- a group name;
- an optional subgroup name;
- a Palette origin;
- one or more Sprite entries;
- a local grid coordinate for each entry.

Final placement is:

```text
Target Position = Group Origin + Local Position
```

The Recipe can use direct Sprite references or unique names. It may be written manually, generated by Codex, or produced by another project-specific tool.

The package intentionally does **not** assume that filename numbering describes visual structure.

### 2. Profile: resolve Unity resources

`TilePaletteProfile` is the serialized Unity-side execution state.

It stores:

- Source Folder;
- Tile Output Folder;
- Palette Prefab;
- groups and subgroups;
- group origins;
- direct Sprite references;
- direct Tile references;
- local positions;
- inclusion state;
- the last layout hash.

Conceptually:

```text
Recipe
= reviewed layout description

Profile
= Unity-ready resolved layout state
```

Recipe application validates duplicate groups, duplicate Sprite assignments, duplicate local cells, duplicate final target cells, missing Sprites, and ambiguous name resolution before changing the Profile.

### 3. Layout Preview: inspect the final 2D result

`Layout Preview` reads the Profile and renders the layout that the next Build will use.

It:

- draws the actual Sprite texture region using real UVs;
- works with standalone PNGs and sliced spritesheets;
- preserves intentional holes in shapes such as arches;
- groups related pieces into compact preview cards;
- does not change real Palette coordinates while packing preview cards;
- shows the final reviewed structure, not an AI candidate list.

Preview is read-only.

### 4. CreatePlan: decide what must change

Before writing anything, the builder indexes:

```text
Sprite → Tile
Tile → Sprite
Tile → Palette positions
Position → Tile
Profile Entry → Target Position
```

Each managed entry becomes one of five states:

| State | Meaning |
| --- | --- |
| `Keep` | Correct Tile already exists at the correct target cell. |
| `Create` | Sprite exists but no corresponding Tile asset exists yet. |
| `Place` | Tile exists but is not currently present in the Palette. |
| `Move` | Tile exists in the Palette but is in the wrong managed position. |
| `Conflict` | The requested write is unsafe or the Profile/Palette state is invalid. |

Blocking conflicts include:

- missing required Profile paths;
- missing Sprite references;
- duplicate Sprite assignments;
- duplicate target cells;
- one Sprite mapping to multiple incompatible Tiles;
- one known Tile appearing in multiple Palette cells;
- Tile/Sprite mismatches;
- a Profile-external Tile occupying a required target cell;
- a Palette prefab without a Tilemap.

Unknown external Tiles elsewhere in the Palette are preserved and ignored.

### 5. Build Palette: execute the plan safely

A Build performs a complete preflight before writing.

```text
Read Profile
    ↓
Validate paths, Sprites, coordinates, and Palette
    ↓
Scan existing Tiles and Palette cells
    ↓
Create LayoutPlan
    ↓
Stop on blocking conflicts
    ↓
Back up Profile and Palette
    ↓
Create only missing ordinary Tile assets
    ↓
Clear old cells for Tiles that must move
    ↓
Place final Tiles
    ↓
Save Palette Prefab
    ↓
Reimport and Validate
    ↓
Save layout hash and Profile
```

Moves are applied in two phases — clear first, then place — so swaps cannot overwrite each other midway through the operation.

### 6. Validate: verify without modifying

`Validate` is read-only.

It rebuilds the plan from the current Profile and Palette state. Validation passes only when managed entries require no `Create`, `Place`, `Move`, or `Conflict` action.

Errors identify the actual resource and coordinate mismatch instead of only reporting a generic count.

### 7. Ctrl+S synchronization: keep accepted manual edits

The Palette remains editable after Build.

When a managed Palette prefab is saved, the editor waits until the current save operation is complete, then compares the saved Palette against its Profile.

Supported synchronization includes:

- **whole-group move** → updates the group origin when every Tile moved by the same offset;
- **single-Tile move** → updates that entry's local position;
- **known Tile deletion** → updates the entry inclusion state;
- **known Tile restoration** → restores inclusion at the current position.

Profile-external Tiles remain outside Profile ownership.

Build-owned saves are wrapped in a synchronization suppression scope so a normal Build cannot accidentally trigger a second Profile rewrite.

## Build Safety

The builder is transactional.

Before modifying the Palette it preserves enough state to recover from failure. If any write, save, import, or post-build validation step fails, it restores the previous Profile and Palette and removes Tile assets created by that failed Build.

The intended contract is:

```text
all changes succeed
or
all managed state is restored
```

A successful repeated Build is idempotent: already-correct content stays `Keep`, no duplicate Tile assets are created, and the Palette does not drift over time.

## Ownership Model

Tile Palette Layout Studio manages only the entries stored in its Profile.

A Palette may contain many other Tiles that belong to different systems or manual workflows. Those Tiles are:

- preserved;
- not imported into the Profile automatically;
- not deleted automatically;
- not treated as warnings merely for existing;
- only considered blocking when they occupy a cell required by the current Profile.

This allows one Profile to manage a focused subset of a much larger Palette safely.

## Adding New Sprite Sets

Typical supported inputs include:

- standalone PNGs;
- spritesheets sliced into multiple Sprites;
- existing `Tile.asset` files;
- Sprites that do not yet have Tile assets;
- semantic or numeric names.

Recommended workflow:

1. Import the new art into the Profile's Source Folder.
2. Slice spritesheets in Unity if needed.
3. Ensure name-based Recipe entries can resolve uniquely, or use direct Sprite references.
4. Review the actual visual relationships.
5. Add or generate the project Recipe.
6. Apply it to the Profile.
7. Check Layout Preview.
8. Build.
9. Validate.

For coding-agent workflows, the agent should inspect the actual Sprite art first and encode the observed structure into the existing Recipe instead of mechanically sorting filenames.

## Public Package vs Project-Specific Data

The GitHub package contains the **generic Editor framework** only.

It does not ship:

- project art;
- project-specific Recipe files;
- project Profiles;
- generated Tile assets;
- Palette prefabs;
- API keys;
- hard-coded project paths.

A consuming project owns its own Recipe and content.

Typical project-side setup:

```text
Assets/Editor/MyGameTilePaletteRecipe.cs
Assets/.../TilePaletteProfile.asset
Assets/.../GeneratedTiles/
Assets/.../MyPalette.prefab
```

The public package exposes the reusable APIs and editor workflow; the project supplies the visual knowledge.

## Scope & Limitations

Tile Palette Layout Studio deliberately separates **visual understanding** from **Palette execution**.

It does not use visual AI, network APIs, proxies, external databases, or runtime components. It does not attempt to infer arbitrary original layouts automatically.

That is intentional: loose Sprite assets often do not contain enough information to recover one uniquely correct structure, and filename order is not a reliable substitute for visual relationships.

Instead:

```text
Developer / Codex understands the art
                ↓
Recipe records the reviewed structure
                ↓
Unity Editor executes it safely and repeatably
```

The tool is editor-only and does not enter player builds.

## Requirements

- Unity **2022.3** or newer
- Unity **2D Tilemap** support
- imported Sprite assets
- a folder for generated ordinary Tile assets
- a Palette prefab containing a `Tilemap`

## Architecture

| Component | Responsibility |
| --- | --- |
| `TilePaletteLayoutRecipe` | Generic Recipe API, validation, and Recipe-to-Profile conversion. |
| `TilePaletteProfile` | Serialized Unity references, groups, origins, local cells, inclusion state, and layout hash. |
| `TilePaletteProfileUtility` | Shared Profile path, identity, coordinate, bounds, and ownership helpers. |
| `TilePalettePreviewUtility` | Real Sprite UV extraction and compact preview group data. |
| `TilePaletteLayoutStudioWindow` | Profile selection, paths, Build, Validate, and final Layout Preview. |
| `TilePaletteBuilder` | Planning, Tile reuse/creation, movement, conflicts, transactional Build, validation, rollback, and Profile sync. |
| `TilePaletteAutoSync` | Detects Palette saves, suppresses Build-owned saves, and synchronizes accepted manual edits. |

Project-specific Recipe files intentionally live outside the package.

## Contributing

Issues and pull requests are welcome.

For layout or build issues, include:

- Unity version;
- expected object structure;
- current Layout Preview;
- relevant Sprite set;
- the exact validation/build error when available.

## License

Tile Palette Layout Studio is released under the [MIT License](LICENSE.md).
