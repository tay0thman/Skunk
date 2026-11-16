using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Rhino;
using Rhino.Commands;

namespace SkunkCheck
{
    /// <summary>
    /// Loads the Script Editor project with Rhino's own project API (what the Script Editor's Build
    /// button runs: Rhino3DProject.Load + Package), then runs small scripts through RhinoCode the way
    /// the published plug-in runs its commands: the shared Skunk.Core.dll must load, and two separate
    /// scripts must see the same Skunk service (one event service per Rhino session).
    /// Adapted from the FileIO RhinoCheck harness. Needs the net10 build and RHINOCHECK-style
    /// start-up flag /netcore-10 (RhinoCode only loads on .NET 10).
    /// </summary>
    internal static class RhprojCheck
    {
        private sealed class TestCommand : Command
        {
            public override string EnglishName => "SkunkRhprojTest";

            protected override Result RunCommand(RhinoDoc doc, RunMode mode) => Result.Success;
        }

        private sealed class PrintProgress<T> : IProgress<T>
        {
            public void Report(T value)
            {
                var msg = value?.GetType().GetProperty("Message")?.GetValue(value);
                var pct = value?.GetType().GetProperty("Progress")?.GetValue(value);
                Console.WriteLine("    build: {0} {1}", pct, msg);
            }
        }

        private static Type T(string name, string assembly) =>
            Type.GetType(name + ", " + assembly) ?? throw new InvalidOperationException("type not found: " + name);

        public static int Run(string rhprojPath, bool package)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Console.WriteLine("UNHANDLED: " + e.ExceptionObject); Console.Out.Flush(); };
            var rhinoCodeId = new Guid("c9cba87a-23ce-4f15-a918-97645c05cde7");
            Console.WriteLine("load RhinoCode plug-in: " + Rhino.PlugIns.PlugIn.LoadPlugIn(rhinoCodeId));
            var swEdit = Stopwatch.StartNew();
            T("RhinoCodePlatform.Rhino3D.Registrar", "RhinoCodePlatform.Rhino3D").GetMethod("StartScriptEditing", new[] { typeof(bool) })
                .Invoke(null, new object[] { false });
            Console.WriteLine("script editing platform started in {0:0.00}s", swEdit.Elapsed.TotalSeconds);

            var projType = T("RhinoCodePlatform.Projects.Rhino3DProject", "RhinoCodePlatform.Projects");
            var storageType = T("Rhino.Runtime.Code.Storage.Local.FileStorage", "Rhino.Runtime.Code");
            dynamic project = Activator.CreateInstance(projType);
            object storage = Activator.CreateInstance(storageType, Path.GetFullPath(rhprojPath));
            projType.GetMethod("Load", new[] { storageType.GetInterfaces().First(i => i.Name == "IStorage") }).Invoke(project, new[] { storage });

            Console.WriteLine("project: {0}  id={1}  host={2}", project.Identity, project.Id, project.Host);
            int codes = 0;
            foreach (dynamic code in (System.Collections.IEnumerable)projType.GetMethod("GetCodes", Type.EmptyTypes).Invoke(project, null))
            {
                codes++;
                Console.WriteLine("  command {0,-22} lang={1}  uri={2}", code.Title, code.LanguageSpec, code.Uri);
            }
            foreach (dynamic res in (System.Collections.IEnumerable)project.Resources)
                Console.WriteLine("  resource {0}  library={1}", res.Uri, res.IsLibrary);
            Console.WriteLine("  toolbar file: {0} (auto={1})", project.Settings.LayoutFile, project.Settings.GenerateLayoutFile);
            Console.WriteLine("  {0} commands", codes);

            if (package)
            {
                var build = projType.GetMethod("CreateBuildTarget", new[] { typeof(int) }).Invoke(null, new object[] { 9 });
                var reportType = T("Rhino.Runtime.Code.ProgressReport", "Rhino.Runtime.Code");
                var progress = Activator.CreateInstance(typeof(PrintProgress<>).MakeGenericType(reportType));
                var sw = Stopwatch.StartNew();
                try
                {
                    projType.GetMethod("Package").Invoke(project, new[] { (object)project.Host, build, progress });
                    Console.WriteLine("  package built in {0:0.0}s", sw.Elapsed.TotalSeconds);
                }
                catch (TargetInvocationException ex)
                {
                    Console.WriteLine("  BUILD FAILED: " + ex.InnerException);
                    return 1;
                }
            }

