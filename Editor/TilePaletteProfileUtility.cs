using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    internal static class TilePaletteProfileUtility
    {
        private const int ShelfWidth = 32;
        private const int GroupSpacing = 2;

        public static void ApplyAnalyzedLayout(
            TilePaletteProfile profile,
            AnalyzedLayout layout,
            IEnumerable<string> expectedResourceIds,
            bool preserveManualChanges)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (expectedResourceIds == null) throw new ArgumentNullException(nameof(expectedResourceIds));
            if (!layout.TryValidate(expectedResourceIds, out string diagnostic))
                throw new InvalidOperationException(diagnostic);

            Dictionary<string, TilePaletteProfileGroup> existingGroups = profile.Groups
                .GroupBy(group => group.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            List<TilePaletteProfileGroup> groups = new List<TilePaletteProfileGroup>();

            foreach (IGrouping<string, AnalyzedLayoutPlacement> analyzedGroup in layout.placements
                         .GroupBy(value => value.groupName + "\n" + value.subgroupName, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                AnalyzedLayoutPlacement first = analyzedGroup.First();
                string id = first.groupName + "/" + first.subgroupName;
                existingGroups.TryGetValue(id, out TilePaletteProfileGroup previousGroup);
                Dictionary<string, TilePaletteProfileEntry> previousByResourceId = previousGroup == null
                    ? new Dictionary<string, TilePaletteProfileEntry>(StringComparer.Ordinal)
                    : previousGroup.entries.Where(entry => !string.IsNullOrEmpty(entry.resourceId))
                        .GroupBy(entry => entry.resourceId, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                Dictionary<Sprite, TilePaletteProfileEntry> previousBySprite = previousGroup == null
                    ? new Dictionary<Sprite, TilePaletteProfileEntry>()
                    : previousGroup.entries.Where(entry => entry.sprite != null)
                        .GroupBy(entry => entry.sprite)
                        .ToDictionary(group => group.Key, group => group.First());

                TilePaletteProfileGroup profileGroup = new TilePaletteProfileGroup
                {
                    groupName = first.groupName,
                    subgroupName = string.IsNullOrWhiteSpace(first.subgroupName) ? "main" : first.subgroupName,
                    origin = previousGroup?.origin ?? Vector3Int.zero,
                    inferenceSource = LayoutInferenceSource.VisionAi,
                    confidence = layout.confidence
                };

                foreach (AnalyzedLayoutPlacement placement in analyzedGroup.OrderBy(value => value.resourceId, StringComparer.Ordinal))
                {
                    if (!previousByResourceId.TryGetValue(placement.resourceId, out TilePaletteProfileEntry previous) &&
                        placement.sprite != null)
                        previousBySprite.TryGetValue(placement.sprite, out previous);

                    profileGroup.entries.Add(new TilePaletteProfileEntry
                    {
                        resourceId = placement.resourceId,
                        assetGuid = placement.assetGuid,
                        localFileId = placement.localFileId,
                        sourceId = placement.sourceId,
                        sprite = placement.sprite,
                        tile = previous?.tile,
                        localPosition = placement.localPosition,
                        hasManualOverride = preserveManualChanges && previous != null && previous.hasManualOverride,
                        manualOverride = previous?.manualOverride ?? placement.localPosition,
                        included = previous?.included ?? true
                    });
                }

                groups.Add(profileGroup);
            }

            PackNewGroups(groups, new HashSet<string>(existingGroups.Keys, StringComparer.Ordinal));
            profile.ReplaceGroups(groups);
            EditorUtility.SetDirty(profile);
        }

        private static void PackNewGroups(IReadOnlyList<TilePaletteProfileGroup> groups, IReadOnlyCollection<string> existingIds)
        {
            List<TilePaletteProfileGroup> existing = groups.Where(group => existingIds.Contains(group.Id)).ToList();
            List<TilePaletteProfileGroup> pending = groups.Where(group => !existingIds.Contains(group.Id)).ToList();
            if (pending.Count == 0) return;

            int cursorX = 0;
            int cursorY = existing.SelectMany(group => group.entries.Where(entry => entry.included)
                    .Select(entry => GetTargetPosition(group, entry).y))
                .DefaultIfEmpty(0)
                .Min();
            if (existing.Count > 0) cursorY -= GroupSpacing;
            int shelfHeight = 0;

            foreach (TilePaletteProfileGroup group in pending)
            {
                BoundsInt bounds = CalculateBounds(group.entries.Where(entry => entry.included)
                    .Select(entry => entry.EffectiveLocalPosition));
                if (cursorX > 0 && cursorX + bounds.size.x > ShelfWidth)
                {
                    cursorX = 0;
                    cursorY -= shelfHeight + GroupSpacing;
                    shelfHeight = 0;
                }

                group.origin = new Vector3Int(cursorX - bounds.xMin, cursorY - bounds.yMax + 1, 0);
                cursorX += bounds.size.x + GroupSpacing;
                shelfHeight = Math.Max(shelfHeight, bounds.size.y);
            }
        }

        public static Vector3Int GetTargetPosition(TilePaletteProfileGroup group, TilePaletteProfileEntry entry) =>
            group.origin + entry.EffectiveLocalPosition;

        public static bool IsManagedCell(TilePaletteProfile profile, Vector3Int position) =>
            profile.Groups.Any(group => group.entries.Any(entry => entry.included && GetTargetPosition(group, entry) == position));

        public static string GetResourceId(TilePaletteProfileEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.resourceId)) return entry.resourceId;
            if (entry.sprite != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(entry.sprite, out string guid, out long localId))
                return guid + ":" + localId;
            return string.Empty;
        }

        public static BoundsInt CalculateBounds(IEnumerable<Vector3Int> positions)
        {
            Vector3Int[] values = positions.ToArray();
            if (values.Length == 0) return new BoundsInt(Vector3Int.zero, Vector3Int.one);
            int minX = values.Min(value => value.x);
            int maxX = values.Max(value => value.x);
            int minY = values.Min(value => value.y);
            int maxY = values.Max(value => value.y);
            return new BoundsInt(minX, minY, 0, maxX - minX + 1, maxY - minY + 1, 1);
        }
    }
}
