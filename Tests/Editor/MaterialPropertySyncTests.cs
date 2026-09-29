using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Kind = ShanFlyer.UIEffects.MaterialPropertyBinding.ValueKind;

namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class MaterialPropertySyncTests
    {
        private GameObject go;
        private MeshRenderer source;
        private Material original, target;
        private MaterialPropertyBlock block;
        private MaterialPropertySynchronizer sync;
        [SetUp] public void Setup()
        {
            go = new GameObject("Property sync", typeof(MeshRenderer));
            source = go.GetComponent<MeshRenderer>();
            original = new Material(Shader.Find("UI/Default"));
            original.SetColor("_Color", Color.white);
            target = new Material(original); source.sharedMaterial = original;
            block = new MaterialPropertyBlock(); sync = new MaterialPropertySynchronizer();
        }
        [TearDown] public void Cleanup()
        { Object.DestroyImmediate(go); Object.DestroyImmediate(original); Object.DestroyImmediate(target); }

        [Test] public void TransfersOverridesWithoutMutatingSourceAndSkipsUnchangedWrites()
        {
            block.SetColor("_Color", Color.red); source.SetPropertyBlock(block);
            var bindings = new[] { new MaterialPropertyBinding("_Color", Kind.Color) };
            Assert.AreEqual(1, sync.Apply(source, target, bindings));
            Assert.AreEqual(Color.red, target.GetColor("_Color"));
            Assert.AreEqual(Color.white, original.GetColor("_Color"));
            Assert.AreEqual(0, sync.Apply(source, target, bindings));
            // A recreated UI material still receives the current override.
            using (var replacement = new MaterialScope(original))
                Assert.AreEqual(1, sync.Apply(source, replacement.Value, bindings));
        }
        [Test] public void RuntimeRenameRefreshesPropertyId()
        {
            block.SetFloat("_Stencil", 3); block.SetFloat("_ColorMask", 7); source.SetPropertyBlock(block);
            var binding = new MaterialPropertyBinding("_Stencil", Kind.Float);
            var bindings = new[] { binding };
            Assert.AreEqual(1, sync.Apply(source, target, bindings));
            binding.PropertyName = "_ColorMask";
            Assert.AreEqual(1, sync.Apply(source, target, bindings));
            Assert.AreEqual(7, target.GetFloat("_ColorMask"));
        }
        [Test] public void MissingOverrideLeavesMaterialValueAndDoesNotLeakPreviousSourceBlock()
        {
            block.SetColor("_Color", Color.red); source.SetPropertyBlock(block);
            var bindings = new[] { new MaterialPropertyBinding("_Color", Kind.Color) };
            sync.Apply(source, target, bindings);
            source.SetPropertyBlock(null); target.SetColor("_Color", Color.blue);
            Assert.AreEqual(0, sync.Apply(source, target, bindings));
            Assert.AreEqual(Color.blue, target.GetColor("_Color"));
        }
        [Test] public void EmptyInvalidAndDisabledBindingsAreSafe()
        {
            block.SetColor("_Color", Color.red); source.SetPropertyBlock(block);
            Assert.AreEqual(0, sync.Apply(source, target, null));
            Assert.AreEqual(0, sync.Apply(source, target, new[] {
                null, new MaterialPropertyBinding(" ", Kind.Color),
                new MaterialPropertyBinding("_Color", Kind.Disabled),
                new MaterialPropertyBinding("_Color", (Kind)999) }));
            Assert.AreEqual(Color.white, target.GetColor("_Color"));
        }
        [TestCase(Kind.FloatArray)] [TestCase(Kind.VectorArray)] [TestCase(Kind.MatrixArray)]
        public void ArrayTransferDetectsChangesAndSkipsEqualValues(Kind kind)
        {
            const string name = "_SyncArray";
            var bindings = new[] { new MaterialPropertyBinding(name, kind) };
            for (int i = 1; i <= 2; ++i)
            {
                if (kind == Kind.FloatArray) { target.SetFloatArray(name, new[] { 0f, 0f }); block.SetFloatArray(name, new[] { (float)i, 2f }); }
                if (kind == Kind.VectorArray) { target.SetVectorArray(name, new[] { Vector4.zero, Vector4.zero }); block.SetVectorArray(name, new[] { Vector4.one * i, Vector4.one }); }
                if (kind == Kind.MatrixArray) { target.SetMatrixArray(name, new[] { Matrix4x4.zero, Matrix4x4.zero }); block.SetMatrixArray(name, new[] { Matrix4x4.Scale(Vector3.one * i), Matrix4x4.identity }); }
                source.SetPropertyBlock(block);
                Assert.AreEqual(1, sync.Apply(source, target, bindings));
                Assert.AreEqual(0, sync.Apply(source, target, bindings));
            }
        }
        private sealed class MaterialScope : System.IDisposable
        {
            internal readonly Material Value;
            internal MaterialScope(Material source) { Value = new Material(source); }
            public void Dispose() { Object.DestroyImmediate(Value); }
        }
    }
}
