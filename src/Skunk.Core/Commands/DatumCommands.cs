using System;
using System.Collections.Generic;
using System.Drawing;
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
using Skunk.Site;
using Skunk.Types;

namespace Skunk.Commands
{
    /// <summary>Architecture > Datum: levels, datum boxes, and objects constrained to levels.</summary>
    internal static class DatumCommands
    {
        /// <summary>
        /// SkLevel: pick (or type) an elevation; the level gets the previous level's extents (and joins its datum box)
        /// or the model's.
        /// </summary>
        public static Result Level(RhinoDoc doc, RunMode mode)
        {
            var lib = TypeTable.Get(doc);
            var names = Kit.TypeNames(lib, ElementKind.Level);
            int typeIndex = Kit.IndexOf(names, Kit.LastType(ElementKind.Level));
            string[] extentsModes = { "Previous", "Model", "Pick" };
            int extents = LevelElement.All(doc).Count > 0 || DatumBoxElement.DefaultBox(doc) != Guid.Empty ? 0 : 1;
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
                var box = extentsModes[extents] == "Previous" ? DatumBoxElement.DefaultBox(doc) : Guid.Empty;
                id = LevelElement.Create(doc, type.Name, name, z, footprint, box);
                if (id != Guid.Empty && heads > 0)
                    LevelElement.SetHeadEnds(new UpdateContext(doc), id, (LevelElement.HeadEnds)(heads - 1));
            }
            if (id == Guid.Empty)
            {
                Log.Error("the level could not be created");
                return Result.Failure;
            }
            var info = LevelElement.Info(doc, doc.Objects.FindId(id));
            var boxInfo = info.Data.Box == Guid.Empty ? null : DatumBoxElement.Info(doc, ElementIO.Find(doc, info.Data.Box));
            Log.Info($"{info.Name} created at {LevelElement.ElevationText(new UpdateContext(doc), type, z)}" + (boxInfo != null ? " in " + boxInfo.Name + "." : "."));
            doc.Views.Redraw();
            return Result.Success;
        }

