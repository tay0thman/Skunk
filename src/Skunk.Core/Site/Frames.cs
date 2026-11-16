using System;
using Rhino;
using Rhino.Geometry;
using Skunk.Core;

namespace Skunk.Site
{
    /// <summary>
    /// What a coordinate or elevation is measured from. The first three are the usual options
    /// (Elevation Origin / Coordinate Origin / Elevation Base); Skunk adds the internal origin
    /// and any named CPlane.
    /// </summary>
    public enum CoordinateBase
    {
        ProjectBasePoint = 0,
        SurveyPoint = 1,
        InternalOrigin = 2,
        NamedCPlane = 3,
        /// <summary>Relative to a level (spot elevations only - "Relative").</summary>
        Relative = 4,
    }

    /// <summary>A resolved measuring frame: a plane plus the values at its origin.</summary>
    public sealed class Frame
    {
        public Plane Plane = Plane.WorldXY;
        public double North0;
        public double East0;
        public double Elevation0;
        public string Label = "";
        public bool Valid = true;

        public double North(Point3d p) => North0 + (p - Plane.Origin) * Plane.YAxis;

        public double East(Point3d p) => East0 + (p - Plane.Origin) * Plane.XAxis;

        public double Elevation(Point3d p) => Elevation0 + (p - Plane.Origin) * Plane.ZAxis;

        /// <summary>World Z of a given elevation in this frame (horizontal frames only).</summary>
        public double WorldZ(double elevation)
        {
            double dz = Plane.ZAxis.Z;
            if (Math.Abs(dz) < 1e-12)
                return Plane.Origin.Z;
            return Plane.Origin.Z + (elevation - Elevation0) / dz;
        }
    }

    public static class Frames
    {
        public static string Describe(CoordinateBase b, string cplane = null)
        {
            switch (b)
            {
                case CoordinateBase.ProjectBasePoint: return "Project Base Point";
                case CoordinateBase.SurveyPoint: return "Survey Point";
                case CoordinateBase.InternalOrigin: return "Internal Origin";
                case CoordinateBase.NamedCPlane: return string.IsNullOrEmpty(cplane) ? "Named CPlane" : "CPlane \"" + cplane + "\"";
                case CoordinateBase.Relative: return "Relative";
                default: return b.ToString();
            }
        }

        /// <summary>
        /// Resolve a frame. <paramref name="relativeLevelZ"/> is the world Z of the level for
        /// <see cref="CoordinateBase.Relative"/> (NaN when the level is missing).
        /// </summary>
        public static Frame Get(RhinoDoc doc, SiteModel site, CoordinateBase b, string cplaneName = null, double relativeLevelZ = double.NaN)
        {
            site = site ?? SiteModel.Get(doc);
            switch (b)
            {
                case CoordinateBase.InternalOrigin:
                    return new Frame { Plane = Plane.WorldXY, Label = "Internal Origin" };
                case CoordinateBase.SurveyPoint:
                    return new Frame
                    {
                        Plane = site.Survey,
                        North0 = site.Data.SpNorth,
                        East0 = site.Data.SpEast,
                        Elevation0 = site.Data.SpElevation,
                        Label = "Survey Point",
                    };
                case CoordinateBase.NamedCPlane:
                    if (NamedPlanes.TryGet(doc, cplaneName, out var plane))
                        return new Frame { Plane = plane, Label = "CPlane " + cplaneName };
                    return new Frame { Plane = Plane.WorldXY, Label = "CPlane " + cplaneName + " (missing)", Valid = false };
                case CoordinateBase.Relative:
                    if (double.IsNaN(relativeLevelZ))
                        return new Frame { Plane = site.Project, Label = "Relative (no level)", Valid = false };
                    var p = site.Project;
                    p.Origin = new Point3d(p.Origin.X, p.Origin.Y, relativeLevelZ);
                    return new Frame { Plane = p, Label = "Relative" };
                case CoordinateBase.ProjectBasePoint:
                default:
                    return new Frame { Plane = site.Project, Label = "Project Base Point" };
            }
        }
    }
}
