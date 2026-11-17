using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using Skunk.Core;
using Skunk.Elements;
using Skunk.Service;
using Skunk.Types;
using Skunk.Units;

namespace Skunk.Commands
{
    /// <summary>Shared command plumbing.</summary>
    internal static class Kit
    {
        /// <summary>Run a command body with the service started and errors reported on the command line.</summary>
        public static Result Run(object doc, object mode, string name, Func<RhinoDoc, RunMode, Result> body)
        {
            var d = doc as RhinoDoc ?? RhinoDoc.ActiveDoc;
            var m = mode is RunMode rm ? rm : RunMode.Interactive;
            if (d == null)
                return Result.Failure;
            try
            {
                SkunkService.Ensure();
                ToolbarAfterUpdate();
                return body(d, m);
            }
            catch (OperationCanceledException)
            {
                return Result.Cancel;
            }
            catch (Exception ex)
            {
                Log.Error(name + " failed", ex);
                return Result.Failure;
            }
        }

        /// <summary>
        /// Once per session, with the first Skunk command: the toolbar kept in the package folder, where package updates
        /// leave it (and opened when no toolbar file holds it).
        /// </summary>
        static void ToolbarAfterUpdate()
        {
            UI.SkunkToolbar.CheckOnce();
            // the Skunk Attributes panel: registered once a session, so Rhino can dock it and bring it back where it was
            UI.SkunkAttributesPanel.Register();
        }

        /// <summary>The active view's CPlane moved to a point: the plane annotations are drawn in.</summary>
        public static Plane ViewPlane(RhinoDoc doc, Point3d origin)
        {
            var vp = doc.Views.ActiveView?.ActiveViewport;
            var plane = vp != null ? vp.ConstructionPlane() : Plane.WorldXY;
            plane.Origin = origin;
            return plane;
        }

        public const ObjectType HostTypes = ObjectType.Surface | ObjectType.PolysrfFilter | ObjectType.Brep | ObjectType.Mesh |
                                            ObjectType.Curve | ObjectType.Point | ObjectType.Extrusion | ObjectType.SubD;

        public static bool HostFilter(RhinoObject obj, GeometryBase geom, ComponentIndex ci) => Anchors.CanHost(obj);

        /// <summary>Command-line option values must be single words: "Target Filled (Project)" -> Target_Filled_Project.</summary>
        public static string OptionValue(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text ?? "")
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                    sb.Append('_');
            }
            var s = sb.ToString().Trim('_');
            if (s.Length == 0 || char.IsDigit(s[0]))
                s = "T" + s;
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }

        public static string[] TypeNames(TypeLibrary lib, ElementKind kind) => lib.Of(kind).Select(t => t.Name).ToArray();

        public static int IndexOf(string[] names, string current)
        {
            int i = Array.FindIndex(names, n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
            return i < 0 ? 0 : i;
        }

        /// <summary>Last used type per category for this session (the Type Selector remembers it).</summary>
        static readonly Dictionary<ElementKind, string> s_lastType = new Dictionary<ElementKind, string>();

        public static string LastType(ElementKind kind) => s_lastType.TryGetValue(kind, out var n) ? n : null;

        public static void SetLastType(ElementKind kind, string name) => s_lastType[kind] = name;

        /// <summary>
        /// Format a model length the way Rhino shows it (Document Properties > Units: unit system, distance
        /// display, precision) - used by the Properties palette, the dialogs and command-line reports.
        /// </summary>
        public static string Length(RhinoDoc doc, double model)
        {
            var style = TypeTable.Resolve(doc, new LengthStyle { Format = LengthFormat.Project, ShowUnitSymbol = DocUnits.IsImperial(doc) });
            return UnitFormat.Length(model * DocUnits.MetersPerUnit(doc), style);
        }

        /// <summary>Choose a level on the command line (or by clicking its line / head).</summary>
        public static LevelInfo PickLevel(RhinoDoc doc, string prompt, Guid current)
        {
            var levels = LevelElement.All(doc);
            if (levels.Count == 0)
            {
                Log.Warn("there are no levels yet - create one with SkLevel");
                return null;
            }
            if (levels.Count == 1)
                return levels[0];
            var go = new GetObject();
            go.SetCommandPrompt(prompt);
            go.GeometryFilter = ObjectType.Curve | ObjectType.Annotation;
            go.SetCustomGeometryFilter((o, g, ci) => ElementIO.KindOf(o) == ElementKind.Level || ElementKinds.IsLevelPart(ElementIO.KindOf(o)));
            var names = levels.Select(l => OptionValue(l.Name)).ToArray();
            int idx = Math.Max(0, levels.FindIndex(l => l.Id == current));
            go.AddOptionList("Level", names, idx);
            go.AcceptNothing(true);
            go.EnablePreSelect(false, true);
            while (true)
            {
                var r = go.Get();
                if (r == GetResult.Option)
                {
                    idx = go.Option().CurrentListOptionIndex;
                    return levels[idx];
                }
                if (r == GetResult.Nothing)
                    return levels[idx];
                if (r != GetResult.Object)
                    return null;
                var o = go.Object(0).Object();
                if (ElementKinds.IsLevelPart(ElementIO.KindOf(o)))
                {
                    var hd = ElementIO.Read<HeadData>(o);
                    o = hd == null ? null : ElementIO.Find(doc, hd.Level);
                }
                var hit = levels.FirstOrDefault(l => l.Id == o?.Id);
                if (hit != null)
                    return hit;
            }
        }

        /// <summary>Run an edit inside an undo record with Skunk's own events suppressed, then process dependents.</summary>
        public static void Edit(RhinoDoc doc, string description, Action<UpdateContext> edit, bool contextChanged = false)
        {
            bool own = !doc.UndoRecordingIsActive;
            uint sn = own ? doc.BeginUndoRecord(description) : 0;
            try
            {
                using (SkunkService.Suppress())
                {
                    var ctx = new UpdateContext(doc);
                    edit(ctx);
                }
                if (contextChanged)
                    SkunkService.Instance?.ContextChanged(doc);
                SkunkService.Instance?.ProcessNow(doc);
            }
            finally
            {
                if (own)
                    doc.EndUndoRecord(sn);
            }
            doc.Views.Redraw();
        }
    }
}
