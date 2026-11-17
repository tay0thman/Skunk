using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Skunk.Core;
using Skunk.Site;
using Skunk.Types;
using Skunk.Units;

namespace Skunk.Elements
{
    /// <summary>A datum box as seen by commands and dialogs.</summary>
    public sealed class DatumBoxInfo
    {
        public Guid Id;
        public string Name;
        public DatumBoxData Data;
        /// <summary>Plan outline: counter-clockwise, at Z = 0.</summary>
        public Polyline Outline;
    }

    /// <summary>
    /// A datum box holds the extents of the levels in it. The box object is a closed polyline on
    /// "__Annotations__::Datum Boxes" - its plan outline - kept flat just under the lowest of its levels; the rest of the box
    /// (the top, over the highest level, and the vertical edges) is drawn on screen by DatumBoxConduit. Move,
    /// scale, rotate or grip-edit the box and every level in it takes the new outline, heads and level lines
    /// included. A level leaves the box when its own extents change (a head dragged sideways, its footprint
    /// edited or moved in plan) and joins it again from the Properties palette. The box never prints.
    /// </summary>
    public static class DatumBoxElement
    {
        /// <summary>Space between the box and the lowest and highest of its levels (model units).</summary>
        public static double Margin(RhinoDoc doc) => DocUnits.FromMeters(doc, 1.0);

        /// <summary>Height of a box with no levels in it, as drawn.</summary>
        static double EmptyHeight(RhinoDoc doc) => DocUnits.FromMeters(doc, 3.0);

        // ---- queries ----------------------------------------------------------------------------

        public static DatumBoxInfo Info(RhinoDoc doc, RhinoObject obj)
        {
            if (obj == null || ElementIO.KindOf(obj) != ElementKind.DatumBox)
                return null;
            var data = ElementIO.Read<DatumBoxData>(obj) ?? new DatumBoxData { Name = obj.Attributes.Name ?? "" };
            if (!TryPlan(obj.Geometry, out var outline))
                outline = Stored(data);
            if (outline == null)
                return null;
            return new DatumBoxInfo
            {
                Id = obj.Id,
                Name = string.IsNullOrEmpty(data.Name) ? obj.Attributes.Name ?? "" : data.Name,
                Data = data,
                Outline = outline,
            };
        }

        /// <summary>All datum boxes, by name.</summary>
        public static List<DatumBoxInfo> All(RhinoDoc doc) =>
            ElementIO.All(doc, ElementKind.DatumBox).Select(o => Info(doc, o)).Where(i => i != null)
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>The plan outline of a live box (counter-clockwise, Z = 0): what the levels in it take.</summary>
        public static bool TryOutline(RhinoDoc doc, Guid id, out Polyline outline)
        {
            outline = null;
            var obj = ElementIO.Find(doc, id);
            if (obj == null || ElementIO.KindOf(obj) != ElementKind.DatumBox)
                return false;
            if (TryPlan(obj.Geometry, out outline))
                return true;
            outline = Stored(ElementIO.Read<DatumBoxData>(obj));
            return outline != null;
        }

        /// <summary>The levels in a box.</summary>
        public static List<Guid> LevelIds(RhinoDoc doc, Guid box)
        {
            var list = new List<Guid>();
            if (box == Guid.Empty)
                return list;
            foreach (var o in LevelObjects(doc))
                if (ElementIO.Read<LevelData>(o)?.Box == box)
                    list.Add(o.Id);
            return list;
        }

        /// <summary>The grids in a box (their ends sit on its outline).</summary>
        public static List<Guid> GridIds(RhinoDoc doc, Guid box)
        {
            var list = new List<Guid>();
            if (box == Guid.Empty)
                return list;
            var index = Service.SkunkService.Instance?.Index(doc);
            var grids = index == null ? ElementIO.All(doc, ElementKind.Grid).ToList()
                : index.Grids.ToList().Select(id => ElementIO.Find(doc, id)).Where(o => o != null && ElementIO.KindOf(o) == ElementKind.Grid).ToList();
            foreach (var o in grids)
                if (ElementIO.Read<GridData>(o)?.Box == box)
                    list.Add(o.Id);
            return list;
        }

