using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    internal sealed class TilePalettePreviewEntry
    {
        public TilePaletteProfileEntry Entry;
        public Vector3Int LocalPosition;
        public Vector3Int TargetPosition;
    }

    internal sealed class TilePalettePreviewGroup
    {
        public TilePaletteProfileGroup Group;
        public BoundsInt Bounds;
        public IReadOnlyList<TilePalettePreviewEntry> Entries;
        public string Id => Group.Id;
    }

    internal static class TilePalettePreviewUtility
    {
        public static Rect GetSpriteUv(Sprite sprite)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            Texture2D texture = sprite.texture;
            Rect textureRect = sprite.textureRect;
            return new Rect(
                textureRect.x / texture.width,
                textureRect.y / texture.height,
                textureRect.width / texture.width,
                textureRect.height / texture.height);
        }

        public static Rect GetSpriteDrawRect(Sprite sprite, Rect cellRect)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            float pixelsPerUnit = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : sprite.rect.width;
            float width = cellRect.width * sprite.rect.width / pixelsPerUnit;
            float height = cellRect.height * sprite.rect.height / pixelsPerUnit;
            return new Rect(
                cellRect.center.x - width * 0.5f,
                cellRect.center.y - height * 0.5f,
                width,
                height);
        }

        public static IReadOnlyList<TilePalettePreviewGroup> BuildGroups(TilePaletteProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            List<TilePalettePreviewGroup> groups = new List<TilePalettePreviewGroup>();
            foreach (TilePaletteProfileGroup group in profile.Groups)
            {
                TilePalettePreviewEntry[] entries = group.entries
                    .Where(entry => entry.included && entry.sprite != null)
                    .Select(entry => new TilePalettePreviewEntry
                    {
                        Entry = entry,
                        LocalPosition = entry.localPosition,
                        TargetPosition = TilePaletteProfileUtility.GetTargetPosition(group, entry)
                    })
                    .ToArray();
                if (entries.Length == 0) continue;
                groups.Add(new TilePalettePreviewGroup
                {
                    Group = group,
                    Bounds = TilePaletteProfileUtility.CalculateBounds(entries.Select(entry => entry.LocalPosition)),
                    Entries = entries
                });
            }
            return groups;
        }
    }
}
