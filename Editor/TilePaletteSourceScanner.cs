using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    internal static class TileSourceNameParser
    {
        private static readonly Regex NumericName = new Regex(@"^(?<group>.+)_(?<index>\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly string[] SemanticSuffixes =
        {
            "_connection_left",
            "_connection_right",
            "_corner_left",
            "_corner_right",
            "_side_left",
            "_side_right",
            "_shadow",
            "_left",
            "_right"
        };

        public static bool TryParse(string sourceId, out string groupName, out int numericIndex, out string semanticSuffix)
        {
            groupName = sourceId;
            numericIndex = -1;
            semanticSuffix = string.Empty;

            Match numericMatch = NumericName.Match(sourceId);
            if (numericMatch.Success)
            {
                groupName = numericMatch.Groups["group"].Value;
                return int.TryParse(numericMatch.Groups["index"].Value, out numericIndex);
            }

            foreach (string suffix in SemanticSuffixes)
            {
                if (!sourceId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                groupName = sourceId.Substring(0, sourceId.Length - suffix.Length);
                semanticSuffix = suffix.TrimStart('_');
                return groupName.Length > 0;
            }

            semanticSuffix = "singleton";
            return !string.IsNullOrWhiteSpace(sourceId);
        }
    }

    internal static class TilePaletteSourceScanner
    {
        public static string DiscoverSourceFolder(TilePaletteProfile profile)
        {
            if (profile != null && AssetDatabase.IsValidFolder(profile.SourceFolderPath))
            {
                return profile.SourceFolderPath;
            }

            string[] selectedPaths = Selection.objects.Select(AssetDatabase.GetAssetPath).Where(path => !string.IsNullOrEmpty(path)).ToArray();
            foreach (string path in selectedPaths)
            {
                if (AssetDatabase.IsValidFolder(path))
                {
                    return path;
                }
            }

            string[] spritePaths = selectedPaths.Where(path => AssetDatabase.LoadAssetAtPath<Sprite>(path) != null).ToArray();
            if (spritePaths.Length > 0)
            {
                string common = Path.GetDirectoryName(spritePaths[0])?.Replace('\\', '/');
                while (!string.IsNullOrEmpty(common) && spritePaths.Any(path => !path.StartsWith(common + "/", StringComparison.OrdinalIgnoreCase)))
                {
                    common = Path.GetDirectoryName(common)?.Replace('\\', '/');
                }

                if (AssetDatabase.IsValidFolder(common))
                {
                    return common;
                }
            }

            return string.Empty;
        }

        public static SourceScanResult Scan(string sourceFolderPath, bool includePixels)
        {
            SourceScanResult result = new SourceScanResult { sourceFolderPath = sourceFolderPath };
            if (!AssetDatabase.IsValidFolder(sourceFolderPath))
            {
                result.errors.Add("Sprite 源文件夹无效：" + sourceFolderPath);
                return result;
            }

            Dictionary<string, SourceSpriteInfo> byResourceId = new Dictionary<string, SourceSpriteInfo>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { sourceFolderPath }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
                if (sprites.Length == 0) continue;

                foreach (Sprite sprite in sprites)
                {
                    string sourceId = sprites.Length == 1 ? Path.GetFileNameWithoutExtension(path) : sprite.name;
                    TileSourceNameParser.TryParse(sourceId, out string groupName, out int numericIndex, out string semanticSuffix);
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string assetGuid, out long localFileId))
                    {
                        result.errors.Add("无法取得 Sprite 稳定 ID：" + path + "/" + sprite.name);
                        continue;
                    }
                    string resourceId = assetGuid + ":" + localFileId;

                    SourceSpriteInfo info = new SourceSpriteInfo
                    {
                        resourceId = resourceId,
                        sourceId = sourceId,
                        assetPath = path,
                        assetGuid = assetGuid,
                        localFileId = localFileId,
                        groupName = groupName,
                        numericIndex = numericIndex,
                        semanticSuffix = semanticSuffix,
                        sprite = sprite,
                        pixels = includePixels ? SpritePixelAnalyzer.Analyze(path, sprite, result.warnings) : null
                    };

                    if (!byResourceId.TryAdd(resourceId, info))
                    {
                        result.errors.Add("重复 Sprite 资源 ID：" + resourceId);
                    }
                }
            }

            foreach (IGrouping<string, SourceSpriteInfo> group in byResourceId.Values.GroupBy(info => info.groupName, StringComparer.Ordinal))
            {
                foreach (IGrouping<int, SourceSpriteInfo> duplicate in group.Where(info => info.numericIndex >= 0).GroupBy(info => info.numericIndex).Where(bucket => bucket.Count() > 1))
                {
                    result.warnings.Add($"名称提示重复：{group.Key}_{duplicate.Key}；视觉分析将使用稳定资源 ID。");
                }
            }

            SourceSpriteInfo[] ordered = byResourceId.Values
                .OrderBy(info => info.assetPath, StringComparer.Ordinal)
                .ThenBy(info => info.localFileId)
                .ToArray();
            for (int index = 0; index < ordered.Length; index++)
                ordered[index].analysisId = "T" + (index + 1).ToString("D4");
            result.sprites.AddRange(ordered);
            if (result.sprites.Count == 0)
            {
                result.errors.Add("源文件夹中没有发现已导入的 Sprite。" );
            }

            return result;
        }
    }

    internal static class SpritePixelReader
    {
        public static Color32[] Read(Sprite sprite, out int width, out int height)
        {
            if (sprite == null || sprite.texture == null)
                throw new InvalidOperationException("Sprite 没有可读取的纹理。");

            Rect rect = sprite.rect;
            width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            height = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            Texture2D sourceTexture = sprite.texture;
            RenderTexture temporary = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            RenderTexture previous = RenderTexture.active;
            Texture2D readable = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            try
            {
                temporary.filterMode = FilterMode.Point;
                Vector2 scale = new Vector2(rect.width / sourceTexture.width, rect.height / sourceTexture.height);
                Vector2 offset = new Vector2(rect.x / sourceTexture.width, rect.y / sourceTexture.height);
                Graphics.Blit(sourceTexture, temporary, scale, offset);
                RenderTexture.active = temporary;
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readable.Apply(false, false);
                return readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                UnityEngine.Object.DestroyImmediate(readable);
            }
        }
    }

    internal static class SpritePixelAnalyzer
    {
        private const int EdgeSamples = 16;

        public static SpritePixelFeatures Analyze(string assetPath, Sprite sprite, ICollection<string> warnings)
        {
            try
            {
                Color32[] pixels = SpritePixelReader.Read(sprite, out int width, out int height);
                return Calculate(pixels, width, 0, 0, width, height);
            }
            catch (Exception exception)
            {
                warnings.Add($"无法读取 {assetPath} 的像素特征：{exception.Message}");
                return new SpritePixelFeatures
                {
                    width = Mathf.RoundToInt(sprite.rect.width),
                    height = Mathf.RoundToInt(sprite.rect.height),
                    alphaBounds = new RectInt(0, 0, Mathf.RoundToInt(sprite.rect.width), Mathf.RoundToInt(sprite.rect.height)),
                    topAlpha = new float[EdgeSamples],
                    bottomAlpha = new float[EdgeSamples],
                    leftAlpha = new float[EdgeSamples],
                    rightAlpha = new float[EdgeSamples],
                    topLuminance = new float[EdgeSamples],
                    bottomLuminance = new float[EdgeSamples],
                    leftLuminance = new float[EdgeSamples],
                    rightLuminance = new float[EdgeSamples]
                };
            }
        }

        private static SpritePixelFeatures Calculate(Color32[] pixels, int textureWidth, int startX, int startY, int width, int height)
        {
            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;
            int opaqueCount = 0;
            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    Color32 pixel = pixels[(startY + localY) * textureWidth + startX + localX];
                    if (pixel.a <= 8)
                    {
                        continue;
                    }

                    opaqueCount++;
                    minX = Math.Min(minX, localX);
                    minY = Math.Min(minY, localY);
                    maxX = Math.Max(maxX, localX);
                    maxY = Math.Max(maxY, localY);
                }
            }

            RectInt bounds = maxX < minX ? new RectInt(0, 0, 0, 0) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            SpritePixelFeatures features = new SpritePixelFeatures
            {
                width = width,
                height = height,
                alphaBounds = bounds,
                opaqueRatio = opaqueCount / (float)Math.Max(1, width * height),
                topAlpha = SampleEdge(pixels, textureWidth, startX, startY, width, height, Edge.Top, false),
                bottomAlpha = SampleEdge(pixels, textureWidth, startX, startY, width, height, Edge.Bottom, false),
                leftAlpha = SampleEdge(pixels, textureWidth, startX, startY, width, height, Edge.Left, false),
                rightAlpha = SampleEdge(pixels, textureWidth, startX, startY, width, height, Edge.Right, false),
                topLuminance = SampleEdge(pixels, textureWidth, startX, startY, width, height, Edge.Top, true),
                bottomLuminance = SampleEdge(pixels, textureWidth, startX, startY, width, height, Edge.Bottom, true),
                leftLuminance = SampleEdge(pixels, textureWidth, startX, startY, width, height, Edge.Left, true),
                rightLuminance = SampleEdge(pixels, textureWidth, startX, startY, width, height, Edge.Right, true),
                touchesTop = maxY == height - 1,
                touchesBottom = minY == 0,
                touchesLeft = minX == 0,
                touchesRight = maxX == width - 1
            };
            return features;
        }

        private static float[] SampleEdge(Color32[] pixels, int textureWidth, int startX, int startY, int width, int height, Edge edge, bool luminance)
        {
            float[] samples = new float[EdgeSamples];
            for (int sampleIndex = 0; sampleIndex < EdgeSamples; sampleIndex++)
            {
                float normalized = (sampleIndex + 0.5f) / EdgeSamples;
                int localX = edge == Edge.Left ? 0 : edge == Edge.Right ? width - 1 : Mathf.Clamp(Mathf.FloorToInt(normalized * width), 0, width - 1);
                int localY = edge == Edge.Bottom ? 0 : edge == Edge.Top ? height - 1 : Mathf.Clamp(Mathf.FloorToInt(normalized * height), 0, height - 1);
                Color32 pixel = pixels[(startY + localY) * textureWidth + startX + localX];
                samples[sampleIndex] = luminance ? (0.2126f * pixel.r + 0.7152f * pixel.g + 0.0722f * pixel.b) / 255f * (pixel.a / 255f) : pixel.a / 255f;
            }

            return samples;
        }

        private enum Edge
        {
            Top,
            Bottom,
            Left,
            Right
        }
    }
}
