using System;
using System.Linq;
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

namespace Skunk.Commands
{
    /// <summary>Architecture > Datum: levels, and objects constrained to them.</summary>
    internal static class DatumCommands
    {
        /// <summary>SkLevel: pick (or type) an elevation; the level gets the previous level's extents or the model's.</summary>
        public static Result Level(RhinoDoc doc, RunMode mode)
        {
            var lib = TypeTable.Get(doc);
            var names = Kit.TypeNames(lib, ElementKind.Level);
            int typeIndex = Kit.IndexOf(names, Kit.LastType(ElementKind.Level));
            string[] extentsModes = { "Previous", "Model", "Pick" };
            int extents = LevelElement.All(doc).Count > 0 ? 0 : 1;
            string name = null;
            // which ends of the level line get a head (Show head at End 1 / End 2); -1 = the type's default
            string[] headModes = { "TypeDefault", "Right", "Left", "Both", "None" };
            int heads = 0;

            var gp = new GetPoint();
            gp.AcceptNumber(true, false);
            gp.AcceptNothing(false);
            Point3d? picked = null;
            double? typed = null;
            while (true)
            {
                gp.ClearCommandOptions();
                gp.SetCommandPrompt("Level elevation (pick a point in an elevation view, or type the elevation)");
                int optType = gp.AddOptionList("Type", names.Select(Kit.OptionValue), typeIndex);
                int optExtents = gp.AddOptionList("Extents", extentsModes, extents);
                int optHeads = gp.AddOptionList("Heads", headModes, heads);
                int optName = gp.AddOption("Name", Kit.OptionValue(name ?? LevelElement.NextName(doc)));
                var r = gp.Get();
                if (r == GetResult.Option)
                {
                    int i = gp.OptionIndex();
                    if (i == optType) typeIndex = gp.Option().CurrentListOptionIndex;
                    else if (i == optExtents) extents = gp.Option().CurrentListOptionIndex;
                    else if (i == optHeads) heads = gp.Option().CurrentListOptionIndex;
                    else if (i == optName)
                    {
                        var gs = new GetString();
                        gs.SetCommandPrompt("Level name");
                        gs.SetDefaultString(name ?? LevelElement.NextName(doc));
                        if (gs.GetLiteralString() == GetResult.String)
                            name = gs.StringResult().Trim();
                    }
                    continue;
                }
                if (r == GetResult.Number)
                {
                    typed = gp.Number();
                    break;
                }
                if (r == GetResult.Point)
                {
                    picked = gp.Point();
                    break;
                }
                return Result.Cancel;
            }

            var type = lib.Find<LevelType>(names[typeIndex]);
            Kit.SetLastType(ElementKind.Level, type.Name);
            double z;
            var ctx = new UpdateContext(doc);
            if (typed.HasValue)
                z = LevelElement.WorldZ(ctx, type, typed.Value);
            else
                z = picked.Value.Z;

            Polyline footprint;
            if (extentsModes[extents] == "Pick")
            {
                var g1 = new GetPoint();
                g1.SetCommandPrompt("First corner of the level extents");
                if (g1.Get() != GetResult.Point)
                    return Result.Cancel;
                var c1 = g1.Point();
                var g2 = new GetPoint();
                g2.SetCommandPrompt("Opposite corner");
                g2.SetBasePoint(c1, true);
                g2.DrawLineFromPoint(c1, true);
                if (g2.Get() != GetResult.Point)
                    return Result.Cancel;
                var c2 = g2.Point();
                var plane = ctx.Site.Project;
                plane.ClosestParameter(c1, out double x1, out double y1);
                plane.ClosestParameter(c2, out double x2, out double y2);
                var pts = new[]
                {
                    plane.PointAt(Math.Min(x1, x2), Math.Min(y1, y2)), plane.PointAt(Math.Max(x1, x2), Math.Min(y1, y2)),
                    plane.PointAt(Math.Max(x1, x2), Math.Max(y1, y2)), plane.PointAt(Math.Min(x1, x2), Math.Max(y1, y2)),
                    plane.PointAt(Math.Min(x1, x2), Math.Min(y1, y2)),
                };
                footprint = new Polyline(pts.Select(p => new Point3d(p.X, p.Y, z)));
            }
            else if (extentsModes[extents] == "Model")
            {
                var all = LevelElement.All(doc);
                footprint = all.Count > 0 && extents == 0 ? LevelElement.DefaultFootprint(doc, z) : ModelFootprint(doc, z);
            }
            else
                footprint = LevelElement.DefaultFootprint(doc, z);

            Guid id;
            using (SkunkService.Suppress())
            {
                id = LevelElement.Create(doc, type.Name, name, z, footprint);
                if (id != Guid.Empty && heads > 0)
                    LevelElement.SetHeadEnds(new UpdateContext(doc), id, (LevelElement.HeadEnds)(heads - 1));
            }
            if (id == Guid.Empty)
            {
                Log.Error("the level could not be created");
                return Result.Failure;
            }
            var info = LevelElement.Info(doc, doc.Objects.FindId(id));
            Log.Info($"{info.Name} created at {LevelElement.ElevationText(new UpdateContext(doc), type, z)}.");
            doc.Views.Redraw();
            return Result.Success;
        }

