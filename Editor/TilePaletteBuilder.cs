using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TilePaletteLayoutStudio
{
    internal enum LayoutPlanAction
    {
        Keep,
        Create,
        Place,
        Move,
        Conflict
    }

    internal sealed class LayoutPlanItem
    {
        public LayoutPlanAction action;
        public TilePaletteProfileGroup group;
        public TilePaletteProfileEntry entry;
        public TileBase tile;
        public Vector3Int? currentPosition;
        public Vector3Int targetPosition;
        public string diagnostic;
        public bool requiresConfirmation;
    }

    internal sealed class LayoutPlan
    {
        public readonly List<LayoutPlanItem> items = new List<LayoutPlanItem>();
        public int Count(LayoutPlanAction action) => items.Count(item => item.action == action);
        public bool HasBlockingConflicts => items.Any(item => item.action == LayoutPlanAction.Conflict);
        public bool HasConfirmedMoves => items.Any(item => item.requiresConfirmation);
        public IEnumerable<string> Errors => items
            .Where(item => item.action == LayoutPlanAction.Conflict)
            .Select(item => item.diagnostic)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal);
    }

    internal sealed class TilePaletteBuildResult
    {
        public int created;
        public int placed;
        public int moved;
        public int kept;
    }

    internal sealed class TilePaletteSyncResult
    {
        public int moved;
        public int deleted;
        public int restored;
        public readonly List<string> warnings = new List<string>();
        public bool HasChanges => moved > 0 || deleted > 0 || restored > 0;
    }

    internal static class TilePaletteProfileStore
    {
        private const string PreferredProfileKey = "TilePaletteLayoutStudio.PreferredProfileGuid";

        public static IReadOnlyList<TilePaletteProfile> FindAll()
        {
            return AssetDatabase.FindAssets("t:TilePaletteProfile")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<TilePaletteProfile>)
                .Where(profile => profile != null)
                .OrderBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal)
                .ToArray();
        }

        public static TilePaletteProfile LoadPreferred()
        {
            string guid = EditorPrefs.GetString(PreferredProfileKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(guid))
            {
                TilePaletteProfile preferred = AssetDatabase.LoadAssetAtPath<TilePaletteProfile>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (preferred != null) return preferred;
            }
            return FindAll().FirstOrDefault();
        }

        public static void SetPreferred(TilePaletteProfile profile)
        {
            if (profile == null)
            {
                EditorPrefs.DeleteKey(PreferredProfileKey);
                return;
            }
            EditorPrefs.SetString(
                PreferredProfileKey,
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile)));
        }

        public static TilePaletteProfile CreateProfile()
        {
            TilePaletteProfile profile = ScriptableObject.CreateInstance<TilePaletteProfile>();
            profile.name = "TilePaletteProfile";
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/TilePaletteProfile.asset");
            AssetDatabase.CreateAsset(profile, path);
            TilePaletteAutoSyncGuard.SaveAssetsWithoutSync();
            SetPreferred(profile);
            return profile;
        }
    }

    internal static class TilePaletteBuilder
    {
        internal static Action AfterPrefabSavedForValidation;

        internal static string FormatUnknownTileWarning(int count, IEnumerable<string> examples)
        {
            return "Palette contains " + count +
                   " Profile-external Tiles; preserved. Examples: " +
                   string.Join(", ", examples.Take(3));
        }

        public static LayoutPlan CreatePlan(TilePaletteProfile profile)
        {
            ValidateProfile(profile);
            TileAssetIndex assets = TileAssetIndex.Load(profile);
            PaletteSnapshot palette = PaletteSnapshot.Load(profile);
            LayoutPlan plan = new LayoutPlan();
            Dictionary<TilePaletteProfileEntry, TileBase> resolved = new Dictionary<TilePaletteProfileEntry, TileBase>();
            HashSet<TileBase> desiredTiles = new HashSet<TileBase>();

            foreach (string conflict in assets.Conflicts)
                plan.items.Add(Conflict(conflict));

            foreach (TilePaletteProfileEntry entry in profile.IncludedEntries)
            {
                TileBase tile = assets.Resolve(entry);
                resolved[entry] = tile;
                if (tile != null && !desiredTiles.Add(tile))
                    plan.items.Add(Conflict("The same Tile is assigned more than once: " + tile.name));
            }

            foreach (TilePaletteProfileGroup group in profile.Groups)
            {
                foreach (TilePaletteProfileEntry entry in group.entries.Where(value => value.included))
                {
                    Vector3Int target = TilePaletteProfileUtility.GetTargetPosition(group, entry);
                    TileBase tile = resolved[entry];
                    if (tile == null)
                    {
                        plan.items.Add(Item(LayoutPlanAction.Create, group, entry, null, null, target));
                        continue;
                    }

                    IReadOnlyList<Vector3Int> currentPositions = palette.Positions(tile);
                    if (currentPositions.Count > 1)
                    {
                        plan.items.Add(Conflict("Tile appears in multiple Palette cells: " + tile.name));
                        continue;
                    }

                    TileBase targetTile = palette.TileAt(target);
                    if (targetTile == tile && currentPositions.Count == 1)
                    {
                        plan.items.Add(Item(LayoutPlanAction.Keep, group, entry, tile, target, target));
                        continue;
                    }

                    if (targetTile != null && !desiredTiles.Contains(targetTile))
                    {
                        plan.items.Add(Conflict(
                            "Target cell " + target + " is occupied by a Tile outside the Profile: " + targetTile.name));
                        continue;
                    }

                    LayoutPlanItem item = Item(
                        currentPositions.Count == 1 ? LayoutPlanAction.Move : LayoutPlanAction.Place,
                        group,
                        entry,
                        tile,
                        currentPositions.Count == 1 ? currentPositions[0] : (Vector3Int?)null,
                        target);
                    item.requiresConfirmation = targetTile != null && targetTile != tile;
                    plan.items.Add(item);
                }
            }

            return plan;
        }

        public static TilePaletteBuildResult Apply(TilePaletteProfile profile)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && string.Equals(stage.assetPath, profile.PalettePrefabPath, StringComparison.Ordinal))
                throw new InvalidOperationException("Close the Palette Prefab editing stage before Build.");

            LayoutPlan preflight = CreatePlan(profile);
            ThrowIfConflicts(preflight);
            string profileBackup = EditorJsonUtility.ToJson(profile);
            string prefabPath = profile.PalettePrefabPath;
            string absolutePrefabPath = AbsolutePath(prefabPath);
            byte[] prefabBackup = File.ReadAllBytes(absolutePrefabPath);
            List<string> createdAssets = new List<string>();

            try
            {
                foreach (LayoutPlanItem item in preflight.items.Where(value => value.action == LayoutPlanAction.Create))
                {
                    string tilePath = UniqueTilePath(profile.TileOutputFolderPath, item.entry.sourceId);
                    Tile tile = ScriptableObject.CreateInstance<Tile>();
                    tile.name = item.entry.sprite.name;
                    tile.sprite = item.entry.sprite;
                    tile.color = Color.white;
                    tile.transform = Matrix4x4.identity;
                    tile.flags = TileFlags.LockColor;
                    tile.colliderType = Tile.ColliderType.Sprite;
                    AssetDatabase.CreateAsset(tile, tilePath);
                    createdAssets.Add(tilePath);
                    item.entry.tile = tile;
                    EditorUtility.SetDirty(profile);
                }
                TilePaletteAutoSyncGuard.SaveAssetsWithoutSync();

                LayoutPlan plan = CreatePlan(profile);
                ThrowIfConflicts(plan);
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    Tilemap tilemap = RequireTilemap(root, prefabPath);
                    HashSet<TileBase> movingTiles = plan.items
                        .Where(item => item.action == LayoutPlanAction.Move)
                        .Select(item => item.tile)
                        .ToHashSet();

                    foreach (Vector3Int position in tilemap.cellBounds.allPositionsWithin)
                    {
                        TileBase current = tilemap.GetTile(position);
                        if (current != null && movingTiles.Contains(current))
                            tilemap.SetTile(position, null);
                    }

                    foreach (LayoutPlanItem item in plan.items.Where(value =>
                                 value.action == LayoutPlanAction.Place || value.action == LayoutPlanAction.Move))
                        tilemap.SetTile(item.targetPosition, item.tile);

                    tilemap.CompressBounds();
                    EditorUtility.SetDirty(tilemap);
                    if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                        throw new InvalidOperationException("Unity failed to save the Palette Prefab.");
                    AfterPrefabSavedForValidation?.Invoke();
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
                ValidateComplete(profile);
                profile.MarkBuilt(CalculateLayoutHash(profile));
                EditorUtility.SetDirty(profile);
                TilePaletteAutoSyncGuard.SaveAssetsWithoutSync();
                LayoutPlan complete = CreatePlan(profile);
                return new TilePaletteBuildResult
                {
                    created = createdAssets.Count,
                    placed = complete.Count(LayoutPlanAction.Place),
                    moved = plan.Count(LayoutPlanAction.Move),
                    kept = complete.Count(LayoutPlanAction.Keep)
                };
            }
            catch
            {
                File.WriteAllBytes(absolutePrefabPath, prefabBackup);
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
                EditorJsonUtility.FromJsonOverwrite(profileBackup, profile);
                EditorUtility.SetDirty(profile);
                foreach (string createdAsset in createdAssets)
                    AssetDatabase.DeleteAsset(createdAsset);
                TilePaletteAutoSyncGuard.SaveAssetsWithoutSync();
                throw;
            }
        }

        public static string ValidateComplete(TilePaletteProfile profile)
        {
            LayoutPlan plan = CreatePlan(profile);
            ThrowIfConflicts(plan);
            int incomplete = plan.Count(LayoutPlanAction.Create) +
                             plan.Count(LayoutPlanAction.Place) +
                             plan.Count(LayoutPlanAction.Move);
            if (incomplete > 0)
                throw new InvalidOperationException("Palette has " + incomplete + " unapplied Profile cells.");
            return CalculateLayoutHash(profile);
        }

        public static TilePaletteSyncResult SyncPaletteToProfile(TilePaletteProfile profile)
        {
            ValidateProfile(profile);
            TileAssetIndex assets = TileAssetIndex.Load(profile);
            if (assets.Conflicts.Count > 0)
                throw new InvalidOperationException(string.Join("\n", assets.Conflicts));

            PaletteSnapshot palette = PaletteSnapshot.Load(profile);
            string profileBackup = EditorJsonUtility.ToJson(profile);
            string profilePath = AssetDatabase.GetAssetPath(profile);
            string absoluteProfilePath = AbsolutePath(profilePath);
            byte[] profileFileBackup = File.ReadAllBytes(absoluteProfilePath);
            TilePaletteSyncResult result = new TilePaletteSyncResult();
            HashSet<TileBase> knownTiles = new HashSet<TileBase>();
            Dictionary<TilePaletteProfileEntry, TileBase> resolved = profile.Groups
                .SelectMany(group => group.entries)
                .ToDictionary(entry => entry, entry => assets.Resolve(entry));

            foreach (TileBase tile in resolved.Values.Where(tile => tile != null))
                knownTiles.Add(tile);
            List<string> unknownTileExamples = new List<string>();
            foreach (KeyValuePair<Vector3Int, TileBase> cell in palette.Cells)
            {
                if (!knownTiles.Contains(cell.Value))
                    unknownTileExamples.Add(cell.Value.name + " at " + cell.Key);
            }
            if (unknownTileExamples.Count > 0)
                result.warnings.Add(FormatUnknownTileWarning(
                    unknownTileExamples.Count,
                    unknownTileExamples));

            try
            {
                Undo.RecordObject(profile, "Sync Tile Palette Profile");
                foreach (TilePaletteProfileGroup group in profile.Groups)
                {
                    List<TilePaletteProfileEntry> included = group.entries.Where(entry => entry.included).ToList();
                    Dictionary<TilePaletteProfileEntry, Vector3Int> found = new Dictionary<TilePaletteProfileEntry, Vector3Int>();
                    foreach (TilePaletteProfileEntry entry in included)
                    {
                        TileBase tile = resolved[entry];
                        if (tile == null) continue;
                        IReadOnlyList<Vector3Int> positions = palette.Positions(tile);
                        if (positions.Count > 1)
                            throw new InvalidOperationException("Tile appears in multiple Palette cells: " + tile.name);
                        if (positions.Count == 1) found[entry] = positions[0];
                    }

                    Vector3Int? sharedDelta = null;
                    bool wholeGroupMoved = included.Count > 0 && found.Count == included.Count;
                    foreach (KeyValuePair<TilePaletteProfileEntry, Vector3Int> pair in found)
                    {
                        Vector3Int expected = TilePaletteProfileUtility.GetTargetPosition(group, pair.Key);
                        Vector3Int delta = pair.Value - expected;
                        if (!sharedDelta.HasValue) sharedDelta = delta;
                        else if (sharedDelta.Value != delta) wholeGroupMoved = false;
                    }

                    if (wholeGroupMoved && sharedDelta.HasValue && sharedDelta.Value != Vector3Int.zero)
                    {
                        group.origin += sharedDelta.Value;
                        result.moved += included.Count;
                    }
                    else
                    {
                        foreach (TilePaletteProfileEntry entry in included)
                        {
                            if (!found.TryGetValue(entry, out Vector3Int current))
                            {
                                entry.included = false;
                                result.deleted++;
                                continue;
                            }
                            Vector3Int local = current - group.origin;
                            if (entry.localPosition == local) continue;
                            entry.localPosition = local;
                            result.moved++;
                        }
                    }

                    foreach (TilePaletteProfileEntry entry in group.entries.Where(entry => !entry.included))
                    {
                        TileBase tile = resolved[entry];
                        if (tile == null) continue;
                        IReadOnlyList<Vector3Int> positions = palette.Positions(tile);
                        if (positions.Count > 1)
                            throw new InvalidOperationException("Tile appears in multiple Palette cells: " + tile.name);
                        if (positions.Count != 1) continue;
                        entry.included = true;
                        entry.localPosition = positions[0] - group.origin;
                        result.restored++;
                    }
                }

                ValidateProfile(profile);
                if (result.HasChanges)
                {
                    EditorUtility.SetDirty(profile);
                    TilePaletteAutoSyncGuard.SaveAssetsWithoutSync();
                }
                return result;
            }
            catch
            {
                EditorJsonUtility.FromJsonOverwrite(profileBackup, profile);
                EditorUtility.SetDirty(profile);
                File.WriteAllBytes(absoluteProfilePath, profileFileBackup);
                AssetDatabase.ImportAsset(profilePath, ImportAssetOptions.ForceUpdate);
                throw;
            }
        }

        public static string CalculateLayoutHash(TilePaletteProfile profile)
        {
            string value = string.Join("|", profile.Groups
                .OrderBy(group => group.Id, StringComparer.Ordinal)
                .SelectMany(group => group.entries
                    .Where(entry => entry.included)
                    .OrderBy(entry => entry.sourceId, StringComparer.Ordinal)
                    .Select(entry => TilePaletteProfileUtility.GetResourceId(entry) + "@" +
                                     TilePaletteProfileUtility.GetTargetPosition(group, entry))));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant().Substring(0, 24);
            }
        }

        private static void ValidateProfile(TilePaletteProfile profile)
        {
            if (profile == null) throw new InvalidOperationException("Select a TilePaletteProfile.");
            if (!AssetDatabase.IsValidFolder(profile.TileOutputFolderPath))
                throw new InvalidOperationException("Profile Tile Output Folder is invalid.");
            if (profile.PalettePrefab == null || string.IsNullOrWhiteSpace(profile.PalettePrefabPath))
                throw new InvalidOperationException("Profile Palette Prefab is invalid.");
            if (profile.Groups.Count == 0)
                throw new InvalidOperationException("Profile contains no layout. Apply a Recipe first.");

            string[] duplicateGroups = profile.Groups.GroupBy(group => group.Id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicateGroups.Length > 0)
                throw new InvalidOperationException("Profile contains duplicate groups: " + string.Join(", ", duplicateGroups));
            if (profile.IncludedEntries.Any(entry => entry.sprite == null))
                throw new InvalidOperationException("Profile contains a missing Sprite reference.");

            string[] duplicateSprites = profile.IncludedEntries
                .GroupBy(TilePaletteProfileUtility.GetResourceId, StringComparer.Ordinal)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
                .Select(group => group.First().sourceId)
                .ToArray();
            if (duplicateSprites.Length > 0)
                throw new InvalidOperationException("Profile assigns the same Sprite more than once: " + string.Join(", ", duplicateSprites));

            Vector3Int[] duplicateCells = profile.Groups
                .SelectMany(group => group.entries.Where(entry => entry.included)
                    .Select(entry => TilePaletteProfileUtility.GetTargetPosition(group, entry)))
                .GroupBy(position => position)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicateCells.Length > 0)
                throw new InvalidOperationException("Profile contains duplicate target cells: " + string.Join(", ", duplicateCells));
        }

        private static void ThrowIfConflicts(LayoutPlan plan)
        {
            if (plan.HasBlockingConflicts)
                throw new InvalidOperationException(string.Join("\n", plan.Errors));
        }

        private static LayoutPlanItem Item(
            LayoutPlanAction action,
            TilePaletteProfileGroup group,
            TilePaletteProfileEntry entry,
            TileBase tile,
            Vector3Int? current,
            Vector3Int target)
        {
            return new LayoutPlanItem
            {
                action = action,
                group = group,
                entry = entry,
                tile = tile,
                currentPosition = current,
                targetPosition = target
            };
        }

        private static LayoutPlanItem Conflict(string message)
        {
            return new LayoutPlanItem
            {
                action = LayoutPlanAction.Conflict,
                diagnostic = message
            };
        }

        private static Tilemap RequireTilemap(GameObject root, string path)
        {
            Tilemap tilemap = root.GetComponentInChildren<Tilemap>(true);
            if (tilemap == null) throw new InvalidOperationException("Palette Prefab contains no Tilemap: " + path);
            return tilemap;
        }

        private static string UniqueTilePath(string folder, string sourceId)
        {
            string safeName = string.Concat((string.IsNullOrWhiteSpace(sourceId) ? "Tile" : sourceId)
                .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            return AssetDatabase.GenerateUniqueAssetPath(folder.TrimEnd('/') + "/" + safeName + ".asset");
        }

        private static string AbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private sealed class TileAssetIndex
        {
            private readonly Dictionary<Sprite, TileBase> bySprite = new Dictionary<Sprite, TileBase>();
            public readonly List<string> Conflicts = new List<string>();

            public static TileAssetIndex Load(TilePaletteProfile profile)
            {
                TileAssetIndex index = new TileAssetIndex();
                foreach (string guid in AssetDatabase.FindAssets("t:TileBase", new[] { profile.TileOutputFolderPath }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    TileBase tile = AssetDatabase.LoadAssetAtPath<TileBase>(path);
                    if (!(tile is Tile plainTile) || plainTile.sprite == null) continue;
                    if (index.bySprite.TryGetValue(plainTile.sprite, out TileBase existing) && existing != tile)
                        index.Conflicts.Add("Multiple Tile assets use Sprite " + plainTile.sprite.name + ".");
                    else
                        index.bySprite[plainTile.sprite] = tile;
                }

                foreach (TilePaletteProfileEntry entry in profile.Groups.SelectMany(group => group.entries))
                {
                    if (entry.tile == null) continue;
                    if (!(entry.tile is Tile plainTile) || plainTile.sprite != entry.sprite)
                        index.Conflicts.Add("Tile does not match Sprite: " + entry.sourceId);
                    else
                        index.bySprite[entry.sprite] = entry.tile;
                }
                return index;
            }

            public TileBase Resolve(TilePaletteProfileEntry entry)
            {
                if (entry.tile != null) return entry.tile;
                return entry.sprite != null && bySprite.TryGetValue(entry.sprite, out TileBase tile) ? tile : null;
            }
        }

        private sealed class PaletteSnapshot
        {
            private readonly Dictionary<Vector3Int, TileBase> byPosition = new Dictionary<Vector3Int, TileBase>();
            private readonly Dictionary<TileBase, List<Vector3Int>> positions = new Dictionary<TileBase, List<Vector3Int>>();
            public IEnumerable<KeyValuePair<Vector3Int, TileBase>> Cells => byPosition;

            public static PaletteSnapshot Load(TilePaletteProfile profile)
            {
                PaletteSnapshot snapshot = new PaletteSnapshot();
                GameObject root = PrefabUtility.LoadPrefabContents(profile.PalettePrefabPath);
                try
                {
                    Tilemap tilemap = RequireTilemap(root, profile.PalettePrefabPath);
                    foreach (Vector3Int position in tilemap.cellBounds.allPositionsWithin)
                    {
                        TileBase tile = tilemap.GetTile(position);
                        if (tile == null) continue;
                        snapshot.byPosition[position] = tile;
                        if (!snapshot.positions.TryGetValue(tile, out List<Vector3Int> cells))
                        {
                            cells = new List<Vector3Int>();
                            snapshot.positions[tile] = cells;
                        }
                        cells.Add(position);
                    }
                    return snapshot;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            public TileBase TileAt(Vector3Int position)
            {
                return byPosition.TryGetValue(position, out TileBase tile) ? tile : null;
            }

            public IReadOnlyList<Vector3Int> Positions(TileBase tile)
            {
                return tile != null && positions.TryGetValue(tile, out List<Vector3Int> cells)
                    ? cells
                    : Array.Empty<Vector3Int>();
            }
        }
    }
}
