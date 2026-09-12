using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    internal static class TilePaletteProfileUtility
    {
        public static Vector3Int GetTargetPosition(
            TilePaletteProfileGroup group,
            TilePaletteProfileEntry entry)
        {
            return group.origin + entry.localPosition;
        }

        public static bool IsManagedCell(TilePaletteProfile profile, Vector3Int position)
        {
            return profile.Groups.Any(group => group.entries.Any(entry =>
                entry.included && GetTargetPosition(group, entry) == position));
        }

        public static string GetResourceId(TilePaletteProfileEntry entry)
        {
            if (entry == null || entry.sprite == null) return string.Empty;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    entry.sprite,
                    out string guid,
                    out long localId))
                return guid + ":" + localId;
            return entry.sprite.GetInstanceID().ToString();
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
