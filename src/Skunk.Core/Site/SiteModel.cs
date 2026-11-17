using System;
using System.Globalization;
using Rhino;
using Rhino.Geometry;
using Skunk.Core;

namespace Skunk.Site
{
    /// <summary>Persisted site / location data (document string "Skunk/Site").</summary>
    public sealed class SiteData
    {
        public int Version = 1;
        /// <summary>Named CPlane holding the project base point (origin) and project north (Y axis).</summary>
        public string PbpCPlane = Keys.DefaultPbpCPlane;
        /// <summary>Named CPlane holding the survey point (origin) and true north (Y axis).</summary>
        public string SpCPlane = Keys.DefaultSpCPlane;
        /// <summary>Last known planes (origin, x axis, y axis) - used when a named CPlane was deleted.</summary>
        public double[] PbpPlane;
        public double[] SpPlane;
        /// <summary>Shared coordinates of the survey point (N/S, E/W, Elev).</summary>
        public double SpNorth;
        public double SpEast;
        public double SpElevation;
        /// <summary>"Clipped" survey point: moving it moves the shared coordinate system with it.</summary>
        public bool SpClipped = true;
        public Guid PbpMarker;
        public Guid SpMarker;
    }

    /// <summary>Which point Rotate True North turns the shared coordinate system about.</summary>
    public enum RotationPivot
    {
        ProjectBasePoint,
        SurveyPoint,
    }

    /// <summary>
    /// The location model:
    ///   Internal origin  = Rhino world origin (fixed, never moves)
    ///   Project base point = origin of a named CPlane; its Y axis is project north
    ///   Survey point       = origin of a second named CPlane; its Y axis is TRUE NORTH,
    ///                        so true north lives on its own CPlane, separate from the world and project.
    /// Shared coordinates of a point P: N = SpNorth + (P-SP)·TN, E = SpEast + (P-SP)·TE, Elev = SpElev + (P-SP)·Z.
    /// </summary>
    public sealed class SiteModel
    {
        public SiteData Data;
        public Plane Project;
        public Plane Survey;
        public bool ProjectFromCPlane;
        public bool SurveyFromCPlane;
        /// <summary>True north as it was read (before any edit to this model of the site).</summary>
        public Vector3d ReadNorth = Vector3d.YAxis;

        public static SiteModel Get(RhinoDoc doc)
        {
            var stored = DocStore.Get<SiteData>(doc, Keys.SiteEntry);
            var data = stored ?? new SiteData();
            if (string.IsNullOrWhiteSpace(data.PbpCPlane))
                data.PbpCPlane = Keys.DefaultPbpCPlane;
            if (string.IsNullOrWhiteSpace(data.SpCPlane))
                data.SpCPlane = Keys.DefaultSpCPlane;
            var m = new SiteModel { Data = data };
            m.ProjectFromCPlane = NamedPlanes.TryGet(doc, data.PbpCPlane, out m.Project);
            if (!m.ProjectFromCPlane)
                m.Project = PlaneFrom(data.PbpPlane) ?? Plane.WorldXY;
            m.SurveyFromCPlane = NamedPlanes.TryGet(doc, data.SpCPlane, out m.Survey);
            if (!m.SurveyFromCPlane)
                m.Survey = PlaneFrom(data.SpPlane) ?? (stored == null ? StartSurvey(doc) : Plane.WorldXY);
            m.Project = Horizontal(m.Project);
            m.Survey = Horizontal(m.Survey);
            m.ReadNorth = m.Survey.YAxis;
            return m;
        }

        /// <summary>
        /// Where a model without a Skunk site starts: the survey point at the internal origin and true north along Rhino's
        /// north - the model's geographic location, which Rhino's Sun shares (world Y in a model that never set one).
        /// </summary>
        public static Plane StartSurvey(RhinoDoc doc)
        {
            var north = GeoLocation.RhinoNorth(doc);
            return new Plane(Point3d.Origin, Vector3d.CrossProduct(north, Vector3d.ZAxis), north);
        }

        /// <summary>
        /// Write the planes to their named CPlanes and the data to the document. Rhino's north (its Sun and Earth anchor
        /// point) follows a new true north when the two agreed (<see cref="GeoLocation.FollowTrueNorth"/>).
        /// </summary>
        public void Save(RhinoDoc doc)
        {
            // the true north Rhino's north was last kept with: as last saved (a true north edited on its named CPlane
            // is newer than that), else as it was read
            var before = PlaneFrom(Data.SpPlane)?.YAxis ?? ReadNorth;
            Project = Horizontal(Project);
            Survey = Horizontal(Survey);
            Data.PbpPlane = PlaneArray(Project);
            Data.SpPlane = PlaneArray(Survey);
            NamedPlanes.Set(doc, Data.PbpCPlane, Project);
            NamedPlanes.Set(doc, Data.SpCPlane, Survey);
            ProjectFromCPlane = SurveyFromCPlane = true;
            DocStore.Set(doc, Keys.SiteEntry, Data);
            GeoLocation.FollowTrueNorth(doc, before, TrueNorth);
            ReadNorth = TrueNorth;
        }

