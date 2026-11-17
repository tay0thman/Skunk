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
    /// <summary>
    /// Where a spot slope's arrow lies and what it measures. The arrow runs through its location on the
    /// host along the fall line there, reaching <see cref="Top"/> uphill and <see cref="Bottom"/> downhill.
    /// Its ends sit on the host straight below / above the line's ends, and the value is the slope between
    /// them, rise over plan run: on a plane that is the plane's slope; on a ruled or warped surface it is
    /// the slope where the arrow actually is.
    /// </summary>
    public sealed class SlopeArrow
    {
        /// <summary>The arrow's location on the host (its anchor).</summary>
        public Point3d Location;
        /// <summary>Unit direction of the arrow line, downhill.</summary>
        public Vector3d Down;
        /// <summary>Reach from the location uphill / downhill (model units).</summary>
        public double Top, Bottom;
        /// <summary>The arrow's ends on the host (on the arrow line when the host has no surface or edge).</summary>
        public Point3d TopEnd, BottomEnd;
        /// <summary>Height of the top end above the bottom end, their plan distance, and rise / run.</summary>
        public double Rise, Run, Slope;
        /// <summary>The arrowhead is at the top end.</summary>
        public bool PointsUp;
        /// <summary>The drawn arrow: tail, head (the arrowhead end) and the plane it lies in.</summary>
        public Point3d Tail, Head;
        public Plane Plane;
        /// <summary>
        /// The arrow sits <see cref="Offset"/> (model units) along <see cref="Side"/> from its line through the location:
        /// in a side view Side points up, so a positive offset is above the host line.
        /// </summary>
        public Vector3d Side;
        public double Offset;
    }

    /// <summary>
    /// Spot elevations and spot coordinates are Rhino leaders; spot slopes are aligned linear
    /// dimensions with the slope as text and one arrowhead. All three are native annotation objects,
    /// so they print, scale in details and export like any Rhino annotation; Skunk keeps their
    /// geometry and text in sync with the host and the coordinate systems.
    /// </summary>
    public static class SpotElement
    {
        // ---- creation ---------------------------------------------------------------------------

        /// <summary>Create a spot annotation and return its id.</summary>
        public static Guid Create(RhinoDoc doc, ElementKind kind, string typeName, RhinoObject host, Point3d point,
            Plane annotationPlane, Point3d? leaderEnd, bool leader, bool shoulder, Guid relativeLevel)
        {
            var ctx = new UpdateContext(doc);
            var type = ctx.Types.Find(kind, typeName);
            if (type == null)
                return Guid.Empty;
            var data = new SpotData
            {
                Type = type.Name,
                Anchor = Anchors.Capture(host, point),
                Leader = leader,
                Shoulder = shoulder,
                RelativeLevel = relativeLevel,
            };
            data.SetPlane(annotationPlane);
            var sample = Anchors.Evaluate(doc, data.Anchor);
            var tip = sample.Found ? sample.Point : point;
            var plane = data.AnnotationPlane(tip);
            if (kind != ElementKind.SpotSlope)
            {
                if (leaderEnd.HasValue && plane.ClosestParameter(leaderEnd.Value, out double s, out double t) && Math.Abs(s) + Math.Abs(t) > ctx.Tol * 10)
                    data.Offset = new[] { s, t };
                else
                {
                    double sym = DocUnits.PaperToModel(doc, 3.175);
                    data.Offset = new[] { sym * 2.5, sym * 2.5 };
                }
            }

            GeometryBase geometry = Build(ctx, kind, type, data, sample, out var fingerprint);
            if (geometry == null)
                return Guid.Empty;
            var attributes = Styles.Attributes(doc, kind, type);
            data.Points = fingerprint;
            attributes = ElementIO.With(attributes, kind, data);
            Guid id = AddGeometry(doc, geometry, attributes);
            if (id != Guid.Empty)
            {
                var obj = doc.Objects.FindId(id);
                ElementIO.Write(doc, obj, kind, data);
                Service.SkunkService.Register(doc, id);
            }
            return id;
        }

        static Guid AddGeometry(RhinoDoc doc, GeometryBase g, ObjectAttributes a)
        {
            switch (g)
            {
                case Leader l: return doc.Objects.AddLeader(l, a);
                case LinearDimension d: return doc.Objects.AddLinearDimension(d, a);
                default: return doc.Objects.Add(g, a);
            }
        }

        // ---- update -----------------------------------------------------------------------------

        /// <summary>
        /// Bring a spot up to date. Interprets user edits first: a moved / grip-edited / copied spot is
        /// re-attached to its host at the point closest to where the user put it (the text offset is
        /// kept); then the value and the geometry are rebuilt from the host and the coordinate system.
        /// </summary>
        public static bool Update(UpdateContext ctx, RhinoObject obj, UpdateReason reason)
        {
            var doc = ctx.Doc;
            var kind = ElementIO.KindOf(obj);
            var data = ElementIO.Read<SpotData>(obj);
            if (data == null)
                return false;
            var type = ctx.Types.Find(kind, data.Type);
            if (type == null)
                return false;
            if (data.Type != type.Name)
                data.Type = type.Name;

            bool isCopy = data.Id != Guid.Empty && data.Id != obj.Id;
            var current = Fingerprint(obj.Geometry);
            var last = ElementIO.Unflatten(data.Points);
            bool edited = isCopy || (last.Count > 0 && current.Count > 0 && !ElementIO.SamePoints(current, last, ctx.Tol * 10));

            if (edited)
                InterpretEdit(ctx, kind, data, obj.Geometry, current, last, isCopy);

            var sample = Anchors.Evaluate(doc, data.Anchor);
            if (sample.HostMissing && ctx.Settings.DeleteSpotsWithHost && !isCopy && (reason & UpdateReason.Host) != 0)
            {
                doc.Objects.Delete(obj, true, true);
                ctx.Modified++;
                return true;
            }
            if (!sample.Found)
                return false;

            var geometry = Build(ctx, kind, type, data, sample, out var fingerprint);
            if (geometry == null)
                return false;

            bool changed = false;
            var newPts = ElementIO.Unflatten(fingerprint);
            bool geometryDiffers = !ElementIO.SamePoints(newPts, current, ctx.Tol) || ElementIO.NormalizeText(TextOf(obj.Geometry)) != ElementIO.NormalizeText(data.Text) ||
                                   StyleIdOf(obj.Geometry) != StyleIdOf(geometry) || (kind == ElementKind.SpotSlope && Turned(obj.Geometry, geometry));
            if (geometryDiffers)
            {
                if (doc.Objects.Replace(obj.Id, geometry, true))
                {
                    changed = true;
                    ctx.Modified++;
                }
                obj = doc.Objects.FindId(obj.Id) ?? obj;
            }
            data.Points = fingerprint;
            if (ApplyTypeAttributes(doc, obj, type))
                obj = doc.Objects.FindId(obj.Id) ?? obj;
            if (ElementIO.Write(doc, obj, kind, data))
                changed = true;
            return changed;
        }

        /// <summary>
        /// A slope arrow's plane turned (its text belongs on the other side of the arrow) while its points stayed: an arrow in
        /// a side view moved from above its host line to below it lands where it is drawn, but its text must follow.
        /// </summary>
        static bool Turned(GeometryBase was, GeometryBase now) =>
            was is LinearDimension a && now is LinearDimension b && (a.Plane.YAxis * b.Plane.YAxis < 1 - 1e-9 || a.Plane.ZAxis * b.Plane.ZAxis < 1 - 1e-9);

        static bool ApplyTypeAttributes(RhinoDoc doc, RhinoObject obj, SkunkType type)
        {
            var a = obj.Attributes.Duplicate();
            Styles.ApplyTypeAttributes(a, type);
            if (a.ColorSource == obj.Attributes.ColorSource && a.ObjectColor == obj.Attributes.ObjectColor &&
                a.PlotWeightSource == obj.Attributes.PlotWeightSource && Math.Abs(a.PlotWeight - obj.Attributes.PlotWeight) < 1e-9)
                return false;
            return doc.Objects.ModifyAttributes(obj, a, true);
        }

        /// <summary>Read the user's edit of the annotation back into the anchor / offset / plane.</summary>
        static void InterpretEdit(UpdateContext ctx, ElementKind kind, SpotData data, GeometryBase g, List<Point3d> current, List<Point3d> last, bool isCopy)
        {
            if (kind == ElementKind.SpotSlope)
            {
                InterpretSlopeEdit(ctx, data, g, current, last, isCopy);
                return;
            }

            if (!(g is Leader leader) || current.Count < 2)
                return;
            var tip = current[0];
            bool rigid = !isCopy && last.Count == current.Count && RigidMove(current, last, ctx.Tol * 10);
            if (isCopy)
                RehostCopy(ctx, data, tip);
            Anchors.Reanchor(ctx.Doc, data.Anchor, tip);
            var sample = Anchors.Evaluate(ctx.Doc, data.Anchor);
            var newTip = sample.Found ? sample.Point : tip;
            var lp = leader.Plane;
            data.SetPlane(lp);
            var plane = data.AnnotationPlane(newTip);
            if (rigid || isCopy)
            {
                // the whole annotation moved: keep the leader offset, snap the point back onto the host
                return;
            }
            // grip edit: keep the text where the user put it
            var end = current[current.Count - 1];
            if (plane.ClosestParameter(end, out double s, out double t))
                data.Offset = new[] { s, t };
        }

        /// <summary>
        /// Read the user's edit of a slope arrow back. One end grip dragged: that end's reach changes,
        /// measured along the arrow, so the arrow keeps following the slope (the sideways part of the drag
        /// is ignored). The arrow moved, rotated, copied, or its line dragged sideways: its location moves
        /// to where the arrow now is, on the host, and direction and value are measured again there.
        /// </summary>
        static void InterpretSlopeEdit(UpdateContext ctx, SpotData data, GeometryBase g, List<Point3d> current, List<Point3d> last, bool isCopy)
        {
            if (!(g is LinearDimension dim))
                return;
            var type = ctx.Types.Find<SpotSlopeType>(data.Type);
            if (type == null)
                return;
            var dp = dim.Plane;
            bool onHost = FollowsHost(type, data);
            // a flat arrow keeps the plane the user turned it into; a host-following one gets its plane from the host
            if (!onHost)
                data.SetPlane(dp);
            bool comparable = current.Count == SlopePointCount && last.Count == SlopePointCount;
            if (!comparable && !isCopy)
                return;   // drawn by an older Skunk (other points recorded): nothing to read back, it is rebuilt

            if (comparable && !isCopy)
            {
                double tol = ctx.Tol * 10;
                bool tailMoved = current[0].DistanceTo(last[0]) > tol;
                bool headMoved = current[1].DistanceTo(last[1]) > tol;
                var sample = tailMoved != headMoved ? Anchors.Evaluate(ctx.Doc, data.Anchor) : null;
                if (sample != null && sample.Found)
                {
                    // one end grip: that end's reach along the arrow line (the arrow keeps following the slope)
                    var before = MeasureSlope(ctx, type, data, sample);
                    bool topEnd = tailMoved != before.PointsUp;   // the tail is the top end unless the arrow points uphill
                    var grip = tailMoved ? current[0] : current[1];
                    // from the drawn grip back to the host end (the arrow is drawn just above and beside the host)
                    var hostPoint = grip - ((tailMoved ? before.Tail : before.Head) - (topEnd ? before.TopEnd : before.BottomEnd));
                    var toward = topEnd ? -before.Down : before.Down;
                    // reach is measured like the ends are placed: along the line in plan, so dragging a grip
                    // onto its own end changes nothing, even on a warped surface
                    var plan = new Vector3d(toward.X, toward.Y, 0);
                    var fromLocation = hostPoint - before.Location;
                    double reach = plan.SquareLength > 1e-6
                        ? new Vector3d(fromLocation.X, fromLocation.Y, 0) * plan / plan.SquareLength
                        : fromLocation * toward;
                    reach = Math.Max(reach, ctx.Tol * 10);
                    data.SlopeTop = topEnd ? reach : before.Top;
                    data.SlopeBottom = topEnd ? before.Bottom : reach;
                    return;
                }
            }

            // the arrow as a whole is somewhere else: its location is the matching point of the arrow as drawn now
            double length = Math.Max(DocUnits.PaperToModel(ctx.Doc, type.ArrowLengthMM), ctx.Tol * 100);
            double top = Math.Max(data.SlopeTop ?? length / 2.0, ctx.Tol * 10);
            double bottom = Math.Max(data.SlopeBottom ?? length / 2.0, ctx.Tol * 10);
            bool up = (type.Direction == SlopeDirection.Up) ^ data.FlipSlope;
            var tail = current.Count == SlopePointCount ? current[2] : dp.Origin;   // the arrow's ends on the dimension line
            var head = current.Count == SlopePointCount ? current[3] : dp.Origin;
            var topPoint = up ? head : tail;
            var bottomPoint = up ? tail : head;
            var onArrow = topPoint + (bottomPoint - topPoint) * (top / (top + bottom));
            if (PlacedInSideView(data))
            {
                // in a side view an arrow moved up or down off its host line stays there (that distance is its offset);
                // along the line it slides, measuring where it now is
                if (isCopy)
                    RehostCopy(ctx, data, onArrow);
                Anchors.Reanchor(ctx.Doc, data.Anchor, onArrow);
                var at = Anchors.Evaluate(ctx.Doc, data.Anchor);
                if (at.Found)
                {
                    var now = MeasureSlope(ctx, type, data, at);
                    data.SlopeOffsetMM = (onArrow - at.Point) * now.Side / DocUnits.PaperToModel(ctx.Doc, 1.0);
                }
                return;
            }
            double offset = DocUnits.PaperToModel(ctx.Doc, data.SlopeOffsetMM ?? type.OffsetFromModelMM);
            double lift = onHost ? SlopeLift(ctx, type) : 0;
            var location = onArrow - dp.YAxis * offset - dp.ZAxis * lift;
            if (isCopy)
                RehostCopy(ctx, data, location);
            // straight down / up onto the host, so the arrow lands at the plan position it was moved to
            if (Anchors.OnHost(Anchors.Evaluate(ctx.Doc, data.Anchor), location, ctx.Tol, out Point3d below))
                location = below;
            Anchors.Reanchor(ctx.Doc, data.Anchor, location);
        }

        /// <summary>
        /// A spot copied together with its host belongs to the copied host: when the copy no longer
        /// touches its original host, look for an object added in the same operation that it touches.
        /// </summary>
        static void RehostCopy(UpdateContext ctx, SpotData data, Point3d point)
        {
            if (ctx.Added.Count == 0)
                return;
            double reach = ctx.Tol * 10;
            if (Distance(ElementIO.Find(ctx.Doc, data.Anchor.Host), point) <= reach)
                return;
            foreach (var id in ctx.Added)
            {
                var o = ElementIO.Find(ctx.Doc, id);
                if (o == null || o.Id == data.Id || !Anchors.CanHost(o) || ElementIO.KindOf(o) != ElementKind.None)
                    continue;
                if (Distance(o, point) <= reach)
                {
                    data.Anchor.Host = o.Id;
                    return;
                }
            }
        }

        static double Distance(RhinoObject host, Point3d point)
        {
            if (host == null)
                return double.MaxValue;
            var probe = Anchors.Capture(host, point);
            var p = probe.LastPoint;
            return probe.Kind == HostKind.Free || !p.IsValid ? double.MaxValue : p.DistanceTo(point);
        }

        static bool RigidMove(List<Point3d> a, List<Point3d> b, double tol)
        {
            var d0 = a[0] - b[0];
            for (int i = 1; i < a.Count; i++)
                if ((a[i] - b[i] - d0).Length > tol)
                    return false;
            return true;
        }

        // ---- geometry ---------------------------------------------------------------------------

        static GeometryBase Build(UpdateContext ctx, ElementKind kind, SkunkType type, SpotData data, HostSample sample, out double[] fingerprint)
        {
            fingerprint = null;
            var style = ctx.Style(type);
            switch (kind)
            {
                case ElementKind.SpotElevation:
                    {
                        string text = ElevationText(ctx, (SpotElevationType)type, data, sample, out double value);
                        data.Text = text;
                        data.Value = value;
                        var leader = BuildLeader(ctx, data, sample.Point, text, style, ((SpotElevationType)type).SymbolSizeMM, out var pts);
                        fingerprint = ElementIO.Flatten(pts);
                        return leader;
                    }
                case ElementKind.SpotCoordinate:
                    {
                        string text = CoordinateText(ctx, (SpotCoordinateType)type, data, sample, out double value);
                        data.Text = text;
                        data.Value = value;
                        var leader = BuildLeader(ctx, data, sample.Point, text, style, ((SpotCoordinateType)type).SymbolSizeMM, out var pts);
                        fingerprint = ElementIO.Flatten(pts);
                        return leader;
                    }
                case ElementKind.SpotSlope:
                    {
                        var dim = BuildSlope(ctx, (SpotSlopeType)type, data, sample, style, out string text, out double value);
                        data.Text = text;
                        data.Value = value;
                        if (dim == null)
                            return null;
                        fingerprint = ElementIO.Flatten(SlopePoints(dim));
                        return dim;
                    }
            }
            return null;
        }

        static Leader BuildLeader(UpdateContext ctx, SpotData data, Point3d tip, string text, DimensionStyle style, double symbolMM, out Point3d[] pts)
        {
            var plane = data.AnnotationPlane(tip);
            double ox = data.Offset != null && data.Offset.Length > 1 ? data.Offset[0] : 1;
            double oy = data.Offset != null && data.Offset.Length > 1 ? data.Offset[1] : 1;
            if (!data.Leader)
            {
                double sym = DocUnits.PaperToModel(ctx.Doc, symbolMM);
                ox = sym * 0.6;
                oy = 0;
            }
            var end = plane.PointAt(ox, oy);
            if (end.DistanceTo(tip) < ctx.Tol * 10)
                end = plane.PointAt(ctx.Tol * 100, 0);
            pts = new[] { tip, end };
            var leader = Leader.Create(text, plane, style, pts);
            if (leader == null)
                return null;
            bool landing = data.Leader && data.Shoulder;
            if (leader.LeaderHasLanding != landing)
                leader.LeaderHasLanding = landing;
            return leader;
        }

        static LinearDimension BuildSlope(UpdateContext ctx, SpotSlopeType t, SpotData data, HostSample s, DimensionStyle style, out string text, out double value)
        {
            var arrow = MeasureSlope(ctx, t, data, s);
            value = arrow.Slope;
            text = UnitFormat.Compose(data.Prefix, "", UnitFormat.Slope(value, t.Slope), data.Suffix, true);
            // zero-length extension lines: the definition points are the arrow's ends, its two grips
            var dim = LinearDimension.Create(Rhino.Geometry.AnnotationType.Aligned, style, arrow.Plane, arrow.Plane.XAxis, arrow.Tail, arrow.Head, arrow.Plane.Origin, 0.0);
            if (dim == null)
                return null;
            dim.PlainText = text;
            return dim;
        }

        /// <summary>Measure a slope arrow at its location (see <see cref="SlopeArrow"/>).</summary>
        public static SlopeArrow MeasureSlope(UpdateContext ctx, SpotSlopeType t, SpotData data, HostSample s)
        {
            var a = new SlopeArrow { Location = s.Point };
            double pointSlope = Anchors.Slope(s, out Vector3d down);
            Vector3d dir, n;
            bool onHost = FollowsHost(t, data);
            if (!onHost || !HostPlane(s, down, out dir, out n))
            {
                // flat in the plane the spot was placed in (the view's CPlane; a side view's always)
                onHost = false;
                var basePlane = data.AnnotationPlane(s.Point);
                n = basePlane.ZAxis;
                dir = down - (down * n) * n;
                if (!dir.Unitize())
                    dir = basePlane.XAxis;
            }
            a.Down = dir;
            double length = Math.Max(DocUnits.PaperToModel(ctx.Doc, t.ArrowLengthMM), ctx.Tol * 100);
            a.Top = Math.Max(data.SlopeTop ?? length / 2.0, ctx.Tol * 10);
            a.Bottom = Math.Max(data.SlopeBottom ?? length / 2.0, ctx.Tol * 10);
            var top = s.Point - dir * a.Top;
            var bottom = s.Point + dir * a.Bottom;

            // the ends on the host straight below / above the line's ends (on a curve: on the piece under the
            // spot, so a polyline segment's own slope), and the slope between them
            bool measured = Anchors.SlopeEnd(s, top, ctx.Tol, out Point3d topOnHost) & Anchors.SlopeEnd(s, bottom, ctx.Tol, out Point3d bottomOnHost);
            a.TopEnd = measured ? topOnHost : top;
            a.BottomEnd = measured ? bottomOnHost : bottom;
            var span = a.TopEnd - a.BottomEnd;
            a.Rise = span.Z;
            a.Run = Math.Sqrt(span.X * span.X + span.Y * span.Y);
            a.Slope = !measured ? pointSlope : a.Run > 1e-12 ? Math.Abs(a.Rise) / a.Run : double.PositiveInfinity;

            // the drawn arrow: on the host (or flat in its plane), arrowhead downhill unless it points up
            a.PointsUp = (t.Direction == SlopeDirection.Up) ^ data.FlipSlope;
            var drawnTop = onHost ? a.TopEnd : top;
            var drawnBottom = onHost ? a.BottomEnd : bottom;
            var tail = a.PointsUp ? drawnBottom : drawnTop;
            var head = a.PointsUp ? drawnTop : drawnBottom;
            var x = head - tail;
            if (!x.Unitize())
                x = a.PointsUp ? -dir : dir;
            var z = n - (n * x) * x;
            if (!z.Unitize())
                z = n;
            var y = Vector3d.CrossProduct(z, x);
            if (!y.Unitize())
            {
                y = Vector3d.CrossProduct(Vector3d.ZAxis, x);
                if (!y.Unitize())
                    y = Vector3d.YAxis;
            }
            // just above the host (so it shows in shaded views) and beside its location by its offset (the instance's, else
            // the type's); in a side view the offset is measured up from the host line, where the arrow is moved up or down
            bool sideView = PlacedInSideView(data);
            a.Side = sideView && y * Vector3d.ZAxis < 0 ? -y : y;
            a.Offset = DocUnits.PaperToModel(ctx.Doc, data.SlopeOffsetMM ?? t.OffsetFromModelMM);
            var shift = a.Side * a.Offset + z * (onHost ? SlopeLift(ctx, t) : 0);
            a.Tail = tail + shift;
            a.Head = head + shift;
            // in a side view the text (above the dimension line) stays on the far side of the arrow from the host line; the
            // style draws text forward, so it reads whichever way the plane faces
            var planeY = sideView ? (a.Offset >= 0 ? a.Side : -a.Side) : y;
            a.Plane = new Plane(a.Tail + (a.Head - a.Tail) * 0.5, x, planeY);
            return a;
        }

        /// <summary>
        /// The spot was placed in a side view: an elevation or a section, whose CPlane is not level (more than about 8°
        /// off). A slope arrow and its text then lie in that CPlane whatever the type's placement: drawn on the host they
        /// would lie in a plane the side view sees edge-on (a profile drawn in the elevation, a roof seen from the side).
        /// </summary>
        public static bool PlacedInSideView(SpotData data) => Math.Abs(data.AnnotationPlane(Point3d.Origin).ZAxis.Z) < 0.99;

        /// <summary>A slope arrow follows its host: its type says so and it was not placed in a side view.</summary>
        public static bool FollowsHost(SpotSlopeType t, SpotData data) => t.Placement == SlopePlacement.OnHost && !PlacedInSideView(data);

        /// <summary>
        /// The plane a slope arrow lies in when it follows its host: the surface's tangent plane (normal
        /// turned up) with the arrow down the fall line, or for an edge / curve the plane through the
        /// tangent and the horizontal across it. False when the host gives no direction (points, free spots).
        /// </summary>
        static bool HostPlane(HostSample s, Vector3d down, out Vector3d dir, out Vector3d normal)
        {
            dir = down;
            normal = Vector3d.Zero;
            if (!s.Normal.IsZero)
            {
                normal = s.Normal;
                normal.Unitize();
                if (normal.Z < 0)
                    normal = -normal;
                if (dir.IsZero)
                    dir = Vector3d.XAxis;
                dir = dir - (dir * normal) * normal;
                if (!dir.Unitize())
                {
                    dir = Vector3d.YAxis - (Vector3d.YAxis * normal) * normal;
                    if (!dir.Unitize())
                        return false;
                }
                return true;
            }
            if (!s.Tangent.IsZero && !dir.IsZero)
            {
                dir.Unitize();
                var across = Vector3d.CrossProduct(Vector3d.ZAxis, dir);
                if (!across.Unitize())
                    return false;
                normal = Vector3d.CrossProduct(dir, across);
                normal.Unitize();
                return true;
            }
            return false;
        }

        /// <summary>Gap between a host-following slope arrow and its surface (keeps it visible in shaded views).</summary>
        static double SlopeLift(UpdateContext ctx, SpotSlopeType t) =>
            Math.Max(DocUnits.PaperToModel(ctx.Doc, t.TextSizeMM) * 0.15, ctx.Tol * 10);

        // ---- text -------------------------------------------------------------------------------

        public static string ElevationText(UpdateContext ctx, SpotElevationType t, SpotData d, HostSample s, out double value)
        {
            var frame = ctx.Frame(t.ElevationOrigin, t.OriginCPlane, d.RelativeLevel);
            var units = TypeTable.Resolve(ctx.Doc, t.Units);
            double mpu = DocUnits.MetersPerUnit(ctx.Doc);
            string F(double v) => UnitFormat.Length(v * mpu, units);
            string mark = frame.Valid ? "" : " (?)";
            var p = s.Point;
            value = frame.Elevation(p);
            var display = d.Display ?? t.Display;
            if (display == ElevationDisplay.Actual)
                return UnitFormat.Compose(d.Prefix, t.ElevationIndicator, F(value), d.Suffix, t.IndicatorAsPrefix) + mark;

            Anchors.TopBottom(ctx.Doc, d.Anchor, p, out double top, out double bottom);
            double topValue = frame.Elevation(new Point3d(p.X, p.Y, top));
            double bottomValue = frame.Elevation(new Point3d(p.X, p.Y, bottom));
            string topText = UnitFormat.Compose(d.Prefix, t.TopIndicator, F(topValue), d.Suffix, t.IndicatorAsPrefix);
            string bottomText = UnitFormat.Compose(display == ElevationDisplay.TopAndBottom ? d.LowerPrefix : d.Prefix, t.BottomIndicator, F(bottomValue),
                display == ElevationDisplay.TopAndBottom ? d.LowerSuffix : d.Suffix, t.IndicatorAsPrefix);
            switch (display)
            {
                case ElevationDisplay.Top:
                    value = topValue;
                    return topText + mark;
                case ElevationDisplay.Bottom:
                    value = bottomValue;
                    return bottomText + mark;
                default:
                    value = topValue;
                    return topText + "\n" + bottomText + mark;
            }
        }

        public static string CoordinateText(UpdateContext ctx, SpotCoordinateType t, SpotData d, HostSample s, out double value)
        {
            var frame = ctx.Frame(t.CoordinateOrigin, t.OriginCPlane, Guid.Empty);
            var units = TypeTable.Resolve(ctx.Doc, t.Units);
            double mpu = DocUnits.MetersPerUnit(ctx.Doc);
            string F(double v) => UnitFormat.Length(v * mpu, units);
            var p = s.Point;
            value = frame.North(p);
            var lines = new List<string>();
            string Line(CoordinateValue cv, string prefix, string suffix)
            {
                switch (cv)
                {
                    case CoordinateValue.NorthSouth: return UnitFormat.Compose(prefix, t.NorthIndicator, F(frame.North(p)), suffix, t.IndicatorAsPrefix);
                    case CoordinateValue.EastWest: return UnitFormat.Compose(prefix, t.EastIndicator, F(frame.East(p)), suffix, t.IndicatorAsPrefix);
                    case CoordinateValue.Elevation: return UnitFormat.Compose(prefix, t.ElevationIndicator, F(frame.Elevation(p)), suffix, t.IndicatorAsPrefix);
                    default: return null;
                }
            }
            var top = Line(t.TopValue, d.Prefix, d.Suffix);
            var bottom = Line(t.BottomValue, d.LowerPrefix, d.LowerSuffix);
            if (top != null)
                lines.Add(top);
            if (bottom != null)
                lines.Add(bottom);
            if (t.IncludeElevation && t.TopValue != CoordinateValue.Elevation && t.BottomValue != CoordinateValue.Elevation)
                lines.Add(UnitFormat.Compose("", t.ElevationIndicator, F(frame.Elevation(p)), "", t.IndicatorAsPrefix));
            if (lines.Count == 0)
                lines.Add("?");
            if (!frame.Valid)
                lines[lines.Count - 1] += " (?)";
            return string.Join("\n", lines);
        }

        // ---- helpers ----------------------------------------------------------------------------

        /// <summary>Points that identify the annotation's placement (leader points / dimension plane).</summary>
        public static List<Point3d> Fingerprint(GeometryBase g)
        {
            switch (g)
            {
                case Leader l:
                    {
                        var plane = l.Plane;
                        var pts = l.Points2D;
                        if (pts == null)
                            return new List<Point3d>();
                        return pts.Select(p => plane.PointAt(p.X, p.Y)).ToList();
                    }
                case LinearDimension d:
                    return SlopePoints(d);
            }
            return new List<Point3d>();
        }

        /// <summary>
        /// The points of a slope arrow compared to read user edits: the two definition points (the arrow's
        /// end grips: tail, head), the arrow's ends on the dimension line, and the dimension line point.
        /// Skunk 1.1 and older recorded three plane points instead (<see cref="SlopePointCount"/> tells them apart).
        /// </summary>
        static List<Point3d> SlopePoints(LinearDimension d)
        {
            if (!d.Get3dPoints(out Point3d def1, out Point3d def2, out Point3d arrow1, out Point3d arrow2, out Point3d line, out Point3d _))
                return new List<Point3d>();
            return new List<Point3d> { def1, def2, arrow1, arrow2, line };
        }

        const int SlopePointCount = 5;

        static string TextOf(GeometryBase g)
        {
            switch (g)
            {
                case LinearDimension d: return d.PlainUserText ?? "";
                case AnnotationBase a: return a.PlainText ?? "";
            }
            return "";
        }

        static Guid StyleIdOf(GeometryBase g) => g is AnnotationBase a ? a.DimensionStyleId : Guid.Empty;

        /// <summary>Current text shown by a spot (for the Properties palette).</summary>
        public static string CurrentText(RhinoObject obj) => TextOf(obj?.Geometry);
    }
}
