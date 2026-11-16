using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Skunk.Core;

namespace Skunk.Elements
{
    /// <summary>What a spot is attached to.</summary>
    public enum HostKind
    {
        /// <summary>No host: the spot measures its own point.</summary>
        Free = 0,
        Point,
        Curve,
        BrepFace,
        Mesh,
    }

    /// <summary>
    /// A host reference that survives transforms and most edits: surface parameters are stored
    /// normalised to the face domain, mesh points as face + barycentric weights, curve points as a
    /// normalised parameter. Moving, rotating or scaling the host keeps the anchor on the same spot.
    /// </summary>
    public sealed class AnchorData
    {
        public Guid Host;
        public HostKind Kind;
        public int Index = -1;
        public double U;
        public double V;
        public double[] T;
        /// <summary>Last evaluated world point (fallback when the host is missing).</summary>
        public double[] Last;

        [System.Text.Json.Serialization.JsonIgnore]
        public Point3d LastPoint => Last != null && Last.Length >= 3 ? new Point3d(Last[0], Last[1], Last[2]) : Point3d.Unset;

        public void SetLast(Point3d p) => Last = new[] { p.X, p.Y, p.Z };
    }

    /// <summary>Common element data.</summary>
    public abstract class ElementData
    {
        public int V = 1;
        /// <summary>Object id at the last write; a different id means the object is a copy.</summary>
        public Guid Id;
        public string Type = "";
        public string Comments = "";
    }

    /// <summary>Spot elevation / spot coordinate / spot slope instance data.</summary>
    public sealed class SpotData : ElementData
    {
        public AnchorData Anchor = new AnchorData();
        /// <summary>Annotation plane axes (origin = the spot point).</summary>
        public double[] PlaneX = { 1, 0, 0 };
        public double[] PlaneY = { 0, 1, 0 };
        /// <summary>Leader end relative to the spot point, in annotation plane coordinates.</summary>
        public double[] Offset = { 1, 1 };
        public bool Leader = true;
        public bool Shoulder = true;
        /// <summary>Level a relative spot elevation is measured from ("Relative Base").</summary>
        public Guid RelativeLevel;
        /// <summary>"Single / Upper Value Prefix / Suffix" and "Lower Value Prefix / Suffix".</summary>
        public string Prefix = "";
        public string Suffix = "";
        public string LowerPrefix = "";
        public string LowerSuffix = "";
        /// <summary>Spot slope: the arrow direction is reversed relative to the type default.</summary>
        public bool FlipSlope;
        /// <summary>
        /// Spot slope: how far the arrow reaches uphill (top) and downhill (bottom) from its location, in
        /// model units. Set by dragging the arrow's end grips; null = half the type's arrow length.
        /// </summary>
        public double? SlopeTop;
        public double? SlopeBottom;
        /// <summary>"Display Elevations" (instance); null = the type's default.</summary>
        public Skunk.Types.ElevationDisplay? Display;
        /// <summary>Generated geometry fingerprint (to tell user edits from Skunk's own).</summary>
        public double[] Points;
        public string Text = "";
        public double Value = double.NaN;

        public Plane AnnotationPlane(Point3d origin)
        {
            var x = new Vector3d(PlaneX[0], PlaneX[1], PlaneX[2]);
            var y = new Vector3d(PlaneY[0], PlaneY[1], PlaneY[2]);
            var p = new Plane(origin, x, y);
            return p.IsValid ? p : new Plane(origin, Vector3d.XAxis, Vector3d.YAxis);
        }

        public void SetPlane(Plane p)
        {
            PlaneX = new[] { p.XAxis.X, p.XAxis.Y, p.XAxis.Z };
            PlaneY = new[] { p.YAxis.X, p.YAxis.Y, p.YAxis.Z };
        }
    }

    /// <summary>Level instance data (the level object itself is a closed planar polyline).</summary>
    public sealed class LevelData : ElementData
    {
        public string Name = "";
        /// <summary>Head flags per footprint edge ("Show head at End 1 / End 2").</summary>
        public List<bool> End1 = new List<bool>();
        public List<bool> End2 = new List<bool>();
        /// <summary>Head objects: key "edge:end" (end 1 or 2).</summary>
        public Dictionary<string, Guid> Heads = new Dictionary<string, Guid>();
        public bool BuildingStory = true;
        /// <summary>"Structural" (identity data).</summary>
        public bool Structural;
        /// <summary>Line pattern override for this level: null / empty = by type.</summary>
        public string LinePattern;
        public double ComputationHeight;
        /// <summary>Named CPlane created for this level (to rename / delete it).</summary>
        public string CPlane = "";
        public double Z = double.NaN;
        public double[] Points;
    }