        /// <summary>A short text that changes whenever any coordinate system changes.</summary>
        public string Signature() =>
            string.Join("|", PlaneKey(Project), PlaneKey(Survey),
                Data.SpNorth.ToString("R", CultureInfo.InvariantCulture),
                Data.SpEast.ToString("R", CultureInfo.InvariantCulture),
                Data.SpElevation.ToString("R", CultureInfo.InvariantCulture));

        // ---- directions -------------------------------------------------------------------------

        public Vector3d ProjectNorth => Project.YAxis;
        public Vector3d ProjectEast => Project.XAxis;
        public Vector3d TrueNorth => Survey.YAxis;
        public Vector3d TrueEast => Survey.XAxis;

        /// <summary>
        /// Angle from project north to true north in plan, in degrees, positive clockwise (true north
        /// toward the east of project north), shown as "Angle to True North ... East".
        /// </summary>
        public double AngleToTrueNorthDegrees => ClockwiseAngle(ProjectNorth, TrueNorth);

        /// <summary>Clockwise plan angle (degrees, -180..180) that turns <paramref name="from"/> onto <paramref name="to"/>.</summary>
        public static double ClockwiseAngle(Vector3d from, Vector3d to)
        {
            double a1 = Math.Atan2(from.Y, from.X);
            double a2 = Math.Atan2(to.Y, to.X);
            double ccw = (a2 - a1) * 180.0 / Math.PI;
            double cw = -ccw;
            while (cw > 180)
                cw -= 360;
            while (cw <= -180)
                cw += 360;
            return cw;
        }

        // ---- coordinates ------------------------------------------------------------------------

        /// <summary>Shared (survey) coordinates of a world point.</summary>
        public Point3d Shared(Point3d world)
        {
            var v = world - Survey.Origin;
            return new Point3d(Data.SpEast + v * Survey.XAxis, Data.SpNorth + v * Survey.YAxis, Data.SpElevation + v * Survey.ZAxis);
        }

        /// <summary>World point of shared coordinates (east = X, north = Y, elevation = Z).</summary>
        public Point3d WorldFromShared(double east, double north, double elevation) =>
            Survey.Origin + (east - Data.SpEast) * Survey.XAxis + (north - Data.SpNorth) * Survey.YAxis + (elevation - Data.SpElevation) * Survey.ZAxis;

        /// <summary>Project coordinates (relative to the project base point, along project north).</summary>
        public Point3d ProjectCoordinates(Point3d world)
        {
            var v = world - Project.Origin;
            return new Point3d(v * Project.XAxis, v * Project.YAxis, v * Project.ZAxis);
        }

        /// <summary>Shared coordinates of the project base point (the values shown on the PBP).</summary>
        public Point3d ProjectBasePointShared => Shared(Project.Origin);

        /// <summary>Origin of the shared coordinate system in world coordinates.</summary>
        public Point3d SharedOrigin => WorldFromShared(0, 0, 0);

        // ---- edits (callers save + update dependents) -------------------------------------------

        /// <summary>Move the project base point; the model and the shared system stay.</summary>
        public void MoveProjectBasePoint(Point3d newOrigin) => Project.Origin = newOrigin;

        /// <summary>
        /// Move the survey point. Clipped: the shared coordinate system moves with it (its N/E/Elev stay).
        /// Unclipped: the shared system stays and the survey point's own N/E/Elev change.
        /// </summary>
        public void MoveSurveyPoint(Point3d newOrigin, bool clipped)
        {
            if (!clipped)
            {
                var shared = Shared(newOrigin);
                Data.SpEast = shared.X;
                Data.SpNorth = shared.Y;
                Data.SpElevation = shared.Z;
            }
            Survey.Origin = newOrigin;
        }

        /// <summary>Place the project base point at the given shared coordinates.</summary>
        public void SetProjectBasePointShared(double east, double north, double elevation) =>
            Project.Origin = WorldFromShared(east, north, elevation);

        /// <summary>Rotate true north by an angle (degrees, clockwise positive) about a pivot; the model stays.</summary>
        public void RotateTrueNorth(double clockwiseDegrees, RotationPivot pivot)
        {
            var center = pivot == RotationPivot.ProjectBasePoint ? Project.Origin : Survey.Origin;
            // keep the shared coordinates of the pivot fixed while the axes turn
            var pivotShared = Shared(center);
            var xf = Transform.Rotation(-clockwiseDegrees * Math.PI / 180.0, Vector3d.ZAxis, center);
            var survey = Survey;
            survey.Transform(xf);
            Survey = survey;
            // shift the survey plane so that the pivot keeps its shared coordinates
            var now = Shared(center);
            var fix = (pivotShared.X - now.X) * Survey.XAxis + (pivotShared.Y - now.Y) * Survey.YAxis + (pivotShared.Z - now.Z) * Survey.ZAxis;
            Survey.Origin = Survey.Origin - fix;
        }

