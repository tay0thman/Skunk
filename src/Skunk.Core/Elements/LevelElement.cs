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
    /// <summary>A level as seen by commands and dialogs.</summary>
    public sealed class LevelInfo
    {
        public Guid Id;
        public string Name;
        public double Z;
        public LevelData Data;
        public LevelType Type;
        public Polyline Footprint;
    }

    /// <summary>
    /// A level is a closed horizontal polyline (its plan extents) on the "Skunk::Levels" layer, drawn
    /// with a datum linetype. Seen from the side in an elevation or section view it is the level line;
    /// each footprint edge can carry a level head at either end (a leader with the head symbol, the
    /// level name and its elevation) in the vertical plane of that edge, facing outward - so heads read
    /// correctly in the Front / Right / Back / Left views and in details looking at the building.
    /// The level also owns a named CPlane (its work plane) and moves the objects attached to it.
    /// </summary>
    public static class LevelElement
    {
        // ---- queries ----------------------------------------------------------------------------

        public static bool TryGetZ(RhinoDoc doc, Guid id, out double z)
        {
            z = double.NaN;
            var obj = ElementIO.Find(doc, id);
            if (obj == null || ElementIO.KindOf(obj) != ElementKind.Level)
                return false;
            if (!TryFootprint(obj.Geometry, out var pl))
                return false;
            z = pl[0].Z;
            return true;
        }

        public static LevelInfo Info(RhinoDoc doc, RhinoObject obj, TypeLibrary types = null)
        {
            if (obj == null || ElementIO.KindOf(obj) != ElementKind.Level || !TryFootprint(obj.Geometry, out var pl))
                return null;
            var data = ElementIO.Read<LevelData>(obj) ?? new LevelData { Name = obj.Attributes.Name ?? "" };
            types = types ?? TypeTable.Get(doc);
            return new LevelInfo
            {
                Id = obj.Id,
                Name = string.IsNullOrEmpty(data.Name) ? obj.Attributes.Name : data.Name,
                Z = pl[0].Z,
                Data = data,
                Type = types.Find<LevelType>(data.Type),
                Footprint = pl,
            };
        }

        /// <summary>All levels, lowest first.</summary>
        public static List<LevelInfo> All(RhinoDoc doc)
        {
            var types = TypeTable.Get(doc);
            return ElementIO.All(doc, ElementKind.Level).Select(o => Info(doc, o, types)).Where(i => i != null)
                .OrderBy(i => i.Z).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string NextName(RhinoDoc doc)
        {
            var names = new HashSet<string>(All(doc).Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
            for (int i = names.Count + 1; ; i++)
            {
                string n = "Level " + i;
                if (!names.Contains(n))
                    return n;
            }
        }

        static string UniqueName(RhinoDoc doc, string wanted, Guid self)
        {
            var names = new HashSet<string>(All(doc).Where(l => l.Id != self).Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(wanted))
                return wanted;
            for (int i = 2; ; i++)
            {
                string n = wanted + " (" + i + ")";
                if (!names.Contains(n))
                    return n;
            }
        }

        /// <summary>The footprint new levels use: the last level's, or the model extents plus a margin.</summary>
        public static Polyline DefaultFootprint(RhinoDoc doc, double z)
        {
            var existing = All(doc);
            if (existing.Count > 0)
            {
                var pl = new Polyline(existing[existing.Count - 1].Footprint);
                for (int i = 0; i < pl.Count; i++)
                    pl[i] = new Point3d(pl[i].X, pl[i].Y, z);
                return pl;
            }
            var bb = BoundingBox.Empty;
            foreach (var o in doc.Objects.GetObjectList(new ObjectEnumeratorSettings { NormalObjects = true, LockedObjects = true, HiddenObjects = false }))
            {
                if (ElementIO.KindOf(o) != ElementKind.None)
                    continue;
                bb.Union(o.Geometry.GetBoundingBox(false));
            }
            var site = SiteModel.Get(doc);
            var plane = site.Project;
            double minX, minY, maxX, maxY;
            if (bb.IsValid && bb.Diagonal.Length > doc.ModelAbsoluteTolerance)
            {
                double margin = Math.Max(bb.Diagonal.X, bb.Diagonal.Y) * DocStore.Settings(doc).ExtentsMargin;
                // extents in project axes so heads face the project's elevations
                minX = minY = double.MaxValue;
                maxX = maxY = double.MinValue;
                foreach (var c in bb.GetCorners())
                {
                    plane.ClosestParameter(c, out double s, out double t);
                    minX = Math.Min(minX, s); maxX = Math.Max(maxX, s);
                    minY = Math.Min(minY, t); maxY = Math.Max(maxY, t);
                }
                minX -= margin; minY -= margin; maxX += margin; maxY += margin;
            }
            else
            {
                double half = DocUnits.FromMeters(doc, 15.0);
                minX = minY = -half;
                maxX = maxY = half;
            }
            var pts = new[]
            {
                plane.PointAt(minX, minY), plane.PointAt(maxX, minY), plane.PointAt(maxX, maxY), plane.PointAt(minX, maxY), plane.PointAt(minX, minY),
            };
            var poly = new Polyline(pts.Select(p => new Point3d(p.X, p.Y, z)));
            return poly;
        }

        // ---- create -----------------------------------------------------------------------------

        public static Guid Create(RhinoDoc doc, string typeName, string name, double z, Polyline footprint)
        {
            var ctx = new UpdateContext(doc);
            var type = ctx.Types.Find<LevelType>(typeName);
            var pl = Normalize(footprint, z);
            var data = new LevelData
            {
                Type = type?.Name ?? "",
                Name = UniqueName(doc, string.IsNullOrWhiteSpace(name) ? NextName(doc) : name.Trim(), Guid.Empty),
            };
            var a = Styles.Attributes(doc, ElementKind.Level, type);
            a.Name = data.Name;
            a.LinetypeSource = ObjectLinetypeSource.LinetypeFromLayer;
            a = ElementIO.With(a, ElementKind.Level, data);
            var id = doc.Objects.AddCurve(new PolylineCurve(pl), a);
            if (id == Guid.Empty)
                return id;
            Update(ctx, doc.Objects.FindId(id), UpdateReason.Created);
            Service.SkunkService.Register(doc, id);
            return id;
        }

        // ---- update -----------------------------------------------------------------------------

        public static bool Update(UpdateContext ctx, RhinoObject obj, UpdateReason reason)
        {
            var doc = ctx.Doc;
            if (obj == null || !TryFootprint(obj.Geometry, out var pl))
                return false;
            var data = ElementIO.Read<LevelData>(obj) ?? new LevelData();
            var type = ctx.Types.Find<LevelType>(data.Type);
            if (type == null)
                return false;
            data.Type = type.Name;
            bool isCopy = data.Id != Guid.Empty && data.Id != obj.Id;
            bool changed = false;

            double z = DecideZ(pl, data.Z, ctx.Tol);
            var normalized = Normalize(pl, z);
            if (!SamePolyline(normalized, pl, ctx.Tol))
            {
                if (doc.Objects.Replace(obj.Id, new PolylineCurve(normalized), true))
                {
                    ctx.Modified++;
                    changed = true;
                }
                obj = doc.Objects.FindId(obj.Id);
            }

            // name: copies get a new unique name; a rename in Rhino's Properties (object name) is honoured
            string attrName = obj.Attributes.Name;
            if (isCopy)
            {
                data.Name = UniqueName(doc, NextName(doc), obj.Id);
                data.Heads.Clear();
                data.CPlane = "";
            }
            else if (!string.IsNullOrWhiteSpace(attrName) && attrName != data.Name)
                data.Name = UniqueName(doc, attrName.Trim(), obj.Id);
            if (string.IsNullOrWhiteSpace(data.Name))
                data.Name = UniqueName(doc, NextName(doc), obj.Id);

            // elevation change moves the objects attached to the level (hosted elements follow)
            if (!isCopy && !double.IsNaN(data.Z) && Math.Abs(z - data.Z) > ctx.Tol)
                MoveAttached(ctx, obj.Id, z - data.Z);
            data.Z = z;
            data.Points = ElementIO.Flatten(normalized);
            ctx.ForgetLevel(obj.Id);

            if (SyncHeads(ctx, obj, data, type, normalized))
                changed = true;
            SyncCPlane(ctx, data, z);

            var a = obj.Attributes.Duplicate();
            if (a.Name != data.Name)
                a.Name = data.Name;
            Styles.ApplyTypeAttributes(a, type);
            ApplyLinePattern(doc, a, string.IsNullOrEmpty(data.LinePattern) ? type.LinePattern : data.LinePattern, type.LinePatternScale);
            data.Id = obj.Id;
            a.SetUserString(Keys.Kind, ElementKind.Level.ToString());
            a.SetUserString(Keys.Data, Json.Write(data));
            if (!SameAttributes(a, obj.Attributes))
            {
                doc.Objects.ModifyAttributes(obj, a, true);
                changed = true;
            }
            return changed;
        }

        static bool SameAttributes(ObjectAttributes a, ObjectAttributes b) =>
            a.Name == b.Name && a.GetUserString(Keys.Data) == b.GetUserString(Keys.Data) &&
            a.GetUserString(Keys.Kind) == b.GetUserString(Keys.Kind) && a.ColorSource == b.ColorSource &&
            a.ObjectColor == b.ObjectColor && a.PlotWeightSource == b.PlotWeightSource &&
            a.LinetypeSource == b.LinetypeSource && a.LinetypeIndex == b.LinetypeIndex &&
            Math.Abs(a.LinetypePatternScale - b.LinetypePatternScale) < 1e-12;

        /// <summary>The level line's linetype: a named linetype, or the layer's (Skunk Datum).</summary>
        static void ApplyLinePattern(RhinoDoc doc, ObjectAttributes a, string pattern, double scale)
        {
            if (Styles.TryLinetype(doc, pattern, out int index))
            {
                a.LinetypeSource = ObjectLinetypeSource.LinetypeFromObject;
                a.LinetypeIndex = index;
            }
            else
                a.LinetypeSource = ObjectLinetypeSource.LinetypeFromLayer;
            a.LinetypePatternScale = scale > 0 ? scale : 1.0;
        }

        /// <summary>The user moved or edited a level head: a vertical move moves the level, anything else snaps back.</summary>
        public static void HeadEdited(UpdateContext ctx, RhinoObject head)
        {
            var doc = ctx.Doc;
            var hd = ElementIO.Read<HeadData>(head);
            var level = hd == null ? null : ElementIO.Find(doc, hd.Level);
            var data = level == null ? null : ElementIO.Read<LevelData>(level);
            string key = hd == null ? "" : hd.Edge + ":" + hd.End;
            if (data == null || !data.Heads.TryGetValue(key, out Guid owned) || owned != head.Id)
            {
                // orphan or copied head: heads are derived from their level
                doc.Objects.Delete(head, true, true);
                ctx.Modified++;
                return;
            }
            if (!TryFootprint(level.Geometry, out var pl) || hd.Edge < 0 || hd.Edge >= pl.SegmentCount)
                return;
            var expected = pl[hd.End == 1 ? hd.Edge : hd.Edge + 1];
            var pts = SpotElement.Fingerprint(head.Geometry);
            if (pts.Count > 0)
            {
                double dz = pts[0].Z - expected.Z;
                if (Math.Abs(dz) > ctx.Tol)
                {
                    doc.Objects.Transform(level.Id, Transform.Translation(0, 0, dz), true);
                    ctx.Modified++;
                    level = doc.Objects.FindId(level.Id);
                }
            }
            Update(ctx, level, UpdateReason.Self);
        }

        /// <summary>The user deleted a head: turn that head off (heads are hidden, not deleted).</summary>
        public static void HeadDeleted(UpdateContext ctx, Guid levelId, int edge, int end, Guid headId)
        {
            var level = ElementIO.Find(ctx.Doc, levelId);
            var data = level == null ? null : ElementIO.Read<LevelData>(level);
            if (data == null)
                return;
            string key = edge + ":" + end;
            if (!data.Heads.TryGetValue(key, out Guid owned) || owned != headId)
                return;
            data.Heads.Remove(key);
            var list = end == 1 ? data.End1 : data.End2;
            if (edge >= 0 && edge < list.Count)
                list[edge] = false;
            ElementIO.Write(ctx.Doc, level, ElementKind.Level, data);
            ctx.Modified++;
        }

        /// <summary>Which ends of the level line carry a head ("Show head at End 1 / End 2").</summary>
        public enum HeadEnds
        {
            Right = 0,
            Left,
            Both,
            None,
        }

        /// <summary>The ends shown on the edges that have heads; null when edges differ.</summary>
        public static HeadEnds? CurrentHeadEnds(LevelData d)
        {
            HeadEnds? found = null;
            int n = Math.Min(d.End1.Count, d.End2.Count);
            for (int e = 0; e < n; e++)
            {
                if (!d.End1[e] && !d.End2[e])
                    continue;
                var here = d.End1[e] && d.End2[e] ? HeadEnds.Both : d.End1[e] ? HeadEnds.Left : HeadEnds.Right;
                if (found.HasValue && found.Value != here)
                    return null;
                found = here;
            }
            return found ?? HeadEnds.None;
        }

        /// <summary>
        /// Put the heads at the left end, the right end, both ends or none, on every edge that shows heads
        /// (or the type's default edges when none does). Left / right are as seen in that elevation.
        /// </summary>
        public static void SetHeadEnds(LevelData d, LevelType t, int edges, HeadEnds ends)
        {
            Resize(d.End1, edges, _ => false);
            Resize(d.End2, edges, _ => false);
            var target = new List<int>();
            for (int e = 0; e < edges; e++)
                if (d.End1[e] || d.End2[e])
                    target.Add(e);
            if (target.Count == 0)
                for (int e = 0; e < edges; e++)
                    if (Default(t, e))
                        target.Add(e);
            if (target.Count == 0)
                for (int e = 0; e < edges; e++)
                    target.Add(e);
            for (int e = 0; e < edges; e++)
            {
                bool on = target.Contains(e) && ends != HeadEnds.None;
                d.End1[e] = on && (ends == HeadEnds.Left || ends == HeadEnds.Both);
                d.End2[e] = on && (ends == HeadEnds.Right || ends == HeadEnds.Both);
            }
        }

        /// <summary>Set the head ends of a level and rebuild its heads.</summary>
        public static bool SetHeadEnds(UpdateContext ctx, Guid id, HeadEnds ends)
        {
            var obj = ElementIO.Find(ctx.Doc, id);
            var data = obj == null ? null : ElementIO.Read<LevelData>(obj);
            if (data == null || !TryFootprint(obj.Geometry, out var pl))
                return false;
            var type = ctx.Types.Find<LevelType>(data.Type);
            SetHeadEnds(data, type, Normalize(pl, pl[0].Z).SegmentCount, ends);
            ElementIO.Write(ctx.Doc, obj, ElementKind.Level, data);
            return Update(ctx, ctx.Doc.Objects.FindId(id), UpdateReason.Self);
        }

        /// <summary>The level was deleted: delete its heads and its named CPlane.</summary>
        public static void Deleted(UpdateContext ctx, Guid levelId, LevelData data)
        {
            if (data == null)
                return;
            foreach (var id in data.Heads.Values.ToList())
            {
                var head = ElementIO.Find(ctx.Doc, id);
                if (head != null)
                {
                    ctx.Doc.Objects.Delete(head, true, true);
                    ctx.Modified++;
                }
            }
            if (!string.IsNullOrEmpty(data.CPlane) && !All(ctx.Doc).Any(l => l.Id != levelId && l.Name == data.CPlane))
                NamedPlanes.Delete(ctx.Doc, data.CPlane);
        }

        // ---- edits used by commands and dialogs -------------------------------------------------

        public static bool SetElevationZ(UpdateContext ctx, Guid id, double worldZ)
        {
            var obj = ElementIO.Find(ctx.Doc, id);
            if (obj == null || !TryFootprint(obj.Geometry, out var pl))
                return false;
            double dz = worldZ - pl[0].Z;
            if (Math.Abs(dz) > ctx.Tol)
            {
                ctx.Doc.Objects.Transform(id, Transform.Translation(0, 0, dz), true);
                ctx.Modified++;
            }
            return Update(ctx, ctx.Doc.Objects.FindId(id), UpdateReason.Self);
        }

        public static bool Rename(UpdateContext ctx, Guid id, string name)
        {
            var obj = ElementIO.Find(ctx.Doc, id);
            if (obj == null || string.IsNullOrWhiteSpace(name))
                return false;
            var a = obj.Attributes.Duplicate();
            a.Name = UniqueName(ctx.Doc, name.Trim(), id);
            ctx.Doc.Objects.ModifyAttributes(obj, a, true);
            return Update(ctx, ctx.Doc.Objects.FindId(id), UpdateReason.Self);
        }

        /// <summary>Displayed elevation of a world Z for a level type ("Elevation" relative to the elevation base).</summary>
        public static double DisplayElevation(UpdateContext ctx, LevelType type, double z)
        {
            var frame = ctx.Frame(type?.ElevationBase ?? CoordinateBase.ProjectBasePoint, type?.BaseCPlane, Guid.Empty);
            var o = ctx.Site.Project.Origin;
            return frame.Elevation(new Point3d(o.X, o.Y, z));
        }

        /// <summary>World Z of a displayed elevation.</summary>
        public static double WorldZ(UpdateContext ctx, LevelType type, double elevation)
        {
            var frame = ctx.Frame(type?.ElevationBase ?? CoordinateBase.ProjectBasePoint, type?.BaseCPlane, Guid.Empty);
            return frame.WorldZ(elevation);
        }

        public static string ElevationText(UpdateContext ctx, LevelType type, double z)
        {
            var units = TypeTable.Resolve(ctx.Doc, type?.Units);
            double value = DisplayElevation(ctx, type, z);
            return UnitFormat.Length(value * DocUnits.MetersPerUnit(ctx.Doc), units);
        }

        // ---- heads ------------------------------------------------------------------------------

        static bool SyncHeads(UpdateContext ctx, RhinoObject level, LevelData data, LevelType type, Polyline pl)
        {
            var doc = ctx.Doc;
            bool changed = false;
            int edges = pl.SegmentCount;
            Resize(data.End1, edges, i => type.End1Default && Default(type, i));
            Resize(data.End2, edges, i => type.End2Default && Default(type, i));
            var style = ctx.Style(type);
            string text = data.Name + (type.ShowElevation ? "\n" + ElevationText(ctx, type, pl[0].Z) : "");
            var wanted = new HashSet<string>();
            for (int e = 0; e < edges; e++)
            {
                for (int end = 1; end <= 2; end++)
                {
                    bool on = end == 1 ? data.End1[e] : data.End2[e];
                    if (!on)
                        continue;
                    string key = e + ":" + end;
                    wanted.Add(key);
                    var leader = HeadLeader(ctx, type, style, pl[e], pl[e + 1], end, text, out var pts);
                    if (leader == null)
                        continue;
                    RhinoObject head = data.Heads.TryGetValue(key, out Guid hid) ? ElementIO.Find(doc, hid) : null;
                    var hd = new HeadData { Level = level.Id, Edge = e, End = end, Type = type.Name };
                    if (head != null && ElementIO.KindOf(head) == ElementKind.LevelHead)
                    {
                        var cur = SpotElement.Fingerprint(head.Geometry);
                        bool same = ElementIO.SamePoints(cur, pts, ctx.Tol) && ElementIO.NormalizeText((head.Geometry as Leader)?.PlainText) == ElementIO.NormalizeText(text) &&
                                    (head.Geometry as Leader)?.DimensionStyleId == style.Id;
                        if (!same && doc.Objects.Replace(head.Id, leader, true))
                        {
                            ctx.Modified++;
                            changed = true;
                        }
                        head = doc.Objects.FindId(head.Id);
                        hd.Id = head.Id;
                        var a = head.Attributes.Duplicate();
                        Styles.ApplyTypeAttributes(a, type);
                        a.SetUserString(Keys.Owner, level.Id.ToString());
                        a.SetUserString(Keys.Data, Json.Write(hd));
                        if (a.GetUserString(Keys.Data) != head.Attributes.GetUserString(Keys.Data) || a.ColorSource != head.Attributes.ColorSource || a.ObjectColor != head.Attributes.ObjectColor)
                            doc.Objects.ModifyAttributes(head, a, true);
                    }
                    else
                    {
                        var a = Styles.Attributes(doc, ElementKind.LevelHead, type);
                        a.SetUserString(Keys.Owner, level.Id.ToString());
                        a = ElementIO.With(a, ElementKind.LevelHead, hd);
                        var id = doc.Objects.AddLeader(leader, a);
                        if (id != Guid.Empty)
                        {
                            data.Heads[key] = id;
                            var created = doc.Objects.FindId(id);
                            hd.Id = id;
                            ElementIO.Write(doc, created, ElementKind.LevelHead, hd);
                            ctx.Modified++;
                            changed = true;
                        }
                    }
                }
            }
            foreach (var key in data.Heads.Keys.ToList())
            {
                if (wanted.Contains(key))
                    continue;
                var head = ElementIO.Find(doc, data.Heads[key]);
                if (head != null)
                {
                    doc.Objects.Delete(head, true, true);
                    ctx.Modified++;
                    changed = true;
                }
                data.Heads.Remove(key);
            }
            return changed;
        }

        static bool Default(LevelType type, int edge) =>
            type.EdgesDefault == null || type.EdgesDefault.Length == 0 || (edge < type.EdgesDefault.Length && type.EdgesDefault[edge]);

        static void Resize(List<bool> list, int count, Func<int, bool> fallback)
        {
            while (list.Count < count)
                list.Add(fallback(list.Count));
            while (list.Count > count)
                list.RemoveAt(list.Count - 1);
        }

        static Leader HeadLeader(UpdateContext ctx, LevelType type, DimensionStyle style, Point3d a, Point3d b, int end, string text, out Point3d[] pts)
        {
            pts = null;
            var u = b - a;
            u.Z = 0;
            if (!u.Unitize())
                return null;
            var vertex = end == 1 ? a : b;
            var dir = end == 1 ? -u : u;
            // X along the edge, Y up: the normal (X x Y) faces outward for a counter-clockwise footprint
            var plane = new Plane(vertex, u, Vector3d.ZAxis);
            double length = Math.Max(DocUnits.PaperToModel(ctx.Doc, type.HeadLengthMM), ctx.Tol * 100);
            var tail = vertex + dir * length;
            pts = new[] { vertex, tail };
            var leader = Leader.Create(text, plane, style, pts);
            if (leader != null && leader.LeaderHasLanding)
                leader.LeaderHasLanding = false;
            return leader;
        }

        static void SyncCPlane(UpdateContext ctx, LevelData data, double z)
        {
            if (!ctx.Settings.LevelCPlanes)
                return;
            if (!string.IsNullOrEmpty(data.CPlane) && data.CPlane != data.Name)
                NamedPlanes.Delete(ctx.Doc, data.CPlane);
            var p = ctx.Site.Project;
            p.Origin = new Point3d(p.Origin.X, p.Origin.Y, z);
            NamedPlanes.Set(ctx.Doc, data.Name, p);
            data.CPlane = data.Name;
            OwnedCPlanes.Add(ctx.Doc, data.Name);
        }

        /// <summary>Names of the named CPlanes Skunk made for levels (so stale ones can be removed after Undo).</summary>
        public sealed class OwnedCPlanes
        {
            public List<string> Names = new List<string>();

            const string Entry = "LevelCPlanes";

            public static void Add(RhinoDoc doc, string name)
            {
                var list = DocStore.Get<OwnedCPlanes>(doc, Entry) ?? new OwnedCPlanes();
                if (list.Names.Contains(name))
                    return;
                list.Names.Add(name);
                DocStore.Set(doc, Entry, list);
            }

            /// <summary>Delete Skunk's level CPlanes whose level no longer exists (e.g. after undoing SkLevel).</summary>
            public static void RemoveStale(RhinoDoc doc)
            {
                var list = DocStore.Get<OwnedCPlanes>(doc, Entry);
                if (list == null || list.Names.Count == 0)
                    return;
                var live = new HashSet<string>(All(doc).Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
                var keep = new List<string>();
                foreach (var n in list.Names)
                {
                    if (live.Contains(n))
                        keep.Add(n);
                    else
                        NamedPlanes.Delete(doc, n);
                }
                if (keep.Count != list.Names.Count)
                {
                    list.Names = keep;
                    DocStore.Set(doc, Entry, list);
                }
            }
        }

        // ---- attached objects ---------------------------------------------------------------------

        /// <summary>Objects attached to a level ("Base Constraint").</summary>
        public static RhinoObject[] Attached(RhinoDoc doc, Guid levelId)
        {
            var found = doc.Objects.FindByUserString(Keys.Level, levelId.ToString(), false);
            return found ?? Array.Empty<RhinoObject>();
        }

        static void MoveAttached(UpdateContext ctx, Guid levelId, double dz)
        {
            var xf = Transform.Translation(0, 0, dz);
            foreach (var o in Attached(ctx.Doc, levelId))
            {
                if (o.IsDeleted || ElementIO.KindOf(o) == ElementKind.Level)
                    continue;
                if (ctx.Doc.Objects.Transform(o.Id, xf, true) != Guid.Empty)
                {
                    ctx.Moved.Add(o.Id);
                    ctx.Modified++;
                }
            }
        }

        public static void Attach(RhinoDoc doc, RhinoObject obj, Guid levelId)
        {
            var a = obj.Attributes.Duplicate();
            if (levelId == Guid.Empty)
                a.DeleteUserString(Keys.Level);
            else
                a.SetUserString(Keys.Level, levelId.ToString());
            doc.Objects.ModifyAttributes(obj, a, true);
        }

        public static Guid AttachedLevel(RhinoObject obj) =>
            Guid.TryParse(obj?.Attributes.GetUserString(Keys.Level), out Guid g) ? g : Guid.Empty;

        // ---- geometry helpers ---------------------------------------------------------------------

        public static bool TryFootprint(GeometryBase g, out Polyline pl)
        {
            pl = null;
            if (!(g is Curve c))
                return false;
            if (c.TryGetPolyline(out var poly) && poly.Count >= 4 && poly.IsClosed)
            {
                pl = poly;
                return true;
            }
            if (!c.IsClosed)
                return false;
            var bb = c.GetBoundingBox(true);
            pl = new Polyline(new[]
            {
                new Point3d(bb.Min.X, bb.Min.Y, bb.Min.Z), new Point3d(bb.Max.X, bb.Min.Y, bb.Min.Z),
                new Point3d(bb.Max.X, bb.Max.Y, bb.Min.Z), new Point3d(bb.Min.X, bb.Max.Y, bb.Min.Z), new Point3d(bb.Min.X, bb.Min.Y, bb.Min.Z),
            });
            return true;
        }

        /// <summary>Flatten to Z, close, and orient counter-clockwise (seen from above).</summary>
        public static Polyline Normalize(Polyline input, double z)
        {
            var pts = input.Select(p => new Point3d(p.X, p.Y, z)).ToList();
            if (pts.Count > 1 && pts[0].DistanceTo(pts[pts.Count - 1]) > 1e-12)
                pts.Add(pts[0]);
            double area = 0;
            for (int i = 0; i < pts.Count - 1; i++)
                area += pts[i].X * pts[i + 1].Y - pts[i + 1].X * pts[i].Y;
            if (area < 0)
                pts.Reverse();
            return new Polyline(pts);
        }

        /// <summary>
        /// The level's Z after an edit: unchanged when only some vertices moved vertically (the
        /// majority wins), the new Z when the whole level moved.
        /// </summary>
        static double DecideZ(Polyline pl, double previous, double tol)
        {
            int n = pl.IsClosed ? pl.Count - 1 : pl.Count;
            var zs = new List<double>();
            for (int i = 0; i < n; i++)
                zs.Add(pl[i].Z);
            double min = zs.Min(), max = zs.Max();
            if (max - min <= tol)
                return zs[0];
            var groups = zs.GroupBy(z => Math.Round(z / Math.Max(tol, 1e-9))).OrderByDescending(g => g.Count()).ToList();
            if (groups.Count > 1 && groups[0].Count() == groups[1].Count() && !double.IsNaN(previous))
            {
                var keep = groups.FirstOrDefault(g => Math.Abs(g.First() - previous) <= tol);
                if (keep != null)
                    return keep.First();
            }
            return groups[0].First();
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
    }
}
