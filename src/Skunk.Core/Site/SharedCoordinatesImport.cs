using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Rhino;
using Rhino.Geometry;
using Skunk.Commands;
using Skunk.Core;
using Skunk.Units;

namespace Skunk.Site
{
    /// <summary>Where the Rhino model sits relative to the BIM model the shared coordinates come from.</summary>
    public enum ImportBasis
    {
        /// <summary>Rhino's world origin is the BIM model's internal origin (exported in internal coordinates).</summary>
        InternalOrigin = 0,
        /// <summary>Rhino's world origin is the BIM model's project base point, axes along project north.</summary>
        ProjectBasePoint,
        /// <summary>Rhino's world is the BIM model's shared coordinate system (exported in shared coordinates).</summary>
        SharedCoordinates,
    }

    /// <summary>
    /// Shared coordinates read from a BIM model (SkImportSharedCoordinates). <see cref="Snippet"/> runs in the
    /// model's Python console and prints one <see cref="Marker"/> line: both base points (internal position and
    /// their own shared values) and the model's internal / shared transform. Pasted back, it sets Skunk's project
    /// base point, survey point and true north so that Skunk's shared coordinates match the model's. Lengths in
    /// the line are the BIM model's internal units, decimal feet.
    /// </summary>
    public sealed class ImportedSite
    {
        public const string Marker = "SKUNK-SITE";

        public string Model = "", Location = "", Version = "";
        public Point3d PbpInternal, SpInternal;
        /// <summary>Shared coordinates (east, north, elevation) of the two points, as the BIM model reports them.</summary>
        public Point3d PbpShared, SpShared;
        public bool? Clipped;
        /// <summary>Internal to shared coordinates (feet).</summary>
        public Transform InternalToShared = Transform.Identity;
        /// <summary>Largest disagreement (feet) between the transform and the points' own shared values; NaN when they gave none.</summary>
        public double Mismatch = double.NaN;

        /// <summary>The Python snippet to run in the BIM model's console (IronPython 2.7 and CPython 3).</summary>
        public static string Snippet => @"# Skunk - Import Shared Coordinates
# Run this in the model's Python console (pyRevit or RevitPythonShell),
# then copy the SKUNK-SITE line it prints back into Skunk.
import json
from Autodesk.Revit.DB import BasePoint, BuiltInParameter, FilteredElementCollector
doc = __revit__.ActiveUIDocument.Document

def xyz(p):
    return [p.X, p.Y, p.Z]

def base_point(survey):
    for b in FilteredElementCollector(doc).OfClass(BasePoint):
        if b.IsShared == survey:
            return b

def position(b):
    try:
        return xyz(b.Position)
    except Exception:
        return xyz(b.get_BoundingBox(None).Min)

def value(b, bip):
    q = b.get_Parameter(bip)
    return q.AsDouble() if q is not None else None

def shared(b):
    return [value(b, BuiltInParameter.BASEPOINT_EASTWEST_PARAM),
            value(b, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM),
            value(b, BuiltInParameter.BASEPOINT_ELEVATION_PARAM)]

pbp = base_point(False)
sp = base_point(True)
t = doc.ActiveProjectLocation.GetTotalTransform()
site = {'v': 1, 'units': 'ft', 'model': doc.Title,
        'location': doc.ActiveProjectLocation.Name,
        'version': str(__revit__.Application.VersionNumber),
        'pbp': {'internal': position(pbp), 'shared': shared(pbp)},
        'sp': {'internal': position(sp), 'shared': shared(sp)},
        'total': {'origin': xyz(t.Origin), 'x': xyz(t.BasisX), 'y': xyz(t.BasisY), 'z': xyz(t.BasisZ)}}

try:
    site['sp']['clipped'] = bool(sp.Clipped)
except Exception:
    pass

print('SKUNK-SITE ' + json.dumps(site))
";

        // ---- reading the pasted line --------------------------------------------------------------

