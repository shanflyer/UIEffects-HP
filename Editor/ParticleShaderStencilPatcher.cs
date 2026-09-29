using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ShanFlyer.UIEffects
{
    // Changes ShaderLab state only. Program bodies, comments and includes are preserved.
    public static class ParticleShaderStencilPatcher
    {
        private sealed class Token
        {
            internal string value;
            internal int start, end, depth;
            internal bool quoted;
        }
        private readonly struct Edit
        {
            internal readonly int start, end;
            internal readonly string text;
            internal Edit(int start, int end, string text) { this.start = start; this.end = end; this.text = text; }
        }
        private const string StencilState = @"Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
            Fail Keep
            ZFail Keep
        }";
        private static bool Is(Token token, string word) => !token.quoted && string.Equals(token.value, word, StringComparison.OrdinalIgnoreCase);

        public static string ShaderName(string source)
        {
            var tokens = Scan(source);
            for (int i = 0; i + 1 < tokens.Count; i++)
                if (Is(tokens[i], "Shader") && tokens[i + 1].quoted) return tokens[i + 1].value;
            throw new InvalidOperationException("Shader declaration not found.");
        }

        public static IEnumerable<string> Dependencies(string source)
        {
            var tokens = Scan(source);
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (!tokens[i + 1].quoted) continue;
                if (Is(tokens[i], "Fallback")) yield return tokens[i + 1].value;
                if (Is(tokens[i], "UsePass"))
                {
                    var reference = tokens[i + 1].value;
                    int split = reference.LastIndexOf('/');
                    if (split <= 0) throw new InvalidOperationException("Invalid UsePass: " + reference);
                    yield return reference.Substring(0, split);
                }
            }
        }

        public static string Rewrite(string source, string name, IReadOnlyDictionary<string, string> names)
        {
            var tokens = Scan(source);
            var edits = new List<Edit>();
            int shader = tokens.FindIndex(t => Is(t, "Shader"));
            if (shader < 0 || !tokens[shader + 1].quoted) throw new InvalidOperationException("Missing shader name.");
            edits.Add(new Edit(tokens[shader + 1].start, tokens[shader + 1].end, Quote(name)));
            int properties = tokens.FindIndex(t => Is(t, "Properties"));
            string[] propertyNames = { "_Stencil", "_StencilComp", "_StencilOp", "_StencilReadMask", "_StencilWriteMask", "_ColorMask" };
            string[] labels = { "Stencil Reference", "Stencil Comparison", "Stencil Pass", "Stencil Read Mask", "Stencil Write Mask", "Color Mask" };
            int nativeColorMask = 15;
            int colorCommand = -1;
            for (int i = 0; i + 1 < tokens.Count; i++)
                if (Is(tokens[i], "ColorMask") && (Is(tokens[i + 1], "RGB") || Is(tokens[i + 1], "RGBA"))) { colorCommand = i; break; }
            if (colorCommand >= 0 && colorCommand + 1 < tokens.Count)
            {
                string value = tokens[colorCommand + 1].value.ToUpperInvariant();
                if (value.All(c => "RGBA".Contains(c)) && value.Length > 0)
                {
                    nativeColorMask = 0;
                    foreach (char c in value) nativeColorMask |= c == 'R' ? 8 : c == 'G' ? 4 : c == 'B' ? 2 : 1;
                }
            }
            int[] defaults = { 0, 8, 0, 255, 255, nativeColorMask };
            var declarations = new StringBuilder();
            int propOpen = properties < 0 ? -1 : properties + 1;
            int propClose = propOpen < 0 ? -1 : Close(tokens, propOpen);
            for (int i = 0; i < propertyNames.Length; i++)
            {
                bool exists = propOpen >= 0 && tokens.Skip(propOpen + 1).Take(propClose - propOpen - 1).Any(t => Is(t, propertyNames[i]));
                if (!exists) declarations.Append("\n        [HideInInspector] ").Append(propertyNames[i]).Append(" (\"").Append(labels[i]).Append("\", Float) = ").Append(defaults[i]);
            }
            if (propOpen >= 0) edits.Add(new Edit(tokens[propOpen].end, tokens[propOpen].end, declarations + "\n"));
            else
            {
                int open = shader + 2;
                if (!Is(tokens[open], "{")) throw new InvalidOperationException("Missing shader body.");
                edits.Add(new Edit(tokens[open].end, tokens[open].end, "\n    Properties {" + declarations + "\n    }\n"));
            }
            int subshaderCount = 0;
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                var token = tokens[i];
                if (Is(token, "SubShader"))
                {
                    subshaderCount++;
                    int open = i + 1, close = Close(tokens, open), depth = tokens[open].depth + 1;
                    string insert = "\n        " + StencilState + "\n        ColorMask [_ColorMask]\n";
                    bool hasStencil = tokens.Skip(open + 1).Take(close - open - 1).Any(t => t.depth == depth && Is(t, "Stencil"));
                    if (hasStencil) insert = "\n        ColorMask [_ColorMask]\n";
                    int tags = tokens.FindIndex(open + 1, close - open - 1, t => t.depth == depth && Is(t, "Tags"));
                    if (tags >= 0) edits.Add(new Edit(tokens[tags + 1].end, tokens[tags + 1].end, " \"UIEffectsHPStencil\" = \"1\" "));
                    else insert += "        Tags { \"UIEffectsHPStencil\" = \"1\" }\n";
                    edits.Add(new Edit(tokens[open].end, tokens[open].end, insert));
                }
                else if (Is(token, "Stencil"))
                {
                    int close = Close(tokens, i + 1);
                    edits.Add(new Edit(token.start, tokens[close].end, StencilState));
                    i = close;
                }
                else if (Is(token, "ColorMask"))
                {
                    int last = i + 1;
                    // Unity's depth passes write no color or a single red depth channel.
                    // Neither state defines the color channels of the forward UI pass.
                    if (tokens[last].value == "0" || Is(tokens[last], "R")) continue;
                    if (tokens[last].value == "[")
                    {
                        while (last < tokens.Count && tokens[last].value != "]") last++;
                        if (last == tokens.Count) throw new InvalidOperationException("Invalid ColorMask property.");
                    }
                    edits.Add(new Edit(token.start, tokens[last].end, "ColorMask [_ColorMask]"));
                    i = last;
                }
                else if ((Is(token, "Fallback") || Is(token, "UsePass")) && tokens[i + 1].quoted)
                {
                    string reference = tokens[i + 1].value, suffix = "";
                    if (Is(token, "UsePass"))
                    {
                        int slash = reference.LastIndexOf('/'); suffix = reference.Substring(slash); reference = reference.Substring(0, slash);
                    }
                    if (names.TryGetValue(reference, out var mapped)) edits.Add(new Edit(tokens[i + 1].start, tokens[i + 1].end, Quote(mapped + suffix)));
                    else if (Is(token, "Fallback") && reference == "Hidden/Universal Render Pipeline/FallbackError")
                        edits.Add(new Edit(tokens[i + 1].start, tokens[i + 1].end, "Off"));
                    else throw new InvalidOperationException("Unconverted shader dependency: " + reference);
                }
            }
            if (subshaderCount == 0) throw new InvalidOperationException("No SubShader found.");
            var output = new StringBuilder(source);
            int boundary = source.Length;
            foreach (var edit in edits.OrderByDescending(e => e.start).ThenByDescending(e => e.end))
            {
                if (edit.end > boundary) throw new InvalidOperationException("Overlapping ShaderLab edits.");
                output.Remove(edit.start, edit.end - edit.start).Insert(edit.start, edit.text);
                boundary = edit.start;
            }
            return "// Generated by UI Effects HP. ShaderLab stencil adaptation; original shader code follows.\n" + output;
        }

        private static string Quote(string value)
        {
            if (value.IndexOfAny(new[] { '\"', '\r', '\n', '\\' }) >= 0) throw new ArgumentException("Invalid shader name.");
            return "\"" + value + "\"";
        }
        private static int Close(List<Token> tokens, int open)
        {
            if (open >= tokens.Count || !Is(tokens[open], "{")) throw new InvalidOperationException("Expected ShaderLab block.");
            for (int i = open + 1; i < tokens.Count; i++)
                if (Is(tokens[i], "}") && tokens[i].depth == tokens[open].depth) return i;
            throw new InvalidOperationException("Unclosed ShaderLab block.");
        }
        private static List<Token> Scan(string source)
        {
            var result = new List<Token>(); int depth = 0;
            for (int p = 0; p < source.Length;)
            {
                if (char.IsWhiteSpace(source[p])) { p++; continue; }
                if (p + 1 < source.Length && source[p] == '/' && source[p + 1] == '/')
                { while (p < source.Length && source[p] != '\n') p++; continue; }
                if (p + 1 < source.Length && source[p] == '/' && source[p + 1] == '*')
                {
                    int end = source.IndexOf("*/", p + 2, StringComparison.Ordinal);
                    if (end < 0) throw new InvalidOperationException("Unclosed shader comment.");
                    p = end + 2; continue;
                }
                int start = p; bool quoted = source[p] == '"';
                if (quoted)
                {
                    p++;
                    while (p < source.Length && source[p] != '"') { if (source[p] == '\\') p++; p++; }
                    if (p >= source.Length) throw new InvalidOperationException("Unclosed shader string.");
                    p++;
                }
                else if (char.IsLetterOrDigit(source[p]) || source[p] == '_')
                    while (p < source.Length && (char.IsLetterOrDigit(source[p]) || source[p] == '_')) p++;
                else p++;
                string value = source.Substring(start + (quoted ? 1 : 0), p - start - (quoted ? 2 : 0));
                if (!quoted && (value == "CGPROGRAM" || value == "CGINCLUDE" || value == "HLSLPROGRAM" || value == "HLSLINCLUDE" || value == "GLSLPROGRAM"))
                {
                    string endWord = value.StartsWith("CG", StringComparison.Ordinal) ? "ENDCG" : value.StartsWith("HLSL", StringComparison.Ordinal) ? "ENDHLSL" : "ENDGLSL";
                    var end = Regex.Match(source.Substring(p), @"(?m)^\s*" + endWord + @"\b");
                    if (!end.Success) throw new InvalidOperationException("Unclosed shader program.");
                    p += end.Index + end.Length; continue;
                }
                if (!quoted && value == "}") depth--;
                result.Add(new Token { value = value, start = start, end = p, depth = depth, quoted = quoted });
                if (!quoted && value == "{") depth++;
                if (depth < 0) throw new InvalidOperationException("Unexpected ShaderLab closing brace.");
            }
            if (depth != 0) throw new InvalidOperationException("Unclosed ShaderLab body.");
            return result;
        }
    }
}
