# Tile Palette Layout Studio

Tile Palette Layout Studio is a focused Unity Editor tool for turning an explicitly reviewed layout Recipe into a safe, maintainable Tile Palette.

It does **not** guess how arbitrary art should connect. A developer or Codex decides the project-specific arrangement once; the package then stores it in a Profile, shows the final two-dimensional preview, builds the Palette transactionally, validates the result, and keeps accepted manual edits after `Ctrl+S`.

## Final workflow

```text
Imported Sprites
      ↓
Developer/Codex reviews the art
      ↓
Project Recipe: Sprite → group + local cell
      ↓
Profile: resolved Sprite/Tile references + final positions
      ↓
Layout Preview
      ↓
Build Palette: preflight → backup → write → validate → rollback on failure
      ↓
Independent Validate
      ↓
Optional manual move/delete in the Palette → Ctrl+S → Profile sync
```

## What the package does

- Resolves Recipe entries from direct `Sprite` references or unique Sprite/file names.
- Rejects missing Sprites, duplicate groups, duplicate Sprite assignments, and overlapping target cells.
- Reuses existing ordinary `Tile` assets and creates only missing Tiles.
- Shows one compact preview card per Recipe group/subgroup using the real Sprite texture region.
- Builds a Palette only after a read-only preflight succeeds.
- Validates every Profile Tile after writing.
- Restores the previous Profile and Palette files if Build fails.
- Keeps unrelated Tiles outside Recipe target cells untouched and silent.
- Blocks Build when an unrelated Tile occupies a Recipe target cell.
- Detects manual group moves, individual Tile moves, deletions, and restorations when the Palette is saved.
- Rolls the Profile back if `Ctrl+S` synchronization finds a duplicate known Tile or another invalid result.
- Supports multiple Profiles and Palette prefabs.

## What the package does not do

- No visual AI, API key, endpoint, proxy, HTTP request, or network dependency.
- No automatic Row/Column/Grid candidate selection.
- No project asset names, fixed Sprite counts, fixed paths, or RPG-specific layout rules.
- No automatic deletion of unknown or unrelated Palette Tiles.
- No promise that file numbering describes visual structure; the Recipe must express the reviewed structure.

## Requirements

- Unity `2022.3` or newer.
- Unity 2D Tilemap support.
- Imported Sprite assets.
- A folder for generated ordinary `Tile` assets.
- A Palette prefab containing a `Tilemap`.

## Installation

In Unity Package Manager:

1. Click **Add package from git URL**.
2. Enter:

```text
https://github.com/uushu/tile-palette-layout-studio.git
```

For package development, use **Add package from disk** and select this repository's `package.json`.

Do not also copy the package source into `Assets/Editor`; one Unity project should compile only one active copy.

## Project setup

1. Open **Tools > Tile Palette > Layout Studio**.
2. Create or select a `TilePaletteProfile`.
3. Assign:
   - **Source Folder**: where name-based Recipe entries are resolved.
   - **Tile Output Folder**: where missing ordinary Tile assets are created.
   - **Palette Prefab**: the Tile Palette to build and validate.
4. Add a project-specific Editor Recipe outside the package.
5. Apply the Recipe to the Profile.
6. Inspect **Layout Preview**.
7. Click **Build Palette**.
8. Click **Validate** independently.

## Minimal Recipe example

Place project Recipes in your own Editor folder, not inside this package:

```csharp
using TilePaletteLayoutStudio;
using UnityEditor;
using UnityEngine;

internal static class ProjectTilePaletteRecipe
{
    private const string ProfilePath = "Assets/TilePalette/TilePaletteProfile.asset";

    [MenuItem("Tools/Tile Palette/Apply Project Recipe")]
    public static void Apply()
    {
        TilePaletteProfile profile =
            AssetDatabase.LoadAssetAtPath<TilePaletteProfile>(ProfilePath);

        TilePaletteLayoutRecipe.Apply(
            profile,
            TilePaletteLayoutRecipe.Group(
                "arch",
                "main",
                new Vector3Int(0, 0, 0),
                TilePaletteLayoutRecipe.Tile("arch_left", 0, 0),
                TilePaletteLayoutRecipe.Tile("arch_top", 1, 0),
                TilePaletteLayoutRecipe.Tile("arch_right", 2, 0),
                TilePaletteLayoutRecipe.Tile("arch_base_left", 0, -1),
                TilePaletteLayoutRecipe.Tile("arch_base_right", 2, -1)));
    }
}
```

