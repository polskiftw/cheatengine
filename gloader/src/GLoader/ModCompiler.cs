using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GLoader
{
    internal static class ModCompiler
    {
        public static Assembly Compile(
            ModSource mod,
            IReadOnlyList<MetadataReference> references,
            bool isServerTarget)
        {
            var symbols = isServerTarget
                ? new[] { "GLOADER", "GLOADER_SERVER" }
                : new[] { "GLOADER", "GLOADER_CLIENT" };

            var parseOptions = new CSharpParseOptions(
                languageVersion: LanguageVersion.Latest,
                documentationMode: DocumentationMode.None,
                kind: SourceCodeKind.Regular,
                preprocessorSymbols: symbols);

            var syntaxTrees = mod.SourceFiles
                .Select(path => CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path),
                    parseOptions,
                    path))
                .ToArray();

            var assemblyName = "gloader.mod." + SanitizeAssemblyName(mod.Id);

            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees,
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: true,
                    deterministic: true));

            using (var peStream = new MemoryStream())
            {
                var emitResult = compilation.Emit(peStream);

                if (!emitResult.Success)
                {
                    var diagnostics = emitResult.Diagnostics
                        .Where(diagnostic =>
                            diagnostic.Severity == DiagnosticSeverity.Error ||
                            diagnostic.IsWarningAsError)
                        .OrderBy(diagnostic => diagnostic.Location.SourceTree?.FilePath)
                        .ThenBy(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line)
                        .Select(FormatDiagnostic)
                        .ToArray();

                    throw new ModCompilationException(
                        mod.DisplayName,
                        diagnostics.Length == 0
                            ? "Compilation failed with no compiler diagnostics."
                            : string.Join(Environment.NewLine, diagnostics));
                }

                return Assembly.Load(peStream.ToArray());
            }
        }

        private static string FormatDiagnostic(Diagnostic diagnostic)
        {
            var span = diagnostic.Location.GetLineSpan();
            if (!span.IsValid || string.IsNullOrWhiteSpace(span.Path))
            {
                return diagnostic.ToString();
            }

            return string.Format(
                "{0}({1},{2}): {3} {4}: {5}",
                span.Path,
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1,
                diagnostic.Severity.ToString().ToLowerInvariant(),
                diagnostic.Id,
                diagnostic.GetMessage());
        }

        private static string SanitizeAssemblyName(string value)
        {
            var characters = value
                .Select(character =>
                    char.IsLetterOrDigit(character) || character == '.' || character == '_'
                        ? character
                        : '_')
                .ToArray();

            return new string(characters);
        }
    }

    internal sealed class ModCompilationException : Exception
    {
        public ModCompilationException(string modName, string diagnostics)
            : base("Mod '" + modName + "' did not compile:" + Environment.NewLine + diagnostics)
        {
        }
    }
}
