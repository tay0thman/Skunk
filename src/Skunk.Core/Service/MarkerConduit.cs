using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Skunk.Core;
using Skunk.Site;

namespace Skunk.Service
{
    /// <summary>
    /// Screen-size glyphs for the internal origin, project base point (circle + X) and survey point
    /// (triangle + cross), with project north and true north arrows - the site points.
    /// Never printed: the point objects themselves sit on a no-print layer.
    /// </summary>
    public sealed class MarkerConduit : DisplayConduit
    {
        static readonly Color Pbp = Color.FromArgb(30, 90, 210);
        static readonly Color Sp = Color.FromArgb(30, 150, 90);
        static readonly Color Io = Color.FromArgb(200, 60, 60);

        readonly Dictionary<uint, (string Text, SiteModel Site, SkunkSettings Settings, long Stamp)> _cache =
            new Dictionary<uint, (string, SiteModel, SkunkSettings, long)>();
        readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null)
                return;
            var (site, settings) = Get(doc);
            if (site == null || !settings.ShowSiteMarkers)
                return;
            e.IncludeBoundingBox(new BoundingBox(new[] { site.Project.Origin, site.Survey.Origin, Point3d.Origin }));
        }

        protected override void DrawForeground(DrawEventArgs e)
        {
            if (e.Display.IsPrinting)
                return;
            var doc = e.RhinoDoc ?? RhinoDoc.ActiveDoc;
            if (doc == null)
                return;
            var (site, settings) = Get(doc);
            if (site == null || !settings.ShowSiteMarkers)
                return;
            double px = Math.Max(10, settings.MarkerPixelSize);
            var vp = e.Viewport;
            if (settings.ShowInternalOrigin)
                DrawInternalOrigin(e, vp, px * 0.8);
            DrawProjectBasePoint(e, vp, site, px);
            DrawSurveyPoint(e, vp, site, px);
        }

        (SiteModel, SkunkSettings) Get(RhinoDoc doc)
        {
            // re-read at most every 250 ms (named CPlanes and the site JSON are cheap but not free)
            long now = _clock.ElapsedMilliseconds;
            if (_cache.TryGetValue(doc.RuntimeSerialNumber, out var c) && now - c.Stamp < 250)
                return (c.Site, c.Settings);
            try
            {
                var site = SiteModel.Get(doc);
                var settings = DocStore.Settings(doc);
                _cache[doc.RuntimeSerialNumber] = (site.Signature(), site, settings, now);
                return (site, settings);
            }
            catch
            {
                return (null, new SkunkSettings());
            }
        }

        /// <summary>World length of a screen distance at a point.</summary>
        static double Px(RhinoViewport vp, Point3d at, double pixels)
        {
            if (!vp.GetWorldToScreenScale(at, out double pixelsPerUnit) || pixelsPerUnit <= 0)
                return pixels * 0.01;
            return pixels / pixelsPerUnit;
        }

        static Plane ScreenPlane(RhinoViewport vp, Point3d at) => new Plane(at, vp.CameraX, vp.CameraY);

        static void DrawInternalOrigin(DrawEventArgs e, RhinoViewport vp, double px)
        {
            var o = Point3d.Origin;
            double s = Px(vp, o, px);
            e.Display.DrawLine(new Line(o, o + Vector3d.XAxis * s), Io, 2);
            e.Display.DrawLine(new Line(o, o + Vector3d.YAxis * s), Color.FromArgb(60, 160, 60), 2);
            e.Display.DrawLine(new Line(o, o + Vector3d.ZAxis * s), Color.FromArgb(60, 90, 200), 2);
            e.Display.Draw2dText("Internal Origin", Io, o, false, 11);
        }

        static void DrawProjectBasePoint(DrawEventArgs e, RhinoViewport vp, SiteModel site, double px)
        {
            var o = site.Project.Origin;
            double r = Px(vp, o, px * 0.5);
            var sp = ScreenPlane(vp, o);
            e.Display.DrawCircle(new Circle(sp, r), Pbp, 2);
            double d = r * 0.7071;
            e.Display.DrawLine(new Line(sp.PointAt(-d, -d), sp.PointAt(d, d)), Pbp, 2);
            e.Display.DrawLine(new Line(sp.PointAt(-d, d), sp.PointAt(d, -d)), Pbp, 2);
            // project north arrow
            var n = site.ProjectNorth;
            var tip = o + n * r * 3.2;
            e.Display.DrawArrow(new Line(o + n * r * 1.2, tip), Pbp, 0, 0.25);
            e.Display.Draw2dText("PN", Pbp, tip + n * r * 0.6, true, 11);
            e.Display.Draw2dText("Project Base Point", Pbp, o - sp.YAxis * r * 1.8, true, 11);
        }

        static void DrawSurveyPoint(DrawEventArgs e, RhinoViewport vp, SiteModel site, double px)
        {
            var o = site.Survey.Origin;
            double r = Px(vp, o, px * 0.55);
            var sp = ScreenPlane(vp, o);
            var a = sp.PointAt(0, r);
            var b = sp.PointAt(-r * 0.866, -r * 0.5);
            var c = sp.PointAt(r * 0.866, -r * 0.5);
            e.Display.DrawPolyline(new[] { a, b, c, a }, Sp, 2);
            e.Display.DrawLine(new Line(sp.PointAt(-r * 0.6, 0), sp.PointAt(r * 0.6, 0)), Sp, 1);
            e.Display.DrawLine(new Line(sp.PointAt(0, -r * 0.6), sp.PointAt(0, r * 0.6)), Sp, 1);
            var n = site.TrueNorth;
            var tip = o + n * r * 3.4;
            e.Display.DrawArrow(new Line(o + n * r * 1.2, tip), Sp, 0, 0.25);
            e.Display.Draw2dText("TN", Sp, tip + n * r * 0.6, true, 11);
            e.Display.Draw2dText("Survey Point", Sp, o - sp.YAxis * r * 1.8, true, 11);
        }
    }
}
