using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Skunk.Core;

namespace Skunk.Elements
{
    /// <summary>Sides of a view grid bubbles can be on: as the view shows them, top and bottom of the screen, left and right.</summary>
    [Flags]
    public enum GridBubbleSides
    {
        None = 0,
        Top = 1,
        Bottom = 2,
        Left = 4,
        Right = 8,
        All = Top | Bottom | Left | Right,
    }

    /// <summary>
    /// Layout details show grids the way they read in that view, as grids do on drawings: a detail looking down (a
    /// plan) or at an angle shows the plan grid and bubbles; a detail looking level (an elevation or a section) shows
    /// only the elevation parts that face it - the ones beyond the grid end nearest to it, on grids it looks along -
    /// and nothing else of any grid. A detail can also keep its own bubble sides (SkGridBubbles): it then shows only
    /// the bubbles on those sides of it - in a plan the grid ends at its top, bottom, left or right, in an elevation the
    /// top or bottom bubbles. Skunk sets Rhino's Hide in Detail overrides on the parts, so details print and export as
    /// they show. Model views show every part (__Annotations__::Grid Elevations can be switched off there).
    /// </summary>
    public static class GridViews
    {
        /// <summary>A layout detail: its viewport id (what Hide in Detail names), how it looks, and its own bubble sides.</summary>
        public sealed class Detail
        {
            public Guid ViewportId;
            public Vector3d Direction;
            public bool Parallel;
            /// <summary>The view's right and up, for the sides of it a bubble is on.</summary>
            public Vector3d Right = Vector3d.XAxis;
            public Vector3d Up = Vector3d.YAxis;
            /// <summary>The sides its bubbles show on; null = every bubble.</summary>
            public GridBubbleSides? Sides;
        }

        /// <summary>Every detail on every layout of the model.</summary>
        public static List<Detail> Details(RhinoDoc doc)
        {
            var list = new List<Detail>();
            try
            {
                foreach (var page in doc.Views.GetPageViews() ?? Array.Empty<Rhino.Display.RhinoPageView>())
                    foreach (var d in page.GetDetailViews() ?? Array.Empty<DetailViewObject>())
                    {
                        var detail = Of(d);
                        if (detail != null)
                            list.Add(detail);
                    }
            }
            catch (Exception ex)
            {
                Log.Debug("details: " + ex.Message);
            }
            return list;
        }

        /// <summary>One layout detail as the grids see it (null when it has no viewport).</summary>
        public static Detail Of(DetailViewObject d)
        {
            var vp = d?.Viewport;
            if (vp == null)
                return null;
            var dir = vp.CameraDirection;
            dir.Unitize();
            var right = vp.CameraX;
            var up = vp.CameraUp;
            right.Unitize();
            up.Unitize();
            return new Detail { ViewportId = vp.Id, Direction = dir, Parallel = vp.IsParallelProjection, Right = right, Up = up, Sides = SidesOf(d) };
        }

        /// <summary>The bubble sides a detail keeps (its user text), or null when it shows every bubble.</summary>
        public static GridBubbleSides? SidesOf(RhinoObject detail)
        {
            var s = detail?.Attributes.GetUserString(Keys.DetailGridBubbles);
            return !string.IsNullOrWhiteSpace(s) && Enum.TryParse<GridBubbleSides>(s, true, out var sides) ? sides : (GridBubbleSides?)null;
        }

        /// <summary>Give a detail its own bubble sides (null: it shows every bubble again). True when they changed.</summary>
        public static bool SetSides(RhinoDoc doc, DetailViewObject detail, GridBubbleSides? sides)
        {
            if (detail == null || SidesOf(detail) == sides)
                return false;
            var a = detail.Attributes.Duplicate();
            a.SetUserString(Keys.DetailGridBubbles, sides.HasValue ? sides.Value.ToString() : null);
            return doc.Objects.ModifyAttributes(detail, a, true);
        }

        /// <summary>What the details are, how they look and which bubble sides they keep (to notice any change).</summary>
        public static string Signature(List<Detail> details) =>
            string.Join(";", details.Select(d => d.ViewportId.ToString("N") + ":" + V(d.Direction) + "/" + V(d.Up) + (d.Parallel ? "p" : "") +
                                                 (d.Sides.HasValue ? "s" + (int)d.Sides.Value : "")));

        static string V(Vector3d v) =>
            v.X.ToString("0.###", CultureInfo.InvariantCulture) + "," + v.Y.ToString("0.###", CultureInfo.InvariantCulture) + "," +
            v.Z.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>A detail looking within 30 degrees of straight down: a plan.</summary>
        public static bool IsPlan(Detail d) => d.Direction.Z < -0.866;

