using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TilePaletteLayoutStudio
{
    internal sealed class DefaultTileAssetFactory : ITileAssetFactory
    {
        public bool CanReuse(TileBase tile, Sprite expectedSprite, out string diagnostic)
        {
            if (tile is Tile plainTile && plainTile.sprite == expectedSprite)
            {
                diagnostic = string.Empty;
                return true;
            }
            diagnostic = tile == null ? "Tile 为空。" : $"{tile.name} 是无法安全解析来源的 {tile.GetType().Name}。";
            return false;
        }

        public TileBase Create(Sprite sprite, string assetPath)
        {
            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = sprite.name;
            tile.sprite = sprite;
            tile.color = Color.white;
            tile.transform = Matrix4x4.identity;
            tile.flags = TileFlags.LockColor;
            tile.colliderType = Tile.ColliderType.Sprite;
            AssetDatabase.CreateAsset(tile, assetPath);
            return tile;
        }
    }

    internal sealed class TilePaletteBuildResult
    {
        public int created;
        public int placed;
        public int moved;
        public int kept;
        public int removed;
        public string message;
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
                .OrderBy(profile => AssetDatabase.GetAssetPath(profile), StringComparer.Ordinal)
                .ToArray();
        }

        public static TilePaletteProfile LoadPreferred()
        {
            string guid = EditorPrefs.GetString(PreferredProfileKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(guid))
            {
                TilePaletteProfile preferred = AssetDatabase.LoadAssetAtPath<TilePaletteProfile>(AssetDatabase.GUIDToAssetPath(guid));
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
            EditorPrefs.SetString(PreferredProfileKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile)));
        }

        public static TilePaletteProfile CreateProfile()
        {
            TilePaletteProfile profile = ScriptableObject.CreateInstance<TilePaletteProfile>();
            profile.name = "TilePaletteProfile";
            string discovered = TilePaletteSourceScanner.DiscoverSourceFolder(null);
            if (!string.IsNullOrWhiteSpace(discovered))
                profile.SourceFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(discovered);
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/TilePaletteProfile.asset");
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            SetPreferred(profile);
            return profile;
        }
    }

    internal static class TilePaletteBuilder
    {
        public static LayoutPlan CreatePlan(TilePaletteProfile profile)
        {
            ValidateProfile(profile);
            TileAssetIndex assets = TileAssetIndex.Load(profile);
            PaletteSnapshot palette = PaletteSnapshot.Load(profile, assets);
            LayoutPlan plan = new LayoutPlan();
            Dictionary<Vector3Int, TilePaletteProfileEntry> desiredCells = new Dictionary<Vector3Int, TilePaletteProfileEntry>();
            HashSet<TileBase> desiredTiles = new HashSet<TileBase>();

            foreach (TilePaletteProfileGroup group in profile.Groups)
            {
                foreach (TilePaletteProfileEntry entry in group.entries.Where(value => value.included))
                {
                    Vector3Int target = TilePaletteProfileUtility.GetTargetPosition(group, entry);
                    if (!desiredCells.TryAdd(target, entry))
                    {
                        plan.items.Add(NewItem(LayoutPlanAction.Conflict, group, entry, null, null, target, "Profile 目标坐标重复。"));
                        continue;
                    }

                    TileBase tile = assets.Resolve(entry);
                    if (tile == null)
                    {
                        plan.items.Add(NewItem(LayoutPlanAction.Create, group, entry, null, null, target, "缺少普通 Tile 资源；构建时创建。"));
                        continue;
                    }
                    desiredTiles.Add(tile);
                    List<Vector3Int> currentPositions = palette.Positions(tile);
                    if (currentPositions.Count > 1)
                    {
                        Vector3Int retained = currentPositions.Contains(target)
                            ? target
                            : currentPositions.OrderBy(position => position.x).ThenBy(position => position.y).First();
                        foreach (Vector3Int duplicate in currentPositions.Where(position => position != retained))
                        {
                            plan.items.Add(NewItem(
                                LayoutPlanAction.Orphan,
                                group,
                                entry,
                                tile,
                                duplicate,
                                null,
                                $"{entry.sourceId} 重复；构建时保留 {retained}，删除额外格 {duplicate}。"));
                        }
                        currentPositions = new List<Vector3Int> { retained };
                    }

                    TileBase targetTile = palette.TileAt(target);
                    if (targetTile == tile)
                    {
                        plan.items.Add(NewItem(LayoutPlanAction.Keep, group, entry, tile, target, target, string.Empty));
                    }
                    else if (targetTile != null)
                    {
                        string targetName = targetTile.name;
                        plan.items.Add(NewItem(LayoutPlanAction.Conflict, group, entry, tile, currentPositions.Count == 0 ? (Vector3Int?)null : currentPositions[0], target, $"目标格已被 {targetName} 占用。"));
                    }
                    else if (currentPositions.Count == 1)
                    {
                        plan.items.Add(NewItem(LayoutPlanAction.Move, group, entry, tile, currentPositions[0], target, string.Empty));
                    }
                    else
                    {
                        plan.items.Add(NewItem(LayoutPlanAction.Place, group, entry, tile, null, target, string.Empty));
                    }
                }
            }

            foreach (LayoutPlanItem item in plan.items.Where(value => value.action == LayoutPlanAction.Conflict && value.targetPosition.HasValue && value.tile != null).ToArray())
            {
                TileBase occupyingTile = palette.TileAt(item.targetPosition.Value);
                if (occupyingTile == null) continue;
                item.action = LayoutPlanAction.Move;
                item.requiresConfirmation = desiredTiles.Contains(occupyingTile);
                item.diagnostic = item.requiresConfirmation
                    ? "目标格由另一个已知 Tile 占据；完整构建会按确认后的 Profile 执行交换/循环移动。"
                    : $"目标格中的未知 Tile {occupyingTile.name} 会从 Palette 删除，项目资源文件保留。";
            }

            foreach (KeyValuePair<Vector3Int, TileBase> current in palette.byPosition)
            {
                if (desiredTiles.Contains(current.Value)) continue;
                if (!TilePaletteProfileUtility.IsManagedCell(profile, current.Key)) continue;
                plan.items.Add(NewItem(
                    LayoutPlanAction.Orphan,
                    null,
                    null,
                    current.Value,
                    current.Key,
                    null,
                    $"未知 Tile {current.Value.name} 会从 Palette 删除，项目资源文件保留。"));
            }
            foreach (string conflict in assets.conflicts)
                plan.items.Add(NewItem(LayoutPlanAction.Conflict, null, null, null, null, null, conflict));
            return plan;
        }

        public static TilePaletteBuildResult Apply(TilePaletteProfile profile, bool safeOnly)
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出 Play Mode 并等待编译结束。" );
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && string.Equals(stage.assetPath, profile.PalettePrefabPath, StringComparison.Ordinal))
                throw new InvalidOperationException("请先关闭目标 Palette Prefab 编辑模式。" );

            LayoutPlan initialPlan = CreatePlan(profile);
            if (initialPlan.HasBlockingConflicts)
                throw new InvalidOperationException("存在阻止写入的冲突：\n" + string.Join("\n", initialPlan.BlockingConflicts.Select(item => item.diagnostic).Distinct()));
            if (safeOnly && initialPlan.HasConfirmedMoves)
                throw new InvalidOperationException("计划包含交换或循环移动；请使用 Build or Update 并确认差异。" );

            byte[] prefabBackup = File.ReadAllBytes(AbsolutePath(profile.PalettePrefabPath));
            string profileBackup = EditorJsonUtility.ToJson(profile);
            List<string> createdPaths = new List<string>();
            using (TilePaletteAutoSyncGuard.Suppress())
            try
            {
                foreach (LayoutPlanItem item in initialPlan.items.Where(value =>
                             value.entry != null && value.tile != null && value.entry.tile == null))
                    item.entry.tile = item.tile;

                ITileAssetFactory factory = new DefaultTileAssetFactory();
                foreach (LayoutPlanItem item in initialPlan.items.Where(value => value.action == LayoutPlanAction.Create))
                {
                    string path = UniqueTilePath(profile.TileOutputFolderPath, item.entry.sourceId);
                    item.entry.tile = factory.Create(item.entry.sprite, path);
                    createdPaths.Add(path);
                }
                AssetDatabase.SaveAssets();

                LayoutPlan plan = CreatePlan(profile);
                if (plan.HasBlockingConflicts)
                    throw new InvalidOperationException("创建 Tile 后计划出现冲突。" );
                if (safeOnly && plan.HasConfirmedMoves)
                    throw new InvalidOperationException("创建 Tile 后计划包含需要确认的移动。" );
                GameObject root = PrefabUtility.LoadPrefabContents(profile.PalettePrefabPath);
                try
                {
                    Tilemap tilemap = RequireTilemap(root, profile.PalettePrefabPath);
                    foreach (LayoutPlanItem item in plan.items.Where(value => value.action == LayoutPlanAction.Move))
                    {
                        if (item.currentPosition.HasValue && tilemap.GetTile(item.currentPosition.Value) == item.tile)
                            tilemap.SetTile(item.currentPosition.Value, null);
                    }
                    foreach (LayoutPlanItem item in plan.items.Where(value => value.action == LayoutPlanAction.Orphan))
                    {
                        if (item.currentPosition.HasValue && tilemap.GetTile(item.currentPosition.Value) == item.tile)
                            tilemap.SetTile(item.currentPosition.Value, null);
                    }
                    foreach (LayoutPlanItem item in plan.items.Where(value => value.action == LayoutPlanAction.Place || value.action == LayoutPlanAction.Move))
                    {
                        if (!item.targetPosition.HasValue || tilemap.GetTile(item.targetPosition.Value) != null)
                            throw new InvalidOperationException("应用期间目标格状态已变化：" + item.targetPosition);
                        tilemap.SetTile(item.targetPosition.Value, item.tile);
                    }
                    tilemap.CompressBounds();
                    EditorUtility.SetDirty(tilemap);
                    if (PrefabUtility.SaveAsPrefabAsset(root, profile.PalettePrefabPath) == null)
                        throw new InvalidOperationException("Unity 无法保存 Palette Prefab。" );
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string validation = ValidateComplete(profile);
                profile.MarkBuilt(CalculateLayoutHash(profile));
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                return new TilePaletteBuildResult
                {
                    created = createdPaths.Count,
                    placed = plan.Count(LayoutPlanAction.Place),
                    moved = plan.Count(LayoutPlanAction.Move),
                    kept = plan.Count(LayoutPlanAction.Keep),
                    removed = plan.Count(LayoutPlanAction.Orphan),
                    message = validation
                };
            }
            catch
            {
                File.WriteAllBytes(AbsolutePath(profile.PalettePrefabPath), prefabBackup);
                foreach (string createdPath in createdPaths) AssetDatabase.DeleteAsset(createdPath);
                EditorJsonUtility.FromJsonOverwrite(profileBackup, profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
        }

        public static string ValidateComplete(TilePaletteProfile profile)
        {
            ValidateProfile(profile);
            LayoutPlan plan = CreatePlan(profile);
            int incomplete = plan.Count(LayoutPlanAction.Create) + plan.Count(LayoutPlanAction.Place) + plan.Count(LayoutPlanAction.Move);
            if (plan.HasBlockingConflicts || incomplete > 0)
                throw new InvalidOperationException($"验证失败：{plan.Count(LayoutPlanAction.Conflict)} 个冲突，{incomplete} 个未完成项。" );
            return "验证通过";
        }

        public static ManualSyncProposal AnalyzeManualChanges(TilePaletteProfile profile)
        {
            ValidateProfile(profile);
            TileAssetIndex assets = TileAssetIndex.Load(profile);
            PaletteSnapshot palette = PaletteSnapshot.Load(profile, assets);
            ManualSyncProposal proposal = new ManualSyncProposal();
            foreach (TilePaletteProfileGroup group in profile.Groups)
            {
                List<ManualSyncChange> changes = new List<ManualSyncChange>();
                foreach (TilePaletteProfileEntry entry in group.entries.Where(value => value.included))
                {
                    TileBase tile = assets.Resolve(entry);
                    List<Vector3Int> positions = tile == null ? new List<Vector3Int>() : palette.Positions(tile);
                    if (positions.Count > 1)
                    {
                        proposal.conflicts.Add(entry.sourceId + " 在 Palette 中出现多次。" );
                        continue;
                    }
                    Vector3Int expected = TilePaletteProfileUtility.GetTargetPosition(group, entry);
                    if (positions.Count == 0)
                        changes.Add(new ManualSyncChange { group = group, entry = entry, isMissing = true });
                    else if (positions[0] != expected)
                        changes.Add(new ManualSyncChange { group = group, entry = entry, currentPosition = positions[0], delta = positions[0] - expected });
                }

                int includedCount = group.entries.Count(value => value.included);
                if (changes.Count == includedCount && changes.All(value => !value.isMissing) && changes.Select(value => value.delta).Distinct().Count() == 1)
                    proposal.groupTranslations[group] = changes[0].delta;
                else
                    proposal.entryChanges.AddRange(changes);

                foreach (TilePaletteProfileEntry entry in group.entries.Where(value => !value.included))
                {
                    TileBase tile = assets.Resolve(entry);
                    List<Vector3Int> positions = tile == null ? new List<Vector3Int>() : palette.Positions(tile);
                    if (positions.Count > 1)
                    {
                        proposal.conflicts.Add(entry.sourceId + " 在 Palette 中出现多次。" );
                        continue;
                    }
                    if (positions.Count == 1)
                    {
                        proposal.entryChanges.Add(new ManualSyncChange
                        {
                            group = group,
                            entry = entry,
                            currentPosition = positions[0],
                            isReactivated = true
                        });
                    }
                }
            }

            HashSet<TileBase> managed = new HashSet<TileBase>(
                profile.Groups.SelectMany(group => group.entries).Select(assets.Resolve).Where(tile => tile != null));
            foreach (KeyValuePair<Vector3Int, TileBase> pair in palette.byPosition.Where(pair =>
                         !managed.Contains(pair.Value) &&
                         TilePaletteProfileUtility.IsManagedCell(profile, pair.Key)))
                proposal.conflicts.Add($"未知 Tile {pair.Value.name} 位于 {pair.Key}，不会自动加入 Profile。" );
            return proposal;
        }

        public static void AcceptManualChanges(
            TilePaletteProfile profile,
            ManualSyncProposal proposal,
            bool acceptMissingAsDisabled,
            bool allowSafeChangesWithConflicts = false)
        {
            if (proposal.conflicts.Count > 0 && !allowSafeChangesWithConflicts)
                throw new InvalidOperationException("存在手工同步冲突，必须先处理。" );
            Undo.RecordObject(profile, "Accept Tile Palette Manual Changes");
            foreach (KeyValuePair<TilePaletteProfileGroup, Vector3Int> translation in proposal.groupTranslations)
            {
                translation.Key.origin += translation.Value;
                translation.Key.inferenceSource = LayoutInferenceSource.Manual;
            }
            foreach (ManualSyncChange change in proposal.entryChanges)
            {
                if (change.isReactivated)
                {
                    change.entry.included = true;
                    change.entry.hasManualOverride = true;
                    change.entry.manualOverride = change.currentPosition.Value - change.group.origin;
                    change.group.inferenceSource = LayoutInferenceSource.Manual;
                    continue;
                }
                if (change.isMissing)
                {
                    if (!acceptMissingAsDisabled) throw new InvalidOperationException(change.entry.sourceId + " 已从 Palette 删除；需要明确允许停用。" );
                    change.entry.included = false;
                    continue;
                }
                change.entry.hasManualOverride = true;
                change.entry.manualOverride = change.currentPosition.Value - change.group.origin;
                change.group.inferenceSource = LayoutInferenceSource.Manual;
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        public static TilePaletteAutoSyncResult SyncManualChangesAutomatically(TilePaletteProfile profile)
        {
            TilePaletteAutoSyncResult result = new TilePaletteAutoSyncResult();
            string profileBackup = EditorJsonUtility.ToJson(profile);
            byte[] prefabBackup = File.ReadAllBytes(AbsolutePath(profile.PalettePrefabPath));
            using (TilePaletteAutoSyncGuard.Suppress())
            try
            {
                CleanupPalette(profile, result);
                ManualSyncProposal proposal = AnalyzeManualChanges(profile);
                result.warnings.AddRange(proposal.conflicts.Select(conflict => "[TilePalette] 自动同步冲突：" + conflict));
                if (proposal.HasChanges)
                {
                    result.translatedGroups = proposal.groupTranslations.Count;
                    result.movedEntries = proposal.entryChanges.Count(change => !change.isMissing && !change.isReactivated);
                    result.disabledEntries = proposal.entryChanges.Count(change => change.isMissing);
                    result.reactivatedEntries = proposal.entryChanges.Count(change => change.isReactivated);
                    AcceptManualChanges(profile, proposal, true, true);
                }
            }
            catch
            {
                File.WriteAllBytes(AbsolutePath(profile.PalettePrefabPath), prefabBackup);
                EditorJsonUtility.FromJsonOverwrite(profileBackup, profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
            return result;
        }

        private static void CleanupPalette(TilePaletteProfile profile, TilePaletteAutoSyncResult result)
        {
            ValidateProfile(profile);
            TileAssetIndex assets = TileAssetIndex.Load(profile);
            HashSet<TileBase> knownTiles = new HashSet<TileBase>(
                profile.Groups.SelectMany(group => group.entries).Select(assets.Resolve).Where(tile => tile != null));
            Dictionary<TileBase, Vector3Int> expected = profile.Groups
                .SelectMany(group => group.entries.Where(entry => entry.included)
                    .Select(entry => new
                    {
                        tile = assets.Resolve(entry),
                        position = TilePaletteProfileUtility.GetTargetPosition(group, entry)
                    }))
                .Where(value => value.tile != null)
                .GroupBy(value => value.tile)
                .ToDictionary(group => group.Key, group => group.First().position);

            GameObject root = PrefabUtility.LoadPrefabContents(profile.PalettePrefabPath);
            bool changed = false;
            try
            {
                Tilemap tilemap = RequireTilemap(root, profile.PalettePrefabPath);
                Dictionary<TileBase, List<Vector3Int>> positions = new Dictionary<TileBase, List<Vector3Int>>();
                foreach (Vector3Int position in tilemap.cellBounds.allPositionsWithin)
                {
                    TileBase tile = tilemap.GetTile(position);
                    if (tile == null) continue;
                    if (!knownTiles.Contains(tile))
                    {
                        if (!TilePaletteProfileUtility.IsManagedCell(profile, position))
                            continue;
                        tilemap.SetTile(position, null);
                        changed = true;
                        result.removedUnknownTiles++;
                        result.warnings.Add(
                            $"[TilePalette] {tile.name} 不在 Profile 中，已从格子 {position} 移除。");
                        continue;
                    }
                    if (!positions.TryGetValue(tile, out List<Vector3Int> list))
                    {
                        list = new List<Vector3Int>();
                        positions[tile] = list;
                    }
                    list.Add(position);
                }

                foreach (KeyValuePair<TileBase, List<Vector3Int>> pair in positions.Where(pair => pair.Value.Count > 1))
                {
                    if (!expected.TryGetValue(pair.Key, out Vector3Int target) || !pair.Value.Contains(target))
                    {
                        result.warnings.Add(
                            $"[TilePalette] {pair.Key.name} 重复且位置不明确，出现于格子 {string.Join(", ", pair.Value)}，未同步。");
                        continue;
                    }
                    foreach (Vector3Int duplicate in pair.Value.Where(position => position != target))
                    {
                        tilemap.SetTile(duplicate, null);
                        changed = true;
                        result.removedDuplicateTiles++;
                        result.warnings.Add(
                            $"[TilePalette] {pair.Key.name} 重复，已移除格子 {duplicate}。");
                    }
                }

                if (!changed) return;
                tilemap.CompressBounds();
                EditorUtility.SetDirty(tilemap);
                if (PrefabUtility.SaveAsPrefabAsset(root, profile.PalettePrefabPath) == null)
                    throw new InvalidOperationException("Unity 无法保存清理后的 Palette Prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
        }

        public static string CalculateLayoutHash(TilePaletteProfile profile)
        {
            string value = string.Join("|", profile.Groups.SelectMany(group => group.entries.Where(entry => entry.included)
                .Select(entry => TilePaletteProfileUtility.GetResourceId(entry) + "@" + TilePaletteProfileUtility.GetTargetPosition(group, entry)))
                .OrderBy(item => item, StringComparer.Ordinal));
            return Hash128.Compute(value).ToString();
        }

        private static void ValidateProfile(TilePaletteProfile profile)
        {
            if (profile == null) throw new InvalidOperationException("请选择 TilePaletteProfile。" );
            if (!AssetDatabase.IsValidFolder(profile.SourceFolderPath)) throw new InvalidOperationException("Profile 的源文件夹无效。" );
            if (!AssetDatabase.IsValidFolder(profile.TileOutputFolderPath)) throw new InvalidOperationException("Profile 的 Tile 输出文件夹无效。" );
            if (profile.PalettePrefab == null || string.IsNullOrEmpty(profile.PalettePrefabPath)) throw new InvalidOperationException("Profile 的 Palette Prefab 无效。" );
            string[] duplicateIds = profile.IncludedEntries
                .Select(entry => new { entry, id = TilePaletteProfileUtility.GetResourceId(entry) })
                .Where(value => !string.IsNullOrWhiteSpace(value.id))
                .GroupBy(value => value.id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.First().entry.sourceId)
                .ToArray();
            if (duplicateIds.Length > 0) throw new InvalidOperationException("Profile 重复分配 Sprite：" + string.Join(", ", duplicateIds));
            if (profile.IncludedEntries.Any(entry => entry.sprite == null)) throw new InvalidOperationException("Profile 存在丢失 Sprite 引用。" );
        }

        private static LayoutPlanItem NewItem(LayoutPlanAction action, TilePaletteProfileGroup group, TilePaletteProfileEntry entry, TileBase tile, Vector3Int? current, Vector3Int? target, string diagnostic)
        {
            return new LayoutPlanItem { action = action, group = group, entry = entry, tile = tile, currentPosition = current, targetPosition = target, diagnostic = diagnostic };
        }

        private static Tilemap RequireTilemap(GameObject root, string path)
        {
            Tilemap[] maps = root.GetComponentsInChildren<Tilemap>(true);
            if (maps.Length != 1) throw new InvalidOperationException($"{path} 需要且只能包含一个 Tilemap，实际为 {maps.Length}。" );
            return maps[0];
        }

        private static string UniqueTilePath(string folder, string sourceId)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string safeName = new string((sourceId ?? "tile").Select(character => invalid.Contains(character) ? '_' : character).ToArray());
            string path = folder.TrimEnd('/') + "/" + safeName + ".asset";
            return AssetDatabase.GenerateUniqueAssetPath(path);
        }

        private static string AbsolutePath(string assetPath)
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName ?? throw new InvalidOperationException("无法解析 Unity 项目根目录。" );
            return Path.GetFullPath(Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private sealed class TileAssetIndex
        {
            private readonly Dictionary<Sprite, TileBase> bySprite = new Dictionary<Sprite, TileBase>();
            public readonly Dictionary<TileBase, Sprite> byTile = new Dictionary<TileBase, Sprite>();
            public readonly List<string> conflicts = new List<string>();

            public static TileAssetIndex Load(TilePaletteProfile profile)
            {
                TileAssetIndex index = new TileAssetIndex();
                foreach (string guid in AssetDatabase.FindAssets("t:TileBase", new[] { profile.TileOutputFolderPath }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    TileBase tile = AssetDatabase.LoadAssetAtPath<TileBase>(path);
                    if (tile is Tile plain && plain.sprite != null)
                    {
                        if (index.bySprite.TryGetValue(plain.sprite, out TileBase duplicate))
                            index.conflicts.Add($"多个 Tile 引用同一 Sprite：{AssetDatabase.GetAssetPath(duplicate)} 与 {path}");
                        else
                        {
                            index.bySprite[plain.sprite] = tile;
                            index.byTile[tile] = plain.sprite;
                        }
                    }
                    else if (profile.IncludedEntries.All(entry => entry.tile != tile))
                    {
                        index.conflicts.Add($"无法识别来源的自定义 TileBase：{path}");
                    }
                }
                foreach (TilePaletteProfileEntry entry in profile.IncludedEntries.Where(entry => entry.tile != null))
                {
                    if (!index.byTile.ContainsKey(entry.tile)) index.byTile[entry.tile] = entry.sprite;
                    if (!index.bySprite.ContainsKey(entry.sprite)) index.bySprite[entry.sprite] = entry.tile;
                }
                return index;
            }

            public TileBase Resolve(TilePaletteProfileEntry entry)
            {
                if (entry.tile != null)
                {
                    if (entry.tile is Tile plain && plain.sprite != entry.sprite) conflicts.Add($"{entry.sourceId} 的 Tile 引用了不同 Sprite。" );
                    return entry.tile;
                }
                if (entry.sprite != null && bySprite.TryGetValue(entry.sprite, out TileBase tile))
                {
                    return tile;
                }
                return null;
            }
        }

        private sealed class PaletteSnapshot
        {
            public readonly Dictionary<Vector3Int, TileBase> byPosition = new Dictionary<Vector3Int, TileBase>();
            private readonly Dictionary<TileBase, List<Vector3Int>> positions = new Dictionary<TileBase, List<Vector3Int>>();

            public static PaletteSnapshot Load(TilePaletteProfile profile, TileAssetIndex assets)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(profile.PalettePrefabPath);
                try
                {
                    Tilemap tilemap = RequireTilemap(root, profile.PalettePrefabPath);
                    PaletteSnapshot snapshot = new PaletteSnapshot();
                    foreach (Vector3Int position in tilemap.cellBounds.allPositionsWithin)
                    {
                        TileBase tile = tilemap.GetTile(position);
                        if (tile == null) continue;
                        snapshot.byPosition[position] = tile;
                        if (!snapshot.positions.TryGetValue(tile, out List<Vector3Int> list))
                        {
                            list = new List<Vector3Int>();
                            snapshot.positions[tile] = list;
                        }
                        list.Add(position);
                    }
                    return snapshot;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            public TileBase TileAt(Vector3Int position) => byPosition.TryGetValue(position, out TileBase tile) ? tile : null;
            public List<Vector3Int> Positions(TileBase tile) => tile != null && positions.TryGetValue(tile, out List<Vector3Int> list) ? list : new List<Vector3Int>();
        }
    }
}