    /// <summary>Level head data (child of a level).</summary>
    public sealed class HeadData : ElementData
    {
        public Guid Level;
        public int Edge;
        public int End;
    }

    /// <summary>North arrow data.</summary>
    public sealed class NorthArrowData : ElementData
    {
        /// <summary>true = true north (survey CPlane), false = project north.</summary>
        public bool TrueNorth = true;
        public double Size;
    }

    /// <summary>Plan cut (clipping plane for a level plan view).</summary>
    public sealed class PlanCutData : ElementData
    {
        public Guid Level;
        public Guid Viewport;
    }

    /// <summary>Reading / writing Skunk data on Rhino objects.</summary>
    public static class ElementIO
    {
        public static ElementKind KindOf(RhinoObject obj) =>
            obj == null ? ElementKind.None : ElementKinds.Parse(obj.Attributes.GetUserString(Keys.Kind));

        public static ElementKind KindOf(ObjectAttributes a) =>
            a == null ? ElementKind.None : ElementKinds.Parse(a.GetUserString(Keys.Kind));

        public static T Read<T>(RhinoObject obj) where T : ElementData =>
            obj == null ? null : Json.Read<T>(obj.Attributes.GetUserString(Keys.Data));

        /// <summary>Write the data (and kind) into a copy of the attributes.</summary>
        public static ObjectAttributes With(ObjectAttributes a, ElementKind kind, ElementData data)
        {
            var copy = a.Duplicate();
            copy.SetUserString(Keys.Kind, kind.ToString());
            copy.SetUserString(Keys.Data, Json.Write(data));
            return copy;
        }

        /// <summary>Update the stored data when it changed (no-op otherwise).</summary>
        public static bool Write(RhinoDoc doc, RhinoObject obj, ElementKind kind, ElementData data)
        {
            data.Id = obj.Id;
            string text = Json.Write(data);
            if (obj.Attributes.GetUserString(Keys.Data) == text && obj.Attributes.GetUserString(Keys.Kind) == kind.ToString())
                return false;
            var a = obj.Attributes.Duplicate();
            a.SetUserString(Keys.Kind, kind.ToString());
            a.SetUserString(Keys.Data, text);
            return doc.Objects.ModifyAttributes(obj, a, true);
        }

        public static RhinoObject Find(RhinoDoc doc, Guid id)
        {
            if (doc == null || id == Guid.Empty)
                return null;
            var o = doc.Objects.FindId(id);
            return o == null || o.IsDeleted ? null : o;
        }

        /// <summary>All live objects carrying a Skunk kind.</summary>
        public static IEnumerable<RhinoObject> All(RhinoDoc doc, params ElementKind[] kinds)
        {
            var set = new HashSet<ElementKind>(kinds ?? Array.Empty<ElementKind>());
            var settings = new ObjectEnumeratorSettings
            {
                NormalObjects = true,
                LockedObjects = true,
                HiddenObjects = true,
                DeletedObjects = false,
                ReferenceObjects = true,
                IncludeLights = false,
                IncludeGrips = false,
            };
            foreach (var o in doc.Objects.GetObjectList(settings))
            {
                var k = KindOf(o);
                if (k == ElementKind.None)
                    continue;
                if (set.Count == 0 || set.Contains(k))
                    yield return o;
            }
        }

        public static double[] Flatten(IEnumerable<Point3d> pts)
        {
            var list = new List<double>();
            foreach (var p in pts)
            {
                list.Add(p.X);
                list.Add(p.Y);
                list.Add(p.Z);
            }
            return list.ToArray();
        }

        public static List<Point3d> Unflatten(double[] a)
        {
            var list = new List<Point3d>();
            if (a == null)
                return list;
            for (int i = 0; i + 2 < a.Length; i += 3)
                list.Add(new Point3d(a[i], a[i + 1], a[i + 2]));
            return list;
        }

        /// <summary>Rhino stores annotation line breaks as CR LF; compare texts without that difference.</summary>
        public static string NormalizeText(string text) => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');

        public static bool SamePoints(IList<Point3d> a, IList<Point3d> b, double tol)
        {
            if (a == null || b == null || a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].DistanceTo(b[i]) > tol)
                    return false;
            return true;
        }
    }
}