        /// <summary>A parallel detail looking within 10 degrees of level: an elevation or a section.</summary>
        public static bool IsElevation(Detail d) => d.Parallel && Math.Abs(d.Direction.Z) < 0.174;

        /// <summary>
        /// The side of a view a grid end is on: where the end's outward direction points on the screen - up or down for a
        /// grid that runs more up and down the view than across it, else left or right.
        /// </summary>
        public static GridBubbleSides SideOf(Vector3d outward, Vector3d right, Vector3d up)
        {
            double x = outward * right, y = outward * up;
            if (Math.Abs(y) >= Math.Abs(x))
                return y >= 0 ? GridBubbleSides.Top : GridBubbleSides.Bottom;
            return x >= 0 ? GridBubbleSides.Right : GridBubbleSides.Left;
        }

        /// <summary>
        /// Whether a grid part shows in a detail. A plan part (the grid line, a plan bubble) shows in every detail but an
        /// elevation. An elevation part shows only in an elevation that looks at its face: back along the grid, within
        /// 15 degrees, from beyond the end it stands at (<paramref name="facing"/> is that end's outward direction).
        /// </summary>
        public static bool Shows(Detail d, bool elevationPart, Vector3d facing)
        {
            if (!elevationPart)
                return !IsElevation(d);
            if (!IsElevation(d))
                return false;
            var v = d.Direction;
            v.Z = 0;
            var f = facing;
            f.Z = 0;
            if (!v.Unitize() || !f.Unitize())
                return false;
            return v * f < -Math.Cos(RhinoMath.ToRadians(15));
        }

        /// <summary>
        /// Whether a part of a grid shows in a detail: <see cref="Shows"/>, and for a bubble the detail's own sides - a plan
        /// bubble on a side of the detail it keeps, an elevation bubble at the top or bottom when it keeps that side.
        /// </summary>
        public static bool ShowsPart(Detail d, int part, bool bubble, Vector3d outward)
        {
            if (part == GridElement.PlanBubble)
            {
                if (!Shows(d, false, outward))
                    return false;
                return !bubble || !d.Sides.HasValue || (d.Sides.Value & SideOf(outward, d.Right, d.Up)) != 0;
            }
            if (!Shows(d, true, outward))
                return false;
            if (!bubble || !d.Sides.HasValue || part == GridElement.ElevationLine)
                return true;
            return (d.Sides.Value & (part == GridElement.ElevationTop ? GridBubbleSides.Top : GridBubbleSides.Bottom)) != 0;
        }

        /// <summary>
        /// Set the Hide in Detail overrides of grids and their parts for these details (other details' overrides are
        /// left alone). Returns how many objects changed.
        /// </summary>
        public static int Apply(RhinoDoc doc, IEnumerable<Guid> gridIds, List<Detail> details)
        {
            if (details == null || details.Count == 0)
                return 0;
            int changed = 0;
            foreach (var gid in gridIds.ToList())
            {
                var grid = ElementIO.Find(doc, gid);
                var data = grid == null ? null : ElementIO.Read<GridData>(grid);
                if (data == null || !GridElement.TryLine(grid.Geometry, out var line))
                    continue;
                var dir = line.Direction;
                dir.Z = 0;
                dir.Unitize();
                changed += Set(doc, grid, details, d => Shows(d, false, Vector3d.Unset));
                foreach (var id in data.Heads.Values)
                {
                    var part = ElementIO.Find(doc, id);
                    var hd = part == null ? null : ElementIO.Read<GridHeadData>(part);
                    if (hd == null)
                        continue;
                    var outward = hd.End == 1 ? -dir : dir;
                    bool bubble = part.Geometry is AnnotationBase;
                    changed += Set(doc, part, details, d => ShowsPart(d, hd.Elevation, bubble, outward));
                }
            }
            return changed;
        }

        static int Set(RhinoDoc doc, RhinoObject obj, List<Detail> details, Func<Detail, bool> shows)
        {
            var a = obj.Attributes.Duplicate();
            bool modified = false;
            foreach (var d in details)
            {
                bool hide = !shows(d);
                bool hidden = a.HasHideInDetailOverrideSet(d.ViewportId);
                // what Add / RemoveHideInDetailOverride return is no guide (Add answers false when it adds): ask again after
                if (hide && !hidden)
                    a.AddHideInDetailOverride(d.ViewportId);
                else if (!hide && hidden)
                    a.RemoveHideInDetailOverride(d.ViewportId);
                modified |= a.HasHideInDetailOverrideSet(d.ViewportId) != hidden;
            }
            if (!modified)
                return 0;
            doc.Objects.ModifyAttributes(obj, a, true);
            return 1;
        }
    }
}