        /// <summary>The box a new level joins: the highest level's, or the only box when there are no levels yet.</summary>
        public static Guid DefaultBox(RhinoDoc doc)
        {
            var levels = LevelElement.All(doc);
            if (levels.Count > 0)
            {
                var box = levels[levels.Count - 1].Data.Box;
                return box != Guid.Empty && ElementIO.Find(doc, box) != null ? box : Guid.Empty;
            }
            var boxes = ElementIO.All(doc, ElementKind.DatumBox).Take(2).ToList();
            return boxes.Count == 1 ? boxes[0].Id : Guid.Empty;
        }

        /// <summary>
        /// The outline a new box takes around levels: theirs when they all share one (nothing moves), otherwise the
        /// rectangle around all of them in project axes (its first side runs east along the south). Z = 0.
        /// </summary>
        public static Polyline FitOutline(RhinoDoc doc, IList<LevelInfo> levels)
        {
            double tol = Math.Max(doc.ModelAbsoluteTolerance, 1e-9);
            if (levels == null || levels.Count == 0)
                return LevelElement.Normalize(LevelElement.DefaultFootprint(doc, 0), 0);
            var first = LevelElement.Normalize(levels[0].Footprint, 0);
            if (levels.All(l => SamePlan(LevelElement.Normalize(l.Footprint, 0), first, tol)))
                return first;
            var plane = SiteModel.Get(doc).Project;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var l in levels)
                foreach (var p in l.Footprint)
                {
                    plane.ClosestParameter(p, out double s, out double t);
                    minX = Math.Min(minX, s); maxX = Math.Max(maxX, s);
                    minY = Math.Min(minY, t); maxY = Math.Max(maxY, t);
                }
            return Rectangle(plane, minX, minY, maxX, maxY);
        }

        /// <summary>A rectangle in a plane's axes, counter-clockwise from its (minX, minY) corner, at Z = 0.</summary>
        public static Polyline Rectangle(Plane plane, double minX, double minY, double maxX, double maxY)
        {
            var pts = new[]
            {
                plane.PointAt(minX, minY), plane.PointAt(maxX, minY), plane.PointAt(maxX, maxY), plane.PointAt(minX, maxY), plane.PointAt(minX, minY),
            };
            return LevelElement.Normalize(new Polyline(pts), 0);
        }

        /// <summary>Angle of a box's first side from project east, counter-clockwise, in degrees (-180, 180].</summary>
        public static double Rotation(RhinoDoc doc, Polyline outline)
        {
            var east = SiteModel.Get(doc).Project.XAxis;
            var side = outline[1] - outline[0];
            double a = RhinoMath.ToDegrees(Math.Atan2(side.Y, side.X) - Math.Atan2(east.Y, east.X));
            while (a <= -180)
                a += 360;
            while (a > 180)
                a -= 360;
            return a;
        }