        /// <summary>Read the <see cref="Marker"/> line out of pasted text (the whole console output is fine).</summary>
        public static bool TryParse(string pasted, out ImportedSite site, out string error)
        {
            site = null;
            error = "";
            string json = null;
            foreach (var raw in (pasted ?? "").Split('\n'))
            {
                int i = raw.IndexOf(Marker, StringComparison.Ordinal);
                if (i >= 0)
                {
                    json = raw.Substring(i + Marker.Length).Trim();
                    break;
                }
            }
            if (json == null)
            {
                error = "No " + Marker + " line found. Run the snippet in the model's Python console and paste the line it prints.";
                return false;
            }
            try
            {
                using (var d = JsonDocument.Parse(json))
                {
                    var r = d.RootElement;
                    var s = new ImportedSite
                    {
                        Model = Text(r, "model"),
                        Location = Text(r, "location"),
                        Version = Text(r, "version"),
                    };
                    var units = Text(r, "units");
                    if (units.Length > 0 && units != "ft")
                    {
                        error = "The line is in '" + units + "'; Skunk reads decimal feet (\"ft\").";
                        return false;
                    }
                    var pbp = r.GetProperty("pbp");
                    var sp = r.GetProperty("sp");
                    var total = r.GetProperty("total");
                    s.PbpInternal = Point(pbp.GetProperty("internal"));
                    s.SpInternal = Point(sp.GetProperty("internal"));
                    var pbpShared = OptionalPoint(pbp, "shared");
                    var spShared = OptionalPoint(sp, "shared");
                    if (sp.TryGetProperty("clipped", out var c) && (c.ValueKind == JsonValueKind.True || c.ValueKind == JsonValueKind.False))
                        s.Clipped = c.GetBoolean();

                    var reported = FromBasis(Point(total.GetProperty("origin")), (Vector3d)Point(total.GetProperty("x")),
                        (Vector3d)Point(total.GetProperty("y")), (Vector3d)Point(total.GetProperty("z")));
                    if (!reported.TryGetInverse(out var inverse))
                    {
                        error = "The model's coordinate transform in the line cannot be inverted.";
                        return false;
                    }
                    // which way the transform runs is read from the points' own shared values: the direction that
                    // reproduces them wins (shared -> internal is the documented one, so it wins a tie)
                    double asInverse = Disagreement(inverse, s, pbpShared, spShared);
                    double asIs = Disagreement(reported, s, pbpShared, spShared);
                    bool useInverse = double.IsNaN(asInverse) || asInverse <= asIs;
                    s.InternalToShared = useInverse ? inverse : reported;
                    s.Mismatch = useInverse ? asInverse : asIs;
                    s.PbpShared = pbpShared ?? Map(s.InternalToShared, s.PbpInternal);
                    s.SpShared = spShared ?? Map(s.InternalToShared, s.SpInternal);
                    site = s;
                    return true;
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is KeyNotFoundException || ex is InvalidOperationException || ex is FormatException)
            {
                error = "Could not read the " + Marker + " line: " + ex.Message;
                return false;
            }
        }

        static string Text(JsonElement r, string name) =>
            r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        static Point3d Point(JsonElement a)
        {
            if (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() < 3)
                throw new FormatException("expected three numbers");
            return new Point3d(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble());
        }

        static Point3d? OptionalPoint(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var a) || a.ValueKind != JsonValueKind.Array || a.GetArrayLength() < 3)
                return null;
            for (int i = 0; i < 3; i++)
                if (a[i].ValueKind != JsonValueKind.Number)
                    return null;
            return Point(a);
        }

        static Transform FromBasis(Point3d o, Vector3d x, Vector3d y, Vector3d z)
        {
            var t = Transform.Identity;
            t.M00 = x.X; t.M10 = x.Y; t.M20 = x.Z;
            t.M01 = y.X; t.M11 = y.Y; t.M21 = y.Z;
            t.M02 = z.X; t.M12 = z.Y; t.M22 = z.Z;
            t.M03 = o.X; t.M13 = o.Y; t.M23 = o.Z;
            return t;
        }

        static Point3d Map(Transform t, Point3d p)
        {
            p.Transform(t);
            return p;
        }

        static double Disagreement(Transform internalToShared, ImportedSite s, Point3d? pbpShared, Point3d? spShared)
        {
            double worst = double.NaN;
            if (pbpShared.HasValue)
                worst = Map(internalToShared, s.PbpInternal).DistanceTo(pbpShared.Value);
            if (spShared.HasValue)
            {
                double d = Map(internalToShared, s.SpInternal).DistanceTo(spShared.Value);
                worst = double.IsNaN(worst) ? d : Math.Max(worst, d);
            }
            return worst;
        }

        // ---- applying it to the Rhino model -----------------------------------------------------

