using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Skunk.Core;
using Skunk.Elements;
using Skunk.Site;
using Skunk.Types;

namespace Skunk.Service
{
    /// <summary>
    /// Keeps the project base point / survey point marker objects (point objects you can select and
    /// move) and the two named CPlanes in sync, in both directions.
    /// </summary>
    public static class SiteSync
    {
        public static RhinoObject Marker(RhinoDoc doc, Guid id, ElementKind kind)
        {
            var o = ElementIO.Find(doc, id);
            return o != null && ElementIO.KindOf(o) == kind && o.Geometry is Point ? o : null;
        }

        /// <summary>
        /// Apply marker moves to the site, re-create deleted markers, move markers after a named CPlane
        /// was edited elsewhere. Returns true when the coordinate systems changed.
        /// </summary>
        public static bool Run(RhinoDoc doc, ISet<Guid> changed, string lastSignature, out SiteModel site)
        {
            site = SiteModel.Get(doc);
            string before = site.Signature();
            var data = site.Data;
            double tol = Math.Max(doc.ModelAbsoluteTolerance, 1e-9);
            bool dirty = false;

            var pbp = Marker(doc, data.PbpMarker, ElementKind.ProjectBasePoint);
            if (pbp != null && changed != null && changed.Contains(pbp.Id))
            {
                var p = ((Point)pbp.Geometry).Location;
                if (p.DistanceTo(site.Project.Origin) > tol)
                {
                    site.MoveProjectBasePoint(p);
                    dirty = true;
                }
            }
            var sp = Marker(doc, data.SpMarker, ElementKind.SurveyPoint);
            if (sp != null && changed != null && changed.Contains(sp.Id))
            {
                var p = ((Point)sp.Geometry).Location;
                if (p.DistanceTo(site.Survey.Origin) > tol)
                {
                    site.MoveSurveyPoint(p, data.SpClipped);
                    dirty = true;
                }
            }
            if (!site.ProjectFromCPlane || !site.SurveyFromCPlane)
                dirty = true;
            // a plane edited on its named CPlane (Rhino's Named CPlanes panel) is saved like any other change: the last
            // known planes stay current, and Rhino's north follows a true north turned there (SiteModel.Save)
            if (Moved(site.Project, data.PbpPlane, tol) || Moved(site.Survey, data.SpPlane, tol))
                dirty = true;
            if (dirty)
                site.Save(doc);

            // markers follow the planes (also after a named CPlane was updated in Rhino's panel)
            EnsureMarker(doc, ref data.PbpMarker, ElementKind.ProjectBasePoint, site.Project.Origin, "Project Base Point", tol, ref dirty);
            EnsureMarker(doc, ref data.SpMarker, ElementKind.SurveyPoint, site.Survey.Origin, "Survey Point", tol, ref dirty);
            if (dirty)
                DocStore.Set(doc, Keys.SiteEntry, data);

            string after = site.Signature();
            return after != before || (lastSignature != null && after != lastSignature);
        }

        /// <summary>A site plane is no longer where it was last saved (never saved: not moved).</summary>
        static bool Moved(Plane now, double[] stored, double tol)
        {
            var saved = SiteModel.PlaneFrom(stored);
            return saved.HasValue && (now.Origin.DistanceTo(saved.Value.Origin) > tol || (now.YAxis - saved.Value.YAxis).Length > 1e-9);
        }

        static void EnsureMarker(RhinoDoc doc, ref Guid id, ElementKind kind, Point3d location, string name, double tol, ref bool dirty)
        {
            var o = Marker(doc, id, kind);
            if (o == null)
            {
                // re-use a marker found by kind (e.g. after an import), else create one
                foreach (var candidate in ElementIO.All(doc, kind))
                {
                    if (candidate.Geometry is Point)
                    {
                        o = candidate;
                        id = o.Id;
                        dirty = true;
                        break;
                    }
                }
            }
            if (o == null)
            {
                var a = Styles.Attributes(doc, kind, null);
                a.Name = name;
                id = doc.Objects.AddPoint(location, a);
                dirty = true;
                return;
            }
            var p = ((Point)o.Geometry).Location;
            if (p.DistanceTo(location) > tol)
                doc.Objects.Replace(o.Id, location);
        }

        /// <summary>Delete duplicate markers (copies / pastes): there is one project base point and one survey point.</summary>
        public static void RemoveDuplicates(RhinoDoc doc, IEnumerable<Guid> added)
        {
            var data = SiteModel.Get(doc).Data;
            foreach (var id in added)
            {
                var o = ElementIO.Find(doc, id);
                var k = ElementIO.KindOf(o);
                if (k == ElementKind.ProjectBasePoint && id != data.PbpMarker && Marker(doc, data.PbpMarker, k) != null)
                    doc.Objects.Delete(o, true, true);
                else if (k == ElementKind.SurveyPoint && id != data.SpMarker && Marker(doc, data.SpMarker, k) != null)
                    doc.Objects.Delete(o, true, true);
            }
        }
    }
}
