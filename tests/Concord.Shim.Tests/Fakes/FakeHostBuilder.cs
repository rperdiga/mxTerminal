// tests/Concord.Shim.Tests/Fakes/FakeHostBuilder.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Concord.Shim.Tests.Fakes;

/// <summary>
/// Roslyn-emit utility for in-test fake-host DLLs.
///
/// SAFETY NOTE: All Emit* methods produce DLLs with assembly name
/// "FakeHost" by default. Tests share that name across instances but
/// stay isolated by emitting to per-test Guid-named temp directories
/// — ConcordHostLoadContext loads via LoadFromAssemblyPath (path-keyed,
/// not name-keyed), so each test sees its own copy. Callers that
/// switch to LoadFromAssemblyName MUST pass a unique assemblyName per
/// test to avoid resolving a previous test's already-loaded copy.
/// </summary>
internal static class FakeHostBuilder
{
    /// <summary>
    /// Resolves the .NET runtime directory used to source baseline metadata
    /// references (System.Runtime.dll, netstandard.dll, etc.) for Roslyn
    /// in-memory compilation. Centralised so the four Emit* helpers don't
    /// each repeat the suppression-free guard.
    /// </summary>
    private static string RuntimeDir =>
        Path.GetDirectoryName(typeof(object).Assembly.Location)
        ?? throw new InvalidOperationException(
            "Cannot locate .NET runtime directory; single-file deployment unsupported in test context.");

    private static List<MetadataReference> BaselineRefs() => new()
    {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        // Use the actual ExtensionsAPI assembly, not the sentinel's test-assembly location.
        MetadataReference.CreateFromFile(
            typeof(Mendix.StudioPro.ExtensionsAPI.UI.DockablePane.DockablePaneExtension).Assembly.Location),
        MetadataReference.CreateFromFile(Path.Combine(RuntimeDir, "System.Runtime.dll")),
        MetadataReference.CreateFromFile(Path.Combine(RuntimeDir, "netstandard.dll")),
    };

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