        static Polyline ModelFootprint(RhinoDoc doc, double z)
        {
            // ignore existing levels: extents of the model objects only
            var levels = LevelElement.All(doc);
            if (levels.Count == 0)
                return LevelElement.DefaultFootprint(doc, z);
            var bb = BoundingBox.Empty;
            foreach (var o in doc.Objects.GetObjectList(new ObjectEnumeratorSettings { NormalObjects = true, LockedObjects = true }))
                if (ElementIO.KindOf(o) == ElementKind.None)
                    bb.Union(o.Geometry.GetBoundingBox(false));
            if (!bb.IsValid)
                return LevelElement.DefaultFootprint(doc, z);
            double m = Math.Max(bb.Diagonal.X, bb.Diagonal.Y) * DocStore.Settings(doc).ExtentsMargin;
            return new Polyline(new[]
            {
                new Point3d(bb.Min.X - m, bb.Min.Y - m, z), new Point3d(bb.Max.X + m, bb.Min.Y - m, z),
                new Point3d(bb.Max.X + m, bb.Max.Y + m, z), new Point3d(bb.Min.X - m, bb.Max.Y + m, z), new Point3d(bb.Min.X - m, bb.Min.Y - m, z),
            });
        }

        /// <summary>SkGoToLevel: set the CPlane to a level; optionally a plan view with a view range.</summary>
        public static Result GoToLevel(RhinoDoc doc, RunMode mode)
        {
            var view = doc.Views.ActiveView;
            if (view == null)
                return Result.Failure;
            var planView = new OptionToggle(true, "No", "Yes");
            var viewRange = new OptionToggle(true, "No", "Yes");
            var levels = LevelElement.All(doc);
            if (levels.Count == 0)
            {
                Log.Warn("there are no levels yet - create one with SkLevel");
                return Result.Nothing;
            }
            var go = new GetOption();
            go.SetCommandPrompt("Level to go to");
            int current = 0;
            var cp = view.ActiveViewport.ConstructionPlane();
            for (int i = 0; i < levels.Count; i++)
                if (Math.Abs(levels[i].Z - cp.Origin.Z) < doc.ModelAbsoluteTolerance)
                    current = i;
            LevelInfo level = null;
            while (true)
            {
                go.ClearCommandOptions();
                int optLevel = go.AddOptionList("Level", levels.Select(l => Kit.OptionValue(l.Name)), current);
                go.AddOptionToggle("PlanView", ref planView);
                go.AddOptionToggle("ViewRange", ref viewRange);
                go.AcceptNothing(true);
                var r = go.Get();
                if (r == GetResult.Option)
                {
                    if (go.OptionIndex() == optLevel)
                    {
                        current = go.Option().CurrentListOptionIndex;
                        level = levels[current];
                        break;
                    }
                    continue;
                }
                if (r == GetResult.Nothing)
                {
                    level = levels[current];
                    break;
                }
                return Result.Cancel;
            }

            var ctx = new UpdateContext(doc);
            var plane = ctx.Site.Project;
            plane.Origin = new Point3d(plane.Origin.X, plane.Origin.Y, level.Z);
            var vp = view.ActiveViewport;
            vp.SetConstructionPlane(plane);
            if (planView.CurrentValue)
            {
                vp.ChangeToParallelProjection(true);
                var bb = new BoundingBox(level.Footprint);
                var target = bb.Center;
                double height = Math.Max(bb.Diagonal.Length, 1.0);
                vp.SetCameraLocations(target, target + Vector3d.ZAxis * height);
                vp.CameraUp = plane.YAxis;
                vp.ZoomBoundingBox(bb);
                vp.Name = level.Name;
            }
            if (viewRange.CurrentValue)
            {
                using (SkunkService.Suppress())
                    PlanCutElement.Ensure(doc, level.Id, vp.Id);
            }
            else
            {
                using (SkunkService.Suppress())
                    PlanCutElement.Clear(doc, vp.Id);
            }
            view.Redraw();
            Log.Info("CPlane set to " + level.Name + (viewRange.CurrentValue ? " with a plan cut (view range)" : "") + ".");
            return Result.Success;
        }

