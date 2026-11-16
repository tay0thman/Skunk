using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Skunk.Core;
using Skunk.Site;
using Skunk.Types;

namespace Skunk.Elements
{
    /// <summary>
    /// A north arrow that always points to true north (or project north): a block instance that
    /// Skunk re-orients whenever true north is rotated, and re-scales with the annotation scale.
    /// </summary>
    public static class NorthArrowElement
    {
        public const string BlockName = Keys.BlockPrefix + "North Arrow";

        public static int EnsureBlock(RhinoDoc doc)
        {
            var existing = doc.InstanceDefinitions.Find(BlockName);
            if (existing != null && !existing.IsDeleted)
                return existing.Index;
            var geometry = new List<GeometryBase>();
            geometry.Add(new ArcCurve(new Circle(Plane.WorldXY, 0.5)));
            var right = new Polyline(new[] { new Point3d(0, 0.42, 0), new Point3d(0.2, -0.36, 0), new Point3d(0, -0.2, 0), new Point3d(0, 0.42, 0) });
            var left = new Polyline(new[] { new Point3d(0, 0.42, 0), new Point3d(-0.2, -0.36, 0), new Point3d(0, -0.2, 0), new Point3d(0, 0.42, 0) });
            var leftCurve = new PolylineCurve(left);
            geometry.Add(new PolylineCurve(right));
            geometry.Add(leftCurve);
            var solid = doc.HatchPatterns.FindName("Solid");
            int pattern = solid != null ? solid.Index : doc.HatchPatterns.Add(HatchPattern.Defaults.Solid);
            var hatches = Hatch.Create(leftCurve, pattern, 0, 1, 1e-4);
            if (hatches != null && hatches.Length > 0)
                geometry.Add(hatches[0]);
            var style = doc.DimStyles.Current;
            var text = TextEntity.Create("N", new Plane(new Point3d(0, 0.58, 0), Vector3d.XAxis, Vector3d.YAxis), style, false, 0, 0);
            if (text != null)
            {
                text.TextHeight = 0.2;
                text.Justification = TextJustification.BottomCenter;
                geometry.Add(text);
            }
            var attributes = geometry.Select(_ => new ObjectAttributes
            {
                ColorSource = ObjectColorSource.ColorFromParent,
                PlotColorSource = ObjectPlotColorSource.PlotColorFromParent,
                PlotWeightSource = ObjectPlotWeightSource.PlotWeightFromParent,
                LayerIndex = 0,
            }).ToList();
            return doc.InstanceDefinitions.Add(BlockName, "Skunk north arrow (unit size, points to +Y)", Point3d.Origin, geometry, attributes);
        }

        public static Guid Create(RhinoDoc doc, Point3d point, bool trueNorth, string typeName)
        {
            var ctx = new UpdateContext(doc);
            var type = ctx.Types.Find<NorthArrowType>(typeName);
            int index = EnsureBlock(doc);
            if (index < 0)
                return Guid.Empty;
            var data = new NorthArrowData { TrueNorth = trueNorth, Type = type?.Name ?? "" };
            var xf = Desired(ctx, point, data, type);
            var a = ElementIO.With(Styles.Attributes(doc, ElementKind.NorthArrow, type), ElementKind.NorthArrow, data);
            var id = doc.Objects.AddInstanceObject(index, xf, a);
            if (id != Guid.Empty)
            {
                ElementIO.Write(doc, doc.Objects.FindId(id), ElementKind.NorthArrow, data);
                Service.SkunkService.Register(doc, id);
            }
            return id;
        }

        static Transform Desired(UpdateContext ctx, Point3d point, NorthArrowData data, NorthArrowType type)
        {
            var north = data.TrueNorth ? ctx.Site.TrueNorth : ctx.Site.ProjectNorth;
            double angle = Math.Atan2(north.Y, north.X) - Math.PI / 2.0;
            double size = DocUnits.PaperToModel(ctx.Doc, type?.SizeMM ?? 19.05);
            data.Size = size;
            return Transform.Translation(point - Point3d.Origin) * Transform.Rotation(angle, Vector3d.ZAxis, Point3d.Origin) * Transform.Scale(Point3d.Origin, size);
        }

        public static bool Update(UpdateContext ctx, RhinoObject obj)
        {
            if (!(obj is InstanceObject inst))
                return false;
            var data = ElementIO.Read<NorthArrowData>(obj) ?? new NorthArrowData();
            var type = ctx.Types.Find<NorthArrowType>(data.Type);
            var current = inst.InstanceXform;
            var point = inst.InsertionPoint;
            var desired = Desired(ctx, point, data, type);
            bool changed = false;
            if (!SameTransform(current, desired, ctx.Tol) && current.TryGetInverse(out var inv))
            {
                if (ctx.Doc.Objects.Transform(obj.Id, desired * inv, true) != Guid.Empty)
                {
                    ctx.Modified++;
                    changed = true;
                }
                obj = ctx.Doc.Objects.FindId(obj.Id);
            }
            if (ElementIO.Write(ctx.Doc, obj, ElementKind.NorthArrow, data))
                changed = true;
            return changed;
        }