        public static string NextName(RhinoDoc doc)
        {
            var names = new HashSet<string>(All(doc).Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
            for (int i = 1; ; i++)
            {
                string n = "Datum Box " + i;
                if (!names.Contains(n))
                    return n;
            }
        }

        static string UniqueName(RhinoDoc doc, string wanted, Guid self)
        {
            var names = new HashSet<string>(All(doc).Where(b => b.Id != self).Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(wanted))
                return wanted;
            for (int i = 2; ; i++)
            {
                string n = wanted + " (" + i + ")";
                if (!names.Contains(n))
                    return n;
            }
        }

        // ---- create and edit --------------------------------------------------------------------

        /// <summary>
        /// Add a box with this plan outline and put the levels in it: each takes the outline at its own elevation,
        /// with its heads on the sides they were on.
        /// </summary>
        public static Guid Create(UpdateContext ctx, Polyline outline, IEnumerable<Guid> levels, string name = null, IEnumerable<Guid> grids = null)
        {
            var doc = ctx.Doc;
            var plan = LevelElement.Normalize(outline, 0);
            if (plan.SegmentCount < 3)
                return Guid.Empty;
            var data = new DatumBoxData
            {
                Name = UniqueName(doc, string.IsNullOrWhiteSpace(name) ? NextName(doc) : name.Trim(), Guid.Empty),
                Points = ElementIO.Flatten(plan),
            };
            var a = Styles.Attributes(doc, ElementKind.DatumBox, null);
            a.Name = data.Name;
            a.PlotWeightSource = ObjectPlotWeightSource.PlotWeightFromObject;
            a.PlotWeight = -1;
            a = ElementIO.With(a, ElementKind.DatumBox, data);
            double z = outline.Count > 0 ? outline[0].Z : 0;
            var id = doc.Objects.AddCurve(new PolylineCurve(At(plan, z)), a);
            if (id == Guid.Empty)
                return id;
            Service.SkunkService.Register(doc, id);
            foreach (var level in (levels ?? Enumerable.Empty<Guid>()).ToList())
                Attach(ctx, level, id);
            Update(ctx, doc.Objects.FindId(id), UpdateReason.Created);
            foreach (var grid in (grids ?? Enumerable.Empty<Guid>()).ToList())
                GridElement.Attach(ctx, grid, id);
            return id;
        }

        /// <summary>
        /// Put a level in a box - it takes the box's outline at its elevation, its heads staying on the sides they
        /// were on - or, with an empty id, take it out (it keeps its extents). True when the level changed.
        /// </summary>
        public static bool Attach(UpdateContext ctx, Guid levelId, Guid boxId)
        {
            var doc = ctx.Doc;
            var level = ElementIO.Find(doc, levelId);
            var data = level == null ? null : ElementIO.Read<LevelData>(level);
            if (data == null || !LevelElement.TryFootprint(level.Geometry, out var pl))
                return false;
            var old = data.Box;
            if (boxId == Guid.Empty)
            {
                if (old == Guid.Empty)
                    return false;
                data.Box = Guid.Empty;
                ElementIO.Write(doc, level, ElementKind.Level, data);
                LevelElement.Update(ctx, doc.Objects.FindId(levelId), UpdateReason.Context);
                Fit(ctx, old);
                return true;
            }
            if (!TryOutline(doc, boxId, out var outline))
                return false;
            double z = pl[0].Z;
            var before = LevelElement.Normalize(pl, z);
            var after = LevelElement.Normalize(outline, z);
            LevelElement.MapHeads(data, before, after);
            data.Box = boxId;
            // recorded as the level's own extents, so taking the box's outline is not read as an edit of the level
            data.Points = ElementIO.Flatten(after);
            ElementIO.Write(doc, level, ElementKind.Level, data);
            if (!SamePolyline(before, after, ctx.Tol) && doc.Objects.Replace(levelId, new PolylineCurve(after), true))
                ctx.Modified++;
            LevelElement.Update(ctx, doc.Objects.FindId(levelId), UpdateReason.Context);
            Fit(ctx, boxId);
            if (old != Guid.Empty && old != boxId)
                Fit(ctx, old);
            return true;
        }

        public static bool Rename(UpdateContext ctx, Guid id, string name)
        {
            var obj = ElementIO.Find(ctx.Doc, id);
            if (obj == null || ElementIO.KindOf(obj) != ElementKind.DatumBox || string.IsNullOrWhiteSpace(name))
                return false;
            var a = obj.Attributes.Duplicate();
            a.Name = UniqueName(ctx.Doc, name.Trim(), id);
            ctx.Doc.Objects.ModifyAttributes(obj, a, true);
            Apply(ctx, id);
            return true;
        }

        /// <summary>Turn a box about its middle so its first side lies at this angle from project east (degrees).</summary>
        public static bool SetRotation(UpdateContext ctx, Guid id, double degrees)
        {
            var obj = ElementIO.Find(ctx.Doc, id);
            if (obj == null || !TryOutline(ctx.Doc, id, out var outline))
                return false;
            double delta = degrees - Rotation(ctx.Doc, outline);
            if (Math.Abs(delta) < 1e-9)
                return false;
            var c = obj.Geometry.GetBoundingBox(true).Center;
            ctx.Doc.Objects.Transform(id, Transform.Rotation(RhinoMath.ToRadians(delta), Vector3d.ZAxis, c), true);
            ctx.Modified++;
            Apply(ctx, id);
            return true;
        }

        /// <summary>After an edit made with Skunk's events suppressed: the box, then the levels in it.</summary>
        public static void Apply(UpdateContext ctx, Guid id)
        {
            var obj = ElementIO.Find(ctx.Doc, id);
            if (obj == null)
                return;
            if (Update(ctx, obj, UpdateReason.Self))
            {
                foreach (var level in LevelIds(ctx.Doc, id))
                    LevelElement.Update(ctx, ElementIO.Find(ctx.Doc, level), UpdateReason.Context);
                foreach (var grid in GridIds(ctx.Doc, id))
                    GridElement.Update(ctx, ElementIO.Find(ctx.Doc, grid), UpdateReason.Context);
            }
            Fit(ctx, id);
        }

        // ---- update -----------------------------------------------------------------------------

        /// <summary>
        /// Keep a box in shape: a closed polyline (anything else goes back to its outline), flat just under its
        /// lowest level, counter-clockwise, named, never printed. True when its plan outline changed since the last
        /// update: the levels in it must follow.
        /// </summary>
        public static bool Update(UpdateContext ctx, RhinoObject obj, UpdateReason reason)
        {
            if (obj == null || ElementIO.KindOf(obj) != ElementKind.DatumBox)
                return false;
            var doc = ctx.Doc;
            var data = ElementIO.Read<DatumBoxData>(obj) ?? new DatumBoxData();
            bool isCopy = data.Id != Guid.Empty && data.Id != obj.Id;
            var stored = Stored(data);
            double z = obj.Geometry is Curve c0 ? c0.PointAtStart.Z : 0;
            if (!TryPlan(obj.Geometry, out var plan))
            {
                // no longer a closed polyline (rebuilt, filleted, opened): back to its outline
                plan = stored;
                if (plan == null)
                    return false;
            }
            bool outlineChanged = isCopy || stored == null || !SamePlan(plan, stored, ctx.Tol);

            // name: a copy gets the next one; a rename in Rhino's Properties (object name) is honoured
            string attrName = obj.Attributes.Name;
            if (isCopy)
                data.Name = UniqueName(doc, NextName(doc), obj.Id);
            else if (!string.IsNullOrWhiteSpace(attrName) && attrName != data.Name)
                data.Name = UniqueName(doc, attrName.Trim(), obj.Id);
            if (string.IsNullOrWhiteSpace(data.Name))
                data.Name = UniqueName(doc, NextName(doc), obj.Id);

            // height: from just under its lowest level to just over its highest; a box with no levels stays put
            var zs = LevelZs(doc, obj.Id);
            double margin = Margin(doc);
            double bottom = zs.Count > 0 ? zs.Min() - margin : z;
            double top = zs.Count > 0 ? zs.Max() + margin : z + EmptyHeight(doc);
            var want = At(plan, bottom);
            if (!(obj.Geometry is Curve cur) || !cur.TryGetPolyline(out var now) || !SamePolyline(now, want, ctx.Tol))
            {
                if (doc.Objects.Replace(obj.Id, new PolylineCurve(want), true))
                    ctx.Modified++;
                obj = doc.Objects.FindId(obj.Id);
            }

            data.Points = ElementIO.Flatten(plan);
            data.Top = top;
            data.Id = obj.Id;
            var a = obj.Attributes.Duplicate();
            a.Name = data.Name;
            // a drawing aid: it shows in model views and never prints
            a.PlotWeightSource = ObjectPlotWeightSource.PlotWeightFromObject;
            a.PlotWeight = -1;
            a.SetUserString(Keys.Kind, ElementKind.DatumBox.ToString());
            a.SetUserString(Keys.Data, Json.Write(data));
            if (!SameAttributes(a, obj.Attributes))
            {
                doc.Objects.ModifyAttributes(obj, a, true);
                ctx.Modified++;
            }
            return outlineChanged;
        }

        /// <summary>Fit a box's height to its levels (one moved up or down, joined or left).</summary>
        public static void Fit(UpdateContext ctx, Guid id)
        {
            if (id != Guid.Empty)
                Update(ctx, ElementIO.Find(ctx.Doc, id), UpdateReason.Context);
        }

        static bool SameAttributes(ObjectAttributes a, ObjectAttributes b) =>
            a.Name == b.Name && a.GetUserString(Keys.Data) == b.GetUserString(Keys.Data) &&
            a.GetUserString(Keys.Kind) == b.GetUserString(Keys.Kind) &&
            a.PlotWeightSource == b.PlotWeightSource && Math.Abs(a.PlotWeight - b.PlotWeight) < 1e-9;

        // ---- geometry helpers ---------------------------------------------------------------------

        /// <summary>The plan outline (counter-clockwise, Z = 0) of a closed polyline; false for anything else.</summary>
        static bool TryPlan(GeometryBase g, out Polyline outline)
        {
            outline = null;
            if (!(g is Curve c) || !c.TryGetPolyline(out var pl) || pl.Count < 4 || !pl.IsClosed)
                return false;
            outline = LevelElement.Normalize(pl, 0);
            return true;
        }

        static Polyline Stored(DatumBoxData d)
        {
            var pts = ElementIO.Unflatten(d?.Points);
            return pts.Count < 4 ? null : LevelElement.Normalize(new Polyline(pts), 0);
        }

        static Polyline At(Polyline plan, double z) => new Polyline(plan.Select(p => new Point3d(p.X, p.Y, z)));

        /// <summary>The same outline seen from above: same corners in the same order, heights ignored.</summary>
        public static bool SamePlan(Polyline a, Polyline b, double tol)
        {
            if (a == null || b == null || a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                double dx = a[i].X - b[i].X, dy = a[i].Y - b[i].Y;
                if (dx * dx + dy * dy > tol * tol)
                    return false;
            }
            return true;
        }

        static bool SamePolyline(Polyline a, Polyline b, double tol)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].DistanceTo(b[i]) > tol)
                    return false;
            return true;
        }

        /// <summary>Elevations of the levels in a box.</summary>
        static List<double> LevelZs(RhinoDoc doc, Guid box)
        {
            var zs = new List<double>();
            foreach (var o in LevelObjects(doc))
                if (ElementIO.Read<LevelData>(o)?.Box == box && LevelElement.TryFootprint(o.Geometry, out var pl))
                    zs.Add(pl[0].Z);
            return zs;
        }

        /// <summary>Elevations of every level.</summary>
        public static List<double> AllLevelZs(RhinoDoc doc)
        {
            var zs = new List<double>();
            foreach (var o in LevelObjects(doc))
                if (LevelElement.TryFootprint(o.Geometry, out var pl))
                    zs.Add(pl[0].Z);
            return zs;
        }

        /// <summary>The level objects, from the service's index when it runs (no walk over the whole model).</summary>
        static IEnumerable<RhinoObject> LevelObjects(RhinoDoc doc)
        {
            var index = Service.SkunkService.Instance?.Index(doc);
            if (index == null)
                return ElementIO.All(doc, ElementKind.Level).ToList();
            return index.Levels.ToList().Select(id => ElementIO.Find(doc, id))
                .Where(o => o != null && ElementIO.KindOf(o) == ElementKind.Level).ToList();
        }
    }
}
