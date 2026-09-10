using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    internal static class TilePaletteLayoutVerifier
    {
        public static void ApplyConfidence(AnalyzedLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            float modelConfidence = Mathf.Clamp01(layout.confidence);
            float edgeTotal = 0f;
            int edgeCount = 0;

            foreach (IGrouping<string, AnalyzedLayoutPlacement> group in layout.placements
                         .GroupBy(value => value.groupName + "\n" + value.subgroupName, StringComparer.Ordinal))
            {
                Dictionary<Vector3Int, AnalyzedLayoutPlacement> cells = group.ToDictionary(value => value.localPosition);
                foreach (KeyValuePair<Vector3Int, AnalyzedLayoutPlacement> cell in cells)
                {
                    if (cells.TryGetValue(cell.Key + Vector3Int.right, out AnalyzedLayoutPlacement right))
                    {
                        edgeTotal += EdgeSimilarity(cell.Value.pixels?.rightAlpha, right.pixels?.leftAlpha);
                        edgeTotal += EdgeSimilarity(cell.Value.pixels?.rightLuminance, right.pixels?.leftLuminance);
                        edgeCount += 2;
                    }
                    if (cells.TryGetValue(cell.Key + Vector3Int.down, out AnalyzedLayoutPlacement below))
                    {
                        edgeTotal += EdgeSimilarity(cell.Value.pixels?.bottomAlpha, below.pixels?.topAlpha);
                        edgeTotal += EdgeSimilarity(cell.Value.pixels?.bottomLuminance, below.pixels?.topLuminance);
                        edgeCount += 2;
                    }
                }
            }

            if (edgeCount == 0)
            {
                layout.confidence = modelConfidence;
                layout.diagnostics.Add("No adjacent edges were available for local verification.");
                return;
            }

            float edgeConfidence = Mathf.Clamp01(edgeTotal / edgeCount);
            layout.confidence = Mathf.Clamp01(modelConfidence * 0.75f + edgeConfidence * 0.25f);
            layout.diagnostics.Add($"Local edge verification: {edgeConfidence:P0}");
        }

        private static float EdgeSimilarity(float[] first, float[] second)
        {
            if (first == null || second == null || first.Length == 0 || first.Length != second.Length)
                return 0f;
            float error = 0f;
            for (int index = 0; index < first.Length; index++)
                error += Mathf.Abs(first[index] - second[index]);
            return 1f - error / first.Length;
        }
    }
}
