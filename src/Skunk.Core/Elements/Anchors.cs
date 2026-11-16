using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Skunk.Elements
{
    /// <summary>The evaluated position of an anchor on its host.</summary>
    public sealed class HostSample
    {
        public bool Found;
        public bool HostMissing;
        public Point3d Point = Point3d.Unset;
        /// <summary>Surface normal (faces, meshes) - zero for curves and points.</summary>
        public Vector3d Normal = Vector3d.Zero;
        /// <summary>Curve tangent - zero for faces, meshes and points.</summary>
        public Vector3d Tangent = Vector3d.Zero;
        /// <summary>The host geometry the sample was evaluated on (null for free spots and missing hosts).</summary>
        public GeometryBase Geometry;
    }

    /// <summary>Attach spots to hosts and evaluate them after the host moved or changed.</summary>
    public static class Anchors
    {
        /// <summary>Geometry Skunk evaluates for a host object (extrusions and SubDs as breps).</summary>
        public static GeometryBase HostGeometry(RhinoObject o)
        {
            if (o == null)
                return null;
            switch (o.Geometry)
            {
                case Extrusion ex:
                    return ex.ToBrep(false);
                case SubD sd:
                    return sd.ToBrep();
                case Surface srf:
                    return srf.ToBrep();
                default:
                    return o.Geometry;
            }
        }

        public static bool CanHost(RhinoObject o)
        {
            if (o == null)
                return false;
            switch (o.Geometry)
            {
                case Brep _:
                case Extrusion _:
                case SubD _:
                case Surface _:
                case Mesh _:
                case Curve _:
                case Point _:
                    return Elements.ElementIO.KindOf(o) == Core.ElementKind.None || Elements.ElementIO.KindOf(o) == Core.ElementKind.Level;
                default:
                    return false;
            }
        }

        /// <summary>Attach to the point of <paramref name="host"/> closest to <paramref name="near"/>.</summary>
        public static AnchorData Capture(RhinoObject host, Point3d near)
        {
            var a = new AnchorData { Host = host?.Id ?? Guid.Empty, Kind = HostKind.Free };
            a.SetLast(near);
            if (host == null)
                return a;
            Attach(a, HostGeometry(host), near);
            return a;
        }

        /// <summary>Re-attach an anchor to its host at the point closest to <paramref name="near"/>.</summary>
        public static void Reanchor(RhinoDoc doc, AnchorData a, Point3d near)
        {
            var host = ElementIO.Find(doc, a.Host);
            if (host == null)
            {
                a.Kind = HostKind.Free;
                a.SetLast(near);
                return;
            }
            Attach(a, HostGeometry(host), near);
        }

        static void Attach(AnchorData a, GeometryBase g, Point3d near)
        {
            switch (g)
            {
                case Point pt:
                    a.Kind = HostKind.Point;
                    a.SetLast(pt.Location);
                    return;
                case Curve c:
                    if (c.ClosestPoint(near, out double t))
                    {
                        var d = c.Domain;
                        a.Kind = HostKind.Curve;
                        a.U = d.Length > 0 ? (t - d.T0) / d.Length : 0;
                        a.SetLast(c.PointAt(t));
                        return;
                    }
                    break;
                case Brep b:
                    if (AttachBrep(a, b, near))
                        return;
                    break;
                case Mesh m:
                    {
                        var mp = m.ClosestMeshPoint(near, 0.0);
                        if (mp != null && mp.FaceIndex >= 0)
                        {
                            a.Kind = HostKind.Mesh;
                            a.Index = mp.FaceIndex;
                            a.T = (double[])mp.T.Clone();
                            a.SetLast(mp.Point);
                            return;
                        }
                        break;
                    }
            }
            a.Kind = HostKind.Free;
            a.SetLast(near);
        }

        static bool AttachBrep(AnchorData a, Brep b, Point3d near)
        {
            if (b == null || b.Faces.Count == 0)
                return false;
            var bb = b.GetBoundingBox(false);
            double reach = near.DistanceTo(bb.Center) + bb.Diagonal.Length + 1.0;
            if (!b.ClosestPoint(near, out Point3d cp, out ComponentIndex ci, out double s, out double t, reach, out Vector3d _))
                return false;
            int faceIndex = -1;
            double u = 0, v = 0;
            if (ci.ComponentIndexType == ComponentIndexType.BrepFace && ci.Index >= 0 && ci.Index < b.Faces.Count)
            {
                faceIndex = ci.Index;
                u = s;
                v = t;
            }
            else
            {
                IEnumerable<int> candidates = null;
                if (ci.ComponentIndexType == ComponentIndexType.BrepEdge && ci.Index >= 0 && ci.Index < b.Edges.Count)
                    candidates = b.Edges[ci.Index].AdjacentFaces();
                else if (ci.ComponentIndexType == ComponentIndexType.BrepVertex && ci.Index >= 0 && ci.Index < b.Vertices.Count)
                {
                    var faces = new List<int>();
                    foreach (int e in b.Vertices[ci.Index].EdgeIndices())
                        faces.AddRange(b.Edges[e].AdjacentFaces());
                    candidates = faces;
                }
                double best = double.MaxValue;
                foreach (int fi in candidates ?? AllFaces(b))
                {
                    var f = b.Faces[fi];
                    if (!f.ClosestPoint(cp, out double fu, out double fv))
                        continue;
                    double dist = f.PointAt(fu, fv).DistanceTo(cp);
                    if (dist < best)
                    {
                        best = dist;
                        faceIndex = fi;
                        u = fu;
                        v = fv;
                    }
                }
            }
            if (faceIndex < 0)
                return false;
            var face = b.Faces[faceIndex];
            var d0 = face.Domain(0);
            var d1 = face.Domain(1);
            a.Kind = HostKind.BrepFace;
            a.Index = faceIndex;
            a.U = d0.Length > 0 ? (u - d0.T0) / d0.Length : 0;
            a.V = d1.Length > 0 ? (v - d1.T0) / d1.Length : 0;
            a.SetLast(face.PointAt(u, v));
            return true;
        }

        static IEnumerable<int> AllFaces(Brep b)
        {
            for (int i = 0; i < b.Faces.Count; i++)
                yield return i;
        }

        /// <summary>Evaluate the anchor on the current host geometry.</summary>
        public static HostSample Evaluate(RhinoDoc doc, AnchorData a)
        {
            var s = new HostSample();
            var last = a.LastPoint;
            if (a.Kind == HostKind.Free || a.Host == Guid.Empty)
            {
                s.Found = last.IsValid;
                s.Point = last;
                return s;
            }
            var host = ElementIO.Find(doc, a.Host);
            if (host == null)
            {
                s.HostMissing = true;
                s.Found = last.IsValid;
                s.Point = last;
                return s;
            }
            var g = HostGeometry(host);
            s.Geometry = g;
            if (!EvaluateOn(a, g, s))
            {
                // the stored parameters no longer fit (topology changed): re-attach near the last point
                if (last.IsValid)
                {
                    Attach(a, g, last);
                    EvaluateOn(a, g, s);
                }
            }
            if (s.Found)
                a.SetLast(s.Point);
            return s;
        }

        static bool EvaluateOn(AnchorData a, GeometryBase g, HostSample s)
        {
            switch (a.Kind)
            {
                case HostKind.Point when g is Point pt:
                    s.Point = pt.Location;
                    s.Found = true;
                    return true;
                case HostKind.Curve when g is Curve c:
                    {
                        var d = c.Domain;
                        double t = d.T0 + a.U * d.Length;
                        s.Point = c.PointAt(t);
                        s.Tangent = c.TangentAt(t);
                        s.Found = true;
                        return true;
                    }
                case HostKind.BrepFace when g is Brep b:
                    {
                        if (a.Index < 0 || a.Index >= b.Faces.Count)
                            return false;
                        var f = b.Faces[a.Index];
                        var d0 = f.Domain(0);
                        var d1 = f.Domain(1);
                        double u = d0.T0 + a.U * d0.Length;
                        double v = d1.T0 + a.V * d1.Length;
                        s.Point = f.PointAt(u, v);
                        s.Normal = f.NormalAt(u, v);
                        s.Found = s.Point.IsValid;
                        return s.Found;
                    }
                case HostKind.Mesh when g is Mesh m:
                    {
                        if (a.Index < 0 || a.Index >= m.Faces.Count || a.T == null || a.T.Length < 4)
                            return false;
                        s.Point = m.PointAt(a.Index, a.T[0], a.T[1], a.T[2], a.T[3]);
                        s.Normal = FaceNormal(m, a.Index);
                        s.Found = s.Point.IsValid;
                        return s.Found;
                    }
            }
            return false;
        }

        /// <summary>
        /// The host point straight below or above <paramref name="near"/> (the one nearest in height), so a
        /// point keeps its plan position: spot slopes are rise over a plan run. Falls back to the closest
        /// host point where no vertical line meets the host: an edge or curve host, a vertical face, or past
        /// the host's boundary. False for point hosts and free spots, which have nothing to measure along.
        /// </summary>
        public static bool OnHost(HostSample s, Point3d near, double tol, out Point3d onHost)
        {
            var g = s?.Geometry;
            if ((g is Brep || g is Mesh) && near.IsValid)
            {
                var bb = g.GetBoundingBox(false);
                double margin = Math.Max(1.0, bb.Diagonal.Length);
                var line = new Line(new Point3d(near.X, near.Y, bb.Min.Z - margin), new Point3d(near.X, near.Y, bb.Max.Z + margin));
                Point3d[] hits = null;
                if (g is Brep b)
                {
                    if (!Intersection.CurveBrep(new LineCurve(line), b, Math.Max(tol, 1e-9), out Curve[] _, out hits))
                        hits = null;
                }
                else
                    hits = Intersection.MeshLine((Mesh)g, line);
                if (hits != null && hits.Length > 0)
                {
                    onHost = hits[0];
                    foreach (var h in hits)
                        if (Math.Abs(h.Z - near.Z) < Math.Abs(onHost.Z - near.Z))
                            onHost = h;
                    return true;
                }
            }
            return ClosestPoint(s, near, out onHost);
        }

        /// <summary>
        /// The point of the sample's host closest to <paramref name="near"/>: on any face of a brep, on a
        /// mesh, or on a curve. False for point hosts and free spots, which have nothing to measure along.
        /// </summary>
        public static bool ClosestPoint(HostSample s, Point3d near, out Point3d onHost)
        {
            onHost = Point3d.Unset;
            switch (s?.Geometry)
            {
                case Curve c:
                    if (!c.ClosestPoint(near, out double t))
                        return false;
                    onHost = c.PointAt(t);
                    return onHost.IsValid;
                case Brep b:
                    {
                        if (b.Faces.Count == 0)
                            return false;
                        var bb = b.GetBoundingBox(false);
                        double reach = near.DistanceTo(bb.Center) + bb.Diagonal.Length + 1.0;
                        if (!b.ClosestPoint(near, out Point3d cp, out ComponentIndex _, out double _, out double _, reach, out Vector3d _))
                            return false;
                        onHost = cp;
                        return onHost.IsValid;
                    }
                case Mesh m:
                    {
                        var mp = m.ClosestMeshPoint(near, 0.0);
                        if (mp == null)
                            return false;
                        onHost = mp.Point;
                        return onHost.IsValid;
                    }
            }
            return false;
        }

        /// <summary>Face normal computed from the vertices (no need for stored normals).</summary>
        public static Vector3d FaceNormal(Mesh m, int faceIndex)
        {
            var f = m.Faces[faceIndex];
            Point3d A = m.Vertices[f.A], B = m.Vertices[f.B], C = m.Vertices[f.C];
            var n = Vector3d.CrossProduct(B - A, C - A);
            if (f.IsQuad)
            {
                Point3d D = m.Vertices[f.D];
                n += Vector3d.CrossProduct(C - A, D - A);
            }
            n.Unitize();
            return n;
        }

        /// <summary>Top and bottom of the host under / over a point ("Top &amp; Bottom Elevations").</summary>
        public static bool TopBottom(RhinoDoc doc, AnchorData a, Point3d p, out double top, out double bottom)
        {
            top = bottom = p.Z;
            var host = ElementIO.Find(doc, a.Host);
            if (host == null)
                return false;
            var g = HostGeometry(host);
            var bb = g.GetBoundingBox(false);
            if (!bb.IsValid)
                return false;
            double margin = Math.Max(1.0, bb.Diagonal.Length);
            var line = new Line(new Point3d(p.X, p.Y, bb.Min.Z - margin), new Point3d(p.X, p.Y, bb.Max.Z + margin));
            var zs = new List<double>();
            double tol = doc?.ModelAbsoluteTolerance ?? 1e-3;
            switch (g)
            {
                case Brep b:
                    if (Intersection.CurveBrep(new LineCurve(line), b, tol, out Curve[] overlaps, out Point3d[] pts))
                    {
                        foreach (var q in pts)
                            zs.Add(q.Z);
                        foreach (var c in overlaps)
                        {
                            zs.Add(c.PointAtStart.Z);
                            zs.Add(c.PointAtEnd.Z);
                        }
                    }
                    break;
                case Mesh m:
                    {
                        var hits = Intersection.MeshLine(m, line);
                        if (hits != null)
                            foreach (var q in hits)
                                zs.Add(q.Z);
                        break;
                    }
            }
            if (zs.Count == 0)
                return false;
            top = double.MinValue;
            bottom = double.MaxValue;
            foreach (double z in zs)
            {
                top = Math.Max(top, z);
                bottom = Math.Min(bottom, z);
            }
            return true;
        }

        /// <summary>
        /// Slope at a sample as rise/run, with the downhill direction in 3D (unit, zero when flat).
        /// </summary>
        public static double Slope(HostSample s, out Vector3d downhill)
        {
            downhill = Vector3d.Zero;
            if (!s.Normal.IsZero)
            {
                var n = s.Normal;
                n.Unitize();
                double horizontal = Math.Sqrt(n.X * n.X + n.Y * n.Y);
                double vertical = Math.Abs(n.Z);
                var g = new Vector3d(0, 0, -1);
                var d = g - (g * n) * n;
                if (d.Unitize())
                    downhill = d;
                if (vertical < 1e-12)
                    return double.PositiveInfinity;
                return horizontal / vertical;
            }
            if (!s.Tangent.IsZero)
            {
                var t = s.Tangent;
                t.Unitize();
                double run = Math.Sqrt(t.X * t.X + t.Y * t.Y);
                double rise = Math.Abs(t.Z);
                downhill = t.Z <= 0 ? t : -t;
                if (run < 1e-12)
                    return double.PositiveInfinity;
                return rise / run;
            }
            return 0;
        }
    }
}
