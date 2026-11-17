using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Skunk.Core;
using Skunk.Types;
using Skunk.Units;

namespace Skunk.Elements
{
    /// <summary>A grid as seen by commands and dialogs.</summary>
    public sealed class GridInfo
    {
        public Guid Id;
        public string Name;
        public GridData Data;
        public GridType Type;
        /// <summary>From End 1 to End 2.</summary>
        public Line Line;
    }

    /// <summary>One grid's new name, as a rename or a renumbering gave it.</summary>
    public sealed class GridRename
    {
        public Guid Id;
        public string From;
        public string To;
    }

    /// <summary>
    /// A grid is a horizontal line on "__Annotations__::Grids" from End 1 to End 2, drawn with a datum linetype, with a
    /// bubble at either or both ends, in the plan. A bubble is one text centred on the end: the grid's name, whose
    /// mask Rhino draws as an opaque circle with an outline, the type's bubble size across. The circle hides the
    /// grid line under it and never the name (it is the text's own background), it prints at its type size at any
    /// scale, and typing a new name into it renames the grid - the grids after it follow on. Grids show in every
    /// level's plan cut (the cuts leave the grid layers unclipped). In a datum box a grid's ends sit on the box's
    /// outline: move or turn the grid, or change the box, and the ends follow. Drag a bubble along the grid (or one
    /// end of the line along it) and that end goes there, and the grid leaves the box.
    /// </summary>
    public static class GridElement
    {
        // ---- queries ----------------------------------------------------------------------------

        /// <summary>The grid line of an object (an open curve: its two ends).</summary>
        public static bool TryLine(GeometryBase g, out Line line)
        {
            line = Line.Unset;
            if (!(g is Curve c) || c.IsClosed)
                return false;
            line = new Line(c.PointAtStart, c.PointAtEnd);
            return line.Length > RhinoMath.ZeroTolerance;
        }

        public static GridInfo Info(RhinoDoc doc, RhinoObject obj, TypeLibrary types = null)
        {
            if (obj == null || ElementIO.KindOf(obj) != ElementKind.Grid || !TryLine(obj.Geometry, out var line))
                return null;
            var data = ElementIO.Read<GridData>(obj) ?? new GridData { Name = obj.Attributes.Name ?? "" };
            types = types ?? TypeTable.Get(doc);
            return new GridInfo
            {
                Id = obj.Id,
                Name = string.IsNullOrEmpty(data.Name) ? obj.Attributes.Name ?? "" : data.Name,
                Data = data,
                Type = types.Find<GridType>(data.Type),
                Line = line,
            };
        }

