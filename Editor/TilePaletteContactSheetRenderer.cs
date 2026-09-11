using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    internal sealed class TilePaletteContactSheet : IDisposable
    {
        public string Name { get; set; }
        public IReadOnlyList<string> Labels { get; set; }
        public byte[] PngBytes { get; set; }

        public void Dispose()
        {
            PngBytes = null;
        }
    }

    internal static class TilePaletteContactSheetRenderer
    {
        private const int CellSize = 96;
        private const int LabelHeight = 22;
        private const int LabelScale = 3;
        private const int SpritePadding = 6;
        private static readonly Color32 BackgroundColor =
            new Color32(32, 35, 40, 255);
        private static readonly Color32 LabelBackgroundColor =
            new Color32(245, 245, 245, 255);
        private static readonly Color32 LabelTextColor =
            new Color32(20, 20, 20, 255);

        public static TilePaletteContactSheet Render(
            string name,
            IReadOnlyList<SourceSpriteInfo> sources)
        {
            int columns = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Sqrt(sources.Count)),
                1,
                6);
            int rows = Mathf.CeilToInt(sources.Count / (float)columns);
            Texture2D sheet = new Texture2D(
                columns * CellSize,
                rows * CellSize,
                TextureFormat.RGBA32,
                false,
                false);
            try
            {
                Color32[] background = Enumerable
                    .Repeat(
                        BackgroundColor,
                        sheet.width * sheet.height)
                    .ToArray();
                sheet.SetPixels32(background);

                for (int index = 0; index < sources.Count; index++)
                {
                    Color32[] sourcePixels = SpritePixelReader.Read(
                        sources[index].sprite,
                        out int width,
                        out int height);
                    int cellX = index % columns * CellSize;
                    int cellY =
                        (rows - 1 - index / columns) * CellSize;

                    DrawLabelStrip(
                        sheet,
                        cellX,
                        cellY,
                        (index + 1).ToString("D2"));
                    DrawSprite(
                        sheet,
                        cellX,
                        cellY,
                        sourcePixels,
                        width,
                        height);
                }

                sheet.Apply(false, false);
                return new TilePaletteContactSheet
                {
                    Name = name,
                    Labels = sources
                        .Select(source => source.analysisId)
                        .ToArray(),
                    PngBytes = sheet.EncodeToPNG()
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sheet);
            }
        }

        private static void DrawSprite(
            Texture2D sheet,
            int cellX,
            int cellY,
            IReadOnlyList<Color32> sourcePixels,
            int width,
            int height)
        {
            int availableWidth = CellSize - SpritePadding * 2;
            int availableHeight =
                CellSize - LabelHeight - SpritePadding * 2;
            float scale = Mathf.Min(
                availableWidth / (float)Mathf.Max(1, width),
                availableHeight / (float)Mathf.Max(1, height));
            int drawWidth = Mathf.Max(
                1,
                Mathf.RoundToInt(width * scale));
            int drawHeight = Mathf.Max(
                1,
                Mathf.RoundToInt(height * scale));
            int startX =
                cellX + (CellSize - drawWidth) / 2;
            int startY =
                cellY + SpritePadding +
                (availableHeight - drawHeight) / 2;

            for (int y = 0; y < drawHeight; y++)
            for (int x = 0; x < drawWidth; x++)
            {
                int sourceX = Mathf.Clamp(
                    x * width / drawWidth,
                    0,
                    width - 1);
                int sourceY = Mathf.Clamp(
                    y * height / drawHeight,
                    0,
                    height - 1);
                Color32 source =
                    sourcePixels[sourceY * width + sourceX];
                sheet.SetPixel(
                    startX + x,
                    startY + y,
                    CompositeOver(source, BackgroundColor));
            }
        }

        private static void DrawLabelStrip(
            Texture2D texture,
            int cellX,
            int cellY,
            string label)
        {
            int labelY = cellY + CellSize - LabelHeight;
            for (int y = 0; y < LabelHeight; y++)
            for (int x = 0; x < CellSize; x++)
                texture.SetPixel(
                    cellX + x,
                    labelY + y,
                    LabelBackgroundColor);

            int glyphWidth = 3 * LabelScale;
            int spacing = LabelScale;
            int totalWidth =
                label.Length * glyphWidth +
                Mathf.Max(0, label.Length - 1) * spacing;
            int startX =
                cellX + (CellSize - totalWidth) / 2;
            int startY =
                labelY + (LabelHeight - 5 * LabelScale) / 2;
            DrawNumber(
                texture,
                startX,
                startY,
                label,
                LabelScale);
        }

        private static void DrawNumber(
            Texture2D texture,
            int x,
            int y,
            string number,
            int scale)
        {
            foreach (char character in number)
            {
                string[] rows = Digit(character);
                for (int row = 0; row < rows.Length; row++)
                for (int column = 0; column < rows[row].Length; column++)
                {
                    if (rows[row][column] != '1') continue;
                    for (int pixelY = 0; pixelY < scale; pixelY++)
                    for (int pixelX = 0; pixelX < scale; pixelX++)
                        texture.SetPixel(
                            x + column * scale + pixelX,
                            y + (4 - row) * scale + pixelY,
                            LabelTextColor);
                }
                x += 3 * scale + scale;
            }
        }

        internal static Color32 CompositeOver(
            Color32 source,
            Color32 background)
        {
            float alpha = source.a / 255f;
            return new Color32(
                (byte)Mathf.RoundToInt(
                    source.r * alpha +
                    background.r * (1f - alpha)),
                (byte)Mathf.RoundToInt(
                    source.g * alpha +
                    background.g * (1f - alpha)),
                (byte)Mathf.RoundToInt(
                    source.b * alpha +
                    background.b * (1f - alpha)),
                255);
        }

        private static string[] Digit(char value)
        {
            string[] digits =
            {
                "111101101101111",
                "010110010010111",
                "111001111100111",
                "111001111001111",
                "101101111001001",
                "111100111001111",
                "111100111101111",
                "111001001001001",
                "111101111101111",
                "111101111001111"
            };
            string bits =
                digits[Mathf.Clamp(value - '0', 0, 9)];
            return Enumerable.Range(0, 5)
                .Select(row => bits.Substring(row * 3, 3))
                .ToArray();
        }
    }
}
