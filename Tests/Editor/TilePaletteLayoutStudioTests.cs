using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TilePaletteLayoutStudio.Tests
{
    public sealed class TilePaletteLayoutStudioTests
    {
        [Test]
        public void RecipeEntry_RequiresSpriteName()
        {
            Assert.Throws<ArgumentException>(() => TilePaletteLayoutRecipe.Tile(string.Empty, 0, 0));
        }

        [Test]
        public void RecipeGroup_DefaultsToMainSubgroup()
        {
            TilePaletteRecipeGroup group = TilePaletteLayoutRecipe.Group(
                "wall",
                Vector3Int.zero,
                TilePaletteLayoutRecipe.Tile("wall_1", 0, 0));
            Assert.That(group, Is.Not.Null);
        }

        [Test]
        public void ProfileUtility_CalculatesBoundsFromFinalPositions()
        {
            BoundsInt bounds = TilePaletteProfileUtility.CalculateBounds(new[]
            {
                new Vector3Int(-2, 4, 0),
                new Vector3Int(3, 1, 0)
            });
            Assert.That(bounds.min, Is.EqualTo(new Vector3Int(-2, 1, 0)));
            Assert.That(bounds.size, Is.EqualTo(new Vector3Int(6, 4, 1)));
        }

        [Test]
        public void ProfileUtility_ManagedCellsExcludeHoles()
        {
            TilePaletteProfile profile = ScriptableObject.CreateInstance<TilePaletteProfile>();
            try
            {
                profile.ReplaceGroups(new[]
                {
                    new TilePaletteProfileGroup
                    {
                        groupName = "shape",
                        subgroupName = "main",
                        entries = new List<TilePaletteProfileEntry>
                        {
                            new TilePaletteProfileEntry { included = true, localPosition = new Vector3Int(0, 0, 0) },
                            new TilePaletteProfileEntry { included = true, localPosition = new Vector3Int(2, 0, 0) }
                        }
                    }
                });
                Assert.That(TilePaletteProfileUtility.IsManagedCell(profile, Vector3Int.zero), Is.True);
                Assert.That(TilePaletteProfileUtility.IsManagedCell(profile, new Vector3Int(1, 0, 0)), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void UnknownTileWarning_IsOneLineWithAtMostThreeExamples()
        {
            string warning = TilePaletteBuilder.FormatUnknownTileWarning(
                4,
                new[] { "a at (0, 0, 0)", "b at (1, 0, 0)", "c at (2, 0, 0)", "d at (3, 0, 0)" });

            Assert.That(warning, Does.Contain("4 Profile-external Tiles"));
            Assert.That(warning, Does.Contain("a at (0, 0, 0)"));
            Assert.That(warning, Does.Contain("c at (2, 0, 0)"));
            Assert.That(warning, Does.Not.Contain("d at (3, 0, 0)"));
        }
    }
}