        /// <summary>All grids, in the order they were made.</summary>
        public static List<GridInfo> All(RhinoDoc doc)
        {
            var types = TypeTable.Get(doc);
            return ElementIO.All(doc, ElementKind.Grid).Select(o => Info(doc, o, types)).Where(i => i != null)
                .OrderBy(i => i.Data.Seq).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The name the next grid gets: the one after the last grid's (1, 2, 3 or A, B, C), or 1.</summary>
        public static string NextName(RhinoDoc doc)
        {
            var all = All(doc);
            var names = new HashSet<string>(all.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
            string n = all.Count == 0 ? "1" : Increment(all[all.Count - 1].Name);
            for (int guard = 0; names.Contains(n) && guard < 10000; guard++)
                n = Increment(n);
            return n;
        }

        /// <summary>
        /// The next name in a series: a trailing number counts up, keeping its width (9 to 10, A1 to A2, 09 to 10);
        /// otherwise one or two trailing letters do (A to B, Z to AA, AZ to BA, Grid A to Grid B); anything else
        /// (a word) gets " 2".
        /// </summary>
        public static string Increment(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0)
                return "1";
            int i = name.Length;
            while (i > 0 && name[i - 1] >= '0' && name[i - 1] <= '9')
                i--;
            if (i < name.Length && name.Length - i <= 18 && long.TryParse(name.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture, out long v))
                return name.Substring(0, i) + (v + 1).ToString(CultureInfo.InvariantCulture).PadLeft(name.Length - i, '0');
            i = name.Length;
            while (i > 0 && IsLetter(name[i - 1]))
                i--;
            if (i < name.Length && name.Length - i <= 2)
            {
                var letters = name.Substring(i).ToCharArray();
                for (int k = letters.Length - 1; k >= 0; k--)
                {
                    if (letters[k] == 'Z')
                        letters[k] = 'A';
                    else if (letters[k] == 'z')
                        letters[k] = 'a';
                    else
                    {
                        letters[k]++;
                        return name.Substring(0, i) + new string(letters);
                    }
                }
                // every letter wrapped (Z, ZZ): one letter more
                return name.Substring(0, i) + (name[i] <= 'Z' ? 'A' : 'a') + new string(letters);
            }
            return name + " 2";
        }

        static bool IsLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        static bool IsDigit(char c) => c >= '0' && c <= '9';

        static string UniqueName(RhinoDoc doc, string wanted, Guid self)
        {
            var names = new HashSet<string>(All(doc).Where(g => g.Id != self).Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(wanted))
                return wanted;
            for (int i = 2; ; i++)
            {
                string n = wanted + " (" + i + ")";
                if (!names.Contains(n))
                    return n;
            }
        }

        // ---- names: series, renames that the next grids follow, renumbering ----------------------

        /// <summary>A name as typed into a bubble or a field: one line, single spaces, no spaces around it.</summary>
        public static string CleanName(string text)
        {
            var s = ElementIO.NormalizeText(text ?? "").Replace('\n', ' ').Replace('\t', ' ').Trim();
            while (s.Contains("  "))
                s = s.Replace("  ", " ");
            return s;
        }

        /// <summary>
        /// A name as a place in a series: the text before its counter, whether the counter is a number or letters, and
        /// its value (A = 1, Z = 26, AA = 27). False for a name without a counter.
        /// </summary>
        static bool TrySeries(string name, out string prefix, out bool number, out long value)
        {
            name = (name ?? "").Trim();
            prefix = "";
            number = false;
            value = 0;
            int i = name.Length;
            while (i > 0 && IsDigit(name[i - 1]))
                i--;
            if (i < name.Length && name.Length - i <= 18)
            {
                prefix = name.Substring(0, i);
                number = true;
                value = long.Parse(name.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture);
                return true;
            }
            i = name.Length;
            while (i > 0 && IsLetter(name[i - 1]))
                i--;
            if (i < name.Length && name.Length - i <= 3)
            {
                prefix = name.Substring(0, i);
                foreach (char c in name.Substring(i).ToUpperInvariant())
                    value = value * 26 + (c - 'A' + 1);
                return true;
            }
            return false;
        }

        /// <summary>Which of two names comes first in their series (negative: the first one), or null when they are not in one series.</summary>
        public static int? CompareNames(string a, string b)
        {
            if (!TrySeries(a, out var pa, out bool na, out long va) || !TrySeries(b, out var pb, out bool nb, out long vb) ||
                na != nb || !string.Equals(pa, pb, StringComparison.OrdinalIgnoreCase))
                return null;
            return va.CompareTo(vb);
        }

        /// <summary>The name that comes first in its series (1 before 2, A before B), else the first in text order; 1 for none.</summary>
        public static string FirstInSeries(IEnumerable<string> names)
        {
            string best = null;
            foreach (var n in names)
            {
                if (string.IsNullOrWhiteSpace(n))
                    continue;
                int? c = best == null ? -1 : CompareNames(n, best);
                if (c.HasValue ? c.Value < 0 : string.Compare(n, best, StringComparison.OrdinalIgnoreCase) < 0)
                    best = n;
            }
            return best ?? "1";
        }

        /// <summary>
        /// A name for a grid in between the one before it and the next: that grid's name, then a separator and more
        /// (2.5, 2-1, A.1, A'), or a letter after its number or a number after its letter (2A, A1).
        /// </summary>
        public static bool InBetween(string name, string before)
        {
            if (string.IsNullOrEmpty(before) || string.IsNullOrEmpty(name) || name.Length <= before.Length ||
                !name.StartsWith(before, StringComparison.OrdinalIgnoreCase))
                return false;
            char next = name[before.Length], last = before[before.Length - 1];
            if (!char.IsLetterOrDigit(next))
                return true;
            return (IsDigit(last) && IsLetter(next)) || (IsLetter(last) && IsDigit(next));
        }

        /// <summary>
        /// The grids after one grid: those parallel to it, beyond it in the direction its grids are numbered, nearest
        /// first; and the grid just before it. The direction is the way the names of the parallel grids run (when
        /// they do not tell: left to right across grids that run north-south, top to bottom across grids that run
        /// east-west, in the project's plan).
        /// </summary>
        public static List<GridInfo> Following(UpdateContext ctx, List<GridInfo> all, GridInfo grid, out GridInfo before)
        {
            before = null;
            var plane = ctx.Site.Project;
            Vector3d Dir(GridInfo g)
            {
                var v = g.Line.Direction;
                var d = new Vector3d(v * plane.XAxis, v * plane.YAxis, 0);
                d.Unitize();
                return d;
            }
            var dx = Dir(grid);
            var n = new Vector3d(dx.Y, -dx.X, 0);
            if (Math.Abs(dx.Y) >= Math.Abs(dx.X) ? n.X < 0 : n.Y > 0)
                n = -n;
            double sinTol = Math.Sin(Math.Max(ctx.Doc.ModelAngleToleranceRadians, RhinoMath.ToRadians(0.5)));
            double Offset(GridInfo g)
            {
                var w = g.Line.From - grid.Line.From;
                return new Vector3d(w * plane.XAxis, w * plane.YAxis, 0) * n;
            }
            var parallel = all.Where(g => g.Id != grid.Id && Math.Abs(Vector3d.CrossProduct(Dir(g), dx).Z) <= sinTol)
                .Select(g => (Grid: g, At: Offset(g))).Where(t => Math.Abs(t.At) > ctx.Tol).ToList();

            // which way do the names run? every pair of parallel grids in one series votes
            int vote = 0;
            for (int i = 0; i < parallel.Count; i++)
                for (int j = i + 1; j < parallel.Count; j++)
                {
                    int? c = CompareNames(parallel[i].Grid.Name, parallel[j].Grid.Name);
                    if (c.HasValue && c.Value != 0 && Math.Abs(parallel[i].At - parallel[j].At) > ctx.Tol)
                        vote += (parallel[i].At < parallel[j].At) == (c.Value < 0) ? 1 : -1;
                }
            if (vote == 0)
                foreach (var t in parallel)
                {
                    int? c = CompareNames(grid.Name, t.Grid.Name);
                    if (c.HasValue && c.Value != 0)
                        vote += (t.At > 0) == (c.Value < 0) ? 1 : -1;
                }
            int s = vote < 0 ? -1 : 1;
            before = parallel.Where(t => s * t.At < 0).OrderByDescending(t => s * t.At).Select(t => t.Grid).FirstOrDefault();
            return parallel.Where(t => s * t.At > 0).OrderBy(t => s * t.At).Select(t => t.Grid).ToList();
        }

        /// <summary>
        /// Rename a grid, and the grids after it follow on: the parallel grids beyond it, in the direction its grids are
        /// numbered, take the names after the new one. Rename 6 to A and 7, 8, 9 become B, C, D; rename the 6 in
        /// 1, 2, 6, 3, 4 to 3 and the 3 and 4 after it become 4 and 5. A name in between (2.5 or 2A after grid 2, A.1
        /// after A) renames that grid alone. Names stay unique: a grid that has one of the new names takes a name that
        /// was given up. Returns the grids renamed.
        /// </summary>
        public static List<GridRename> RenameFollowing(UpdateContext ctx, Guid id, string name)
        {
            var all = All(ctx.Doc);
            var grid = all.FirstOrDefault(g => g.Id == id);
            name = CleanName(name);
            if (grid == null || name.Length == 0 || name == grid.Name)
                return new List<GridRename>();
            var order = new List<(GridInfo Grid, string Name)> { (grid, name) };
            var after = Following(ctx, all, grid, out var before);
            if (!InBetween(name, before?.Name))
            {
                string n = name;
                foreach (var g in after)
                {
                    n = Increment(n);
                    order.Add((g, n));
                }
            }
            return Assign(ctx, all, order);
        }

        /// <summary>
        /// Name grids one after another in the order given, from the first name (1, 2, 3 or A, B, C). A grid not in the
        /// list that has one of the names takes one the list gave up. Returns the grids renamed.
        /// </summary>
        public static List<GridRename> Renumber(UpdateContext ctx, IEnumerable<Guid> ordered, string first)
        {
            var all = All(ctx.Doc);
            var order = new List<(GridInfo Grid, string Name)>();
            string n = CleanName(first);
            if (n.Length == 0)
                n = "1";
            foreach (var id in ordered.Distinct())
            {
                var g = all.FirstOrDefault(x => x.Id == id);
                if (g == null)
                    continue;
                order.Add((g, n));
                n = Increment(n);
            }
            return order.Count == 0 ? new List<GridRename>() : Assign(ctx, all, order);
        }

        /// <summary>
        /// Grids in the order a direction crosses them, from its start to its end: where each grid's line, extended,
        /// meets the direction's line in plan (a grid that runs along it: where its middle falls).
        /// </summary>
        public static List<Guid> AlongDirection(RhinoDoc doc, IEnumerable<Guid> ids, Point3d from, Point3d to)
        {
            var list = ids.Distinct().ToList();
            var dir = to - from;
            dir.Z = 0;
            if (dir.Length <= RhinoMath.ZeroTolerance)
                return list;
            dir.Unitize();
            double At(Guid id)
            {
                var o = doc.Objects.FindId(id);
                if (o == null || !TryLine(o.Geometry, out var line))
                    return double.MaxValue;
                var e = line.To - line.From;
                e.Z = 0;
                var w = new Vector3d(line.From.X - from.X, line.From.Y - from.Y, 0);
                double cross = dir.X * e.Y - dir.Y * e.X;
                if (Math.Abs(cross) <= 1e-9 * e.Length)
                    return (w + e * 0.5) * dir;
                return (w.X * e.Y - w.Y * e.X) / cross;
            }
            return list.Select(id => (Id: id, At: At(id))).OrderBy(t => t.At).Select(t => t.Id).ToList();
        }

        /// <summary>
        /// Give grids their new names, keeping every name unique: a grid outside the list that has one of the new names
        /// takes a name the list gave up (a swap). All names are written before any grid is updated, so no grid ever
        /// sees another's old name.
        /// </summary>
        static List<GridRename> Assign(UpdateContext ctx, List<GridInfo> all, List<(GridInfo Grid, string Name)> order)
        {
            var doc = ctx.Doc;
            var cmp = StringComparer.OrdinalIgnoreCase;
            var target = new Dictionary<Guid, string>();
            foreach (var o in order)
                target[o.Grid.Id] = o.Name;
            var wanted = new HashSet<string>(order.Select(o => o.Name), cmp);
            var freed = new Queue<string>(order.Select(o => o.Grid.Name).Where(n => !string.IsNullOrEmpty(n) && !wanted.Contains(n)).Distinct(cmp));
            var displaced = all.Where(g => !target.ContainsKey(g.Id) && wanted.Contains(g.Name))
                .OrderBy(g => order.FindIndex(o => cmp.Equals(o.Name, g.Name))).ToList();
            var taken = new HashSet<string>(all.Where(g => !target.ContainsKey(g.Id) && !wanted.Contains(g.Name)).Select(g => g.Name), cmp);
            taken.UnionWith(wanted);
            foreach (var g in displaced)
            {
                string n = null;
                while (freed.Count > 0 && n == null)
                {
                    var f = freed.Dequeue();
                    if (!taken.Contains(f))
                        n = f;
                }
                if (n == null)
                {
                    n = g.Name;
                    for (int i = 2; taken.Contains(n); i++)
                        n = g.Name + " (" + i + ")";
                }
                taken.Add(n);
                target[g.Id] = n;
            }

            var renamed = new List<GridRename>();
            foreach (var kv in target)
            {
                var obj = ElementIO.Find(doc, kv.Key);
                var data = obj == null ? null : ElementIO.Read<GridData>(obj);
                if (data == null || (data.Name == kv.Value && obj.Attributes.Name == kv.Value))
                    continue;
                renamed.Add(new GridRename { Id = kv.Key, From = data.Name, To = kv.Value });
                data.Name = kv.Value;
                var a = obj.Attributes.Duplicate();
                a.Name = kv.Value;
                a.SetUserString(Keys.Data, Json.Write(data));
                doc.Objects.ModifyAttributes(obj, a, true);
                ctx.Modified++;
            }
            foreach (var r in renamed)
                Update(ctx, doc.Objects.FindId(r.Id), UpdateReason.Self);
            return renamed;
        }

        // ---- create -----------------------------------------------------------------------------

        /// <summary>
        /// Add a grid from End 1 to End 2, made horizontal at End 1's height. With a datum box its ends go onto the
        /// box's outline. End bubbles default to the type's.
        /// </summary>
        public static Guid Create(UpdateContext ctx, string typeName, string name, Line line, Guid box = default, bool? end1 = null, bool? end2 = null)
        {
            var doc = ctx.Doc;
            var type = ctx.Types.Find<GridType>(typeName);
            var a = line.From;
            var b = new Point3d(line.To.X, line.To.Y, a.Z);
            if (a.DistanceTo(b) <= ctx.Tol)
                return Guid.Empty;
            var data = new GridData
            {
                Type = type?.Name ?? "",
                Name = UniqueName(doc, string.IsNullOrWhiteSpace(name) ? NextName(doc) : CleanName(name), Guid.Empty),
                End1 = end1 ?? type?.End1Default ?? false,
                End2 = end2 ?? type?.End2Default ?? true,
                Box = box != Guid.Empty && DatumBoxElement.TryOutline(doc, box, out _) ? box : Guid.Empty,
                Seq = NextSeq(doc),
            };
            var attrs = Styles.Attributes(doc, ElementKind.Grid, type);
            attrs.Name = data.Name;
            attrs.LinetypeSource = ObjectLinetypeSource.LinetypeFromLayer;
            attrs = ElementIO.With(attrs, ElementKind.Grid, data);
            var id = doc.Objects.AddCurve(new LineCurve(a, b), attrs);
            if (id == Guid.Empty)
                return id;
            Service.SkunkService.Register(doc, id);
            Update(ctx, doc.Objects.FindId(id), UpdateReason.Created);
            // plan cuts leave the grid layers unclipped, so grids show on every floor
            PlanCutElement.UpdateAll(ctx);
            return id;
        }

        static int NextSeq(RhinoDoc doc) => All(doc).Select(g => g.Data.Seq).DefaultIfEmpty(0).Max() + 1;

        // ---- update -----------------------------------------------------------------------------

        /// <summary>
        /// The order to update grids in: grids first, then copies - nearest to the grid they were copied from first,
        /// then in that grid's order - so a row of copies (an array) is named in turn, going away from the original,
        /// whatever order Rhino reported them in.
        /// </summary>
        public static List<Guid> UpdateOrder(RhinoDoc doc, IEnumerable<Guid> ids)
        {
            var keyed = new List<(Guid Id, int Copy, double Distance, int Seq)>();
            foreach (var id in ids)
            {
                var obj = ElementIO.Find(doc, id);
                var data = obj == null ? null : ElementIO.Read<GridData>(obj);
                bool copy = data != null && data.Id != Guid.Empty && data.Id != obj.Id;
                double distance = 0;
                if (copy)
                {
                    var source = ElementIO.Find(doc, data.Id);
                    distance = source != null && TryLine(source.Geometry, out var sl) && TryLine(obj.Geometry, out var cl)
                        ? Plan(sl.PointAt(0.5)).DistanceTo(Plan(cl.PointAt(0.5)))
                        : double.MaxValue;
                }
                keyed.Add((id, copy ? 1 : 0, distance, data?.Seq ?? 0));
            }
            return keyed.OrderBy(k => k.Copy).ThenBy(k => k.Distance).ThenBy(k => k.Seq).Select(k => k.Id).ToList();
        }

        public static bool Update(UpdateContext ctx, RhinoObject obj, UpdateReason reason)
        {
            var doc = ctx.Doc;
            if (obj == null || ElementIO.KindOf(obj) != ElementKind.Grid)
                return false;
            var data = ElementIO.Read<GridData>(obj) ?? new GridData();
            var type = ctx.Types.Find<GridType>(data.Type);
            if (type == null)
                return false;
            data.Type = type.Name;
            bool isCopy = data.Id != Guid.Empty && data.Id != obj.Id;
            bool changed = false;

            // a horizontal line at End 1's height; anything else (closed, collapsed) goes back to the last line
            var stored = Stored(data);
            Point3d a, b;
            if (TryLine(obj.Geometry, out var raw) && new Point3d(raw.To.X, raw.To.Y, raw.From.Z).DistanceTo(raw.From) > ctx.Tol)
            {
                a = raw.From;
                b = new Point3d(raw.To.X, raw.To.Y, a.Z);
            }
            else if (stored.HasValue)
            {
                a = stored.Value.From;
                b = stored.Value.To;
            }
            else
                return false;

            // name: a copy gets the next one; a rename in Rhino's Properties (object name) is honoured, and the grids
            // after it follow on (that renames and updates this grid too)
            string attrName = obj.Attributes.Name;
            if (isCopy)
            {
                data.Name = UniqueName(doc, NextName(doc), obj.Id);
                data.Heads.Clear();
                data.Seq = NextSeq(doc);
            }
            else if (!string.IsNullOrWhiteSpace(attrName) && CleanName(attrName) != data.Name)
            {
                if (!string.IsNullOrEmpty(data.Name) && RenameFollowing(ctx, obj.Id, attrName).Count > 0)
                    return true;
                data.Name = UniqueName(doc, CleanName(attrName), obj.Id);
            }
            if (string.IsNullOrWhiteSpace(data.Name))
                data.Name = UniqueName(doc, NextName(doc), obj.Id);

            // in a datum box the ends sit on its outline: a grid moved or turned keeps to the box, one whose end
            // was dragged along it keeps that end and leaves the box, one that no longer crosses the box leaves it
            if (data.Box != Guid.Empty)
            {
                if (!DatumBoxElement.TryOutline(doc, data.Box, out var outline))
                    data.Box = Guid.Empty;
                else if (!isCopy && stored.HasValue && EndDraggedAlong(stored.Value, a, b, ctx.Tol))
                    data.Box = Guid.Empty;
                else if (Clip(a, b, outline, out var ca, out var cb))
                {
                    a = ca;
                    b = cb;
                }
                else
                    data.Box = Guid.Empty;
            }

            if (!(obj.Geometry is LineCurve lc) || lc.PointAtStart.DistanceTo(a) > ctx.Tol || lc.PointAtEnd.DistanceTo(b) > ctx.Tol)
            {
                if (doc.Objects.Replace(obj.Id, new LineCurve(a, b), true))
                {
                    ctx.Modified++;
                    changed = true;
                }
                obj = doc.Objects.FindId(obj.Id);
            }
            data.Points = ElementIO.Flatten(new[] { a, b });

            var partsBefore = new HashSet<Guid>(data.Heads.Values);
            if (SyncHeads(ctx, obj, data, type, a, b))
                changed = true;
            bool newParts = data.Heads.Values.Any(p => !partsBefore.Contains(p));

            var attrs = obj.Attributes.Duplicate();
            attrs.Name = data.Name;
            Styles.ApplyTypeAttributes(attrs, type);
            LevelElement.ApplyLinePattern(doc, attrs, type.LinePattern, type.LinePatternScale);
            data.Id = obj.Id;
            attrs.SetUserString(Keys.Kind, ElementKind.Grid.ToString());
            attrs.SetUserString(Keys.Data, Json.Write(data));
            if (!SameAttributes(attrs, obj.Attributes))
            {
                doc.Objects.ModifyAttributes(obj, attrs, true);
                changed = true;
            }
            // a bubble or line just made shows in layout details the way the grid's other parts do
            if (newParts)
            {
                var details = GridViews.Details(doc);
                if (details.Count > 0)
                    ctx.Modified += GridViews.Apply(doc, new[] { obj.Id }, details);
            }
            return changed;
        }

        static Line? Stored(GridData d)
        {
            var pts = ElementIO.Unflatten(d?.Points);
            return pts.Count == 2 ? new Line(pts[0], pts[1]) : (Line?)null;
        }

        static bool SameAttributes(ObjectAttributes a, ObjectAttributes b) =>
            a.Name == b.Name && a.GetUserString(Keys.Data) == b.GetUserString(Keys.Data) &&
            a.GetUserString(Keys.Kind) == b.GetUserString(Keys.Kind) && a.ColorSource == b.ColorSource &&
            a.ObjectColor == b.ObjectColor && a.PlotWeightSource == b.PlotWeightSource && Math.Abs(a.PlotWeight - b.PlotWeight) < 1e-9 &&
            a.LinetypeSource == b.LinetypeSource && a.LinetypeIndex == b.LinetypeIndex &&
            Math.Abs(a.LinetypePatternScale - b.LinetypePatternScale) < 1e-12;

        /// <summary>Exactly one end moved, and only along the grid (a length change, not a move or a turn).</summary>
        static bool EndDraggedAlong(Line before, Point3d a, Point3d b, double tol)
        {
            bool aMoved = Plan(a).DistanceTo(Plan(before.From)) > tol, bMoved = Plan(b).DistanceTo(Plan(before.To)) > tol;
            if (aMoved == bMoved)
                return false;
            var flat = new Line(Plan(before.From), Plan(before.To));
            var moved = aMoved ? Plan(a) : Plan(b);
            return flat.DistanceTo(moved, false) <= tol;
        }

        static Point3d Plan(Point3d p) => new Point3d(p.X, p.Y, 0);

        /// <summary>
        /// Where the grid's line (through a and b, extended both ways) crosses an outline: its first and last
        /// crossing in the grid's direction, at the grid's height. False when it misses the outline.
        /// </summary>
        public static bool Clip(Point3d a, Point3d b, Polyline outline, out Point3d ca, out Point3d cb)
        {
            ca = cb = Point3d.Unset;
            var d = b - a;
            d.Z = 0;
            double len = d.Length;
            if (len < 1e-12 || outline == null)
                return false;
            d /= len;
            double tMin = double.MaxValue, tMax = double.MinValue;
            for (int i = 0; i < outline.Count - 1; i++)
            {
                var q0 = outline[i];
                var e = outline[i + 1] - q0;
                double denom = d.X * e.Y - d.Y * e.X;
                if (Math.Abs(denom) < 1e-12)
                    continue;
                double wx = q0.X - a.X, wy = q0.Y - a.Y;
                double t = (wx * e.Y - wy * e.X) / denom;
                double s = (wx * d.Y - wy * d.X) / denom;
                if (s < -1e-9 || s > 1 + 1e-9)
                    continue;
                tMin = Math.Min(tMin, t);
                tMax = Math.Max(tMax, t);
            }
            if (tMin == double.MaxValue || tMax - tMin < 1e-9)
                return false;
            ca = new Point3d(a.X + d.X * tMin, a.Y + d.Y * tMin, a.Z);
            cb = new Point3d(a.X + d.X * tMax, a.Y + d.Y * tMax, a.Z);
            return true;
        }

        // ---- bubbles ----------------------------------------------------------------------------

        /// <summary>A part of a grid: a plan bubble, or beyond an end, the elevation line and its top and bottom bubbles.</summary>
        public const int PlanBubble = 0, ElevationLine = 1, ElevationTop = 2, ElevationBottom = 3;

        /// <summary>
        /// Part keys in GridData.Heads: "1t" / "2t" the plan bubble at End 1 / End 2; "e1" the elevation line beyond
        /// End 1, "e1ut" / "e1dt" its top / bottom bubble; the same with 2. A bubble is one text (models from 1.6.0 also
        /// had a circle leader under "1", "2", "e1u", "e1d": the next update removes it).
        /// </summary>
        static string Key(int end, bool label, int part = PlanBubble)
        {
            string e = end.ToString(CultureInfo.InvariantCulture), t = label ? "t" : "";
            switch (part)
            {
                case ElevationLine: return "e" + e;
                case ElevationTop: return "e" + e + "u" + t;
                case ElevationBottom: return "e" + e + "d" + t;
                default: return e + t;
            }
        }

        /// <summary>The key of a bubble: the plan bubble at an end, or the top or bottom bubble of its elevation line.</summary>
        public static string BubbleKey(int end, int part = PlanBubble) => Key(end, true, part);

        /// <summary>
        /// Bottom and top of a grid as elevations show it: its datum box's (the box spans its levels), else from just
        /// under the lowest level to just over the highest, else 3 m up from the grid.
        /// </summary>
        public static void Heights(UpdateContext ctx, GridData data, double z, out double bottom, out double top)
        {
            var doc = ctx.Doc;
            var box = data.Box == Guid.Empty ? null : ElementIO.Find(doc, data.Box);
            if (box?.Geometry is Curve bc)
            {
                var bd = ElementIO.Read<DatumBoxData>(box);
                bottom = bc.PointAtStart.Z;
                top = bd != null && !double.IsNaN(bd.Top) && bd.Top > bottom ? bd.Top : bottom + DocUnits.FromMeters(doc, 3.0);
                return;
            }
            var zs = DatumBoxElement.AllLevelZs(doc);
            if (zs.Count > 0)
            {
                double margin = DatumBoxElement.Margin(doc);
                bottom = zs.Min() - margin;
                top = zs.Max() + margin;
                return;
            }
            bottom = z;
            top = z + DocUnits.FromMeters(doc, 3.0);
        }

        /// <summary>
        /// How far beyond its end an elevation line stands: one bubble diameter at the model's annotation scale, so seen
        /// from above its bubble (edge-on, a short stroke) clears the plan bubble. Looking along the grid in an
        /// elevation the distance does not show.
        /// </summary>
        public static double ElevationOffset(RhinoDoc doc, GridType type) => DocUnits.PaperToModel(doc, type.BubbleSizeMM);

        /// <summary>The bubble's centre as a part shows it: a text's anchor (a 1.6.0 circle leader: its point).</summary>
        public static Point3d Center(GeometryBase g)
        {
            switch (g)
            {
                case Leader l:
                    {
                        var pts = SpotElement.Fingerprint(l);
                        return pts.Count > 0 ? pts[0] : Point3d.Unset;
                    }
                case TextEntity t:
                    return t.Plane.Origin;
            }
            return Point3d.Unset;
        }

        /// <summary>A bubble: the grid's name centred on a point; the style draws its mask as the bubble's circle.</summary>
        static TextEntity Bubble(string name, Plane plane, DimensionStyle style)
        {
            var t = TextEntity.Create(name, plane, style, false, 0, 0);
            if (t != null)
                t.Justification = TextJustification.MiddleCenter;
            return t;
        }

        static bool SyncHeads(UpdateContext ctx, RhinoObject grid, GridData data, GridType type, Point3d a, Point3d b)
        {
            var doc = ctx.Doc;
            bool changed = false;
            var style = ctx.Style(type);
            var project = ctx.Site.Project;
            var dir = b - a;
            dir.Z = 0;
            dir.Unitize();
            var wanted = new HashSet<string>();
            for (int end = 1; end <= 2; end++)
            {
                if (!(end == 1 ? data.End1 : data.End2))
                    continue;
                var center = end == 1 ? a : b;
                // in the plan, upright when project north is up
                var bubble = Bubble(data.Name, new Plane(center, project.XAxis, project.YAxis), style);
                if (bubble != null && SyncBubble(ctx, grid, data, type, end, bubble, center))
                    changed = true;
                wanted.Add(Key(end, true));
            }

            // elevation parts, beyond each end and facing out: a vertical line from the grid's bottom to its top, with its
            // bubbles in the vertical plane across the grid, so an elevation or section looking along the grid from that
            // side sees them face on and reads the name the right way round
            Heights(ctx, data, a.Z, out double bottom, out double top);
            double offset = ElevationOffset(doc, type);
            var elevationBubbles = ElevationBubblesOf(data, type);
            bool topBubble = elevationBubbles == GridElevationBubbles.Top || elevationBubbles == GridElevationBubbles.Both;
            bool bottomBubble = elevationBubbles == GridElevationBubbles.Bottom || elevationBubbles == GridElevationBubbles.Both;
            for (int end = 1; end <= 2; end++)
            {
                var outward = end == 1 ? -dir : dir;
                var at = (end == 1 ? a : b) + outward * offset;
                var lo = new Point3d(at.X, at.Y, bottom);
                var hi = new Point3d(at.X, at.Y, top);
                if (SyncLine(ctx, grid, data, type, end, new Line(lo, hi)))
                    changed = true;
                wanted.Add(Key(end, false, ElevationLine));
                // to the right of someone standing beyond this end, looking back along the grid
                var right = Vector3d.CrossProduct(-outward, Vector3d.ZAxis);
                for (int part = ElevationTop; part <= ElevationBottom; part++)
                {
                    if (!(part == ElevationTop ? topBubble : bottomBubble))
                        continue;
                    var center = part == ElevationTop ? hi : lo;
                    var bubble = Bubble(data.Name, new Plane(center, right, Vector3d.ZAxis), style);
                    if (bubble != null && SyncBubble(ctx, grid, data, type, end, bubble, center, part))
                        changed = true;
                    wanted.Add(Key(end, true, part));
                }
            }
            foreach (var key in data.Heads.Keys.ToList())
            {
                if (wanted.Contains(key))
                    continue;
                var part = ElementIO.Find(doc, data.Heads[key]);
                if (part != null)
                {
                    doc.Objects.Delete(part, true, true);
                    ctx.Modified++;
                    changed = true;
                }
                data.Heads.Remove(key);
            }
            return changed;
        }

        /// <summary>The vertical line of a grid in elevations, beyond one end (a curve on __Annotations__::Grid Elevations).</summary>
        static bool SyncLine(UpdateContext ctx, RhinoObject grid, GridData data, GridType type, int end, Line line)
        {
            var doc = ctx.Doc;
            string key = Key(end, false, ElevationLine);
            var hd = new GridHeadData { Grid = grid.Id, End = end, Elevation = ElevationLine, Type = type.Name };
            RhinoObject part = data.Heads.TryGetValue(key, out Guid pid) ? ElementIO.Find(doc, pid) : null;
            if (part != null && ElementIO.KindOf(part) == ElementKind.GridElevation && part.Geometry is Curve)
            {
                bool replaced = false;
                if (!(part.Geometry is LineCurve lc) || lc.PointAtStart.DistanceTo(line.From) > ctx.Tol || lc.PointAtEnd.DistanceTo(line.To) > ctx.Tol)
                {
                    replaced = doc.Objects.Replace(part.Id, new LineCurve(line), true);
                    if (replaced)
                        ctx.Modified++;
                    part = doc.Objects.FindId(part.Id);
                }
                hd.Id = part.Id;
                var a = part.Attributes.Duplicate();
                a.Name = data.Name;
                Styles.ApplyTypeAttributes(a, type);
                LevelElement.ApplyLinePattern(doc, a, type.LinePattern, type.LinePatternScale);
                a.SetUserString(Keys.Owner, grid.Id.ToString());
                a.SetUserString(Keys.Data, Json.Write(hd));
                if (!SameAttributes(a, part.Attributes))
                    doc.Objects.ModifyAttributes(part, a, true);
                return replaced;
            }
            if (part != null)
            {
                doc.Objects.Delete(part, true, true);
                data.Heads.Remove(key);
            }
            var attrs = Styles.Attributes(doc, ElementKind.GridElevation, type);
            attrs.Name = data.Name;
            LevelElement.ApplyLinePattern(doc, attrs, type.LinePattern, type.LinePatternScale);
            attrs.SetUserString(Keys.Owner, grid.Id.ToString());
            attrs = ElementIO.With(attrs, ElementKind.GridElevation, hd);
            var id = doc.Objects.AddCurve(new LineCurve(line), attrs);
            if (id == Guid.Empty)
                return false;
            data.Heads[key] = id;
            hd.Id = id;
            ElementIO.Write(doc, doc.Objects.FindId(id), ElementKind.GridElevation, hd);
            ctx.Modified++;
            return true;
        }

        /// <summary>
        /// A bubble's circle is its text's mask frame, drawn with the text object's linetype: an elevation bubble sits on
        /// the Grid Elevations layer, whose linetype is the dashed datum one, so every bubble draws Continuous itself.
        /// </summary>
        static void Solid(ObjectAttributes a)
        {
            a.LinetypeSource = ObjectLinetypeSource.LinetypeFromObject;
            a.LinetypeIndex = -1;
        }

        /// <summary>
        /// One bubble, in the plan or in an elevation: a text drawn over the grid line (display order 1), so its circle
        /// hides the line. A bubble whose text, formatting or size was changed by hand is made again from the grid. Its
        /// circle is always solid (<see cref="Solid"/>).
        /// </summary>
        static bool SyncBubble(UpdateContext ctx, RhinoObject grid, GridData data, GridType type, int end, TextEntity geometry, Point3d center, int which = PlanBubble)
        {
            var doc = ctx.Doc;
            string key = Key(end, true, which);
            var kind = which == PlanBubble ? ElementKind.GridHead : ElementKind.GridElevation;
            var hd = new GridHeadData { Grid = grid.Id, End = end, Label = true, Elevation = which, Type = type.Name };
            const int order = 1;
            RhinoObject part = data.Heads.TryGetValue(key, out Guid pid) ? ElementIO.Find(doc, pid) : null;
            if (part != null && ElementIO.KindOf(part) == kind && part.Geometry is TextEntity current)
            {
                bool same = Center(current).DistanceTo(center) <= ctx.Tol && current.DimensionStyleId == geometry.DimensionStyleId &&
                            ElementIO.NormalizeText(current.PlainText) == ElementIO.NormalizeText(geometry.PlainText) &&
                            current.RichText == geometry.RichText && Math.Abs(current.TextHeight - geometry.TextHeight) <= 1e-9 * Math.Max(1, geometry.TextHeight) &&
                            current.Justification == geometry.Justification &&
                            current.Plane.XAxis.EpsilonEquals(geometry.Plane.XAxis, 1e-9) && current.Plane.YAxis.EpsilonEquals(geometry.Plane.YAxis, 1e-9);
                bool replaced = false;
                if (!same)
                {
                    replaced = doc.Objects.Replace(part.Id, geometry);
                    if (replaced)
                        ctx.Modified++;
                    part = doc.Objects.FindId(part.Id);
                }
                hd.Id = part.Id;
                var a = part.Attributes.Duplicate();
                Styles.ApplyTypeAttributes(a, type);
                Solid(a);
                a.DisplayOrder = order;
                a.SetUserString(Keys.Owner, grid.Id.ToString());
                a.SetUserString(Keys.Data, Json.Write(hd));
                if (a.GetUserString(Keys.Data) != part.Attributes.GetUserString(Keys.Data) || a.ColorSource != part.Attributes.ColorSource ||
                    a.ObjectColor != part.Attributes.ObjectColor || a.DisplayOrder != part.Attributes.DisplayOrder ||
                    a.LinetypeSource != part.Attributes.LinetypeSource || a.LinetypeIndex != part.Attributes.LinetypeIndex)
                    doc.Objects.ModifyAttributes(part, a, true);
                return replaced;
            }
            if (part != null)
            {
                doc.Objects.Delete(part, true, true);
                data.Heads.Remove(key);
            }
            var attrs = Styles.Attributes(doc, kind, type);
            Solid(attrs);
            attrs.DisplayOrder = order;
            attrs.SetUserString(Keys.Owner, grid.Id.ToString());
            attrs = ElementIO.With(attrs, kind, hd);
            var id = doc.Objects.AddText(geometry, attrs);
            if (id == Guid.Empty)
                return false;
            data.Heads[key] = id;
            hd.Id = id;
            ElementIO.Write(doc, doc.Objects.FindId(id), kind, hd);
            ctx.Modified++;
            return true;
        }

        /// <summary>
        /// The user edited a bubble. A new name typed into it renames the grid, and the grids after it follow on. A plan
        /// bubble moved along the grid takes that end there and the grid leaves its datum box; moved any other way it
        /// snaps back. Elevation parts always snap back (they follow the grid, its levels and its box). A copied or
        /// orphan part is removed.
        /// </summary>
        public static void HeadEdited(UpdateContext ctx, RhinoObject head)
        {
            var doc = ctx.Doc;
            var hd = ElementIO.Read<GridHeadData>(head);
            var grid = hd == null ? null : ElementIO.Find(doc, hd.Grid);
            var data = grid == null ? null : ElementIO.Read<GridData>(grid);
            if (data == null || !data.Heads.TryGetValue(Key(hd.End, hd.Label, hd.Elevation), out Guid owned) || owned != head.Id)
            {
                // orphan or copied part: bubbles are derived from their grid
                doc.Objects.Delete(head, true, true);
                ctx.Modified++;
                return;
            }
            // a name typed into a bubble renames the grid (that also updates its bubbles, this one included)
            if (head.Geometry is AnnotationBase typedIn)
            {
                string typed = CleanName(typedIn.PlainText);
                if (typed.Length > 0 && typed != data.Name && RenameFollowing(ctx, grid.Id, typed).Count > 0)
                    return;
            }
            if (hd.Elevation != PlanBubble || !TryLine(grid.Geometry, out var line))
            {
                Update(ctx, grid, UpdateReason.Self);
                return;
            }
            var a = line.From;
            var b = line.To;
            var now = Center(head.Geometry);
            if (now.IsValid)
            {
                var dir = b - a;
                dir.Z = 0;
                dir.Unitize();
                var move = now - (hd.End == 1 ? a : b);
                move.Z = 0;
                double along = move * dir;
                if (Math.Abs(along) > ctx.Tol)
                {
                    var na = hd.End == 1 ? a + dir * along : a;
                    var nb = hd.End == 2 ? b + dir * along : b;
                    if ((nb - na) * dir > ctx.Tol)
                    {
                        data.Box = Guid.Empty;
                        ElementIO.Write(doc, grid, ElementKind.Grid, data);
                        if (doc.Objects.Replace(grid.Id, new LineCurve(na, nb), true))
                            ctx.Modified++;
                        grid = doc.Objects.FindId(grid.Id);
                    }
                }
            }
            Update(ctx, grid, UpdateReason.Self);
        }

        /// <summary>
        /// The user deleted a grid part. A plan bubble switches that end's bubble off (bubbles are hidden, not
        /// deleted); an elevation part comes back (it is part of the grid: the type's Elevation Bubbles sets them).
        /// </summary>
        public static void HeadDeleted(UpdateContext ctx, Guid gridId, int end, bool label, int which, Guid partId)
        {
            var grid = ElementIO.Find(ctx.Doc, gridId);
            var data = grid == null ? null : ElementIO.Read<GridData>(grid);
            string key = Key(end, label, which);
            if (data == null || !data.Heads.TryGetValue(key, out Guid owned) || owned != partId)
                return;
            data.Heads.Remove(key);
            if (which == PlanBubble && end == 1)
                data.End1 = false;
            else if (which == PlanBubble)
                data.End2 = false;
            ElementIO.Write(ctx.Doc, grid, ElementKind.Grid, data);
            Update(ctx, ctx.Doc.Objects.FindId(gridId), UpdateReason.Self);
        }

        /// <summary>The grid was deleted: delete its bubbles.</summary>
        public static void Deleted(UpdateContext ctx, Guid gridId, GridData data)
        {
            if (data == null)
                return;
            foreach (var id in data.Heads.Values.ToList())
            {
                var part = ElementIO.Find(ctx.Doc, id);
                if (part != null)
                {
                    ctx.Doc.Objects.Delete(part, true, true);
                    ctx.Modified++;
                }
            }
        }

        // ---- edits used by commands and dialogs -------------------------------------------------

        /// <summary>Bubbles at End 1 / End 2 ("Plan View Symbols End 1 / End 2").</summary>
        public static bool SetEnds(UpdateContext ctx, Guid id, bool end1, bool end2)
        {
            var obj = ElementIO.Find(ctx.Doc, id);
            var data = obj == null ? null : ElementIO.Read<GridData>(obj);
            if (data == null)
                return false;
            data.End1 = end1;
            data.End2 = end2;
            ElementIO.Write(ctx.Doc, obj, ElementKind.Grid, data);
            return Update(ctx, ctx.Doc.Objects.FindId(id), UpdateReason.Context);
        }

        /// <summary>Where a grid's bubbles are in elevations: its own choice, else its type's.</summary>
        public static GridElevationBubbles ElevationBubblesOf(GridData data, GridType type) =>
            data?.ElevationBubbles ?? type?.ElevationBubbles ?? GridElevationBubbles.Top;

        static GridElevationBubbles Elevation(bool top, bool bottom) =>
            top && bottom ? GridElevationBubbles.Both : top ? GridElevationBubbles.Top : bottom ? GridElevationBubbles.Bottom : GridElevationBubbles.None;

        /// <summary>A grid's own elevation bubbles (null: its type's again).</summary>
        public static bool SetElevationBubbles(UpdateContext ctx, Guid id, GridElevationBubbles? value)
        {
            var obj = ElementIO.Find(ctx.Doc, id);
            var data = obj == null ? null : ElementIO.Read<GridData>(obj);
            if (data == null || data.ElevationBubbles == value)
                return false;
            data.ElevationBubbles = value;
            ElementIO.Write(ctx.Doc, obj, ElementKind.Grid, data);
            return Update(ctx, ctx.Doc.Objects.FindId(id), UpdateReason.Context);
        }

        /// <summary>
        /// Bubbles on these sides of a view (its right and up): each grid end whose outward direction points to one of
        /// them gets a bubble. With <paramref name="addOnly"/> bubbles are only added (a layout detail hides the others
        /// itself); otherwise the other ends lose theirs. Returns how many grids changed.
        /// </summary>
        public static int SetBubbleSides(UpdateContext ctx, IEnumerable<Guid> ids, GridBubbleSides sides, Vector3d right, Vector3d up, bool addOnly)
        {
            int changed = 0;
            foreach (var id in ids.Distinct().ToList())
            {
                var obj = ElementIO.Find(ctx.Doc, id);
                var data = obj == null ? null : ElementIO.Read<GridData>(obj);
                if (data == null || !TryLine(obj.Geometry, out var line))
                    continue;
                var dir = line.Direction;
                dir.Z = 0;
                if (!dir.Unitize())
                    continue;
                bool end1 = (sides & GridViews.SideOf(-dir, right, up)) != 0;
                bool end2 = (sides & GridViews.SideOf(dir, right, up)) != 0;
                if (addOnly)
                {
                    end1 |= data.End1;
                    end2 |= data.End2;
                }
                if (end1 == data.End1 && end2 == data.End2)
                    continue;
                SetEnds(ctx, id, end1, end2);
                changed++;
            }
            return changed;
        }

        /// <summary>
        /// Bubbles at the top and / or bottom of grids' elevation lines (the grids' own Elevation Bubbles). With
        /// <paramref name="addOnly"/> they are only added to what each grid shows now. Returns how many grids changed.
        /// </summary>
        public static int SetElevationSides(UpdateContext ctx, IEnumerable<Guid> ids, GridBubbleSides sides, bool addOnly)
        {
            int changed = 0;
            foreach (var id in ids.Distinct().ToList())
            {
                var info = Info(ctx.Doc, ElementIO.Find(ctx.Doc, id), ctx.Types);
                if (info == null)
                    continue;
                var now = ElevationBubblesOf(info.Data, info.Type);
                bool top = (sides & GridBubbleSides.Top) != 0, bottom = (sides & GridBubbleSides.Bottom) != 0;
                if (addOnly)
                {
                    top |= now == GridElevationBubbles.Top || now == GridElevationBubbles.Both;
                    bottom |= now == GridElevationBubbles.Bottom || now == GridElevationBubbles.Both;
                }
                // already showing them (by its own choice or its type's): leave the grid as it is
                var wanted = Elevation(top, bottom);
                if (wanted == now)
                    continue;
                SetElevationBubbles(ctx, id, wanted);
                changed++;
            }
            return changed;
        }

        /// <summary>Rename a grid; the grids after it follow on (see <see cref="RenameFollowing"/>).</summary>
        public static bool Rename(UpdateContext ctx, Guid id, string name)
        {
            var obj = ElementIO.Find(ctx.Doc, id);
            if (obj == null || ElementIO.KindOf(obj) != ElementKind.Grid || string.IsNullOrWhiteSpace(name))
                return false;
            return RenameFollowing(ctx, id, name).Count > 0;
        }

        /// <summary>Put a grid in a datum box (its ends go onto the box's outline) or, with an empty id, take it out.</summary>
        public static bool Attach(UpdateContext ctx, Guid gridId, Guid boxId)
        {
            var obj = ElementIO.Find(ctx.Doc, gridId);
            var data = obj == null ? null : ElementIO.Read<GridData>(obj);
            if (data == null || data.Box == boxId)
                return false;
            if (boxId != Guid.Empty && !DatumBoxElement.TryOutline(ctx.Doc, boxId, out _))
                return false;
            data.Box = boxId;
            // the line as it is now is the grid's own: joining is not an edit of its ends
            if (TryLine(obj.Geometry, out var line))
                data.Points = ElementIO.Flatten(new[] { line.From, line.To });
            ElementIO.Write(ctx.Doc, obj, ElementKind.Grid, data);
            return Update(ctx, ctx.Doc.Objects.FindId(gridId), UpdateReason.Context);
        }
    }
}
