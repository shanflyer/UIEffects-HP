using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace ShanFlyer.UIEffects
{
    public sealed class ParticleShaderGenerator : EditorWindow
    {
        public enum Target { BuiltIn, URP }
        private Target target;
        private static bool busy;
        private string status;
        private MessageType statusType;
        private const string OutputRoot = "Assets/UIEffectsGenerated/ParticleShaders";
        private const string UrpPackage = "com.unity.render-pipelines.universal";

        [MenuItem("Tools/UI Effects HP/Generate Particle Shaders")]
        public static void Open() => GetWindow<ParticleShaderGenerator>("Particle Shaders");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Particle Shader Generator", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(busy))
            {
                target = (Target)EditorGUILayout.Popup("Target", (int)target, new[] { "Built-in", "URP" });
                EditorGUILayout.HelpBox("Generate stencil-enabled copies of all default particle shaders for the selected pipeline. Source shaders and project settings stay unchanged.", MessageType.Info);
                EditorGUILayout.LabelField("Output", OutputRoot, EditorStyles.wordWrappedLabel);
                if (GUILayout.Button(busy ? "Generating..." : "Generate Shaders")) GenerateSelected();
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusType);
        }

        private async void GenerateSelected()
        {
            if (busy) return;
            var pipeline = GraphicsSettings.currentRenderPipeline;
            bool matches = target == Target.BuiltIn ? !pipeline
                : pipeline && pipeline.GetType().FullName == "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset";
            if (!matches && !EditorUtility.DisplayDialog("Pipeline Mismatch",
                "The selected shader target differs from the current render pipeline. Generate the shaders anyway? No pipeline settings or packages will be changed.",
                "Generate Anyway", "Cancel")) return;
            busy = true; status = "";
            try
            {
                string root, version, origin;
                if (target == Target.BuiltIn)
                {
                    version = Application.unityVersion;
                    root = await BuiltinSource(); origin = File.ReadAllText(Path.Combine(root, "source-url.txt"));
                }
                else
                {
                    var installed = InstalledUrp();
                    root = installed?.resolvedPath ?? Path.Combine(EditorApplication.applicationContentsPath, "Resources/PackageManager/BuiltInPackages", UrpPackage);
                    if (!Directory.Exists(Path.Combine(root, "Shaders/Particles"))) root = await DownloadUrpSource();
                    version = JsonUtility.FromJson<PackageVersion>(File.ReadAllText(Path.Combine(root, "package.json"))).version;
                    origin = UrpPackage + "@" + version;
                    if (File.Exists(Path.Combine(root, "source-url.txt"))) origin += " (" + File.ReadAllText(Path.Combine(root, "source-url.txt")) + ")";
                }
                string output = GenerateFromSource(root, target, version, origin);
                status = "Generated shaders: " + output;
                statusType = MessageType.Info;
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(output); EditorGUIUtility.PingObject(Selection.activeObject);
            }
            catch (OperationCanceledException) { status = "Generation cancelled."; statusType = MessageType.Info; }
            catch (Exception exception) { status = exception.Message; statusType = MessageType.Error; Debug.LogException(exception); }
            finally { busy = false; EditorUtility.ClearProgressBar(); if (this) Repaint(); }
        }
        [Serializable] private sealed class PackageVersion { public string version; }
        private static PackageInfo InstalledUrp() => PackageInfo.GetAllRegisteredPackages().FirstOrDefault(p => p.name == UrpPackage);

        private static async Task<string> DownloadUrpSource()
        {
            // Download only shader source from Unity's matching editor release branch.
            // The project manifest and active render pipeline are never changed.
            var editor = Application.unityVersion.Split('.');
            string release = editor[0] + "." + editor[1];
            string cache = Path.GetFullPath("Library/UIEffectsHP/ShaderSources/URP-" + release);
            string complete = Path.Combine(cache, "source-directory.txt");
            if (File.Exists(complete))
            {
                string saved = Path.Combine(cache, File.ReadAllText(complete));
                if (File.Exists(Path.Combine(saved, "package.json")) && Directory.Exists(Path.Combine(saved, "Shaders/Particles"))) return saved;
            }
            string origin = "https://raw.githubusercontent.com/Unity-Technologies/Graphics/" + release
                + "/staging/Packages/" + UrpPackage + "/";
            string extracted = Path.Combine(cache, Guid.NewGuid().ToString("N"));
            string[] files = { "package.json", "LICENSE.md", "Shaders/Particles/ParticlesLit.shader",
                "Shaders/Particles/ParticlesSimpleLit.shader", "Shaders/Particles/ParticlesUnlit.shader" };
            foreach (string file in files)
            {
                using (var request = UnityWebRequest.Get(origin + file))
                {
                    request.timeout = 30;
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("Particle Shader Source", "Downloading URP shader source: " + file, request.downloadProgress))
                        { request.Abort(); throw new OperationCanceledException(); }
                        await Task.Delay(100);
                    }
                    if (request.result != UnityWebRequest.Result.Success) throw new IOException("Could not download " + origin + file + ": " + request.error);
                    string path = Path.Combine(extracted, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, request.downloadHandler.data);
                }
            }
            File.WriteAllText(Path.Combine(extracted, "source-url.txt"), origin);
            File.WriteAllText(complete, Path.GetFileName(extracted));
            return extracted;
        }

        private static async Task<string> BuiltinSource()
        {
            string cache = Path.GetFullPath("Library/UIEffectsHP/ShaderSources/" + Application.unityVersion);
            string complete = Path.Combine(cache, "source-directory.txt");
            if (File.Exists(complete))
            {
                string saved = Path.Combine(cache, File.ReadAllText(complete));
                if (File.Exists(Path.Combine(saved, "source-url.txt")) && Directory.GetFiles(saved, "*.shader", SearchOption.AllDirectories).Length > 0) return saved;
            }
            Directory.CreateDirectory(cache);
            var urls = new List<string>();
            var versionFile = File.ReadAllText("ProjectSettings/ProjectVersion.txt");
            var revision = Regex.Match(versionFile, Regex.Escape(Application.unityVersion) + @"\s*\(([a-f0-9]+)\)");
            if (revision.Success) urls.Add("https://download.unity3d.com/download_unity/" + revision.Groups[1].Value + "/builtin_shaders-" + Application.unityVersion + ".zip");
            // Exact-version Unity source mirror; never substitute another editor version.
            urls.Add("https://codeload.github.com/chsxf/unity-built-in-shaders/zip/refs/tags/v" + Application.unityVersion);
            var failures = new List<string>();
            foreach (string url in urls)
            {
                try
                {
                    using (var request = UnityWebRequest.Get(url))
                    {
                        request.timeout = 30;
                        var operation = request.SendWebRequest();
                        while (!operation.isDone)
                        {
                            if (EditorUtility.DisplayCancelableProgressBar("Particle Shader Source", "Downloading Unity " + Application.unityVersion, request.downloadProgress))
                            { request.Abort(); throw new OperationCanceledException(); }
                            await Task.Delay(100);
                        }
                        if (request.result != UnityWebRequest.Result.Success) throw new IOException(request.error);
                        string extracted = Path.Combine(cache, Guid.NewGuid().ToString("N"));
                        ExtractSourceArchive(request.downloadHandler.data, extracted);
                        File.WriteAllText(Path.Combine(extracted, "source-url.txt"), url);
                        File.WriteAllText(complete, Path.GetFileName(extracted));
                        return extracted;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { failures.Add(url + ": " + error.Message); }
            }
            throw new IOException("Could not obtain matching Unity shader source.\n" + string.Join("\n", failures));
        }

        public static void ExtractSourceArchive(byte[] data, string destination)
            => ExtractSourceArchive(data, destination, 0);

        private static void ExtractSourceArchive(byte[] data, string destination, int depth)
        {
            string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            long total = 0;
            using (var stream = new MemoryStream(data, false))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    string relative = entry.FullName.Replace('\\', '/');
                    if (relative.EndsWith("/", StringComparison.Ordinal)) continue;
                    string extension = Path.GetExtension(relative).ToLowerInvariant();
                    bool nested = Path.GetFileName(relative).Equals("builtin_shaders.zip", StringComparison.OrdinalIgnoreCase);
                    if (!nested && extension != ".shader" && extension != ".cginc" && extension != ".hlsl" && !Path.GetFileName(relative).StartsWith("license", StringComparison.OrdinalIgnoreCase)) continue;
                    string path = Path.GetFullPath(Path.Combine(root, relative));
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe shader archive path.");
                    total += entry.Length;
                    if (entry.Length > 16 * 1024 * 1024 || total > 128 * 1024 * 1024) throw new IOException("Shader archive exceeds source size limits.");
                    if (nested)
                    {
                        if (depth != 0) throw new IOException("Unexpected nested shader archive.");
                        using (var input = entry.Open())
                        using (var contents = new MemoryStream())
                        {
                            input.CopyTo(contents);
                            ExtractSourceArchive(contents.ToArray(), destination, depth + 1);
                        }
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var input = entry.Open()) using (var output = File.Create(path)) input.CopyTo(output);
                }
            }
            if (Directory.GetFiles(destination, "*.shader", SearchOption.AllDirectories).Length == 0)
                throw new IOException("Archive contains no shader source.");
        }

        public static string GenerateFromSource(string sourceRoot, Target target, string version, string origin)
        {
            sourceRoot = Path.GetFullPath(sourceRoot);
            if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException(sourceRoot);
            var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
            string search = target == Target.URP ? Path.Combine(sourceRoot, "Shaders/Particles") : sourceRoot;
            if (target == Target.BuiltIn)
            {
                var roots = Directory.GetDirectories(sourceRoot, "DefaultResourcesExtra", SearchOption.AllDirectories);
                if (roots.Length != 1) throw new IOException("Expected one Unity DefaultResourcesExtra shader directory.");
                search = roots[0];
            }
            foreach (string path in Directory.GetFiles(search, "*.shader", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(path);
                string name = ParticleShaderStencilPatcher.ShaderName(source);
                if (!catalog.TryAdd(name, path)) throw new InvalidOperationException("Duplicate shader source: " + name);
            }
            var selected = new HashSet<string>(catalog.Keys.Where(n => target == Target.URP
                ? n.StartsWith("Universal Render Pipeline/Particles/", StringComparison.Ordinal)
                : n.StartsWith("Particles/", StringComparison.Ordinal) || n.Contains("/Particles/")), StringComparer.Ordinal);
            int particleCount = selected.Count;
            if (particleCount == 0) throw new InvalidOperationException("No default particle shaders found.");
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            var pending = new Queue<string>(selected);
            while (pending.Count > 0)
            {
                string name = pending.Dequeue();
                foreach (string reference in ParticleShaderStencilPatcher.Dependencies(File.ReadAllText(catalog[name])))
                {
                    if (reference == "Hidden/Universal Render Pipeline/FallbackError") continue;
                    string resolved = catalog.ContainsKey(reference) ? reference : "Legacy Shaders/" + reference;
                    if (!catalog.ContainsKey(resolved)) throw new InvalidOperationException("Shader dependency source not found: " + reference);
                    aliases[reference] = resolved;
                    if (selected.Add(resolved)) pending.Enqueue(resolved);
                }
            }
            string licensePath = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
                .Where(p => Path.GetFileName(p).Equals("license.txt", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(p).Equals("LICENSE.md", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Replace('\\', '/').EndsWith("/Shaders/license.txt", StringComparison.Ordinal)).FirstOrDefault();
            if (licensePath == null) throw new IOException("Unity shader source license is missing.");
            string folderName = target.ToString();
            string output = OutputRoot + "/" + folderName;
            int suffix = 1;
            while (Directory.Exists(output) || File.Exists(output) || File.Exists(output + ".meta"))
                output = OutputRoot + "/" + folderName + "_" + suffix++;
            string prefix = "UI Effects HP/Generated/" + Path.GetFileName(output) + "/";
            var names = selected.ToDictionary(n => n, n => prefix + n, StringComparer.Ordinal);
            foreach (var alias in aliases) names[alias.Key] = names[alias.Value];
            // Stage the complete result before importing any shader. Do not overwrite existing assets.
            var files = new Dictionary<string, string>();
            var report = new StringBuilder("Unity particle shader stencil copies\nTarget: " + target + "\nVersion: " + version + "\nSource: " + origin + "\nParticle shaders: " + particleCount + "\nDependency shaders: " + (selected.Count - particleCount) + "\n\n");
            foreach (string name in selected.OrderBy(n => n, StringComparer.Ordinal))
            {
                string filename = Regex.Replace(name, @"[^A-Za-z0-9_.-]", "_") + ".shader";
                if (files.ContainsKey(filename)) throw new InvalidOperationException("Shader filename collision: " + name);
                string source = File.ReadAllText(catalog[name]);
                string rewritten = ParticleShaderStencilPatcher.Rewrite(source, names[name], names);
                rewritten = CopyLocalIncludes(rewritten, Path.GetDirectoryName(catalog[name]), sourceRoot, files, new HashSet<string>());
                files.Add(filename, rewritten);
                report.Append(name).Append(" -> ").Append(names[name]).Append('\n');
            }
            Directory.CreateDirectory(output);
            try
            {
                foreach (var file in files)
                {
                    string path = Path.Combine(output, file.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, file.Value.Replace("__UIEFFECTS_OUTPUT__", output).Replace("\r\n", "\n"), new UTF8Encoding(false));
                }
                File.Copy(licensePath, Path.Combine(output, "UnityShaderLicense.txt"));
                File.WriteAllText(Path.Combine(output, "GenerationReport.txt"), report.ToString());
            }
            catch (Exception error) { throw new IOException("Generation could not finish writing " + output + ". No existing shader was overwritten.", error); }
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var errors = new List<string>();
                foreach (string path in Directory.GetFiles(output, "*.shader"))
                {
                    var shader = AssetDatabase.LoadAssetAtPath<Shader>(path.Replace('\\', '/'));
                    if (!shader || ShaderUtil.ShaderHasError(shader)) errors.Add(Path.GetFileName(path));
                }
                if (errors.Count > 0) Debug.LogWarning("Shader files were generated at " + output
                    + ", but these imports reported errors (check the target pipeline dependencies): " + string.Join(", ", errors));
            }
            Debug.Log("Generated " + particleCount + " particle shaders and " + (selected.Count - particleCount) + " dependency shaders at " + output);
            return output;
        }

        private static string CopyLocalIncludes(string source, string currentDirectory, string root,
            Dictionary<string, string> files, HashSet<string> visiting)
        {
            return Regex.Replace(source, "(?m)(^\\s*#\\s*include(?:_with_pragmas)?\\s*\")([^\"]+)(\")", match =>
            {
                string include = match.Groups[2].Value;
                if (include.StartsWith("Packages/", StringComparison.Ordinal)) return match.Value;
                string path = Path.GetFullPath(Path.Combine(currentDirectory, include));
                if (!File.Exists(path)) return match.Value; // Editor CGIncludes are resolved by Unity.
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Shader include escapes source directory: " + include);
                string relative = "Includes/" + Path.GetRelativePath(root, path).Replace('\\', '/');
                if (!files.ContainsKey(relative) && visiting.Add(path))
                {
                    string contents = CopyLocalIncludes(File.ReadAllText(path), Path.GetDirectoryName(path), root, files, visiting);
                    files[relative] = contents; visiting.Remove(path);
                }
                // Generated includes use project-absolute include paths after this placeholder is resolved below.
                return match.Groups[1].Value + "__UIEFFECTS_OUTPUT__/" + relative + match.Groups[3].Value;
            });
        }
    }
}
