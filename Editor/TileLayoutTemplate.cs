using System.Collections.Generic;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    [CreateAssetMenu(menuName = "Tile Palette/Layout Template", fileName = "tile-layout-template")]
    public sealed class TileLayoutTemplate : ScriptableObject
    {
        public string templateName;
        public List<string> keywords = new List<string>();
        public Vector2Int countRange = new Vector2Int(1, 999);
        public Vector2Int matrixSize = Vector2Int.one;
        public List<Vector2Int> holes = new List<Vector2Int>();
        public List<TemplateIndexPosition> indexMapping = new List<TemplateIndexPosition>();
    }
}
