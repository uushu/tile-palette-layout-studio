using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    internal static class TilePaletteLayoutVerifier
    {
        // Kept for source compatibility with the analyzer. Confidence is no
        // longer calculated; this pass now repairs obvious sparse layouts and
        // improves local ordering using the already-scanned Sprite edges.
        public static void ApplyConfidence(AnalyzedLayout layout)
        {
            RepairLayout(layout);
        }

        internal static void RepairLayout(AnalyzedLayout layout)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));

            int movedByCompaction = 0;
            int reorderedRows = 0;
            int reorderedColumns = 0;

            foreach (IGrouping<string, AnalyzedLayoutPlacement> grouping
                     in layout.placements.GroupBy(
                         value => value.groupName + "\n" + value.subgroupName,
                         StringComparer.Ordinal))
            {
                List<AnalyzedLayoutPlacement> group = grouping.ToList();
                if (group.Count <= 1) continue;

                movedByCompaction += CompactCoordinateRanks(group);
                reorderedRows += OptimizeRows(group);
                reorderedColumns += OptimizeColumns(group);
                NormalizeToTopLeft(group);
            }

            layout.diagnostics.Add(
                "Local layout repair: " +
                $"compacted={movedByCompaction}, " +
                $"rows={reorderedRows}, columns={reorderedColumns}.");
        }

        private static int CompactCoordinateRanks(
            IReadOnlyList<AnalyzedLayoutPlacement> group)
        {
            int[] xs = group
                .Select(value => value.localPosition.x)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            int[] ys = group
                .Select(value => value.localPosition.y)
                .Distinct()
                .OrderByDescending(value => value)
                .ToArray();

            Dictionary<int, int> xRank = xs
                .Select((value, index) => new { value, index })
                .ToDictionary(pair => pair.value, pair => pair.index);
            Dictionary<int, int> yRank = ys
                .Select((value, index) => new { value, index })
                .ToDictionary(pair => pair.value, pair => -pair.index);

            int moved = 0;
            foreach (AnalyzedLayoutPlacement placement in group)
            {
                Vector3Int compact = new Vector3Int(
                    xRank[placement.localPosition.x],
                    yRank[placement.localPosition.y],
                    0);
                if (compact != placement.localPosition) moved++;
                placement.localPosition = compact;
            }

            return moved;
        }

        private static int OptimizeRows(
            IReadOnlyList<AnalyzedLayoutPlacement> group)
        {
            int changed = 0;
            int[] rows = group
                .Select(value => value.localPosition.y)
                .Distinct()
                .OrderByDescending(value => value)
                .ToArray();

            foreach (int row in rows)
            {
                List<AnalyzedLayoutPlacement> values = group
                    .Where(value => value.localPosition.y == row)
                    .OrderBy(value => value.localPosition.x)
                    .ThenBy(value => value.resourceId, StringComparer.Ordinal)
                    .ToList();
                if (values.Count < 3) continue;

                List<AnalyzedLayoutPlacement> candidate =
                    FindGreedyPath(values, true);
                if (candidate.SequenceEqual(values)) continue;

                float before = LayoutEdgeScore(group);
                Vector3Int[] oldPositions = values
                    .Select(value => value.localPosition)
                    .ToArray();
                int[] slots = oldPositions
                    .Select(value => value.x)
                    .OrderBy(value => value)
                    .ToArray();

                for (int index = 0; index < candidate.Count; index++)
                {
                    Vector3Int position = candidate[index].localPosition;
                    candidate[index].localPosition =
                        new Vector3Int(slots[index], row, position.z);
                }

                float after = LayoutEdgeScore(group);
                if (after > before + 0.015f)
                {
                    changed++;
                    continue;
                }

                for (int index = 0; index < values.Count; index++)
                    values[index].localPosition = oldPositions[index];
            }

            return changed;
        }

        private static int OptimizeColumns(
            IReadOnlyList<AnalyzedLayoutPlacement> group)
        {
            int changed = 0;
            int[] columns = group
                .Select(value => value.localPosition.x)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();

            foreach (int column in columns)
            {
                List<AnalyzedLayoutPlacement> values = group
                    .Where(value => value.localPosition.x == column)
                    .OrderByDescending(value => value.localPosition.y)
                    .ThenBy(value => value.resourceId, StringComparer.Ordinal)
                    .ToList();
                if (values.Count < 3) continue;

                List<AnalyzedLayoutPlacement> candidate =
                    FindGreedyPath(values, false);
                if (candidate.SequenceEqual(values)) continue;

                float before = LayoutEdgeScore(group);
                Vector3Int[] oldPositions = values
                    .Select(value => value.localPosition)
                    .ToArray();
                int[] slots = oldPositions
                    .Select(value => value.y)
                    .OrderByDescending(value => value)
                    .ToArray();

                for (int index = 0; index < candidate.Count; index++)
                {
                    Vector3Int position = candidate[index].localPosition;
                    candidate[index].localPosition =
                        new Vector3Int(column, slots[index], position.z);
                }

                float after = LayoutEdgeScore(group);
                if (after > before + 0.015f)
                {
                    changed++;
                    continue;
                }

                for (int index = 0; index < values.Count; index++)
                    values[index].localPosition = oldPositions[index];
            }

            return changed;
        }

        private static List<AnalyzedLayoutPlacement> FindGreedyPath(
            IReadOnlyList<AnalyzedLayoutPlacement> values,
            bool horizontal)
        {
            List<AnalyzedLayoutPlacement> best = values.ToList();
            float bestScore = PathScore(best, horizontal);

            for (int start = 0; start < values.Count; start++)
            {
                List<AnalyzedLayoutPlacement> remaining = values.ToList();
                List<AnalyzedLayoutPlacement> path =
                    new List<AnalyzedLayoutPlacement>
                    {
                        values[start]
                    };
                remaining.Remove(values[start]);

                while (remaining.Count > 0)
                {
                    AnalyzedLayoutPlacement current = path[path.Count - 1];
                    AnalyzedLayoutPlacement next = remaining
                        .OrderByDescending(candidate =>
                            horizontal
                                ? HorizontalPairScore(current, candidate)
                                : VerticalPairScore(current, candidate))
                        .ThenBy(candidate => candidate.resourceId, StringComparer.Ordinal)
                        .First();
                    path.Add(next);
                    remaining.Remove(next);
                }

                float score = PathScore(path, horizontal);
                if (score > bestScore)
                {
                    best = path;
                    bestScore = score;
                }
            }

            return best;
        }

        private static float PathScore(
            IReadOnlyList<AnalyzedLayoutPlacement> path,
            bool horizontal)
        {
            if (path.Count < 2) return 0f;

            float total = 0f;
            for (int index = 0; index < path.Count - 1; index++)
            {
                total += horizontal
                    ? HorizontalPairScore(path[index], path[index + 1])
                    : VerticalPairScore(path[index], path[index + 1]);
            }

            return total / (path.Count - 1);
        }

        private static float LayoutEdgeScore(
            IReadOnlyList<AnalyzedLayoutPlacement> group)
        {
            Dictionary<Vector3Int, AnalyzedLayoutPlacement> cells = group
                .GroupBy(value => value.localPosition)
                .ToDictionary(bucket => bucket.Key, bucket => bucket.First());

            float total = 0f;
            int count = 0;

            foreach (KeyValuePair<Vector3Int, AnalyzedLayoutPlacement> cell in cells)
            {
                if (cells.TryGetValue(
                        cell.Key + Vector3Int.right,
                        out AnalyzedLayoutPlacement right))
                {
                    total += HorizontalPairScore(cell.Value, right);
                    count++;
                }

                if (cells.TryGetValue(
                        cell.Key + Vector3Int.down,
                        out AnalyzedLayoutPlacement below))
                {
                    total += VerticalPairScore(cell.Value, below);
                    count++;
                }
            }

            return count == 0 ? 0f : total / count;
        }

        private static float HorizontalPairScore(
            AnalyzedLayoutPlacement left,
            AnalyzedLayoutPlacement right)
        {
            return PairScore(
                left?.pixels?.rightAlpha,
                right?.pixels?.leftAlpha,
                left?.pixels?.rightLuminance,
                right?.pixels?.leftLuminance,
                left?.pixels?.touchesRight ?? false,
                right?.pixels?.touchesLeft ?? false);
        }

        private static float VerticalPairScore(
            AnalyzedLayoutPlacement top,
            AnalyzedLayoutPlacement bottom)
        {
            return PairScore(
                top?.pixels?.bottomAlpha,
                bottom?.pixels?.topAlpha,
                top?.pixels?.bottomLuminance,
                bottom?.pixels?.topLuminance,
                top?.pixels?.touchesBottom ?? false,
                bottom?.pixels?.touchesTop ?? false);
        }

        private static float PairScore(
            float[] firstAlpha,
            float[] secondAlpha,
            float[] firstLuminance,
            float[] secondLuminance,
            bool firstTouches,
            bool secondTouches)
        {
            float alpha = EdgeSimilarity(firstAlpha, secondAlpha);
            float luminance = EdgeSimilarity(firstLuminance, secondLuminance);
            float information = Mathf.Max(
                Mean(firstAlpha),
                Mean(secondAlpha));
            float informationWeight = Mathf.Lerp(0.3f, 1f, information);
            float touchBonus = firstTouches && secondTouches ? 0.08f : 0f;

            return Mathf.Clamp01(
                (alpha * 0.55f + luminance * 0.45f) *
                informationWeight +
                touchBonus);
        }

        private static float EdgeSimilarity(float[] first, float[] second)
        {
            if (first == null ||
                second == null ||
                first.Length == 0 ||
                first.Length != second.Length)
                return 0f;

            float error = 0f;
            for (int index = 0; index < first.Length; index++)
                error += Mathf.Abs(first[index] - second[index]);

            return Mathf.Clamp01(1f - error / first.Length);
        }

        private static float Mean(float[] values)
        {
            if (values == null || values.Length == 0) return 0f;
            float total = 0f;
            for (int index = 0; index < values.Length; index++)
                total += values[index];
            return total / values.Length;
        }

        private static void NormalizeToTopLeft(
            IReadOnlyList<AnalyzedLayoutPlacement> group)
        {
            int minX = group.Min(value => value.localPosition.x);
            int maxY = group.Max(value => value.localPosition.y);
            Vector3Int offset = new Vector3Int(-minX, -maxY, 0);

            foreach (AnalyzedLayoutPlacement placement in group)
                placement.localPosition += offset;
        }
    }
}
