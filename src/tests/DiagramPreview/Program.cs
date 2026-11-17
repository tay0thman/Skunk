using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Skunk.Elements;

namespace DiagramPreview
{
    /// <summary>
    /// The SkGridBubbles diagram for a few layouts, as PNG files: a plan with grids 1-6 and A-D (and A.1 part way), the
    /// same plan in a view turned a quarter, the grids standing in an elevation, and the sample for a view with no
    /// grids. Only managed geometry: Rhino's native code is not loaded here.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("usage: DiagramPreview <folder for the pictures>");
                return 2;
            }
            Eto.Platform.Initialize(new Eto.Wpf.Platform());
            System.IO.Directory.CreateDirectory(args[0]);
            var grids = new List<GridInfo>();
            for (int i = 0; i < 6; i++)
                grids.Add(new GridInfo { Name = (i + 1).ToString(), Line = new Line(new Point3d(i * 24, -6, 0), new Point3d(i * 24, 78, 0)) });
            for (int i = 0; i < 4; i++)
                grids.Add(new GridInfo { Name = ((char)('A' + i)).ToString(), Line = new Line(new Point3d(-6, i * 24, 0), new Point3d(126, i * 24, 0)) });
            grids.Add(new GridInfo { Name = "A.1", Line = new Line(new Point3d(-6, 12, 0), new Point3d(60, 12, 0)) });
            var topLeft = GridBubbleSides.Top | GridBubbleSides.Left;
            var bottomRight = GridBubbleSides.Bottom | GridBubbleSides.Right;
            var pictures = new (string File, GridBubbleDiagram Diagram, GridBubbleSides Sides)[]
            {
                ("plan-top-left.png", GridBubbleDiagram.Plan(grids, Vector3d.XAxis, Vector3d.YAxis, topLeft), topLeft),
                ("plan-turned-bottom-right.png", GridBubbleDiagram.Plan(grids, Vector3d.YAxis, -Vector3d.XAxis, bottomRight), bottomRight),
                ("elevation-top.png", GridBubbleDiagram.Elevation(grids.Take(6), Vector3d.XAxis, GridBubbleSides.Top), GridBubbleSides.Top),
                ("sample-all.png", GridBubbleDiagram.SamplePlan(GridBubbleSides.All), GridBubbleSides.All),
            };
            foreach (var p in pictures)
            {
                string file = System.IO.Path.Combine(args[0], p.File);
                Skunk.UI.GridBubblesDialog.SavePicture(file, p.Diagram, p.Sides);
                Console.WriteLine("wrote " + file + " (" + p.Diagram.BubbleCount + " bubbles)");
            }
            return 0;
        }
    }
}