        static bool SameTransform(Transform a, Transform b, double tol)
        {
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    if (Math.Abs(a[r, c] - b[r, c]) > Math.Max(tol, 1e-9) * (c == 3 ? 1 : 0.01))
                        return false;
            return true;
        }
    }

    /// <summary>
    /// Plan cut: a clipping plane at level + cut height that clips one viewport, with a depth that
    /// reaches below the level - a floor plan view range in Rhino.
    /// </summary>
    public static class PlanCutElement
    {
        public static Guid Ensure(RhinoDoc doc, Guid levelId, Guid viewportId)
        {
            var ctx = new UpdateContext(doc);
            var level = LevelElement.Info(doc, ElementIO.Find(doc, levelId), ctx.Types);
            if (level == null)
                return Guid.Empty;
            // one plan cut per viewport: re-use the one already clipping this viewport
            foreach (var o in ElementIO.All(doc, ElementKind.PlanCut))
            {
                var d = ElementIO.Read<PlanCutData>(o);
                if (d != null && d.Viewport == viewportId)
                {
                    d.Level = levelId;
                    ElementIO.Write(doc, o, ElementKind.PlanCut, d);
                    Update(ctx, doc.Objects.FindId(o.Id));
                    return o.Id;
                }
            }
            Build(ctx, level, out Plane plane, out double du, out double dv, out double depth);
            var a = Styles.Attributes(doc, ElementKind.PlanCut, null);
            a.Name = "Plan Cut: " + level.Name;
            var data = new PlanCutData { Level = levelId, Viewport = viewportId };
            a = ElementIO.With(a, ElementKind.PlanCut, data);
            var id = doc.Objects.AddClippingPlane(plane, du, dv, new[] { viewportId }, a);
            if (id == Guid.Empty)
                return id;
            Update(ctx, doc.Objects.FindId(id));
            Service.SkunkService.Register(doc, id);
            return id;
        }

        static void Build(UpdateContext ctx, LevelInfo level, out Plane plane, out double du, out double dv, out double depth)
        {
            var bb = new BoundingBox(level.Footprint);
            double cut = DocUnits.FromMeters(ctx.Doc, ctx.Settings.CutHeightMeters);
            depth = cut + DocUnits.FromMeters(ctx.Doc, ctx.Settings.ViewDepthMeters);
            var c = bb.Center;
            // the clipping plane normal points to the clipped side: everything above the cut is hidden
            plane = new Plane(new Point3d(c.X, c.Y, level.Z + cut), Vector3d.XAxis, Vector3d.YAxis);
            du = Math.Max(bb.Diagonal.X, ctx.Tol * 100);
            dv = Math.Max(bb.Diagonal.Y, ctx.Tol * 100);
        }

        public static bool Update(UpdateContext ctx, RhinoObject obj)
        {
            if (obj == null || !(obj.Geometry is ClippingPlaneSurface cps))
                return false;
            var data = ElementIO.Read<PlanCutData>(obj);
            var level = data == null ? null : LevelElement.Info(ctx.Doc, ElementIO.Find(ctx.Doc, data.Level), ctx.Types);
            if (level == null)
                return false;
            Build(ctx, level, out Plane plane, out double du, out double dv, out double depth);
            var g = (ClippingPlaneSurface)cps.Duplicate();
            bool same = g.Plane.Origin.DistanceTo(plane.Origin) < ctx.Tol && g.PlaneDepthEnabled && Math.Abs(g.PlaneDepth - depth) < ctx.Tol;
            if (same)
                return false;
            var xf = Transform.Translation(0, 0, plane.Origin.Z - g.Plane.Origin.Z);
            g.Transform(xf);
            g.PlaneDepth = depth;
            g.PlaneDepthEnabled = true;
            bool ok = ctx.Doc.Objects.Replace(obj.Id, g, true);
            if (ok)
                ctx.Modified++;
            return ok;
        }

        /// <summary>Remove the plan cut of a viewport (Go To Level with ViewRange=No).</summary>
        public static void Clear(RhinoDoc doc, Guid viewportId)
        {
            foreach (var o in ElementIO.All(doc, ElementKind.PlanCut).ToList())
            {
                var d = ElementIO.Read<PlanCutData>(o);
                if (d != null && d.Viewport == viewportId)
                    doc.Objects.Delete(o, true, true);
            }
        }
    }
}