        return Compile(outputDir, assemblyName, source, BaselineRefs());
    }

    /// <summary>
    /// Emits FakeHost.dll containing both FakeDockablePane (existing) AND
    /// a FakeHostEntry type with a parameterless ctor that, on first
    /// instantiation, writes "fake-entry-ran" to the side-channel file.
    /// Used by HostKickstart tests to assert the bootstrap chain fires
    /// exactly once.
    /// </summary>
    public static string EmitFakeHostWithEntry(string outputDir, string sideChannelPath, string assemblyName = "FakeHost")
    {
        var escapedPath = sideChannelPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var source = $$"""
            using System;
            using System.IO;
            using System.Threading;
            using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;

            namespace FakeHost
            {
                public class FakeDockablePane : DockablePaneExtension
                {
                    public override string Id => "FakeHost";
                    public string Marker => "fake-host-marker";
                    public override DockablePaneViewModelBase Open() => null!;
                }

                public class FakeHostEntry
                {
                    private static int _ran;
                    public FakeHostEntry()
                    {
                        if (Interlocked.Exchange(ref _ran, 1) != 0) return;
                        File.AppendAllText("{{escapedPath}}", "fake-entry-ran\n");
                    }
                }
            }
            """;

        return Compile(outputDir, assemblyName, source, BaselineRefs());
    }

    /// <summary>
    /// Emits FakeHost.dll containing FakeHostEntry (parameterless) AND
    /// FakePane : DockablePaneExtension whose ctor matches the production
    /// 9-arg pane ctor shape. Only the first 2 services
    /// (ILocalRunConfigurationsService, IExtensionFileService) are stored
    /// and exposed via marker properties; the remaining 7 ctor parameters
    /// use <c>object?</c> so the Roslyn-emitted source doesn't have to
    /// import every service interface namespace. <see cref="Activator.CreateInstance"/>
    /// boxes references; the 7 nulls go in cleanly.
    /// </summary>
    public static string EmitFakeHostWithPaneAndEntry(string outputDir, string sideChannelPath, string assemblyName = "FakeHost")
    {
        var escapedPath = sideChannelPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var source = $$"""
            using System;
            using System.IO;
            using System.Threading;
            using Mendix.StudioPro.ExtensionsAPI.Services;
            using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
            using Mendix.StudioPro.ExtensionsAPI.UI.Services;

            namespace FakeHost
            {
                public class FakePane : DockablePaneExtension
                {
                    private readonly ILocalRunConfigurationsService? _localRunConfigs;
                    private readonly IExtensionFileService? _fileService;

                    public FakePane(
                        ILocalRunConfigurationsService localRunConfigs,
                        IExtensionFileService fileService,
                        object? pageGenerationService,
                        object? navigationManagerService,
                        object? microflowService,
                        object? nameValidationService,
                        object? untypedModelAccessService,
                        object? microflowExpressionService,
                        object? versionControlService)
                    {
                        _localRunConfigs = localRunConfigs;
                        _fileService = fileService;
                    }

                    public override string Id => "FakeHost";
                    public string LocalRunConfigsMarker => _localRunConfigs?.GetType().Name ?? "";
                    public string FileServiceMarker => _fileService?.GetType().Name ?? "";
                    public override DockablePaneViewModelBase Open() => null!;
                }

                public class FakeHostEntry
                {
                    private static int _ran;
                    public FakeHostEntry()
                    {
                        if (Interlocked.Exchange(ref _ran, 1) != 0) return;
                        File.AppendAllText("{{escapedPath}}", "fake-entry-ran\n");
                    }
                }
            }
            """;

        return Compile(outputDir, assemblyName, source, BaselineRefs());
    }

    /// <summary>
    /// Emits FakeHost.dll containing FakeHostEntry (parameterless) AND
    /// FakeMenu : MenuExtension whose ctor takes <see cref="IDockingWindowService"/>
    /// and whose <c>GetMenus()</c> yields a single
    /// <c>MenuViewModel("fake-caption", () => { })</c>.
    /// </summary>
    public static string EmitFakeHostWithMenuAndEntry(string outputDir, string sideChannelPath, string assemblyName = "FakeHost")
    {
        var escapedPath = sideChannelPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.IO;
            using System.Threading;
            using Mendix.StudioPro.ExtensionsAPI.UI.Menu;
            using Mendix.StudioPro.ExtensionsAPI.UI.Services;

            namespace FakeHost
            {
                public class FakeMenu : MenuExtension
                {
                    private readonly IDockingWindowService _docking;
                    public FakeMenu(IDockingWindowService docking) => _docking = docking;
                    public override IEnumerable<MenuViewModel> GetMenus()
                    {
                        yield return new MenuViewModel("fake-caption", () => { });
                    }
                }

                public class FakeHostEntry
                {
                    private static int _ran;
                    public FakeHostEntry()
                    {
                        if (Interlocked.Exchange(ref _ran, 1) != 0) return;
                        File.AppendAllText("{{escapedPath}}", "fake-entry-ran\n");
                    }
                }
            }
            """;

        return Compile(outputDir, assemblyName, source, BaselineRefs());
    }

    /// <summary>
    /// Emits FakeHost.dll containing FakeHostEntry (parameterless) AND
    /// FakeWebServer : WebServerExtension whose ctor takes
    /// <see cref="IExtensionFileService"/> and whose
    /// <c>InitializeWebServer(webServer)</c> calls
    /// <c>webServer.AddRoute("fake", ...)</c>.
    /// </summary>
    public static string EmitFakeHostWithWebServerAndEntry(string outputDir, string sideChannelPath, string assemblyName = "FakeHost")
    {
        var escapedPath = sideChannelPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var source = $$"""
            using System;
            using System.IO;
            using System.Net;
            using System.Threading;
            using System.Threading.Tasks;
            using Mendix.StudioPro.ExtensionsAPI.Services;
            using Mendix.StudioPro.ExtensionsAPI.UI.WebServer;

            namespace FakeHost
            {
                public class FakeWebServer : WebServerExtension
                {
                    private readonly IExtensionFileService _fileService;
                    public FakeWebServer(IExtensionFileService fileService) => _fileService = fileService;
                    public override void InitializeWebServer(IWebServer webServer)
                    {
                        webServer.AddRoute("fake", (HttpListenerRequest req, HttpListenerResponse resp, CancellationToken ct) => Task.CompletedTask);
                    }
                }

                public class FakeHostEntry
                {
                    private static int _ran;
                    public FakeHostEntry()
                    {
                        if (Interlocked.Exchange(ref _ran, 1) != 0) return;
                        File.AppendAllText("{{escapedPath}}", "fake-entry-ran\n");
                    }
                }
            }
            """;

        // HttpListenerRequest/Response live in System.Net.HttpListener.dll on .NET 8.
        var refs = BaselineRefs();
        refs.Add(MetadataReference.CreateFromFile(Path.Combine(RuntimeDir, "System.Net.HttpListener.dll")));
        return Compile(outputDir, assemblyName, source, refs);
    }

    private static string Compile(string outputDir, string assemblyName, string source, List<MetadataReference> refs)
    {
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
                $"Fake host '{assemblyName}' compile failed:\n" + string.Join("\n", result.Diagnostics));
        return dllPath;
    }
}
