using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Skunk.Elements
{
    /// <summary>
    /// What the SkGridBubbles dialog draws: the grids as the view shows them (their ends on its right and up axes), each
    /// end with the side of the view it is on and whether it gets a bubble - the rule the command applies (a grid end
    /// shows a bubble when its side is ticked; <see cref="GridElement.SetBubbleSides"/>). Looking level, the grids the
    /// view looks along stand as vertical lines with a bubble at the top and / or bottom. With no grids it is a sample
    /// plan (1, 2, 3 across, A, B, C up). No Eto here, so SkunkCheck checks it headless.
    /// </summary>
    public sealed class GridBubbleDiagram
    {
        /// <summary>A grid end: where it is in view coordinates (right, up), the side of the view it is on, its bubble.</summary>
        public sealed class End
        {
            public double X;
            public double Y;
            public GridBubbleSides Side;
            public bool Bubble;
        }

        /// <summary>One grid: End 1 and End 2 in a plan; bottom and top looking level.</summary>
        public sealed class Item
        {
            public string Name;
            public End A;
            public End B;
        }

        public readonly List<Item> Grids = new List<Item>();

        /// <summary>Looking level: vertical lines with top and bottom bubbles (left and right do not apply).</summary>
        public bool LookingLevel;

        /// <summary>No grids to show: a sample plan.</summary>
        public bool Sample;

        public double MinX = double.MaxValue, MinY = double.MaxValue, MaxX = double.MinValue, MaxY = double.MinValue;

        /// <summary>The grids of a plan view: each grid's ends on the view's right and up axes.</summary>
        public static GridBubbleDiagram Plan(IEnumerable<GridInfo> grids, Vector3d right, Vector3d up, GridBubbleSides sides)
        {
            var d = new GridBubbleDiagram();
            Unit(ref right);
            Unit(ref up);
            foreach (var g in grids ?? Enumerable.Empty<GridInfo>())
            {
                var dir = g.Line.Direction;
                dir.Z = 0;
                if (!Unit(ref dir))
                    continue;
                d.Add(g.Name,
                      new End { X = (Vector3d)g.Line.From * right, Y = (Vector3d)g.Line.From * up, Side = GridViews.SideOf(-dir, right, up) },
                      new End { X = (Vector3d)g.Line.To * right, Y = (Vector3d)g.Line.To * up, Side = GridViews.SideOf(dir, right, up) });
            }
            if (d.Grids.Count == 0)
                return SamplePlan(sides);
            d.Apply(sides);
            return d;
        }

        /// <summary>
        /// Looking level: the grids the view looks along, left to right as the view shows them (their place on its right
        /// axis), as vertical lines a fixed share of the spread high, with the top and bottom bubbles.
        /// </summary>
        public static GridBubbleDiagram Elevation(IEnumerable<GridInfo> grids, Vector3d right, GridBubbleSides sides)
        {
            var d = new GridBubbleDiagram { LookingLevel = true };
            Unit(ref right);
            var placed = (grids ?? Enumerable.Empty<GridInfo>())
                .Select(g => (g.Name, X: (((Vector3d)g.Line.From + (Vector3d)g.Line.To) * 0.5) * right)).OrderBy(p => p.X).ToList();
            if (placed.Count == 0)
            {
                placed = new List<(string, double)> { ("1", 0), ("2", 1), ("3", 2) };
                d.Sample = true;
            }
            double spread = placed[placed.Count - 1].X - placed[0].X;
            double height = spread > 0 ? spread * 0.6 : 1;
            foreach (var p in placed)
                d.Add(p.Name, new End { X = p.X, Y = 0, Side = GridBubbleSides.Bottom }, new End { X = p.X, Y = height, Side = GridBubbleSides.Top });
            d.Apply(sides);
            return d;
        }

        /// <summary>A sample plan for a view with no grids: 1, 2, 3 run up the view, A, B, C across it.</summary>
        public static GridBubbleDiagram SamplePlan(GridBubbleSides sides)
        {
            var d = new GridBubbleDiagram { Sample = true };
            for (int i = 0; i < 3; i++)
            {
                d.Add((i + 1).ToString(), new End { X = i * 10, Y = -5, Side = GridBubbleSides.Bottom }, new End { X = i * 10, Y = 25, Side = GridBubbleSides.Top });
                d.Add(((char)('A' + i)).ToString(), new End { X = -5, Y = i * 10, Side = GridBubbleSides.Left }, new End { X = 25, Y = i * 10, Side = GridBubbleSides.Right });
            }
            d.Apply(sides);
            return d;
        }

        /// <summary>Bubbles for these sides: an end shows one when its side is among them.</summary>
        public void Apply(GridBubbleSides sides)
        {
            foreach (var g in Grids)
            {
                g.A.Bubble = (sides & g.A.Side) != 0;
                g.B.Bubble = (sides & g.B.Side) != 0;
            }
        }

        /// <summary>How many grid ends show a bubble.</summary>
        public int BubbleCount => Grids.Sum(g => (g.A.Bubble ? 1 : 0) + (g.B.Bubble ? 1 : 0));

        /// <summary>A unit vector without Rhino's native code (the diagram is also drawn outside Rhino).</summary>
        static bool Unit(ref Vector3d v)
        {
            double length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (length < 1e-12)
                return false;
            v = new Vector3d(v.X / length, v.Y / length, v.Z / length);
            return true;
        }

        void Add(string name, End a, End b)
        {
            Grids.Add(new Item { Name = name ?? "", A = a, B = b });
            foreach (var e in new[] { a, b })
            {
                MinX = Math.Min(MinX, e.X);
                MinY = Math.Min(MinY, e.Y);
                MaxX = Math.Max(MaxX, e.X);
                MaxY = Math.Max(MaxY, e.Y);
            }
        }
    }
}
