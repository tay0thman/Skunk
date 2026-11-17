using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Skunk.Core;
using Skunk.Elements;

namespace Skunk.Service
{
    /// <summary>
    /// The rest of every datum box, drawn on screen: the box object is its plan outline, just under its lowest
    /// level; this adds the vertical edges and the top, just over its highest level, dotted, in the box's colour
    /// (the selection colour while it is selected), with its name at the first corner. Hidden with the box or its
    /// layer, never printed, never rendered.
    /// </summary>
    public sealed class DatumBoxConduit : DisplayConduit
    {
        IEnumerable<(RhinoObject Obj, Polyline Bottom, double Top, string Name)> Boxes(RhinoDoc doc)
        {
            var index = SkunkService.Instance?.Index(doc);
            if (index == null || index.Boxes.Count == 0)
                yield break;
            foreach (var id in index.Boxes.ToList())
            {
                var obj = ElementIO.Find(doc, id);
                if (obj == null || !obj.Visible || !(obj.Geometry is Curve c) || !c.TryGetPolyline(out var pl) || pl.Count < 2)
                    continue;
                int li = obj.Attributes.LayerIndex;
                if (li >= 0 && li < doc.Layers.Count && !doc.Layers[li].IsVisible)
                    continue;
                var data = ElementIO.Read<DatumBoxData>(obj);
                if (data == null || double.IsNaN(data.Top) || data.Top <= pl[0].Z)
                    continue;
                yield return (obj, pl, data.Top, data.Name ?? "");
            }
        }

        /// <summary>Model views and the details on layouts; never a layout page itself (paper space).</summary>
        static bool ModelSpace(RhinoViewport vp) =>
            vp != null && (vp.ViewportType == ViewportType.StandardModelingViewport || vp.ViewportType == ViewportType.DetailViewport);

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            var doc = e.RhinoDoc ?? RhinoDoc.ActiveDoc;
            if (doc == null || !ModelSpace(e.Viewport))
                return;
            foreach (var b in Boxes(doc))
            {
                var bb = b.Bottom.BoundingBox;
                bb.Union(new Point3d(bb.Max.X, bb.Max.Y, b.Top));
                e.IncludeBoundingBox(bb);
            }
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            if (e.Display.IsPrinting || !ModelSpace(e.Viewport))
                return;
            var doc = e.RhinoDoc ?? RhinoDoc.ActiveDoc;
            if (doc == null)
                return;
            foreach (var b in Boxes(doc))
            {
                var color = b.Obj.IsSelected(false) > 0
                    ? Rhino.ApplicationSettings.AppearanceSettings.SelectedObjectColor
                    : b.Obj.Attributes.DrawColor(doc);
                var top = b.Bottom.Select(p => new Point3d(p.X, p.Y, b.Top)).ToList();
                e.Display.DrawDottedPolyline(top, color, false);
                int corners = b.Bottom.IsClosed ? b.Bottom.Count - 1 : b.Bottom.Count;
                for (int i = 0; i < corners; i++)
                    e.Display.DrawDottedLine(b.Bottom[i], top[i], color);
                if (b.Name.Length > 0)
                    e.Display.Draw2dText(b.Name, color, top[0], false, 11);
            }
        }
    }
}
