using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace SkyEditor.SaveEditor.Gui;

/// <summary>
/// Works around an upstream packaging defect: SkyEditor.Core 4.2.10's compiled metadata
/// references "SkyEditor.IO, Version=5.0.8.0", but the SkyEditor.IO 5.0.8 package actually
/// on nuget.org ships a DLL whose real AssemblyVersion is "5.0.0.0". Without this, any net8.0
/// app that constructs an RBSave (or any other *Save type) fails with a FileNotFoundException
/// before any of its own code runs. See SkyEditor.SaveEditor.Cli's copy of this class (and
/// TODO.md) for the full root-cause writeup -- this has to live in each entry assembly, not
/// the SkyEditor.SaveEditor library itself, since the failure happens while CoreCLR is still
/// binding that library's own reference to SkyEditor.Core, before its module initializer (or
/// any of its managed code) gets a chance to run.
/// </summary>
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
