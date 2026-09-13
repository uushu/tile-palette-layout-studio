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
            Assert.That(group.SubgroupName, Is.EqualTo("main"));
        }

        [Test]
        public void RecipeGroup_PreservesExplicitLocalCoordinates()
        {
            TilePaletteRecipeGroup group = TilePaletteLayoutRecipe.Group(
                "arch",
                "main",
                new Vector3Int(10, -5, 0),
                TilePaletteLayoutRecipe.Tile("arch_left", 0, 0),
                TilePaletteLayoutRecipe.Tile("arch_right", 2, -1));

            Assert.That(group.Origin, Is.EqualTo(new Vector3Int(10, -5, 0)));
            Assert.That(group.Entries[1].LocalPosition, Is.EqualTo(new Vector3Int(2, -1, 0)));
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
        public void PreviewUtility_UsesTheRealSpriteTextureRegion()
        {
            Texture2D texture = new Texture2D(8, 8);
            Sprite sprite = Sprite.Create(texture, new Rect(2, 1, 4, 3), Vector2.one * 0.5f, 8f);
            try
            {
                Assert.That(
                    TilePalettePreviewUtility.GetSpriteUv(sprite),
                    Is.EqualTo(new Rect(0.25f, 0.125f, 0.5f, 0.375f)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void PreviewUtility_BuildsOnlyNonEmptyGroups()
        {
            Texture2D texture = new Texture2D(1, 1);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
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
                            new TilePaletteProfileEntry
                            {
                                sprite = sprite,
                                included = true,
                                localPosition = new Vector3Int(2, -3, 0)
                            }
                        }
                    },
                    new TilePaletteProfileGroup
                    {
                        groupName = "empty",
                        subgroupName = "main"
                    }
                });

                IReadOnlyList<TilePalettePreviewGroup> groups = TilePalettePreviewUtility.BuildGroups(profile);
                Assert.That(groups, Has.Count.EqualTo(1));
                Assert.That(groups[0].Id, Is.EqualTo("shape/main"));
                Assert.That(groups[0].Bounds.min, Is.EqualTo(new Vector3Int(2, -3, 0)));
                Assert.That(groups[0].Bounds.size, Is.EqualTo(Vector3Int.one));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void BuildSaveSuppression_IsScoped()
        {
            Assert.That(TilePaletteAutoSyncGuard.IsSuppressed, Is.False);
            using (TilePaletteAutoSyncGuard.Suppress())
                Assert.That(TilePaletteAutoSyncGuard.IsSuppressed, Is.True);

            Assert.That(TilePaletteAutoSyncGuard.IsSuppressed, Is.False);
        }
    }
}