        /// <summary>SkAttachToLevel: objects follow the level when its elevation changes (base constraint).</summary>
        public static Result Attach(RhinoDoc doc, RunMode mode)
        {
            var go = new GetObject();
            go.SetCommandPrompt("Objects to attach to a level");
            go.SetCustomGeometryFilter((o, g, ci) => ElementIO.KindOf(o) == ElementKind.None);
            go.GroupSelect = true;
            go.EnablePreSelect(true, true);
            if (go.GetMultiple(1, 0) != GetResult.Object)
                return Result.Cancel;
            var objects = go.Objects().Select(r => r.Object()).Where(o => o != null).ToList();
            var level = Kit.PickLevel(doc, "Level to attach to (click a level or choose)", NearestBelow(doc, objects));
            if (level == null)
                return Result.Cancel;
            using (SkunkService.Suppress())
                foreach (var o in objects)
                    LevelElement.Attach(doc, o, level.Id);
            Log.Info($"{objects.Count} object(s) attached to {level.Name}; they move when its elevation changes.");
            SkunkService.Instance?.RequestUi();
            return Result.Success;
        }

        static Guid NearestBelow(RhinoDoc doc, System.Collections.Generic.List<RhinoObject> objects)
        {
            var bb = BoundingBox.Empty;
            foreach (var o in objects)
                bb.Union(o.Geometry.GetBoundingBox(false));
            var levels = LevelElement.All(doc);
            var below = levels.Where(l => l.Z <= bb.Min.Z + doc.ModelAbsoluteTolerance).OrderByDescending(l => l.Z).FirstOrDefault();
            return (below ?? levels.FirstOrDefault())?.Id ?? Guid.Empty;
        }

        /// <summary>SkDetachFromLevel.</summary>
        public static Result Detach(RhinoDoc doc, RunMode mode)
        {
            var go = new GetObject();
            go.SetCommandPrompt("Objects to detach from their level");
            go.SetCustomGeometryFilter((o, g, ci) => LevelElement.AttachedLevel(o) != Guid.Empty);
            go.GroupSelect = true;
            go.EnablePreSelect(true, true);
            if (go.GetMultiple(1, 0) != GetResult.Object)
                return Result.Cancel;
            int n = 0;
            using (SkunkService.Suppress())
                foreach (var r in go.Objects())
                {
                    var o = r.Object();
                    if (o == null)
                        continue;
                    LevelElement.Attach(doc, o, Guid.Empty);
                    n++;
                }
            Log.Info(n + " object(s) detached.");
            SkunkService.Instance?.RequestUi();
            return Result.Success;
        }

        /// <summary>SkLevels: the levels manager.</summary>
        public static Result Levels(RhinoDoc doc, RunMode mode)
        {
            if (mode == RunMode.Scripted)
            {
                foreach (var l in LevelElement.All(doc))
                    RhinoApp.WriteLine("  {0,-24} {1,14}   {2}", l.Name, LevelElement.ElevationText(new UpdateContext(doc), l.Type, l.Z), l.Type?.Name);
                return Result.Success;
            }
            UI.LevelsDialog.Show(doc);
            return Result.Success;
        }
    }
}
