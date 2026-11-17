using System;
using System.Globalization;
using Rhino;
using Rhino.Geometry;
using Rhino.Render;
using Skunk.Commands;
using Skunk.Core;
using Skunk.Units;

namespace Skunk.Site
{
    /// <summary>
    /// The project's geographic location, read from the Rhino model: its Earth anchor point - latitude, longitude,
    /// elevation, a place name and address, the model point they belong to - and Rhino's north. Rhino's Sun uses the
    /// same location: in Rhino 9 a change to the Sun's location or north is written to the anchor point (the anchor
    /// point is not written back to a Sun already loaded). Skunk keeps no copy of it: the project location is set in
    /// Rhino (Document Properties > Location with its world map, EarthAnchorPoint, or the Sun panel) and Skunk shows
    /// it. Rhino's north follows
    /// Skunk's true north while the two agree (<see cref="FollowTrueNorth"/>), and a new Skunk site starts with true
    /// north along it (<see cref="SiteModel.StartSurvey"/>).
    /// </summary>
    public sealed class GeoLocation
    {
        /// <summary>Two plan directions within this angle (degrees) are the same north.</summary>
        public const double NorthTolerance = 1e-4;

        /// <summary>Latitude and longitude are set (a new Rhino model leaves them unset).</summary>
        public bool IsSet;
        /// <summary>Degrees, positive north.</summary>
        public double Latitude;
        /// <summary>Degrees, positive east.</summary>
        public double Longitude;
        /// <summary>Elevation of the anchor point in meters (Rhino's unit for it).</summary>
        public double ElevationMeters;
        /// <summary>What the elevation is measured from, in words ("" when Rhino does not say).</summary>
        public string ElevationReference = "";
        public string Name = "";
        public string Description = "";
        /// <summary>The model point at that latitude and longitude.</summary>
        public Point3d ModelPoint;
        /// <summary>Rhino's north, a plan direction: the anchor point's model north, which the Sun's north angle sets.</summary>
        public Vector3d North = Vector3d.YAxis;

