using System;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Skunk.Core;
using Skunk.Elements;
using Skunk.Site;

namespace Skunk.Service
{
    /// <summary>
    /// Keeps the lengths Skunk stores in model units (leader offsets, slope reach, level elevations, datum box
    /// outlines, the recorded edit-detection points, the survey point's shared coordinates) in step with Rhino's model
    /// units. When Rhino changes the units and scales every object, Skunk scales its stored lengths by
    /// the same factor, so the scaled annotations are not mistaken for user edits and nothing moves twice.
    /// </summary>
    public static class UnitScaling
    {
        /// <summary>
        /// The factor the stored lengths need: the one Rhino reported (UnitsChangedWithScaling), or, when
        /// the units changed while Skunk was not running, the change from the recorded units - but only if
        /// the annotations show Rhino scaled them (a change without scaling keeps every number). Records the
        /// current units either way.
        /// </summary>
        public static double Detect(RhinoDoc doc, double reported)
        {
            var settings = DocStore.Settings(doc);
            double now = DocUnits.MetersPerUnit(doc);
            double recorded = settings.MetersPerUnit;
            double factor = reported;
            bool changed = recorded > 0 && Math.Abs(recorded / now - 1) > 1e-9;
            if (changed && Math.Abs(reported - 1) < 1e-12 && ScaledWithUnits(doc, recorded / now))
                factor = recorded / now;
            if (recorded <= 0 || changed)
            {
                settings.MetersPerUnit = now;
                DocStore.SaveSettings(doc, settings);
            }
            return factor;
        }

        /// <summary>Scale every length Skunk stores in model units by <paramref name="f"/> (about the world origin, like Rhino).</summary>
        public static void Apply(RhinoDoc doc, double f)
        {
            double[] Scaled(double[] a)
            {
                if (a == null)
                    return null;
                var b = (double[])a.Clone();
                for (int i = 0; i < b.Length; i++)
                    b[i] *= f;
                return b;
            }
            foreach (var kind in new[] { ElementKind.SpotElevation, ElementKind.SpotCoordinate, ElementKind.SpotSlope })
                foreach (var o in ElementIO.All(doc, kind))
                {
                    var d = ElementIO.Read<SpotData>(o);
                    if (d == null)
                        continue;
                    if (d.Anchor != null)
                        d.Anchor.Last = Scaled(d.Anchor.Last);
                    d.Offset = Scaled(d.Offset);
                    d.Points = Scaled(d.Points);
                    if (d.SlopeTop.HasValue)
                        d.SlopeTop *= f;
                    if (d.SlopeBottom.HasValue)
                        d.SlopeBottom *= f;
                    ElementIO.Write(doc, o, kind, d);
                }
            foreach (var o in ElementIO.All(doc, ElementKind.Level))
            {
                var d = ElementIO.Read<LevelData>(o);
                if (d == null)
                    continue;
                if (!double.IsNaN(d.Z))
                    d.Z *= f;
                d.Points = Scaled(d.Points);
                d.ComputationHeight *= f;
                ElementIO.Write(doc, o, ElementKind.Level, d);
            }
            foreach (var o in ElementIO.All(doc, ElementKind.DatumBox))
            {
                var d = ElementIO.Read<DatumBoxData>(o);
                if (d == null)
                    continue;
                d.Points = Scaled(d.Points);
                if (!double.IsNaN(d.Top))
                    d.Top *= f;
                ElementIO.Write(doc, o, ElementKind.DatumBox, d);
            }
            foreach (var o in ElementIO.All(doc, ElementKind.Grid))
            {
                var d = ElementIO.Read<GridData>(o);
                if (d == null)
                    continue;
                d.Points = Scaled(d.Points);
                ElementIO.Write(doc, o, ElementKind.Grid, d);
            }
            foreach (var o in ElementIO.All(doc, ElementKind.NorthArrow))
            {
                var d = ElementIO.Read<NorthArrowData>(o);
                if (d == null)
                    continue;
                d.Size *= f;
                ElementIO.Write(doc, o, ElementKind.NorthArrow, d);
            }
            // Rhino scales the named CPlanes and the marker points itself; the survey point's shared values are Skunk's
            var site = DocStore.Get<SiteData>(doc, Keys.SiteEntry);
            if (site != null)
            {
                ScaleOrigin(site.PbpPlane, f);
                ScaleOrigin(site.SpPlane, f);
                site.SpNorth *= f;
                site.SpEast *= f;
                site.SpElevation *= f;
                DocStore.Set(doc, Keys.SiteEntry, site);
            }
        }

        static void ScaleOrigin(double[] plane, double f)
        {
            if (plane == null || plane.Length < 3)
                return;
            for (int i = 0; i < 3; i++)
                plane[i] *= f;
        }

        /// <summary>Do the annotations sit at their recorded places times <paramref name="f"/> (scaled) or at them (not)?</summary>
        static bool ScaledWithUnits(RhinoDoc doc, double f)
        {
            int scaled = 0, same = 0;
            void Vote(Point3d recorded, Point3d now)
            {
                if (!recorded.IsValid || !now.IsValid || recorded.DistanceTo(Point3d.Origin) < 1e-9)
                    return;
                var expected = new Point3d(recorded.X * f, recorded.Y * f, recorded.Z * f);
                double size = Math.Max(Math.Max(now.DistanceTo(Point3d.Origin), recorded.DistanceTo(Point3d.Origin)), 1e-12);
                if (now.DistanceTo(expected) <= 1e-6 * size)
                    scaled++;
                else if (now.DistanceTo(recorded) <= 1e-6 * size)
                    same++;
            }
            foreach (var kind in new[] { ElementKind.SpotElevation, ElementKind.SpotCoordinate, ElementKind.SpotSlope })
                foreach (var o in ElementIO.All(doc, kind))
                {
                    if (scaled + same >= 25)
                        break;
                    var d = ElementIO.Read<SpotData>(o);
                    var recorded = ElementIO.Unflatten(d?.Points);
                    var now = SpotElement.Fingerprint(o.Geometry);
                    if (recorded.Count > 0 && now.Count > 0)
                        Vote(recorded[0], now[0]);
                }
            foreach (var o in ElementIO.All(doc, ElementKind.Level))
            {
                var d = ElementIO.Read<LevelData>(o);
                if (d != null && !double.IsNaN(d.Z) && o.Geometry is Curve c)
                    Vote(new Point3d(0, 0, d.Z), new Point3d(0, 0, c.PointAtStart.Z));
            }
            var site = DocStore.Get<SiteData>(doc, Keys.SiteEntry);
            if (site?.PbpPlane != null && site.PbpPlane.Length >= 3 && doc.Objects.FindId(site.PbpMarker)?.Geometry is Point marker)
                Vote(new Point3d(site.PbpPlane[0], site.PbpPlane[1], site.PbpPlane[2]), marker.Location);
            return scaled > same;
        }
    }
}
