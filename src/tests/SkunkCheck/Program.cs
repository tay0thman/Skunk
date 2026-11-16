using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace SkunkCheck
{
    /// <summary>Entry point: sets up assembly resolution for Rhino 9 before any Rhino type is touched.</summary>
    internal static class Program
    {
        static readonly string RhinoSystem =
            System.Environment.GetEnvironmentVariable("RHINO_SYSTEM") ?? @"C:\Program Files\Rhino 9\System";

        static Program()
        {
            System.Environment.SetEnvironmentVariable("PATH", RhinoSystem + ";" + System.Environment.GetEnvironmentVariable("PATH"));
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                var plugins = Path.Combine(Path.GetDirectoryName(RhinoSystem), "Plug-ins");
                foreach (var dir in new[] { RhinoSystem, Path.Combine(RhinoSystem, "netcore"), Path.Combine(plugins, "Grasshopper"), Path.Combine(plugins, "RhinoCode") })
                {
                    var p = Path.Combine(dir, name.Name + ".dll");
                    if (File.Exists(p))
                        return ctx.LoadFromAssemblyPath(p);
                }
                return null;
            };
        }

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return 1;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Run(string[] args) => Tests.RunAll(args);
    }
}