        /// <summary>
        /// SkDatumBox: one box that holds the extents of levels and the ends of grids - all of them, or the ones
        /// picked. Move, scale, rotate or grip-edit the box and every level and grid in it follows; drag a level's
        /// head sideways, or a grid's bubble along it, and that one leaves the box (its Datum Box property puts it back).
        /// </summary>
        public static Result DatumBox(RhinoDoc doc, RunMode mode)
        {
            var all = LevelElement.All(doc);
            var allGrids = GridElement.All(doc);
            var chosen = new List<LevelInfo>();
            var chosenGrids = new List<GridInfo>();
            if (all.Count > 0 || allGrids.Count > 0)
            {
                var go = new GetObject();
                go.SetCommandPrompt("Levels and grids for the datum box (Enter for all of them)");
                go.GeometryFilter = ObjectType.Curve | ObjectType.Annotation;
                go.SetCustomGeometryFilter((o, g, ci) =>
                {
                    var k = ElementIO.KindOf(o);
                    return k == ElementKind.Level || ElementKinds.IsLevelPart(k) || k == ElementKind.Grid || ElementKinds.IsGridPart(k);
                });
                go.GroupSelect = true;
                go.EnablePreSelect(true, true);
                go.AcceptNothing(true);
                var r = go.GetMultiple(1, 0);
                if (r == GetResult.Nothing)
                {
                    chosen = all;
                    chosenGrids = allGrids;
                }
                else if (r == GetResult.Object)
                {
                    foreach (var pickedRef in go.Objects())
                    {
                        var o = pickedRef.Object();
                        var k = ElementIO.KindOf(o);
                        if (ElementKinds.IsLevelPart(k))
                            o = ElementIO.Find(doc, ElementIO.Read<HeadData>(o)?.Level ?? Guid.Empty);
                        else if (ElementKinds.IsGridPart(k))
                            o = ElementIO.Find(doc, ElementIO.Read<GridHeadData>(o)?.Grid ?? Guid.Empty);
                        var hit = all.FirstOrDefault(l => l.Id == o?.Id);
                        if (hit != null && !chosen.Contains(hit))
                            chosen.Add(hit);
                        var grid = allGrids.FirstOrDefault(g => g.Id == o?.Id);
                        if (grid != null && !chosenGrids.Contains(grid))
                            chosenGrids.Add(grid);
                    }
                }
                else
                    return Result.Cancel;
                doc.Objects.UnselectAll();
            }

            var gp = new GetPoint();
            gp.SetCommandPrompt(chosen.Count > 0 ? "First corner of the datum box (Enter to fit the levels)" : "First corner of the datum box (Enter for the model's extents)");
            gp.AcceptNothing(true);
            var pr = gp.Get();
            Polyline outline;
            if (pr == GetResult.Nothing)
                outline = DatumBoxElement.FitOutline(doc, chosen);
            else if (pr == GetResult.Point)
            {
                var c1 = gp.Point();
                var plane = SiteModel.Get(doc).Project;
                Polyline Rect(Point3d a, Point3d b)
                {
                    plane.ClosestParameter(a, out double x1, out double y1);
                    plane.ClosestParameter(b, out double x2, out double y2);
                    var rect = DatumBoxElement.Rectangle(plane, Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
                    return new Polyline(rect.Select(p => new Point3d(p.X, p.Y, a.Z)));
                }
                var g2 = new GetPoint();
                g2.SetCommandPrompt("Opposite corner");
                g2.SetBasePoint(c1, true);
                g2.DynamicDraw += (s, e) => e.Display.DrawPolyline(Rect(c1, e.CurrentPoint), Color.FromArgb(60, 140, 180), 1);
                if (g2.Get() != GetResult.Point)
                    return Result.Cancel;
                outline = Rect(c1, g2.Point());
            }
            else
                return Result.Cancel;
            double area = 0;
            for (int i = 0; outline != null && i < outline.Count - 1; i++)
                area += outline[i].X * outline[i + 1].Y - outline[i + 1].X * outline[i].Y;
            if (outline == null || outline.SegmentCount < 3 || Math.Abs(area) / 2 <= doc.ModelAbsoluteTolerance * doc.ModelAbsoluteTolerance)
            {
                Log.Warn("a datum box needs an area - pick two opposite corners");
                return Result.Failure;
            }

            Guid id = Guid.Empty;
            Kit.Edit(doc, "Skunk Datum Box", ctx => id = DatumBoxElement.Create(ctx, outline, chosen.Select(l => l.Id), null, chosenGrids.Select(g => g.Id)));
            var info = DatumBoxElement.Info(doc, ElementIO.Find(doc, id));
            if (info == null)
            {
                Log.Error("the datum box could not be created");
                return Result.Failure;
            }
            Log.Info($"{info.Name} holds {chosen.Count} level(s) and {chosenGrids.Count} grid(s): move, scale or rotate it and they follow. " +
                     "Drag a level's head sideways, or a grid's bubble along it, to take that one out; its Datum Box property puts it back.");
            return Result.Success;
        }

        /// <summary>
        /// SkGrid: draw grid lines one after another, each named after the last one (1, 2, 3 or A, B, C) with a
        /// bubble at the end (the type's default); Enter ends. Options: type, name, which ends get bubbles, and a
        /// datum box that sets the grids' ends.
        /// </summary>
        public static Result Grid(RhinoDoc doc, RunMode mode)
        {
            var lib = TypeTable.Get(doc);
            var names = Kit.TypeNames(lib, ElementKind.Grid);
            int typeIndex = Kit.IndexOf(names, Kit.LastType(ElementKind.Grid));
            string[] bubbleModes = { "TypeDefault", "End", "Start", "Both", "None" };
            int bubbles = 0;
            var boxes = DatumBoxElement.All(doc);
            var boxNames = new[] { "None" }.Concat(boxes.Select(b => Kit.OptionValue(b.Name))).ToArray();
            var grids = GridElement.All(doc);
            var lastBox = grids.Count > 0 ? grids[grids.Count - 1].Data.Box : Guid.Empty;
            int boxIndex = Math.Max(0, boxes.FindIndex(b => b.Id == lastBox) + 1);
            string name = null;
            int made = 0;
            while (true)
            {
                string current = name ?? GridElement.NextName(doc);
                var g1 = new GetPoint();
                g1.SetCommandPrompt("Start of grid " + current + (made > 0 ? " (Enter when done)" : ""));
                int optType = g1.AddOptionList("Type", names.Select(Kit.OptionValue), typeIndex);
                int optName = g1.AddOption("Name", Kit.OptionValue(current));
                int optBubbles = g1.AddOptionList("Bubbles", bubbleModes, bubbles);
                int optBox = boxes.Count > 0 ? g1.AddOptionList("DatumBox", boxNames, boxIndex) : -1;
                g1.AcceptNothing(true);
                var r = g1.Get();
                if (r == GetResult.Option)
                {
                    int i = g1.OptionIndex();
                    if (i == optType) typeIndex = g1.Option().CurrentListOptionIndex;
                    else if (i == optBubbles) bubbles = g1.Option().CurrentListOptionIndex;
                    else if (i == optBox) boxIndex = g1.Option().CurrentListOptionIndex;
                    else if (i == optName)
                    {
                        var gs = new GetString();
                        gs.SetCommandPrompt("Grid name");
                        gs.SetDefaultString(current);
                        if (gs.GetLiteralString() == GetResult.String && !string.IsNullOrWhiteSpace(gs.StringResult()))
                            name = gs.StringResult().Trim();
                    }
                    continue;
                }
                if (r != GetResult.Point)
                    break;
                var p1 = g1.Point();
                var g2 = new GetPoint();
                g2.SetCommandPrompt("End of grid " + current);
                g2.SetBasePoint(p1, true);
                g2.DrawLineFromPoint(p1, true);
                if (g2.Get() != GetResult.Point)
                    break;
                var p2 = g2.Point();
                if (new Point3d(p2.X, p2.Y, p1.Z).DistanceTo(p1) <= doc.ModelAbsoluteTolerance)
                {
                    Log.Warn("a grid needs two different points in plan");
                    continue;
                }
                var type = lib.Find<GridType>(names[typeIndex]);
                Kit.SetLastType(ElementKind.Grid, type.Name);
                bool? end1 = null, end2 = null;
                switch (bubbleModes[bubbles])
                {
                    case "End": end1 = false; end2 = true; break;
                    case "Start": end1 = true; end2 = false; break;
                    case "Both": end1 = true; end2 = true; break;
                    case "None": end1 = false; end2 = false; break;
                }
                var box = boxIndex > 0 ? boxes[boxIndex - 1].Id : Guid.Empty;
                Guid id = Guid.Empty;
                Kit.Edit(doc, "Skunk Grid", ctx => id = GridElement.Create(ctx, type.Name, current, new Line(p1, p2), box, end1, end2));
                if (id == Guid.Empty)
                {
                    Log.Error("the grid could not be created");
                    break;
                }
                made++;
                name = null;
                doc.Views.Redraw();
            }
            if (made > 0)
                Log.Info(made + " grid(s) drawn. Drag a bubble along its grid to lengthen or shorten that end; type a new name into a bubble to rename the grid and the ones after it.");
            return made > 0 ? Result.Success : Result.Cancel;
        }

        /// <summary>
        /// SkRenumberGrids: name grids one after another (1, 2, 3 or A, B, C). Pick them in the order to number them, or
        /// select several and draw a direction across them; the first takes the start name (by default the first of
        /// their names in its series). A grid not picked that has one of the names takes one the picked grids gave up.
        /// </summary>
        public static Result RenumberGrids(RhinoDoc doc, RunMode mode)
        {
            if (GridElement.All(doc).Count == 0)
            {
                Log.Warn("there are no grids yet - draw them with SkGrid");
                return Result.Nothing;
            }
            var go = new GetObject();
            go.SetCommandPrompt("Grids to renumber, picked in order (or select several, then draw a direction across them)");
            go.GeometryFilter = ObjectType.Curve | ObjectType.Annotation;
            go.SetCustomGeometryFilter((o, g, ci) =>
            {
                var k = ElementIO.KindOf(o);
                return k == ElementKind.Grid || ElementKinds.IsGridPart(k);
            });
            go.EnablePreSelect(true, true);
            go.SubObjectSelect = false;
            if (go.GetMultiple(1, 0) != GetResult.Object)
                return Result.Cancel;
            bool preselected = go.ObjectsWerePreselected;
            var picked = new List<GridInfo>();
            foreach (var pickedRef in go.Objects())
            {
                var o = pickedRef.Object();
                if (ElementKinds.IsGridPart(ElementIO.KindOf(o)))
                    o = ElementIO.Find(doc, ElementIO.Read<GridHeadData>(o)?.Grid ?? Guid.Empty);
                var info = GridElement.Info(doc, o);
                if (info != null && picked.All(p => p.Id != info.Id))
                    picked.Add(info);
            }
            doc.Objects.UnselectAll();
            doc.Views.Redraw();
            if (picked.Count == 0)
                return Result.Cancel;
            // grids selected before the command have no picking order: the order they were made
            if (preselected)
                picked = picked.OrderBy(g => g.Data.Seq).ToList();
            var order = picked.Select(g => g.Id).ToList();
            string start = GridElement.FirstInSeries(picked.Select(g => g.Name));

            if (picked.Count == 1)
            {
                var gs = new GetString();
                gs.SetCommandPrompt("New name for grid " + picked[0].Name);
                gs.SetDefaultString(picked[0].Name);
                if (gs.GetLiteralString() != GetResult.String || string.IsNullOrWhiteSpace(gs.StringResult()))
                    return Result.Cancel;
                start = gs.StringResult();
            }
            else
            {
                while (true)
                {
                    var gp = new GetPoint();
                    gp.SetCommandPrompt("Numbering from " + start + ": start of a direction across the grids (Enter: in the order " +
                                        (preselected ? "they were made" : "picked") + ")");
                    int optStart = gp.AddOption("StartName");
                    gp.AcceptNothing(true);
                    var r = gp.Get();
                    if (r == GetResult.Option)
                    {
                        if (gp.OptionIndex() == optStart)
                        {
                            var gs = new GetString();
                            gs.SetCommandPrompt("Name of the first grid");
                            gs.SetDefaultString(start);
                            if (gs.GetLiteralString() == GetResult.String && !string.IsNullOrWhiteSpace(gs.StringResult()))
                                start = GridElement.CleanName(gs.StringResult());
                        }
                        continue;
                    }
                    if (r == GetResult.Nothing)
                        break;
                    if (r != GetResult.Point)
                        return Result.Cancel;
                    var p1 = gp.Point();
                    var g2 = new GetPoint();
                    g2.SetCommandPrompt("End of the direction (the grids are numbered in the order it crosses them)");
                    g2.SetBasePoint(p1, true);
                    var arrow = Color.FromArgb(60, 140, 180);
                    g2.DynamicDraw += (s, e) => e.Display.DrawArrow(new Line(p1, e.CurrentPoint), arrow);
                    if (g2.Get() != GetResult.Point)
                        return Result.Cancel;
                    order = GridElement.AlongDirection(doc, order, p1, g2.Point());
                    break;
                }
            }

            List<GridRename> renamed = null;
            Kit.Edit(doc, "Skunk Renumber Grids", ctx => renamed = GridElement.Renumber(ctx, order, start));
            if (renamed == null || renamed.Count == 0)
            {
                Log.Info("the grids already have those names");
                return Result.Success;
            }
            var names = order.Select(id => GridElement.Info(doc, doc.Objects.FindId(id))?.Name).Where(n => n != null);
            var swapped = renamed.Where(x => !order.Contains(x.Id)).ToList();
            Log.Info("grids renumbered: " + string.Join(", ", names) +
                     (swapped.Count == 0 ? "" : ". Names kept unique: " + string.Join(", ", swapped.Select(x => "grid " + x.From + " is now " + x.To))));
            return Result.Success;
        }

        /// <summary>The sides SkGridBubbles used last (this session); top and left to begin with.</summary>
        static GridBubbleSides s_bubbleSides = GridBubbleSides.Top | GridBubbleSides.Left;

        static string SidesText(GridBubbleSides sides)
        {
            var named = new[] { GridBubbleSides.Top, GridBubbleSides.Bottom, GridBubbleSides.Left, GridBubbleSides.Right }
                .Where(s => (sides & s) != 0).Select(s => s.ToString().ToLowerInvariant()).ToList();
            if (named.Count == 0)
                return "no side";
            return "the " + (named.Count == 1 ? named[0] : string.Join(", ", named.Take(named.Count - 1)) + " and " + named[named.Count - 1]);
        }

        /// <summary>The grid a grid object or one of its parts belongs to (empty for anything else).</summary>
        static Guid GridOf(RhinoObject o)
        {
            var k = ElementIO.KindOf(o);
            if (k == ElementKind.Grid)
                return o.Id;
            return ElementKinds.IsGridPart(k) ? ElementIO.Read<GridHeadData>(o)?.Grid ?? Guid.Empty : Guid.Empty;
        }

        /// <summary>Grids a viewport shows; looking level, only those it looks along (their elevation parts face it).</summary>
        static List<Guid> GridsIn(RhinoDoc doc, Rhino.Display.RhinoViewport vp, bool level)
        {
            var look = vp.CameraDirection;
            look.Z = 0;
            look.Unitize();
            var list = new List<Guid>();
            foreach (var g in GridElement.All(doc))
            {
                var bb = new BoundingBox(new[] { g.Line.From, g.Line.To });
                bb.Inflate(Math.Max(doc.ModelAbsoluteTolerance, 1e-6));
                if (!vp.IsVisible(bb) && !level)
                    continue;
                if (level)
                {
                    var dir = g.Line.Direction;
                    dir.Z = 0;
                    if (!dir.Unitize() || Math.Abs(dir * look) < Math.Cos(RhinoMath.ToRadians(15)))
                        continue;
                }
                list.Add(g.Id);
            }
            return list;
        }

        /// <summary>
        /// SkGridBubbles: grid bubbles on the top, bottom, left and / or right of the active view. In a model view it sets
        /// the grids' own bubbles: in a plan, the ends on those sides of the view; in a view looking level, the top and
        /// bottom of the elevation lines of the grids it looks along. In a layout detail the sides become the detail's:
        /// it shows only the bubbles on them (a grid gets a bubble where it needs one), while model views and other
        /// details keep theirs. On a layout with no detail active, every detail on it.
        /// </summary>
        public static Result GridBubbles(RhinoDoc doc, RunMode mode)
        {
            if (GridElement.All(doc).Count == 0)
            {
                Log.Warn("there are no grids yet - draw them with SkGrid");
                return Result.Nothing;
            }
            var view = doc.Views.ActiveView;
            if (view == null)
                return Result.Failure;
            var details = new List<DetailViewObject>();
            if (view is Rhino.Display.RhinoPageView page)
            {
                var active = page.ActiveDetailId == Guid.Empty ? null : doc.Objects.FindId(page.ActiveDetailId) as DetailViewObject;
                if (active != null)
                    details.Add(active);
                else
                    details.AddRange(page.GetDetailViews() ?? Array.Empty<DetailViewObject>());
                if (details.Count == 0)
                {
                    Log.Warn("this layout has no details");
                    return Result.Nothing;
                }
            }
            bool inDetails = details.Count > 0;
            var sides = s_bubbleSides;
            if (details.Count == 1 && GridViews.SidesOf(details[0]) is GridBubbleSides kept)
                sides = kept;
            var preselected = inDetails ? new List<Guid>() :
                doc.Objects.GetSelectedObjects(false, false).Select(GridOf).Where(id => id != Guid.Empty).Distinct().ToList();
            string where = !inDetails ? "this view" : details.Count == 1 ? "this detail" : "the " + details.Count + " details on this layout";

            List<Guid> picked = null;
            bool showAll = false;
            if (mode == RunMode.Interactive)
            {
                var chosen = ChooseBubbleSides(doc, view, details, ref sides, preselected, ref picked, out showAll);
                if (chosen != Result.Success)
                    return chosen;
            }
            else
            {
                while (true)
                {
                    var top = new OptionToggle((sides & GridBubbleSides.Top) != 0, "Off", "On");
                    var bottom = new OptionToggle((sides & GridBubbleSides.Bottom) != 0, "Off", "On");
                    var left = new OptionToggle((sides & GridBubbleSides.Left) != 0, "Off", "On");
                    var right = new OptionToggle((sides & GridBubbleSides.Right) != 0, "Off", "On");
                    string prompt = "Bubbles on " + SidesText(sides) + " of " + where;
                    GetBaseClass get;
                    GetObject pick = null;
                    if (inDetails || preselected.Count > 0)
                    {
                        var g = new GetOption();
                        g.SetCommandPrompt(prompt + (preselected.Count > 0 ? ", for the " + preselected.Count + " selected grid(s)" : "") + " (Enter to apply)");
                        get = g;
                    }
                    else
                    {
                        pick = new GetObject();
                        pick.SetCommandPrompt(prompt + ": pick grids (Enter for every grid in it)");
                        pick.GeometryFilter = ObjectType.Curve | ObjectType.Annotation;
                        pick.SetCustomGeometryFilter((o, geo, ci) => GridOf(o) != Guid.Empty);
                        pick.EnablePreSelect(false, true);
                        get = pick;
                    }
                    get.AddOptionToggle("Top", ref top);
                    get.AddOptionToggle("Bottom", ref bottom);
                    get.AddOptionToggle("Left", ref left);
                    get.AddOptionToggle("Right", ref right);
                    int optShowAll = inDetails ? get.AddOption("ShowAll") : -1;
                    get.AcceptNothing(true);
                    var r = pick != null ? pick.GetMultiple(1, 0) : ((GetOption)get).Get();
                    if (r == GetResult.Option)
                    {
                        if (get.OptionIndex() == optShowAll)
                        {
                            showAll = true;
                            break;
                        }
                        sides = (top.CurrentValue ? GridBubbleSides.Top : 0) | (bottom.CurrentValue ? GridBubbleSides.Bottom : 0) |
                                (left.CurrentValue ? GridBubbleSides.Left : 0) | (right.CurrentValue ? GridBubbleSides.Right : 0);
                        continue;
                    }
                    if (r == GetResult.Nothing)
                        break;
                    if (r == GetResult.Object && pick != null)
                    {
                        picked = pick.Objects().Select(x => GridOf(x.Object())).Where(id => id != Guid.Empty).Distinct().ToList();
                        break;
                    }
                    return Result.Cancel;
                }
            }
            if (!showAll)
                s_bubbleSides = sides;

            if (inDetails)
            {
                int added = 0;
                Kit.Edit(doc, "Skunk Grid Bubbles", ctx =>
                {
                    foreach (var dobj in details)
                    {
                        GridViews.SetSides(doc, dobj, showAll ? (GridBubbleSides?)null : sides);
                        if (showAll)
                            continue;
                        var d = GridViews.Of(dobj);
                        bool level = GridViews.IsElevation(d);
                        var grids = GridsIn(doc, dobj.Viewport, level);
                        added += level ? GridElement.SetElevationSides(ctx, grids, sides, true)
                                       : GridElement.SetBubbleSides(ctx, grids, sides, d.Right, d.Up, true);
                    }
                    GridViews.Apply(doc, GridElement.All(doc).Select(g => g.Id), GridViews.Details(doc));
                });
                if (showAll)
                    Log.Info(Cap(where) + " shows every grid bubble again.");
                else
                    Log.Info(Cap(where) + " shows grid bubbles on " + SidesText(sides) + " only" +
                             (added > 0 ? " (" + added + " grid(s) got a bubble there)" : "") +
                             ". Model views and other details keep theirs; ShowAll puts every bubble back.");
                return Result.Success;
            }

            var vp = view.ActiveViewport;
            bool lookingLevel = LooksLevel(vp);
            var scope = picked ?? (preselected.Count > 0 ? preselected : GridsIn(doc, vp, lookingLevel));
            if (scope.Count == 0)
            {
                Log.Warn(lookingLevel ? "no grid runs along this view (its elevation bubbles would face it)" : "no grid in this view");
                return Result.Nothing;
            }
            int changed = 0;
            Kit.Edit(doc, "Skunk Grid Bubbles", ctx =>
                changed = lookingLevel ? GridElement.SetElevationSides(ctx, scope, sides, false)
                                       : GridElement.SetBubbleSides(ctx, scope, sides, vp.CameraX, vp.CameraUp, false));
            if (lookingLevel)
                Log.Info("elevation bubbles on " + SidesText(sides & (GridBubbleSides.Top | GridBubbleSides.Bottom)) + " of " + scope.Count +
                         " grid(s) this view looks along (" + changed + " changed)" +
                         ((sides & (GridBubbleSides.Left | GridBubbleSides.Right)) != 0 ? "; left and right do not apply looking level" : "") + ".");
            else
                Log.Info("bubbles on " + SidesText(sides) + " of this view for " + scope.Count + " grid(s) (" + changed + " changed).");
            return Result.Success;
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>A model view looking within 60 degrees of level: SkGridBubbles sets the grids' elevation bubbles there.</summary>
        static bool LooksLevel(Rhino.Display.RhinoViewport vp)
        {
            var dir = vp.CameraDirection;
            dir.Unitize();
            return Math.Abs(dir.Z) < 0.5;
        }

        /// <summary>
        /// SkGridBubbles' dialog (run interactively): the side boxes around a diagram of the grids they apply to - the grids
        /// of the active model view (or the selected or picked ones), or of the layout detail, as that view shows them.
        /// Select grids closes it for a pick and opens it again; Show every bubble (a detail) sets <paramref name="showAll"/>.
        /// </summary>
        static Result ChooseBubbleSides(RhinoDoc doc, Rhino.Display.RhinoView view, List<DetailViewObject> details, ref GridBubbleSides sides,
                                        List<Guid> preselected, ref List<Guid> picked, out bool showAll)
        {
            showAll = false;
            while (true)
            {
                Vector3d right, up;
                bool level;
                List<Guid> scope;
                string scopeText, note;
                if (details.Count > 0)
                {
                    var d = GridViews.Of(details[0]);
                    right = d.Right;
                    up = d.Up;
                    level = GridViews.IsElevation(d);
                    scope = GridsIn(doc, details[0].Viewport, level);
                    scopeText = (details.Count == 1 ? "This detail" : "The " + details.Count + " details on this layout (the diagram shows the first)") +
                                ": " + GridCount(scope.Count) + (level ? " it looks along, seen level" : "") + ".";
                    note = "Only " + (details.Count == 1 ? "this detail changes" : "these details change") + ": " +
                           (level ? "it shows the top and / or bottom bubbles you tick" : "it shows the bubbles on the sides you tick") +
                           " (a grid gets one there if it has none) and hides the rest. Model views and other details keep theirs.";
                }
                else
                {
                    var vp = view.ActiveViewport;
                    right = vp.CameraX;
                    up = vp.CameraUp;
                    level = LooksLevel(vp);
                    scope = picked ?? (preselected.Count > 0 ? preselected : GridsIn(doc, vp, level));
                    string which = picked != null ? "The " + GridCount(picked.Count) + " you picked" :
                                   preselected.Count > 0 ? "The " + GridCount(preselected.Count) + " selected" :
                                   level ? "The " + GridCount(scope.Count) + " the " + vp.Name + " view looks along" :
                                   "Every grid in the " + vp.Name + " view (" + scope.Count + ")";
                    scopeText = which + (level ? ", seen level." : ", as the " + vp.Name + " view shows them.");
                    note = level
                        ? "Looking level, these grids get bubbles at the top and / or bottom of their elevation lines (each grid's own Elevation Bubbles)."
                        : "Each grid end on a ticked side of the view gets a bubble and the other ends lose theirs. Model views share the grids' " +
                          "bubbles; a layout detail with sides of its own keeps them.";
                }
                var infos = scope.Select(id => GridElement.Info(doc, doc.Objects.FindId(id))).Where(i => i != null).ToList();
                var diagram = level ? GridBubbleDiagram.Elevation(infos, right, sides) : GridBubbleDiagram.Plan(infos, right, up, sides);
                if (diagram.Sample)
                    scopeText = (level ? "No grid runs along this view" : details.Count > 0 ? "No grid in this detail" : "No grid in the " + view.ActiveViewport.Name + " view") +
                                ": the diagram shows a sample." + (details.Count == 0 ? " Select grids... picks some." : "");
                var choice = UI.GridBubblesDialog.Show(diagram, sides, scopeText, note, details.Count > 0, details.Count == 0, out var chosen);
                if (choice == UI.GridBubblesDialog.Choice.Cancel)
                    return Result.Cancel;
                sides = chosen;
                if (choice == UI.GridBubblesDialog.Choice.ShowAll)
                {
                    showAll = true;
                    return Result.Success;
                }
                if (choice == UI.GridBubblesDialog.Choice.Apply)
                    return Result.Success;
                // Select grids: pick them, then the dialog again (Enter or Esc keeps the grids it had)
                var pick = new GetObject();
                pick.SetCommandPrompt("Pick the grids for the bubbles (Enter when done)");
                pick.GeometryFilter = ObjectType.Curve | ObjectType.Annotation;
                pick.SetCustomGeometryFilter((o, geo, ci) => GridOf(o) != Guid.Empty);
                pick.EnablePreSelect(false, true);
                pick.AcceptNothing(true);
                if (pick.GetMultiple(1, 0) == GetResult.Object)
                {
                    var ids = pick.Objects().Select(x => GridOf(x.Object())).Where(id => id != Guid.Empty).Distinct().ToList();
                    if (ids.Count > 0)
                        picked = ids;
                }
                doc.Objects.UnselectAll();
            }
        }

        static string GridCount(int grids) => grids == 1 ? "1 grid" : grids + " grids";

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

        // the Plan View / View Range choices of the last Go To Level, for the session
        static bool s_planView = true, s_viewRange = true;

        /// <summary>The level the CPlane is at (its index), else the first.</summary>
        internal static int CurrentLevel(System.Collections.Generic.IList<LevelInfo> levels, double cplaneZ, double tol)
        {
            int current = 0;
            for (int i = 0; i < levels.Count; i++)
                if (Math.Abs(levels[i].Z - cplaneZ) < tol)
                    current = i;
            return current;
        }

        /// <summary>
        /// SkGoToLevel: set the CPlane to a level; optionally a plan view with a view range. An interactive run opens the Go
        /// To Level dialog; -SkGoToLevel (scripted) keeps the command-line options for macros.
        /// </summary>
        public static Result GoToLevel(RhinoDoc doc, RunMode mode)
        {
            var view = doc.Views.ActiveView;
            if (view == null)
                return Result.Failure;
            var levels = LevelElement.All(doc);
            if (levels.Count == 0)
            {
                Log.Warn("there are no levels yet - create one with SkLevel");
                return Result.Nothing;
            }
            int current = CurrentLevel(levels, view.ActiveViewport.ConstructionPlane().Origin.Z, doc.ModelAbsoluteTolerance);
            bool plan = s_planView, range = s_viewRange;
            LevelInfo level = null;
            if (mode == RunMode.Interactive)
            {
                if (!UI.GoToLevelDialog.Show(doc, levels, current, view.ActiveViewport.Name, ref plan, ref range, out int chosen))
                    return Result.Cancel;
                level = levels[chosen];
            }
            else
            {
                var planView = new OptionToggle(plan, "No", "Yes");
                var viewRange = new OptionToggle(range, "No", "Yes");
                var go = new GetOption();
                go.SetCommandPrompt("Level to go to");
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
                plan = planView.CurrentValue;
                range = viewRange.CurrentValue;
            }
            s_planView = plan;
            s_viewRange = range;

            var ctx = new UpdateContext(doc);
            var plane = ctx.Site.Project;
            plane.Origin = new Point3d(plane.Origin.X, plane.Origin.Y, level.Z);
            var vp = view.ActiveViewport;
            vp.SetConstructionPlane(plane);
            if (plan)
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
            if (range)
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
            Log.Info("CPlane set to " + level.Name + (range ? " with a plan cut (view range)" : "") + ".");
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
