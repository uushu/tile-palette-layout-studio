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
        private const int CellSize = 80;

        public static TilePaletteContactSheet Render(
            string name,
            IReadOnlyList<SourceSpriteInfo> sources)
        {
            int columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(sources.Count)), 1, 6);
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
                    .Repeat(new Color32(32, 35, 40, 255), sheet.width * sheet.height)
                    .ToArray();
                sheet.SetPixels32(background);
                for (int index = 0; index < sources.Count; index++)
                {
                    Color32[] sourcePixels = SpritePixelReader.Read(
                        sources[index].sprite,
                        out int width,
                        out int height);
                    int cellX = index % columns * CellSize;
                    int cellY = (rows - 1 - index / columns) * CellSize;
                    int drawSize = CellSize - 14;
                    for (int y = 0; y < drawSize; y++)
                    for (int x = 0; x < drawSize; x++)
                    {
                        int sourceX = Mathf.Clamp(x * width / drawSize, 0, width - 1);
                        int sourceY = Mathf.Clamp(y * height / drawSize, 0, height - 1);
                        sheet.SetPixel(
                            cellX + x + 7,
                            cellY + y + 12,
                            sourcePixels[sourceY * width + sourceX]);
                    }
                    DrawNumber(sheet, cellX + 3, cellY + 2, index + 1);
                }

                sheet.Apply(false, false);
                return new TilePaletteContactSheet
                {
                    Name = name,
                    Labels = sources.Select(source => source.analysisId).ToArray(),
                    PngBytes = sheet.EncodeToPNG()
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sheet);
            }
        }

        private static void DrawNumber(Texture2D texture, int x, int y, int number)
        {
            foreach (char character in number.ToString())
            {
                string[] rows = Digit(character);
                for (int row = 0; row < rows.Length; row++)
                for (int column = 0; column < rows[row].Length; column++)
                    if (rows[row][column] == '1')
                        texture.SetPixel(x + column, y + 4 - row, Color.white);
                x += 4;
            }
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
            string bits = digits[Mathf.Clamp(value - '0', 0, 9)];
            return Enumerable.Range(0, 5)
                .Select(row => bits.Substring(row * 3, 3))
                .ToArray();
        }
    }
}