A Recipe entry records:

- the Sprite identity,
- the object group and optional subgroup,
- the local cell within that object,
- the group's Palette origin.

The Profile resolves and stores the actual `Sprite` and `TileBase` references. This lets asset renames remain stable after the Recipe has been applied, while reapplying a name-based Recipe still requires the names to resolve uniquely.

## Preview

`Layout Preview` is the final Profile layout, not an AI candidate list. It:

- draws actual Sprite texture regions instead of generic asset icons,
- preserves empty cells inside shapes such as arches,
- packs groups compactly for review without changing their real Palette origins,
- shows the full group/subgroup name,
- exposes Sprite and target-cell details in tooltips.

Preview is read-only. Only applying a Recipe or accepting a manual Palette save changes the Profile; only **Build Palette** changes the Palette.

## Build safety

Build performs these steps:

1. Validate Profile paths and Recipe data.
2. Index reusable Tile assets by Sprite.
3. Read the current Palette.
4. Create a plan containing `Keep`, `Create`, `Place`, `Move`, or blocking `Conflict` items.
5. Stop before writing if any blocking conflict exists.
6. Back up the Profile and Palette bytes.
7. Create only missing ordinary Tile assets.
8. Move/place known Tiles into Recipe target cells.
9. Save the Palette without triggering manual-save synchronization.
10. Validate every Profile cell.
11. Save the final layout hash.
12. On any exception, restore both files and delete Tiles created by that Build.

A second Build of an already matching Palette is idempotent: it creates and moves nothing.

## Manual edits and Ctrl+S

After a successful Build, you may edit the Palette manually:

- If every Tile in one group moves by the same offset, `Ctrl+S` updates only the group origin.
- If individual known Tiles move, `Ctrl+S` updates their local positions.
- If a known Tile is deleted, `Ctrl+S` marks that Profile entry as excluded.
- If an excluded known Tile is restored, `Ctrl+S` includes it again at its current position.
- Unrelated external Tiles remain outside Profile ownership.
- A duplicate known Tile causes synchronization to fail with a specific error and restores the previous Profile file.

Successful synchronization logs only:

```text
[TilePalette] 自动同步完成
```

Normal external Tiles do not create warning spam. Errors identify the Tile or invalid Profile condition that blocked the operation.

## Validation

The package Editor tests cover:

- Recipe argument validation,
- explicit local-coordinate preservation,
- Profile bounds and managed-cell holes,
- real Sprite UV preview data,
- compact preview group creation,
- Build-save synchronization suppression.

A consuming project should additionally validate its own Recipe coverage and exemplar structures because those rules are project-specific.

## Architecture

- `TilePaletteLayoutRecipe`: explicit Recipe API and Recipe-to-Profile conversion.
- `TilePaletteProfile`: serialized references, groups, local coordinates, origins, inclusion state, and build hash.
- `TilePalettePreviewUtility`: real Sprite UVs and compact group preview data.
- `TilePaletteLayoutStudioWindow`: Profile paths, Build, Validate, and final Preview.
- `TilePaletteBuilder`: planning, Tile reuse/creation, transactional Build, validation, synchronization, and rollback.
- `TilePaletteAutoSync`: distinguishes user Palette saves from Build-owned saves and triggers Profile synchronization.
- `TilePaletteProfileUtility`: target positions, layout bounds, and managed-cell checks.

## Local project versus GitHub package

A project can temporarily keep the generic source in `Assets/Editor` while developing the tool. GitHub users should install the UPM package. Do not activate both forms in the same Unity project.

Project-specific Recipes, Profiles, source art, generated Tiles, and Palette prefabs remain in the consuming project and are never published with the generic package.

## License

Tile Palette Layout Studio is released under the MIT License. See `LICENSE.md`.
