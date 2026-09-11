using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TilePaletteLayoutStudio
{
    internal static class StudioConstants
    {
        public const string ReportFolder = "Library/TilePaletteLayoutStudio/Reports";
    }

    public enum LayoutInferenceSource
    {
        VisionAi = 2,
        Manual = 4
    }

    internal enum LayoutPlanAction
    {
        Keep,
        Create,
        Place,
        Move,
        Conflict,
        Orphan
    }

    [Serializable]
    public sealed class TilePaletteProfileEntry
    {
        public string resourceId;
        public string assetGuid;
        public long localFileId;
        public string sourceId;
        public Sprite sprite;
        public TileBase tile;
        public Vector3Int localPosition;
        public bool hasManualOverride;
        public Vector3Int manualOverride;
        public bool included = true;

        public Vector3Int EffectiveLocalPosition => hasManualOverride ? manualOverride : localPosition;
    }

    [Serializable]
    public sealed class TilePaletteProfileGroup
    {
        public string groupName;
        public string subgroupName;
        public Vector3Int origin;
        public LayoutInferenceSource inferenceSource;
        [Range(0f, 1f)] public float confidence;
        public List<TilePaletteProfileEntry> entries = new List<TilePaletteProfileEntry>();

        public string Id => string.IsNullOrWhiteSpace(subgroupName) ? groupName : groupName + "/" + subgroupName;
    }

    [Serializable]
    public sealed class TemplateIndexPosition
    {
        public int index;
        public Vector2Int position;
    }

    [Serializable]
    internal sealed class AnalyzedLayoutPlacement
    {
        public string resourceId;
        public string sourceId;
        public Sprite sprite;
        public SpritePixelFeatures pixels;
        public string assetGuid;
        public long localFileId;
        public string groupName;
        public string subgroupName;
        public Vector3Int localPosition;
    }

    [Serializable]
    internal sealed class AnalyzedLayout
    {
        public LayoutInferenceSource inferenceSource;
        [Range(0f, 1f)] public float confidence;
        public List<AnalyzedLayoutPlacement> placements = new List<AnalyzedLayoutPlacement>();
        public List<string> diagnostics = new List<string>();

        public bool TryValidate(IEnumerable<string> expectedResourceIds, out string diagnostic)
        {
            string[] expected = expectedResourceIds.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            string[] actual = placements.Select(value => value.resourceId).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                diagnostic = "视觉结果包含遗漏、重复或未知 Sprite。";
                return false;
            }

            foreach (IGrouping<string, AnalyzedLayoutPlacement> group in placements.GroupBy(value => value.groupName + "\n" + value.subgroupName, StringComparer.Ordinal))
            {
                if (group.GroupBy(value => value.localPosition).Any(bucket => bucket.Count() > 1))
                {
                    diagnostic = "视觉结果存在坐标冲突：" + group.Key.Replace('\n', '/');
                    return false;
                }
            }

            diagnostic = string.Empty;
            return true;
        }
    }

    internal sealed class LayoutPlanItem
    {
        public LayoutPlanAction action;
        public TilePaletteProfileGroup group;
        public TilePaletteProfileEntry entry;
        public TileBase tile;
        public Vector3Int? currentPosition;
        public Vector3Int? targetPosition;
        public string diagnostic;
        public bool requiresConfirmation;
    }

    internal sealed class LayoutPlan
    {
        public readonly List<LayoutPlanItem> items = new List<LayoutPlanItem>();
        public int Count(LayoutPlanAction action) => items.Count(item => item.action == action);
        public bool HasBlockingConflicts => items.Any(item => item.action == LayoutPlanAction.Conflict);
        public bool HasConfirmedMoves => items.Any(item => item.requiresConfirmation);
        public IEnumerable<LayoutPlanItem> BlockingConflicts => items.Where(item => item.action == LayoutPlanAction.Conflict);
    }

    internal sealed class SourceSpriteInfo
    {
        public string resourceId;
        public string analysisId;
        public string sourceId;
        public string assetPath;
        public string assetGuid;
        public long localFileId;
        public string groupName;
        public int numericIndex = -1;
        public string semanticSuffix;
        public Sprite sprite;
        public SpritePixelFeatures pixels;
    }

    internal sealed class SpritePixelFeatures
    {
        public int width;
        public int height;
        public RectInt alphaBounds;
        public float opaqueRatio;
        public float[] topAlpha;
        public float[] bottomAlpha;
        public float[] leftAlpha;
        public float[] rightAlpha;
        public float[] topLuminance;
        public float[] bottomLuminance;
        public float[] leftLuminance;
        public float[] rightLuminance;
        public bool touchesTop;
        public bool touchesBottom;
        public bool touchesLeft;
        public bool touchesRight;
    }

    internal sealed class SourceScanResult
    {
        public string sourceFolderPath;
        public readonly List<SourceSpriteInfo> sprites = new List<SourceSpriteInfo>();
        public readonly List<string> warnings = new List<string>();
        public readonly List<string> errors = new List<string>();
        public bool IsValid => errors.Count == 0;
    }

    internal sealed class InferenceRequest
    {
        public SourceScanResult sources;
        public IReadOnlyList<TileLayoutTemplate> customTemplates;
    }

    internal interface ITileAssetFactory
    {
        bool CanReuse(TileBase tile, Sprite expectedSprite, out string diagnostic);
        TileBase Create(Sprite sprite, string assetPath);
    }

    internal sealed class ManualSyncChange
    {
        public TilePaletteProfileGroup group;
        public TilePaletteProfileEntry entry;
        public Vector3Int? currentPosition;
        public Vector3Int delta;
        public bool isMissing;
        public bool isReactivated;
    }

    internal sealed class ManualSyncProposal
    {
        public readonly Dictionary<TilePaletteProfileGroup, Vector3Int> groupTranslations = new Dictionary<TilePaletteProfileGroup, Vector3Int>();
        public readonly List<ManualSyncChange> entryChanges = new List<ManualSyncChange>();
        public readonly List<string> conflicts = new List<string>();
        public bool HasChanges => groupTranslations.Count > 0 || entryChanges.Count > 0;
    }

    internal sealed class TilePaletteAutoSyncResult
    {
        public int translatedGroups;
        public int movedEntries;
        public int disabledEntries;
        public int reactivatedEntries;
        public int removedUnknownTiles;
        public int removedDuplicateTiles;
        public readonly List<string> warnings = new List<string>();

        public bool HasChanges => translatedGroups > 0 || movedEntries > 0 || disabledEntries > 0 ||
                                  reactivatedEntries > 0 || removedUnknownTiles > 0 || removedDuplicateTiles > 0;
    }
}
