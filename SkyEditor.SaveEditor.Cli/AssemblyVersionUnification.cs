using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace SkyEditor.SaveEditor.Cli;

/// <summary>
/// Works around an upstream packaging defect: SkyEditor.Core 4.2.10's compiled metadata
/// references "SkyEditor.IO, Version=5.0.8.0", but the SkyEditor.IO 5.0.8 package actually
/// on nuget.org ships a DLL whose real AssemblyVersion is "5.0.0.0" -- the package's NuGet
/// version and its assembly's AssemblyVersion diverged upstream. On .NET Framework this is
/// normally papered over by an app.config bindingRedirect; .NET Core/.NET 5+ has no
/// equivalent, so without this, any net8.0 app that so much as constructs an RBSave (or any
/// other *Save type -- they all derive from SkyEditor.Core.IO.GenericFile via BitBlockFile)
/// fails with a FileNotFoundException before any of its own code runs.
/// </summary>
/// <remarks>
/// This must live in the CLI's own entry assembly, not the SkyEditor.SaveEditor library:
/// the failure happens while CoreCLR is still binding SkyEditor.SaveEditor's own
/// AssemblyRef to SkyEditor.Core (and from there to SkyEditor.IO), which happens before
/// that assembly's module initializer -- or any of its managed code -- gets to run. Only a
/// module initializer in the outermost (entry) assembly is guaranteed to run early enough.
/// Any future net8.0 consumer of SkyEditor.SaveEditor (e.g. a GUI) needs the same fix.
/// </remarks>
internal static class AssemblyVersionUnification
{
    [ModuleInitializer]
    public static void Initialize()
    {
        AssemblyLoadContext.Default.Resolving += (context, requested) =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, requested.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }
}
