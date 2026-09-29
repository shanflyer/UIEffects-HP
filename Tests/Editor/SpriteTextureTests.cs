using System;
using System.IO;
using NUnit.Framework;
using ShanFlyer.UIEffects.Internal;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class SpriteTextureTests
    {
        private string folder;
        private Sprite imported;
        private GameObject root;
        private Material material;

        [SetUp]
        public void Setup()
        {
            folder = "Assets/UIEffectSpriteTextureTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            var image = new Texture2D(16, 16);
            try
            {
                var pixels = new Color[256];
                for (int i = 0; i < pixels.Length; ++i) pixels[i] = Color.white;
                image.SetPixels(pixels); image.Apply();
                File.WriteAllBytes(folder + "/Sprite.png", image.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(image); }
            AssetDatabase.ImportAsset(folder + "/Sprite.png", ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(folder + "/Sprite.png");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16;
            importer.spriteBorder = Vector4.one * 4;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            imported = AssetDatabase.LoadAssetAtPath<Sprite>(folder + "/Sprite.png");
            Assert.IsNotNull(imported);
            Assert.IsTrue(EditorUtility.IsPersistent(imported));
            Assert.IsFalse(imported.packed);
            material = new Material(Shader.Find("UI/Default"));
            root = new GameObject("sprite texture tests", typeof(Canvas));
        }

        [TearDown]
        public void Cleanup()
        {
            Object.DestroyImmediate(root); Object.DestroyImmediate(material);
            if (!string.IsNullOrEmpty(folder)) AssetDatabase.DeleteAsset(folder);
        }

        private void CheckBridge(Sprite sprite, Texture2D expected)
        {
            var host = new GameObject("effect", typeof(RectTransform));
            host.transform.SetParent(root.transform, false);
            var node = new GameObject("sprite", typeof(SpriteRenderer));
            node.transform.SetParent(host.transform, false);
            var source = node.GetComponent<SpriteRenderer>();
            source.sprite = sprite; source.sharedMaterial = material;
            var effect = host.AddComponent<UIEffectRenderer>(); effect.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual;
            effect.uniformScale = 1; effect.RefreshSources(); effect.PrepareForUpdate();
            var output = effect.GetRendererIfExists(0);
            Assert.IsNotNull(output);
            foreach (var mode in new[] { SpriteDrawMode.Simple, SpriteDrawMode.Sliced, SpriteDrawMode.Tiled })
            {
                source.drawMode = mode; source.size = new Vector2(2, 2);
                effect.MarkGeometryDirty(); output.UpdateMesh(null);
                Assert.AreSame(expected, output.mainTexture);
                Assert.Greater(output.canvasRenderer.materialCount, 0);
            }
        }

        [Test]
        public void ImportedUnpackedSpriteResolvesAndBindsWithoutAtlasData()
        {
            Assert.AreSame(imported.texture, SpriteTexture.Resolve(imported));
            CheckBridge(imported, imported.texture);
        }

        [Test]
        public void ParticleSpriteSheetAcceptsImportedUnpackedSprite()
        {
            var node = new GameObject("particles", typeof(ParticleSystem));
            node.transform.SetParent(root.transform, false);
            var ps = node.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true; sheet.mode = ParticleSystemAnimationMode.Sprites; sheet.AddSprite(imported);
            Assert.AreSame(imported.texture, ParticleSourceInfo.SpriteSheetTexture(ps));
        }

        [Test]
        public void AtlasPreviewSpritesResolveAndBindWithTheirUvTexture()
        {
            var previousMode = EditorSettings.spritePackerMode;
            Sprite packed = null;
            try
            {
                EditorSettings.spritePackerMode = SpritePackerMode.AlwaysOnAtlas;
                var atlas = new SpriteAtlas();
                AssetDatabase.CreateAsset(atlas, folder + "/Test.spriteatlas");
                atlas.Add(new Object[] { imported });
                var packing = atlas.GetPackingSettings();
                packing.enableRotation = false; packing.enableTightPacking = false;
                atlas.SetPackingSettings(packing);
                SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget, false);
                packed = atlas.GetSprite(imported.name);
                Assert.Greater(atlas.spriteCount, 0); Assert.IsNotNull(packed);
                // Packing an editor preview does not necessarily bind a runtime atlas.
                // Both states must use the texture belonging to the Sprite's native UVs.
                Assert.AreSame(packed.texture, SpriteTexture.Resolve(packed));
                CheckBridge(packed, packed.texture);
                // The imported asset can also acquire an editor atlas binding after packing.
                Assert.AreSame(imported.texture, SpriteTexture.Resolve(imported));
                CheckBridge(imported, imported.texture);
            }
            finally { EditorSettings.spritePackerMode = previousMode; Object.DestroyImmediate(packed); }
        }

        [Test]
        public void RuntimeSpriteAndNullResolveWithoutEditorAtlasLookup()
        {
            Assert.IsNull(SpriteTexture.Resolve(null));
            var texture = new Texture2D(8, 8);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), Vector2.one * .5f);
            try { Assert.AreSame(texture, SpriteTexture.Resolve(sprite)); }
            finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
        }
    }
}