        /// <summary>The Rhino model's geographic location (a new Rhino model: not set, north along world Y).</summary>
        public static GeoLocation Read(RhinoDoc doc)
        {
            var g = new GeoLocation();
            if (doc == null)
                return g;
            try
            {
                // the property hands out a copy each time
                using (var e = doc.EarthAnchorPoint)
                {
                    if (e == null)
                        return g;
                    g.IsSet = e.EarthLocationIsSet();
                    if (g.IsSet)
                    {
                        g.Latitude = e.EarthBasepointLatitude;
                        g.Longitude = e.EarthBasepointLongitude;
                        g.ElevationMeters = e.EarthBasepointElevation;
                    }
                    g.ElevationReference = Reference(e.EarthBasepointElevationCoordinateSystem.ToString());
                    g.Name = (e.Name ?? "").Trim();
                    g.Description = (e.Description ?? "").Trim();
                    g.ModelPoint = e.ModelBasePoint;
                    g.North = Plan(e.ModelNorth) ?? Vector3d.YAxis;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("geographic location: " + ex.Message);
            }
            return g;
        }

        static string Reference(string zero)
        {
            switch (zero)
            {
                case "GroundLevel": return "above ground level";
                case "MeanSeaLevel": return "above mean sea level";
                case "CenterOfEarth": return "from the center of the earth";
                case "WGS1984": return "above the WGS 84 ellipsoid";
                case "EGM2008": return "above the EGM2008 geoid";
                default: return "";
            }
        }

        /// <summary>A short text that changes whenever the location or Rhino's north changes (the panel refreshes on it).</summary>
        public string Signature()
        {
            var ci = CultureInfo.InvariantCulture;
            string R(double v) => Math.Round(v, 9).ToString("R", ci);
            return string.Join("|", IsSet ? "1" : "0", R(Latitude), R(Longitude), R(ElevationMeters), ElevationReference, Name, Description,
                R(ModelPoint.X), R(ModelPoint.Y), R(ModelPoint.Z), R(North.X), R(North.Y));
        }

        // ---- words ------------------------------------------------------------------------------

        /// <summary>39.739200° N</summary>
        public static string FormatLatitude(double degrees) => Degrees(degrees) + (degrees < 0 ? " S" : " N");

        /// <summary>104.990300° W</summary>
        public static string FormatLongitude(double degrees) => Degrees(degrees) + (degrees < 0 ? " W" : " E");

        static string Degrees(double d) => UnitFormat.Decimal(Math.Abs(d), 6, false, false, false) + "°";

        /// <summary>The anchor point's elevation in the model's length format, with what it is measured from.</summary>
        public string Elevation(RhinoDoc doc) =>
            Kit.Length(doc, DocUnits.FromMeters(doc, ElevationMeters)) + (ElevationReference.Length > 0 ? " " + ElevationReference : "");

        /// <summary>Rhino's north the way "Angle to True North" reads: from project north, East or West.</summary>
        public string NorthText(SiteModel site)
        {
            string angle = UnitFormat.AngleEastWest(SiteModel.ClockwiseAngle(site.ProjectNorth, North));
            return SameNorth(North, site.TrueNorth)
                ? angle + " (same as true north)"
                : angle + " (true north is " + UnitFormat.AngleEastWest(site.AngleToTrueNorthDegrees) + ")";
        }

        /// <summary>One line for the command line and the Location report.</summary>
        public string Summary(RhinoDoc doc, SiteModel site)
        {
            string where = IsSet
                ? (Name.Length > 0 ? Name + ", " : "") + FormatLatitude(Latitude) + ", " + FormatLongitude(Longitude) + ", elevation " + Elevation(doc)
                : "not set in this Rhino model";
            return "geographic location " + where + "; Rhino's north " + NorthText(site);
        }

        // ---- north ------------------------------------------------------------------------------

        /// <summary>Rhino's north in the model: a plan direction (world Y in a model that never set one).</summary>
        public static Vector3d RhinoNorth(RhinoDoc doc) => Read(doc).North;

        /// <summary>The plan part of a direction, unitized; null when it has none.</summary>
        public static Vector3d? Plan(Vector3d v)
        {
            var p = new Vector3d(v.X, v.Y, 0);
            return p.Unitize() ? p : (Vector3d?)null;
        }

        /// <summary>Two plan directions are the same north (within <see cref="NorthTolerance"/>).</summary>
        public static bool SameNorth(Vector3d a, Vector3d b) => Math.Abs(SiteModel.ClockwiseAngle(a, b)) <= NorthTolerance;

        /// <summary>The Sun's north angle for a plan direction: degrees from world X, counter-clockwise (world Y is 90).</summary>
        public static double SunNorth(Vector3d north)
        {
            double a = Math.Atan2(north.Y, north.X) * 180.0 / Math.PI;
            return ((a % 360) + 360) % 360;
        }

        /// <summary>
        /// Point Rhino's north - the Earth anchor point's and the Sun's - along a plan direction; the location does not
        /// move. The Sun is written too, or its next change would write its old north back to the anchor point; it gets
        /// the anchor point's latitude and longitude on the way, so its write never moves the location (and a location
        /// that is not set stays not set). True when Rhino's north was written.
        /// </summary>
        public static bool SetRhinoNorth(RhinoDoc doc, Vector3d north)
        {
            var n = Plan(north);
            if (doc == null || !n.HasValue)
                return false;
            var east = Vector3d.CrossProduct(n.Value, Vector3d.ZAxis);
            east.Unitize();
            try
            {
                bool set;
                double latitude, longitude;
                using (var e = doc.EarthAnchorPoint)
                {
                    set = e.EarthLocationIsSet();
                    latitude = e.EarthBasepointLatitude;
                    longitude = e.EarthBasepointLongitude;
                }
                var sun = doc.RenderSettings?.Sun;
                if (sun != null)
                {
                    sun.BeginChange(RenderContent.ChangeContexts.Program);
                    try
                    {
                        if (set)
                        {
                            sun.Latitude = latitude;
                            sun.Longitude = longitude;
                        }
                        sun.North = SunNorth(n.Value);
                    }
                    finally
                    {
                        sun.EndChange();
                    }
                }
                // then the anchor point itself, whatever the Sun wrote to it
                using (var a = doc.EarthAnchorPoint)
                {
                    if (set)
                    {
                        a.EarthBasepointLatitude = latitude;
                        a.EarthBasepointLongitude = longitude;
                    }
                    a.ModelNorth = n.Value;
                    a.ModelEast = east;
                    doc.EarthAnchorPoint = a;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Rhino's north was not changed (" + ex.GetType().Name + ": " + ex.Message + ").");
                return false;
            }
        }

        /// <summary>
        /// Skunk's true north turned from <paramref name="before"/> to <paramref name="after"/>: Rhino's north (its Sun and
        /// Earth anchor point) follows when it was the same as true north before. A north set apart in Rhino stays apart
        /// (the Skunk Attributes panel and the Location dialog show both). True when Rhino's north moved.
        /// </summary>
        public static bool FollowTrueNorth(RhinoDoc doc, Vector3d before, Vector3d after)
        {
            if (doc == null || SameNorth(before, after))
                return false;
            var rhino = RhinoNorth(doc);
            if (SameNorth(rhino, after) || !SameNorth(rhino, before))
                return false;
            if (!SetRhinoNorth(doc, after))
                return false;
            Log.Info("Rhino's north (its Sun and Earth anchor point) follows true north.");
            return true;
        }

        /// <summary>Turn Skunk's true north onto Rhino's north about a pivot (the pivot keeps its shared coordinates). The caller saves.</summary>
        public static void UseRhinoNorth(RhinoDoc doc, SiteModel site, RotationPivot pivot) =>
            site.SetAngleToTrueNorth(SiteModel.ClockwiseAngle(site.ProjectNorth, RhinoNorth(doc)), pivot);

        // ---- points -----------------------------------------------------------------------------

        /// <summary>
        /// Latitude and longitude of a model point, from Rhino's model-to-earth transform (a flat earth around the anchor
        /// point: Rhino gives the error as centimeters within a kilometer). False when the location is not set.
        /// </summary>
        public static bool TryLatLong(RhinoDoc doc, Point3d model, out double latitude, out double longitude)
        {
            latitude = longitude = 0;
            if (doc == null)
                return false;
            try
            {
                using (var e = doc.EarthAnchorPoint)
                {
                    if (e == null || !e.EarthLocationIsSet())
                        return false;
                    var t = e.GetModelToEarthTransform(LengthUnit.FromKnownUnitSystem(doc.ModelUnitSystem));
                    if (!t.IsValid)
                        return false;
                    var b = e.ModelBasePoint;
                    b.Transform(t);
                    var p = model;
                    p.Transform(t);
                    // Rhino documents x = latitude and y = longitude, and Rhino 9.2 returns x = longitude: read the
                    // order off the anchor point itself
                    bool xIsLongitude = Math.Abs(b.X - e.EarthBasepointLongitude) <= Math.Abs(b.X - e.EarthBasepointLatitude);
                    latitude = xIsLongitude ? p.Y : p.X;
                    longitude = xIsLongitude ? p.X : p.Y;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("latitude / longitude: " + ex.Message);
                return false;
            }
        }

        /// <summary>"39.739200° N, 104.990300° W" for a model point, or "" when the location is not set.</summary>
        public static string LatLongText(RhinoDoc doc, Point3d model) =>
            TryLatLong(doc, model, out double lat, out double lon) ? FormatLatitude(lat) + ", " + FormatLongitude(lon) : "";
    }
}
