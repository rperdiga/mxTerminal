// tests/Concord.Shim.Tests/Fakes/FakeHostBuilder.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Concord.Shim.Tests.Fakes;

internal static class FakeHostBuilder
{
    /// <summary>
    /// Emits FakeHost.dll to <paramref name="outputDir"/> with a single type
    /// FakeHost.FakeDockablePane : DockablePaneExtension and a public string
    /// property Marker = "fake-host-marker".
    /// </summary>
    public static string EmitFakeHost(string outputDir, string assemblyName = "FakeHost")
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;

            namespace FakeHost
            {
                public class FakeDockablePane : DockablePaneExtension
                {
                    public override string Id => "FakeHost";
                    public string Marker => "fake-host-marker";
                    public override DockablePaneViewModelBase Open() => null!;
                }
            }
            """;

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException(
                "Cannot locate .NET runtime directory; single-file deployment unsupported in test context.");

        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            // Use the actual ExtensionsAPI assembly, not the sentinel's test-assembly location.
            MetadataReference.CreateFromFile(
                typeof(Mendix.StudioPro.ExtensionsAPI.UI.DockablePane.DockablePaneExtension).Assembly.Location),
            // Add runtime-required refs:
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Directory.CreateDirectory(outputDir);
        var dllPath = Path.Combine(outputDir, assemblyName + ".dll");
        var result = compilation.Emit(dllPath);
        if (!result.Success)
            throw new InvalidOperationException(
                "Fake host compile failed:\n" + string.Join("\n", result.Diagnostics));
        return dllPath;
    }

}