            var specType = T("Rhino.Runtime.Code.Languages.LanguageSpec", "Rhino.Runtime.Code");
            var csharp = specType.GetProperty("CSharp").GetValue(null);
            var registrar = T("RhinoCodePlatform.Rhino3D.Registrar", "RhinoCodePlatform.Rhino3D");
            var swLang = Stopwatch.StartNew();
            registrar.GetMethod("StartScriptingLanguages", new[] { specType, typeof(bool) }).Invoke(null, new[] { csharp, (object)false });
            Console.WriteLine("  C# language started in {0:0.00}s", swLang.Elapsed.TotalSeconds);

            var serverType = T("RhinoCodePlatform.Rhino3D.Projects.Rhino3DProjectServer", "RhinoCodePlatform.Rhino3D");
            dynamic server = serverType.GetProperty("Instance").GetValue(null);
            var refType = T("Rhino.Runtime.Code.Execution.CompileReference", "Rhino.Runtime.Code");
            var dll = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(rhprojPath)), "shared", "Skunk.Core.dll");
            var compileRef = refType.GetMethod("FromPath", new[] { typeof(string) }).Invoke(null, new object[] { dll });
            var doc = RhinoDoc.CreateHeadless(null);
            var cmd = new TestCommand();
            var rc = T("Rhino.Runtime.Code.RhinoCode", "Rhino.Runtime.Code");
            var istorage = storageType.GetInterfaces().First(i => i.Name == "IStorage");
            int failures = 0;

            // two separate scripts, as two different commands would be: both must reach ONE service
            string Probe(string label)
            {
                var path = Path.Combine(Path.GetTempPath(), "Skunk_probe_" + label + ".cs");
                File.WriteAllLines(path, new[]
                {
                    "using Skunk;",
                    "using Skunk.Service;",
                    "var s = SkunkService.Ensure();",
                    "var a = typeof(SkunkEntry).Assembly;",
                    "Rhino.RhinoApp.WriteLine(\"probe " + label + ": \" + a.GetName().Name + \" \" + a.GetName().Version + \" service=\" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(s) + \" alc=\" + System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(a)?.Name + \" doc=\" + (__rhino_doc__ != null));",
                    "System.Console.WriteLine(\"    PROBE " + label + " service=\" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(s) + \" alc=\" + System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(a)?.Name + \" location=\" + a.Location);",
                    "__rhino_command_result__ = Rhino.Commands.Result.Nothing;",
                });
                return path;
            }
            foreach (var label in new[] { "A", "B" })
            {
                var sw = Stopwatch.StartNew();
                dynamic code = rc.GetMethod("CreateCode", new[] { istorage }).Invoke(null, new[] { Activator.CreateInstance(storageType, Probe(label)) });
                code.References.Add((dynamic)compileRef);
                dynamic ctx = server.CreateContext(cmd, doc, RunMode.Scripted, null);
                ctx.RecordDocumentUndo = false;
                Console.WriteLine("  runtime probe " + label + ": running");
                Console.Out.Flush();
                try
                {
                    code.Run(ctx);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  PROBE FAILED: " + ex);
                    return 1;
                }
                var outputs = ctx.Outputs;
                var tryGet = ((object)outputs).GetType().GetMethods().First(m => m.Name == "TryGet" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                var args = new object[] { "__rhino_command_result__", null };
                tryGet.MakeGenericMethod(typeof(Result)).Invoke(outputs, args);
                Console.WriteLine("  runtime probe {0} ran in {1:0.000}s -> __rhino_command_result__ = {2}", label, sw.Elapsed.TotalSeconds, args[1]);
                if (!Equals(args[1], Result.Nothing))
                    failures++;
            }
            doc.Dispose();
            return failures > 0 ? 1 : 0;
        }
    }
}