        /// <summary>Model units per foot for this document.</summary>
        public static double FeetToModel(RhinoDoc doc) => UnitFormat.MetersPerFoot / DocUnits.MetersPerUnit(doc);

        /// <summary>BIM internal coordinates (feet) to Rhino world (model units), for where the Rhino model sits.</summary>
        public Transform ToWorld(RhinoDoc doc, ImportBasis basis)
        {
            var scale = Transform.Scale(Point3d.Origin, FeetToModel(doc));
            switch (basis)
            {
                case ImportBasis.ProjectBasePoint:
                    return scale * Transform.Translation(Point3d.Origin - PbpInternal);
                case ImportBasis.SharedCoordinates:
                    return scale * InternalToShared;
                default:
                    return scale;
            }
        }

        /// <summary>The project base point and survey point planes in Rhino, and the survey point's shared values (model units).</summary>
        public void Planes(RhinoDoc doc, ImportBasis basis, out Plane project, out Plane survey, out Point3d surveyShared)
        {
            var w = ToWorld(doc, basis);
            // project base point: where it is, project north along the internal Y axis
            var pn = Vector3d.YAxis;
            pn.Transform(w);
            project = SiteModel.Horizontal(new Plane(Map(w, PbpInternal), Vector3d.CrossProduct(pn, Vector3d.ZAxis), pn));
            // survey point: where it is, true north along the shared north axis
            InternalToShared.TryGetInverse(out var sharedToInternal);
            var tn = Vector3d.YAxis;
            tn.Transform(sharedToInternal);
            tn.Transform(w);
            survey = SiteModel.Horizontal(new Plane(Map(w, SpInternal), Vector3d.CrossProduct(tn, Vector3d.ZAxis), tn));
            double k = FeetToModel(doc);
            surveyShared = new Point3d(SpShared.X * k, SpShared.Y * k, SpShared.Z * k);
        }

        /// <summary>Set Skunk's project base point, survey point (with its shared values) and true north to the model's.</summary>
        public void Apply(RhinoDoc doc, ImportBasis basis)
        {
            Planes(doc, basis, out var project, out var survey, out var shared);
            var site = SiteModel.Get(doc);
            site.Project = project;
            site.Survey = survey;
            site.Data.SpEast = shared.X;
            site.Data.SpNorth = shared.Y;
            site.Data.SpElevation = shared.Z;
            if (Clipped.HasValue)
                site.Data.SpClipped = Clipped.Value;
            site.Save(doc);
        }

        /// <summary>What an import would set, for the dialog and the command line.</summary>
        public string Describe(RhinoDoc doc, ImportBasis basis)
        {
            Planes(doc, basis, out var project, out var survey, out var _);
            double k = FeetToModel(doc);
            string L(double feet) => Kit.Length(doc, feet * k);
            string Shared(Point3d p) => "N/S " + L(p.Y) + "   E/W " + L(p.X) + "   Elev " + L(p.Z);
            var sb = new StringBuilder();
            sb.AppendLine("Model: " + (Model.Length > 0 ? Model : "(unnamed)") + (Version.Length > 0 ? "  (" + Version + ")" : "") +
                          (Location.Length > 0 ? ", site \"" + Location + "\"" : ""));
            sb.AppendLine("Project base point:  " + Shared(PbpShared));
            sb.AppendLine("Survey point:  " + Shared(SpShared) + (Clipped.HasValue ? (Clipped.Value ? "   (clipped)" : "   (unclipped)") : ""));
            sb.AppendLine("Angle to true north:  " + UnitFormat.AngleEastWest(SiteModel.ClockwiseAngle(project.YAxis, survey.YAxis)));
            sb.AppendLine("In Rhino the project base point lands at  " + Kit.Length(doc, project.Origin.X) + ",  " + Kit.Length(doc, project.Origin.Y) +
                          ",  " + Kit.Length(doc, project.Origin.Z));
            if (double.IsNaN(Mismatch))
                sb.Append("The points' own shared values were not in the line; they are computed from the transform.");
            else if (Mismatch <= 0.001)
                sb.Append("Check: the transform reproduces both points' shared values.");
            else
                sb.Append("Warning: the transform and the points' own shared values disagree by " + L(Mismatch) + ". Check the pasted line.");
            return sb.ToString();
        }
    }
}