        /// <summary>Set the absolute angle from project north to true north (degrees clockwise).</summary>
        public void SetAngleToTrueNorth(double clockwiseDegrees, RotationPivot pivot) =>
            RotateTrueNorth(clockwiseDegrees - AngleToTrueNorthDegrees, pivot);

        /// <summary>
        /// Specify Coordinates at Point: the picked world point gets the given shared coordinates
        /// and true north is set to the given angle (clockwise from project north).
        /// </summary>
        public void SpecifyCoordinatesAtPoint(Point3d world, double east, double north, double elevation, double angleClockwiseDegrees)
        {
            double rad = -angleClockwiseDegrees * Math.PI / 180.0;
            var xf = Transform.Rotation(rad, Vector3d.ZAxis, Point3d.Origin);
            var tn = ProjectNorth;
            tn.Transform(xf);
            var te = Vector3d.CrossProduct(tn, Vector3d.ZAxis);
            te.Unitize();
            var plane = new Plane(Survey.Origin, te, tn);
            Survey = plane;
            // place the survey plane so that 'world' maps to (east, north, elevation)
            var origin = world - (east - Data.SpEast) * Survey.XAxis - (north - Data.SpNorth) * Survey.YAxis - (elevation - Data.SpElevation) * Survey.ZAxis;
            Survey.Origin = origin;
        }

        // ---- helpers ----------------------------------------------------------------------------

        /// <summary>Keep the Z axis vertical (north angles are plan rotations).</summary>
        public static Plane Horizontal(Plane p)
        {
            var y = new Vector3d(p.YAxis.X, p.YAxis.Y, 0);
            if (!y.Unitize())
            {
                y = new Vector3d(-p.XAxis.Y, p.XAxis.X, 0);
                if (!y.Unitize())
                    y = Vector3d.YAxis;
            }
            var x = Vector3d.CrossProduct(y, Vector3d.ZAxis);
            x.Unitize();
            return new Plane(p.Origin, x, y);
        }

        public static double[] PlaneArray(Plane p) => new[]
        {
            p.Origin.X, p.Origin.Y, p.Origin.Z, p.XAxis.X, p.XAxis.Y, p.XAxis.Z, p.YAxis.X, p.YAxis.Y, p.YAxis.Z,
        };

        public static Plane? PlaneFrom(double[] a)
        {
            if (a == null || a.Length < 9)
                return null;
            var p = new Plane(new Point3d(a[0], a[1], a[2]), new Vector3d(a[3], a[4], a[5]), new Vector3d(a[6], a[7], a[8]));
            return p.IsValid ? p : (Plane?)null;
        }

        public static string PlaneKey(Plane p)
        {
            var ci = CultureInfo.InvariantCulture;
            return string.Join(",", Array.ConvertAll(PlaneArray(p), v => Math.Round(v, 9).ToString("R", ci)));
        }
    }

    /// <summary>Named construction plane helpers.</summary>
    public static class NamedPlanes
    {
        public static bool TryGet(RhinoDoc doc, string name, out Plane plane)
        {
            plane = Plane.WorldXY;
            if (doc == null || string.IsNullOrWhiteSpace(name))
                return false;
            int i = doc.NamedConstructionPlanes.Find(name);
            if (i < 0)
                return false;
            plane = doc.NamedConstructionPlanes[i].Plane;
            return plane.IsValid;
        }

        public static void Set(RhinoDoc doc, string name, Plane plane)
        {
            if (doc == null || string.IsNullOrWhiteSpace(name))
                return;
            if (TryGet(doc, name, out var existing) && SiteModel.PlaneKey(existing) == SiteModel.PlaneKey(plane))
                return;
            doc.NamedConstructionPlanes.Add(name, plane);
        }

        public static void Delete(RhinoDoc doc, string name)
        {
            if (doc == null || string.IsNullOrWhiteSpace(name))
                return;
            if (doc.NamedConstructionPlanes.Find(name) >= 0)
                doc.NamedConstructionPlanes.Delete(name);
        }

        public static string[] Names(RhinoDoc doc)
        {
            var list = new System.Collections.Generic.List<string>();
            if (doc == null)
                return list.ToArray();
            foreach (var cp in doc.NamedConstructionPlanes)
                if (cp != null && !string.IsNullOrEmpty(cp.Name))
                    list.Add(cp.Name);
            return list.ToArray();
        }
    }
}
