using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TilePaletteLayoutStudio
{
    public sealed class TilePaletteRecipeEntry
    {
        internal string SpriteName;
        internal Sprite Sprite;
        internal TileBase Tile;
        internal Vector3Int LocalPosition;
    }

    public sealed class TilePaletteRecipeGroup
    {
        internal string GroupName;
        internal string SubgroupName;
        internal Vector3Int Origin;
        internal IReadOnlyList<TilePaletteRecipeEntry> Entries;
    }

    public static class TilePaletteLayoutRecipe
    {
        public static TilePaletteRecipeEntry Tile(string spriteName, int x, int y)
        {
            if (string.IsNullOrWhiteSpace(spriteName))
                throw new ArgumentException("Sprite name is required.", nameof(spriteName));

            return new TilePaletteRecipeEntry
            {
                SpriteName = spriteName,
                LocalPosition = new Vector3Int(x, y, 0)
            };
        }

        public static TilePaletteRecipeEntry Tile(Sprite sprite, int x, int y, TileBase tile = null)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            return new TilePaletteRecipeEntry
            {
                Sprite = sprite,
                SpriteName = sprite.name,
                Tile = tile,
                LocalPosition = new Vector3Int(x, y, 0)
            };
        }

        public static TilePaletteRecipeGroup Group(
            string groupName,
            Vector3Int origin,
            params TilePaletteRecipeEntry[] entries)
        {
            return Group(groupName, "main", origin, entries);
        }

        public static TilePaletteRecipeGroup Group(
            string groupName,
            string subgroupName,
            Vector3Int origin,
            params TilePaletteRecipeEntry[] entries)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentException("Group name is required.", nameof(groupName));

            return new TilePaletteRecipeGroup
            {
                GroupName = groupName,
                SubgroupName = string.IsNullOrWhiteSpace(subgroupName) ? "main" : subgroupName,
                Origin = origin,
                Entries = entries ?? Array.Empty<TilePaletteRecipeEntry>()
            };
        }

        public static void Apply(TilePaletteProfile profile, params TilePaletteRecipeGroup[] recipeGroups)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (recipeGroups == null || recipeGroups.Length == 0)
                throw new InvalidOperationException("Recipe contains no groups.");

            Dictionary<string, Sprite> spritesByName = LoadSpritesByName(profile, recipeGroups);
            Dictionary<Sprite, TileBase> previousTiles = profile.Groups
                .SelectMany(group => group.entries)
                .Where(entry => entry.sprite != null && entry.tile != null)
                .GroupBy(entry => entry.sprite)
                .ToDictionary(group => group.Key, group => group.First().tile);
            HashSet<Sprite> usedSprites = new HashSet<Sprite>();
            HashSet<Vector3Int> usedCells = new HashSet<Vector3Int>();
            HashSet<string> usedGroups = new HashSet<string>(StringComparer.Ordinal);
            List<TilePaletteProfileGroup> groups = new List<TilePaletteProfileGroup>();

            foreach (TilePaletteRecipeGroup recipeGroup in recipeGroups)
            {
                if (recipeGroup == null) throw new InvalidOperationException("Recipe contains a null group.");
                string groupId = recipeGroup.GroupName + "/" + recipeGroup.SubgroupName;
                if (!usedGroups.Add(groupId))
                    throw new InvalidOperationException("Recipe contains duplicate group: " + groupId);
                if (recipeGroup.Entries == null || recipeGroup.Entries.Count == 0)
                    throw new InvalidOperationException("Recipe group contains no tiles: " + groupId);

                TilePaletteProfileGroup profileGroup = new TilePaletteProfileGroup
                {
                    groupName = recipeGroup.GroupName,
                    subgroupName = recipeGroup.SubgroupName,
                    origin = recipeGroup.Origin
                };
                HashSet<Vector3Int> localCells = new HashSet<Vector3Int>();

                foreach (TilePaletteRecipeEntry recipeEntry in recipeGroup.Entries)
                {
                    if (recipeEntry == null)
                        throw new InvalidOperationException("Recipe group contains a null tile: " + groupId);

                    Sprite sprite = recipeEntry.Sprite;
                    if (sprite == null && !spritesByName.TryGetValue(recipeEntry.SpriteName, out sprite))
                        throw new InvalidOperationException("Sprite was not found: " + recipeEntry.SpriteName);
                    if (!usedSprites.Add(sprite))
                        throw new InvalidOperationException("Recipe assigns the same Sprite more than once: " + sprite.name);
                    if (!localCells.Add(recipeEntry.LocalPosition))
                        throw new InvalidOperationException("Recipe group uses the same local cell twice: " + groupId);

                    Vector3Int target = recipeGroup.Origin + recipeEntry.LocalPosition;
                    if (!usedCells.Add(target))
                        throw new InvalidOperationException("Recipe uses the same target cell twice: " + target);

                    TileBase tile = recipeEntry.Tile;
                    if (tile == null) previousTiles.TryGetValue(sprite, out tile);
                    profileGroup.entries.Add(new TilePaletteProfileEntry
                    {
                        sourceId = sprite.name,
                        sprite = sprite,
                        tile = tile,
                        localPosition = recipeEntry.LocalPosition,
                        included = true
                    });
                }

                groups.Add(profileGroup);
            }

            Undo.RecordObject(profile, "Apply Tile Palette Recipe");
            profile.ReplaceGroups(groups);
            EditorUtility.SetDirty(profile);
            TilePaletteAutoSyncGuard.SaveAssetsWithoutSync();
            Debug.Log("[TilePalette] Recipe 应用完成");
        }

        private static Dictionary<string, Sprite> LoadSpritesByName(
            TilePaletteProfile profile,
            IEnumerable<TilePaletteRecipeGroup> groups)
        {
            bool needsLookup = groups.SelectMany(group => group.Entries)
                .Any(entry => entry != null && entry.Sprite == null);
            if (!needsLookup) return new Dictionary<string, Sprite>(StringComparer.Ordinal);
            if (!AssetDatabase.IsValidFolder(profile.SourceFolderPath))
                throw new InvalidOperationException("Profile Source Folder is required for name-based Recipe entries.");

            Dictionary<string, List<Sprite>> candidates = new Dictionary<string, List<Sprite>>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { profile.SourceFolderPath }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Sprite[] pathSprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
                foreach (Sprite sprite in pathSprites)
                {
                    AddCandidate(candidates, sprite.name, sprite);
                }

                if (pathSprites.Length == 1)
                    AddCandidate(candidates, Path.GetFileNameWithoutExtension(path), pathSprites[0]);
            }

            string[] duplicateNames = candidates.Where(pair => pair.Value.Count > 1)
                .Select(pair => pair.Key)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (duplicateNames.Length > 0)
                throw new InvalidOperationException(
                    "Source Folder contains duplicate Sprite names. Use direct Sprite references for: " +
                    string.Join(", ", duplicateNames));

            return candidates.ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.Ordinal);
        }

        private static void AddCandidate(
            IDictionary<string, List<Sprite>> candidates,
            string name,
            Sprite sprite)
        {
            if (string.IsNullOrWhiteSpace(name) || sprite == null) return;
            if (!candidates.TryGetValue(name, out List<Sprite> sprites))
            {
                sprites = new List<Sprite>();
                candidates.Add(name, sprites);
            }
            if (!sprites.Contains(sprite)) sprites.Add(sprite);
        }
    }
}
