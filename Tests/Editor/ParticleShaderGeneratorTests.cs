using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;

namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class ParticleShaderGeneratorTests
    {
        [Test] public void StencilStateIsAppliedToEverySubshaderAndOverridesExistingPassState()
        {
            string source = "Shader \"Particles/Test\" { Properties { _Tint(\"Stencil {\", Color)=(1,1,1,1) } Category { ColorMask RGB SubShader { Tags { \"Queue\"=\"Transparent\" } Pass { Stencil { Ref 2 Comp Always } } } SubShader { Pass {} } } }";
            string result = ParticleShaderStencilPatcher.Rewrite(source, "Generated/Test", new Dictionary<string, string>());
            Assert.AreEqual("Generated/Test", ParticleShaderStencilPatcher.ShaderName(result));
            Assert.AreEqual(3, System.Text.RegularExpressions.Regex.Matches(result, @"Ref \[_Stencil\]").Count);
            StringAssert.Contains("_ColorMask (\"Color Mask\", Float) = 14", result);
            StringAssert.Contains("UIEffectsHPStencil", result);
            StringAssert.DoesNotContain("Ref 2", result);
        }
        [TestCase("0")]
        [TestCase("R")]
        public void DepthOnlyPassDoesNotDisableColorInDrawingPass(string depthChannels)
        {
            string source = "Shader \"Particles/Test\" { SubShader { Pass { ColorMask " + depthChannels + " } Pass {} } }";
            string result = ParticleShaderStencilPatcher.Rewrite(source, "Generated/Test", new Dictionary<string, string>());
            StringAssert.Contains("_ColorMask (\"Color Mask\", Float) = 15", result);
            StringAssert.Contains("Pass { ColorMask " + depthChannels + " }", result);
        }
        [Test] public void ProgramBodiesCommentsAndIncludesRemainUnchanged()
        {
            const string program = "CGPROGRAM\n// Stencil { Ref 9 }\n#include \"UnityCG.cginc\"\nfloat4 frag() { return 1; }\nENDCG";
            string source = "// Shader \"Wrong\" {}\nShader \"Particles/Test\" { SubShader { Pass { " + program + "\n} } }";
            string result = ParticleShaderStencilPatcher.Rewrite(source, "Generated/Test", new Dictionary<string, string>());
            StringAssert.Contains(program, result); StringAssert.Contains("// Shader \"Wrong\" {}", result);
            StringAssert.Contains("Properties", result);
        }
        [Test] public void FallbackAndUsePassPointToConvertedShaders()
        {
            string source = "Shader \"Particles/Test\" { SubShader { UsePass \"Particles/Base/FORWARD\" } Fallback \"Particles/Base\" }";
            string result = ParticleShaderStencilPatcher.Rewrite(source, "Generated/Test",
                new Dictionary<string, string> { { "Particles/Base", "Generated/Base" } });
            StringAssert.Contains("UsePass \"Generated/Base/FORWARD\"", result);
            StringAssert.Contains("Fallback \"Generated/Base\"", result);
            Assert.Throws<InvalidOperationException>(() => ParticleShaderStencilPatcher.Rewrite(source, "Generated/Test", new Dictionary<string, string>()));
        }
        [Test] public void MalformedSourceIsRejectedBeforeWriting()
        {
            Assert.Throws<InvalidOperationException>(() => ParticleShaderStencilPatcher.Rewrite("Shader \"Test\" { SubShader {", "Generated/Test", new Dictionary<string, string>()));
        }
        [Test] public void OfficialNestedBuiltinShaderArchiveIsExtracted()
        {
            using (var inner = new MemoryStream())
            using (var outer = new MemoryStream())
            {
                using (var zip = new ZipArchive(inner, ZipArchiveMode.Create, true))
                    using (var writer = new StreamWriter(zip.CreateEntry("DefaultResourcesExtra/Particle.shader").Open()))
                        writer.Write("Shader \"Particles/Test\" { SubShader { Pass {} } }");
                using (var zip = new ZipArchive(outer, ZipArchiveMode.Create, true))
                    using (var entry = zip.CreateEntry("build/BuiltinShaders/builtin_shaders.zip").Open())
                    { inner.Position = 0; inner.CopyTo(entry); }
                string destination = "Library/UIEffectsHP/ArchiveTests/" + Guid.NewGuid().ToString("N");
                ParticleShaderGenerator.ExtractSourceArchive(outer.ToArray(), destination);
                Assert.IsTrue(File.Exists(Path.Combine(destination, "DefaultResourcesExtra/Particle.shader")));
            }
        }
        [Test] public void ArchiveCannotWriteOutsideItsCache()
        {
            using (var data = new MemoryStream())
            {
                using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true))
                    using (var writer = new StreamWriter(zip.CreateEntry("../escaped.shader").Open())) writer.Write("Shader \"Test\" {}");
                Assert.Throws<IOException>(() => ParticleShaderGenerator.ExtractSourceArchive(data.ToArray(), "Library/UIEffectsHP/ArchiveSafetyTest"));
            }
        }
    }
}
