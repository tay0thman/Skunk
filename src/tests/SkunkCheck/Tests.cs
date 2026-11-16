using System;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Skunk.Core;
using Skunk.Elements;
using Skunk.Service;
using Skunk.Site;
using Skunk.Types;
using Skunk.Units;

namespace SkunkCheck
{
    /// <summary>
    /// Headless checks. Rhino does not raise object events for a headless document, so the tests
    /// tell the service what changed (Touch) and run its processing (ProcessNow) - the same code the
    /// event handlers drive inside Rhino.
    /// </summary>
    internal static class Tests
    {
        static int s_failures;
        static int s_checks;

        static void Check(bool ok, string label)
        {
            s_checks++;
            if (!ok)
                s_failures++;
            Console.WriteLine((ok ? "  PASS " : "  FAIL ") + label);
        }

        static void Eq(string actual, string expected, string label) => EqN(ElementIO.NormalizeText(actual), expected, label);

        static void EqN(string actual, string expected, string label) =>
            Check(actual == expected, label + ": '" + (actual ?? "<null>").Replace("\n", "\\n") + "'" + (actual == expected ? "" : "  (expected '" + expected.Replace("\n", "\\n") + "')"));

        static void Near(double actual, double expected, double tol, string label) =>
            Check(Math.Abs(actual - expected) <= tol, label + ": " + actual.ToString("0.######") + (Math.Abs(actual - expected) <= tol ? "" : "  (expected " + expected.ToString("0.######") + ")"));

        static void Near(Point3d actual, Point3d expected, double tol, string label) =>
            Check(actual.DistanceTo(expected) <= tol, label + ": " + actual + (actual.DistanceTo(expected) <= tol ? "" : "  (expected " + expected + ")"));

        static string[] StartFlags() =>
            (System.Environment.GetEnvironmentVariable("SKUNKCHECK_ARGS") ?? (System.Environment.Version.Major >= 10 ? "/netcore-10" : "/netcore-8"))
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        public static int RunAll(string[] args)
        {
            if (args.Length > 2 && args[0] == "plugin")
            {
                using (new Rhino.Runtime.InProcess.RhinoCore(StartFlags(), Rhino.Runtime.InProcess.WindowStyle.NoWindow))
                {
                    PluginTests(args[1], args[2]);
                    Console.WriteLine();
                    Console.WriteLine(s_failures == 0 ? $"ALL {s_checks} CHECKS PASSED" : $"{s_failures} OF {s_checks} CHECKS FAILED");
                    return s_failures == 0 ? 0 : 1;
                }
            }
            if (args.Length > 1 && args[0] == "rui")
            {
                using (new Rhino.Runtime.InProcess.RhinoCore(StartFlags(), Rhino.Runtime.InProcess.WindowStyle.NoWindow))
                {
                    // parse a COPY of the toolbar file in Rhino and close it again, so nothing points at the project
                    var copy = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SkunkCheck_" + System.IO.Path.GetFileName(args[1]));
                    System.IO.File.Copy(args[1], copy, true);
                    var tf = RhinoApp.ToolbarFiles.Open(copy);
                    if (tf == null)
                    {
                        Console.WriteLine("Rhino could not open " + args[1]);
                        return 1;
                    }
                    Console.WriteLine("opened '{0}' id={1} groups={2}", tf.Name, tf.Id, tf.GroupCount);
                    for (int gi = 0; gi < tf.GroupCount; gi++)
                    {
                        var g = tf.GetGroup(gi);
                        Console.WriteLine("  group '{0}' id={1}", g.Name, g.Id);
                    }
                    var countProp = tf.GetType().GetProperty("ToolbarCount");
                    var getTb = tf.GetType().GetMethod("GetToolbar", new[] { typeof(int) });
                    int toolbars = countProp == null ? -1 : (int)countProp.GetValue(tf);
                    for (int ti = 0; getTb != null && ti < toolbars; ti++)
                    {
                        var tb = getTb.Invoke(tf, new object[] { ti });
                        Console.WriteLine("  toolbar '{0}'", tb.GetType().GetProperty("Name")?.GetValue(tb));
                    }
                    int groups = tf.GroupCount;
                    // the Skunk command finds this group by the id it knows (Skunk.UI.SkunkToolbar.GroupId)
                    var found = Skunk.UI.SkunkToolbar.Find();
                    bool knowsGroup = found != null && found.Path == tf.Path;
                    Console.WriteLine("Skunk command finds the toolbar group by its id: " + knowsGroup);
                    try
                    {
                        Console.WriteLine("Skunk command shows it: " + Skunk.UI.SkunkToolbar.Show(out _) + " (headless: no window to show it in)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Skunk command show (headless): " + ex.GetType().Name + " - " + ex.Message);
                    }
                    Console.WriteLine("closed: " + tf.Close(false));
                    try { System.IO.File.Delete(copy); } catch (System.IO.IOException) { }
                    return groups == 1 && toolbars == 1 && knowsGroup ? 0 : 1;
                }
            }
            if (args.Length > 1 && args[0] == "site")
            {
                using (new Rhino.Runtime.InProcess.RhinoCore(StartFlags(), Rhino.Runtime.InProcess.WindowStyle.NoWindow))
                    return SiteFileCheck(args[1]);
            }
            if (args.Length > 1 && args[0] == "rhproj")
            {
                using (new Rhino.Runtime.InProcess.RhinoCore(StartFlags(), Rhino.Runtime.InProcess.WindowStyle.NoWindow))
                {
                    Console.WriteLine("Rhino " + RhinoApp.Version + " on " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
                    return RhprojCheck.Run(args[1], args.Length > 2 && args[2] == "package");
                }
            }
            Console.WriteLine("== Units (Rhino-free)");
            UnitTests();
            Check(ScriptsCheck.Count == 24, "all " + ScriptsCheck.Count + " Script Editor command scripts compile against Skunk.Core");
            using (new Rhino.Runtime.InProcess.RhinoCore(StartFlags(), Rhino.Runtime.InProcess.WindowStyle.NoWindow))
            {
                Console.WriteLine("Rhino " + RhinoApp.Version + ", headless=" + RhinoApp.IsRunningHeadless);
                var doc = RhinoDoc.Create(null);
                doc.AdjustModelUnitSystem(UnitSystem.Feet, false);
                doc.ModelAbsoluteTolerance = 0.001;
                // Rhino shows feet and inches to 1/8": Skunk's lengths follow these document settings
                doc.ModelDistanceDisplayMode = Rhino.UI.DistanceDisplayMode.FeetInches;
                doc.ModelDistanceDisplayPrecision = 3;
                Skunk.Core.Log.Sink = m => Console.WriteLine("    [log] " + m);
                SkunkService.Ensure();
                SkunkService.Instance.ProcessNow(doc);

                Console.WriteLine("== Site");
                SiteTests(doc);
                Console.WriteLine("== Levels");
                LevelTests(doc);
                Console.WriteLine("== Spot elevations");
                SpotElevationTests(doc);
                Console.WriteLine("== Spot coordinates");
                SpotCoordinateTests(doc);
                Console.WriteLine("== Spot slopes");
                SpotSlopeTests(doc);
                Console.WriteLine("== Spot slope grips and moves");
                SlopeGripTests(doc);
                Console.WriteLine("== Mesh terrain");
                MeshTests(doc);
                Console.WriteLine("== North arrow / plan cut");
                NorthArrowTests(doc);
                Console.WriteLine("== Copies");
                CopyTests(doc);
                Console.WriteLine("== Level line pattern and heads");
                LevelOptionTests(doc);
                Console.WriteLine("== Properties palette content");
                PropertyTests(doc);
                Console.WriteLine("== Type edits");
                TypeTests(doc);
                Console.WriteLine("== Rhino dimensions on a Skunk sublayer");
                DimensionLayerTests(doc);
                Console.WriteLine("== Keyboard aliases (an in-memory alias list, never Rhino's)");
                KeyboardAliasTests();
                doc.Dispose();
                Console.WriteLine("== Units follow Rhino's document units");
                UnitsTests();
                Console.WriteLine("== Import shared coordinates");
                SharedCoordinatesTests();
            }
            Console.WriteLine();
            Console.WriteLine(s_failures == 0 ? $"ALL {s_checks} CHECKS PASSED" : $"{s_failures} OF {s_checks} CHECKS FAILED");
            return s_failures == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------------------------------------

        static void UnitTests()
        {
            double ft = UnitFormat.MetersPerFoot;
            var imp = LengthStyle.Imperial();
            Eq(UnitFormat.Length((100 + 6.5 / 12.0) * ft, imp), "100'-6 1/2\"", "feet-inches");
            Eq(UnitFormat.Length(0, imp), "0'-0\"", "zero");
            Eq(UnitFormat.Length(-2.5 * ft, imp), "-2'-6\"", "negative");
            Eq(UnitFormat.Length((11 + 15.97 / 12.0) * ft, imp), "12'-4\"", "carry rounding");
            Eq(UnitFormat.Length(0.0127, new LengthStyle { Format = LengthFormat.FeetFractionalInches, SuppressZeroFeet = true }), "1/2\"", "suppress zero feet");
            Eq(UnitFormat.Length(10 * ft, new LengthStyle { Format = LengthFormat.FeetFractionalInches, SuppressZeroInches = true }), "10'", "suppress zero inches");
            Eq(UnitFormat.Length(10.5 * ft, new LengthStyle { Format = LengthFormat.FeetFractionalInches, SuppressSpaces = false }), "10' - 6\"", "spaced feet-inches");
            Eq(UnitFormat.Length((100 + 6.5 / 12.0) * ft, new LengthStyle { Format = LengthFormat.FeetFractionalInches, ShowPlusForPositive = true }), "+100'-6 1/2\"", "plus sign");
            Eq(UnitFormat.Length(100.5416667 * ft, new LengthStyle { Format = LengthFormat.DecimalFeet, Precision = 2 }), "100.54'", "decimal feet");
            Eq(UnitFormat.Length(30.6451, new LengthStyle { Format = LengthFormat.Millimeters, Precision = 0, ShowUnitSymbol = false, UseDigitGrouping = true }), "30,645", "mm grouping");
            Eq(UnitFormat.Length(30.6451, new LengthStyle { Format = LengthFormat.Meters, Precision = 3 }), "30.645 m", "meters");
            Eq(UnitFormat.Length(1.2 * ft, new LengthStyle { Format = LengthFormat.FeetDecimalInches, Precision = 1 }), "1'-2.4\"", "feet decimal inches");
            Eq(UnitFormat.Slope(0.02, new SlopeStyle { Format = SlopeFormat.Percent, Precision = 2 }), "2.00%", "slope percent");
            Eq(UnitFormat.Slope(0.25 / 12.0, new SlopeStyle { Format = SlopeFormat.RiseOver12Inches, FractionDenominator = 16 }), "1/4\" / 12\"", "slope rise/12");
            Eq(UnitFormat.Slope(1 / 48.0, new SlopeStyle { Format = SlopeFormat.RatioOneToX, Precision = 0 }), "1:48", "slope 1:x");
            Eq(UnitFormat.Slope(0.02, new SlopeStyle { Format = SlopeFormat.DecimalDegrees, Precision = 2 }), "1.15°", "slope degrees");
            Eq(UnitFormat.Slope(0, new SlopeStyle { Format = SlopeFormat.RatioOneToX }), "FLAT", "flat ratio");
            Eq(UnitFormat.Slope(double.PositiveInfinity, new SlopeStyle()), "VERT", "vertical");
            Eq(UnitFormat.AngleEastWest(30), "30.00° East", "angle east");
            Eq(UnitFormat.AngleEastWest(-12.5), "12.50° West", "angle west");
            Check(UnitFormat.TryParseLength("10'-6 1/2\"", ft, out double m1) && Math.Abs(m1 - (10 + 6.5 / 12) * ft) < 1e-9, "parse feet-inches");
            Check(UnitFormat.TryParseLength("126\"", ft, out double m2) && Math.Abs(m2 - 126 * 0.0254) < 1e-9, "parse inches");
            Check(UnitFormat.TryParseLength("3200 mm", ft, out double m3) && Math.Abs(m3 - 3.2) < 1e-9, "parse mm");
            Check(UnitFormat.TryParseLength("12.5", ft, out double m4) && Math.Abs(m4 - 12.5 * ft) < 1e-9, "parse plain number in model units");
            Check(UnitFormat.TryParseLength("-2' 6\"", ft, out double m5) && Math.Abs(m5 + 2.5 * ft) < 1e-9, "parse negative feet");
            Eq(UnitFormat.Bearing(135), "S 45\u00B000' E", "bearing SE");
            Eq(UnitFormat.Bearing(300.5), "N 59\u00B030' W", "bearing NW");
            Eq(UnitFormat.Bearing(0), "Due N", "bearing due north");
            Eq(UnitFormat.Bearing(270), "Due W", "bearing due west");
            Eq(UnitFormat.Compass(135), "SE", "compass SE");
        }

        static void SiteTests(RhinoDoc doc)
        {
            var site = SiteModel.Get(doc);
            Near(site.AngleToTrueNorthDegrees, 0, 1e-9, "default angle to true north");
            site.SpecifyCoordinatesAtPoint(Point3d.Origin, 1000, 5000, 100, 30);
            Near(site.AngleToTrueNorthDegrees, 30, 1e-9, "specified angle (30 deg East)");
            Near(site.TrueNorth.X, 0.5, 1e-9, "true north leans east");
            var shared = site.Shared(Point3d.Origin);
            Near(shared, new Point3d(1000, 5000, 100), 1e-9, "shared coordinates at the specified point");
            var p = Point3d.Origin + site.TrueNorth * 10;
            Near(site.Shared(p), new Point3d(1000, 5010, 100), 1e-9, "10 ft toward true north");
            Near(site.WorldFromShared(1000, 5010, 100), p, 1e-9, "shared -> world round trip");

            site.RotateTrueNorth(10, RotationPivot.ProjectBasePoint);
            Near(site.AngleToTrueNorthDegrees, 40, 1e-9, "rotate true north +10");
            Near(site.ProjectBasePointShared, new Point3d(1000, 5000, 100), 1e-9, "pivot keeps its shared coordinates");
            site.RotateTrueNorth(-10, RotationPivot.ProjectBasePoint);

            var spBefore = site.Data;
            var probe = new Point3d(25, 40, 3);
            var sharedProbe = site.Shared(probe);
            site.MoveSurveyPoint(new Point3d(10, 0, 0), clipped: false);
            Near(site.Shared(probe), sharedProbe, 1e-9, "unclipped survey point: shared system stays");
            Near(new Point3d(site.Data.SpEast, site.Data.SpNorth, site.Data.SpElevation), site.Shared(new Point3d(10, 0, 0)), 1e-9, "unclipped survey point gets new values");
            double e0 = site.Data.SpEast, n0 = site.Data.SpNorth;
            site.MoveSurveyPoint(new Point3d(20, 0, 0), clipped: true);
            Near(site.Data.SpEast, e0, 1e-12, "clipped survey point keeps its values");
            Check(site.Shared(probe).DistanceTo(sharedProbe) > 1, "clipped survey point moves the shared system");
            site.MoveSurveyPoint(new Point3d(10, 0, 0), clipped: true);
            site.Save(doc);

            Check(NamedPlanes.TryGet(doc, Keys.DefaultSpCPlane, out var tn), "true north lives on its own named CPlane");
            Near(tn.YAxis.X, 0.5, 1e-9, "named CPlane Y axis = true north");
            Check(NamedPlanes.TryGet(doc, Keys.DefaultPbpCPlane, out var pbp) && pbp.YAxis.Y > 0.999, "project base point CPlane keeps project north");

            SkunkService.Instance.ContextChanged(doc);
            SkunkService.Instance.ProcessNow(doc);
            var data = SiteModel.Get(doc).Data;
            Check(SiteSync.Marker(doc, data.PbpMarker, ElementKind.ProjectBasePoint) != null, "project base point marker created");
            Check(SiteSync.Marker(doc, data.SpMarker, ElementKind.SurveyPoint) != null, "survey point marker created");

            // moving the PBP marker moves the project base point (the model stays)
            var marker = SiteSync.Marker(doc, data.PbpMarker, ElementKind.ProjectBasePoint);
            doc.Objects.Replace(marker.Id, new Point3d(0, 0, 2));
            SkunkService.Instance.Touch(doc, new[] { marker.Id });
            SkunkService.Instance.ProcessNow(doc);
            Near(SiteModel.Get(doc).Project.Origin, new Point3d(0, 0, 2), 1e-9, "PBP marker move updates the project base point");
            doc.Objects.Replace(marker.Id, new Point3d(0, 0, 0));
            SkunkService.Instance.Touch(doc, new[] { marker.Id });
            SkunkService.Instance.ProcessNow(doc);
        }

        static Guid s_level;
        static Guid s_attached;

        static void LevelTests(RhinoDoc doc)
        {
            var footprint = new Polyline(new[] { new Point3d(0, 0, 0), new Point3d(100, 0, 0), new Point3d(100, 60, 0), new Point3d(0, 60, 0), new Point3d(0, 0, 0) });
            Guid id;
            using (SkunkService.Suppress())
                id = LevelElement.Create(doc, "", "Level 1", 10, footprint);
            s_level = id;
            Check(id != Guid.Empty, "level created");
            var info = LevelElement.Info(doc, doc.Objects.FindId(id));
            Near(info.Z, 10, 1e-9, "level z");
            Eq(info.Name, "Level 1", "level name");
            Check(info.Data.Heads.Count == 2, "two heads by default (south + east, end 2): " + info.Data.Heads.Count);
            var head = doc.Objects.FindId(info.Data.Heads.Values.First());
            Eq((head.Geometry as Leader)?.PlainText, "Level 1\n10'-0\"", "head text");
            Check(NamedPlanes.TryGet(doc, "Level 1", out var lp) && Math.Abs(lp.Origin.Z - 10) < 1e-9, "level named CPlane at its elevation");
            var southHead = doc.Objects.FindId(info.Data.Heads["0:2"]);
            var hp = (southHead.Geometry as Leader).Plane;
            Near(hp.ZAxis.Y, -1, 1e-9, "south head faces the Front view (normal -Y)");
            var eastHead = doc.Objects.FindId(info.Data.Heads["1:2"]);
            Near((eastHead.Geometry as Leader).Plane.ZAxis.X, 1, 1e-9, "east head faces the Right view (normal +X)");

            // attach a box to the level, then raise the level: the box follows
            var box = new Box(new Plane(new Point3d(0, 0, 10), Vector3d.ZAxis), new Interval(0, 10), new Interval(0, 10), new Interval(0, 10)).ToBrep();
            s_attached = doc.Objects.AddBrep(box);
            LevelElement.Attach(doc, doc.Objects.FindId(s_attached), id);
            using (SkunkService.Suppress())
            {
                var ctx = new UpdateContext(doc);
                LevelElement.SetElevationZ(ctx, id, 12);
            }
            Near(doc.Objects.FindId(s_attached).Geometry.GetBoundingBox(true).Min.Z, 12, 1e-9, "attached object moved with the level");
            info = LevelElement.Info(doc, doc.Objects.FindId(id));
            Eq((doc.Objects.FindId(info.Data.Heads["0:2"]).Geometry as Leader)?.PlainText, "Level 1\n12'-0\"", "head text after elevation change");

            // moving a head vertically moves the level (like dragging a level)
            var h = doc.Objects.FindId(info.Data.Heads["1:2"]);
            doc.Objects.Transform(h.Id, Transform.Translation(0, 0, 3), true);
            SkunkService.Instance.Touch(doc, new[] { h.Id });
            SkunkService.Instance.ProcessNow(doc);
            info = LevelElement.Info(doc, doc.Objects.FindId(id));
            Near(info.Z, 15, 1e-9, "head moved up 3 ft -> level moved");
            Near(doc.Objects.FindId(s_attached).Geometry.GetBoundingBox(true).Min.Z, 15, 1e-9, "attached object followed the head drag");

            // moving a single corner vertically is rejected (the level stays flat)
            var curve = (Curve)doc.Objects.FindId(id).Geometry;
            curve.TryGetPolyline(out var pl);
            pl[1] = new Point3d(pl[1].X, pl[1].Y, pl[1].Z + 5);
            doc.Objects.Replace(id, new PolylineCurve(pl));
            SkunkService.Instance.Touch(doc, new[] { id });
            SkunkService.Instance.ProcessNow(doc);
            info = LevelElement.Info(doc, doc.Objects.FindId(id));
            Near(info.Z, 15, 1e-9, "level flattened back to its elevation");
            Check(info.Footprint.All(p => Math.Abs(p.Z - 15) < 1e-9), "all footprint points at the level elevation");

            // rename through Rhino's object name: CPlane and heads follow
            var o = doc.Objects.FindId(id);
            var a = o.Attributes.Duplicate();
            a.Name = "Ground Floor";
            doc.Objects.ModifyAttributes(o, a, true);
            SkunkService.Instance.Touch(doc, new[] { id });
            SkunkService.Instance.ProcessNow(doc);
            info = LevelElement.Info(doc, doc.Objects.FindId(id));
            Eq(info.Name, "Ground Floor", "renamed level");
            Check(NamedPlanes.TryGet(doc, "Ground Floor", out _) && !NamedPlanes.TryGet(doc, "Level 1", out _), "named CPlane renamed");
            Eq((doc.Objects.FindId(info.Data.Heads["0:2"]).Geometry as Leader)?.PlainText, "Ground Floor\n15'-0\"", "head shows the new name");

            // a second level gets the next name and the same extents
            Guid id2;
            using (SkunkService.Suppress())
                id2 = LevelElement.Create(doc, "", null, 27, LevelElement.DefaultFootprint(doc, 27));
            var info2 = LevelElement.Info(doc, doc.Objects.FindId(id2));
            Eq(info2.Name, "Level 2", "next level name");
            Near(info2.Footprint.BoundingBox.Diagonal.X, 100, 1e-9, "second level copies the extents");
        }

        static Guid s_box;
        static Guid s_spot;

        static void SpotElevationTests(RhinoDoc doc)
        {
            var box = new Box(Plane.WorldXY, new Interval(0, 4), new Interval(0, 4), new Interval(0, 4)).ToBrep();
            s_box = doc.Objects.AddBrep(box);
            var host = doc.Objects.FindId(s_box);
            Guid id;
            using (SkunkService.Suppress())
                id = SpotElement.Create(doc, ElementKind.SpotElevation, "Target Filled (Project)", host, new Point3d(2, 2, 4), Plane.WorldXY, new Point3d(5, 5, 4), true, true, Guid.Empty);
            s_spot = id;
            Check(id != Guid.Empty, "spot elevation created");
            var spot = doc.Objects.FindId(id);
            Eq((spot.Geometry as Leader)?.PlainText, "EL 4'-0\"", "spot elevation on the box top");
            var data = ElementIO.Read<SpotData>(spot);
            Check(data.Anchor.Kind == HostKind.BrepFace, "anchored to a brep face (" + host.Geometry.GetType().Name + "): " + Json.Write(data.Anchor));
            if (data.Anchor.Kind != HostKind.BrepFace)
            {
                Console.WriteLine("    raw: " + spot.Attributes.GetUserString(Keys.Data));
                var probe = Anchors.Capture(host, new Point3d(2, 2, 4));
                Console.WriteLine("    direct capture: " + Json.Write(probe));
                Console.WriteLine("    round trip: " + Json.Write(Json.Read<AnchorData>(Json.Write(probe))));
            }

            // the host moves up 1 ft: the spot follows and re-measures
            doc.Objects.Transform(s_box, Transform.Translation(0, 0, 1), true);
            SkunkService.Instance.Touch(doc, new[] { s_box });
            SkunkService.Instance.ProcessNow(doc);
            spot = doc.Objects.FindId(id);
            Eq((spot.Geometry as Leader)?.PlainText, "EL 5'-0\"", "spot after the host moved up");
            Near(SpotElement.Fingerprint(spot.Geometry)[0], new Point3d(2, 2, 5), 1e-6, "leader point follows the host");
            Near(SpotElement.Fingerprint(spot.Geometry)[1], new Point3d(5, 5, 5), 1e-6, "text offset is kept");

            // the host rotates: the anchor stays on the same spot of the face
            doc.Objects.Transform(s_box, Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin), true);
            SkunkService.Instance.Touch(doc, new[] { s_box });
            SkunkService.Instance.ProcessNow(doc);
            spot = doc.Objects.FindId(id);
            Near(SpotElement.Fingerprint(spot.Geometry)[0], new Point3d(-2, 2, 5), 1e-6, "spot rotated with the host");

            // the user drags the whole spot: it slides along the host (value re-measured)
            doc.Objects.Transform(id, Transform.Translation(1, 0, 0), true);
            SkunkService.Instance.Touch(doc, new[] { id });
            SkunkService.Instance.ProcessNow(doc);
            spot = doc.Objects.FindId(id);
            Near(SpotElement.Fingerprint(spot.Geometry)[0], new Point3d(-1, 2, 5), 1e-6, "dragged spot re-attached on the host");

            // a dragged spot that leaves the face snaps to the closest point of the host
            doc.Objects.Transform(id, Transform.Translation(0, 0, 3), true);
            SkunkService.Instance.Touch(doc, new[] { id });
            SkunkService.Instance.ProcessNow(doc);
            spot = doc.Objects.FindId(id);
            Near(SpotElement.Fingerprint(spot.Geometry)[0].Z, 5, 1e-6, "spot pulled back onto the host surface");
            Eq((spot.Geometry as Leader)?.PlainText, "EL 5'-0\"", "value unchanged on the same face");

            // copies get their own identity
            var copyId = doc.Objects.Transform(id, Transform.Translation(-1, 0, 0), false);
            SkunkService.Instance.Touch(doc, new[] { copyId });
            SkunkService.Instance.ProcessNow(doc);
            var copy = doc.Objects.FindId(copyId);
            var copyData = ElementIO.Read<SpotData>(copy);
            Check(copyData != null && copyData.Id == copyId && copyData.Anchor.Host == s_box, "copy re-identified, same host");

            // relative to a level
            Guid rel;
            using (SkunkService.Suppress())
                rel = SpotElement.Create(doc, ElementKind.SpotElevation, "Triangle (Relative)", host, new Point3d(-2, 2, 5), Plane.WorldXY, null, true, true, s_level);
            var relObj = doc.Objects.FindId(rel);
            Eq((relObj.Geometry as Leader)?.PlainText, "-10'-0\"", "relative to the level at 15'");
            using (SkunkService.Suppress())
                LevelElement.SetElevationZ(new UpdateContext(doc), s_level, 5);
            SkunkService.Instance.Touch(doc, new[] { s_level });
            SkunkService.Instance.ProcessNow(doc);
            Eq((doc.Objects.FindId(rel).Geometry as Leader)?.PlainText, "0'-0\"", "relative spot follows the level");

            // top & bottom of a slab-like box
            var slab = new Box(Plane.WorldXY, new Interval(20, 30), new Interval(0, 10), new Interval(-1, 0)).ToBrep();
            var slabId = doc.Objects.AddBrep(slab);
            Guid tb;
            using (SkunkService.Suppress())
                tb = SpotElement.Create(doc, ElementKind.SpotElevation, "Top & Bottom (Project)", doc.Objects.FindId(slabId), new Point3d(25, 5, 0), Plane.WorldXY, null, true, true, Guid.Empty);
            Eq((doc.Objects.FindId(tb).Geometry as Leader)?.PlainText, "T.O. 0'-0\"\nB.O. -1'-0\"", "top and bottom elevations");

            // deleting the host deletes its spots
            var tbObj = doc.Objects.FindId(tb);
            doc.Objects.Delete(slabId, true);
            using (SkunkService.Suppress())
                SpotElement.Update(new UpdateContext(doc), tbObj, UpdateReason.Host);
            Check(ElementIO.Find(doc, tb) == null, "spot deleted with its host");
        }

        static void SpotCoordinateTests(RhinoDoc doc)
        {
            // survey coordinates in decimal feet: a format chosen in the type stays, whatever Rhino shows
            var types = TypeTable.Get(doc);
            types.Find<SpotCoordinateType>("Target (Survey)").Units = new LengthStyle { Format = LengthFormat.DecimalFeet, Precision = 2 };
            TypeTable.Save(doc, types);
            var pt = doc.Objects.AddPoint(new Point3d(0, 0, 0));
            Guid id;
            using (SkunkService.Suppress())
                id = SpotElement.Create(doc, ElementKind.SpotCoordinate, "Target (Survey)", doc.Objects.FindId(pt), Point3d.Origin, Plane.WorldXY, new Point3d(3, 3, 0), true, true, Guid.Empty);
            var spot = doc.Objects.FindId(id);
            var site = SiteModel.Get(doc);
            var shared = site.Shared(Point3d.Origin);
            string expected = "N " + shared.Y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "'\nE " + shared.X.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "'";
            Eq((spot.Geometry as Leader)?.PlainText, expected, "spot coordinate in shared coordinates");

            // rotating true north re-labels every coordinate
            site.RotateTrueNorth(5, RotationPivot.SurveyPoint);
            site.Save(doc);
            SkunkService.Instance.ContextChanged(doc);
            SkunkService.Instance.ProcessNow(doc);
            var s2 = SiteModel.Get(doc).Shared(Point3d.Origin);
            string expected2 = "N " + s2.Y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "'\nE " + s2.X.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "'";
            Eq((doc.Objects.FindId(id).Geometry as Leader)?.PlainText, expected2, "spot coordinate after Rotate True North");
            site = SiteModel.Get(doc);
            site.RotateTrueNorth(-5, RotationPivot.SurveyPoint);
            site.Save(doc);

            // relative to a named CPlane
            doc.NamedConstructionPlanes.Add("Grid A", new Plane(new Point3d(10, 10, 0), Vector3d.XAxis, Vector3d.YAxis));
            var lib = TypeTable.Get(doc);
            var t = (SpotCoordinateType)lib.Duplicate(lib.SpotCoordinate[0], "Grid A");
            t.CoordinateOrigin = CoordinateBase.NamedCPlane;
            t.OriginCPlane = "Grid A";
            TypeTable.Save(doc, lib);
            Guid g;
            using (SkunkService.Suppress())
                g = SpotElement.Create(doc, ElementKind.SpotCoordinate, "Grid A", doc.Objects.FindId(pt), Point3d.Origin, Plane.WorldXY, null, true, true, Guid.Empty);
            Eq((doc.Objects.FindId(g).Geometry as Leader)?.PlainText, "N -10.00'\nE -10.00'", "spot coordinate relative to a named CPlane");
        }

        /// <summary>
        /// A host-following slope arrow: in the surface's plane, down the fall line (downhill in plan
        /// toward <paramref name="downhillInPlan"/>), just above the surface at the picked point.
        /// </summary>
        static void OnSurface(LinearDimension dim, Point3d picked, Vector3d surfaceNormal, Vector3d downhillInPlan, string label)
        {
            if (dim == null)
            {
                Check(false, label + ": no dimension");
                return;
            }
            var n = surfaceNormal;
            n.Unitize();
            if (n.Z < 0)
                n = -n;
            var x = dim.Plane.XAxis;
            Check(dim.Plane.ZAxis * n > 0.99999, label + ": arrow lies in the surface plane (normal " + dim.Plane.ZAxis + ")");
            Check(Math.Abs(x * n) < 1e-6 && x.Z <= 1e-9, label + ": arrow runs down the slope (" + x + ")");
            var plan = new Vector3d(x.X, x.Y, 0);
            plan.Unitize();
            Check(plan * downhillInPlan > 0.9999, label + ": downhill in plan");
            double gap = (dim.Plane.Origin - picked) * n;
            Check(gap > 0 && gap < 0.25 * dim.Plane.Origin.DistanceTo(picked) + 0.5, label + ": just above the surface (gap " + gap.ToString("0.####") + ")");
        }

        static void SpotSlopeTests(RhinoDoc doc)
        {
            // a plane falling 2% toward +X
            var srf = NurbsSurface.CreateFromCorners(new Point3d(0, 0, 0), new Point3d(10, 0, -0.2), new Point3d(10, 10, -0.2), new Point3d(0, 10, 0));
            var hid = doc.Objects.AddSurface(srf);
            Guid id;
            using (SkunkService.Suppress())
                id = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Percent)", doc.Objects.FindId(hid), new Point3d(5, 5, -0.1), Plane.WorldXY, null, true, true, Guid.Empty);
            Check(id != Guid.Empty, "spot slope created");
            var dim = doc.Objects.FindId(id).Geometry as LinearDimension;
            Eq(dim?.PlainUserText, "2.00%", "slope text");
            OnSurface(dim, new Point3d(5, 5, -0.1), new Vector3d(2, 0, 100), Vector3d.XAxis, "2% plane");

            // steepen the surface by moving it with a shear-free rotation about Y: slope changes
            doc.Objects.Transform(hid, Transform.Rotation(Math.Atan(0.02), Vector3d.YAxis, new Point3d(5, 5, -0.1)), true);
            SkunkService.Instance.Touch(doc, new[] { hid });
            SkunkService.Instance.ProcessNow(doc);
            dim = doc.Objects.FindId(id).Geometry as LinearDimension;
            Eq(dim?.PlainUserText, "4.00%", "slope updated after rotating the host");
            var face = ((Brep)doc.Objects.FindId(hid).Geometry).Faces[0];
            face.ClosestPoint(new Point3d(5, 5, -0.1), out double fu, out double fv);
            OnSurface(dim, face.PointAt(fu, fv), face.NormalAt(fu, fv), Vector3d.XAxis, "arrow re-tilted with the host");

            // a 45 degree roof: the arrow lies on the roof, not flat through it
            var roof = NurbsSurface.CreateFromCorners(new Point3d(200, 0, 0), new Point3d(210, 0, -10), new Point3d(210, 10, -10), new Point3d(200, 10, 0));
            var roofId = doc.Objects.AddSurface(roof);
            Guid steep;
            using (SkunkService.Suppress())
                steep = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Percent)", doc.Objects.FindId(roofId), new Point3d(205, 5, -5), Plane.WorldXY, null, true, true, Guid.Empty);
            var steepDim = doc.Objects.FindId(steep).Geometry as LinearDimension;
            Eq(steepDim?.PlainUserText, "100.00%", "45 degree roof slope");
            OnSurface(steepDim, new Point3d(205, 5, -5), new Vector3d(1, 0, 1), Vector3d.XAxis, "45 degree roof");

            // the flat (view plane) placement is still available as a type option
            var lib = TypeTable.Get(doc);
            var flat = (SpotSlopeType)lib.Duplicate(lib.Find<SpotSlopeType>("Slope Arrow (Percent)"), "Slope Arrow (Flat)");
            flat.Placement = SlopePlacement.ViewPlane;
            TypeTable.Save(doc, lib);
            Guid flatId;
            using (SkunkService.Suppress())
                flatId = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Flat)", doc.Objects.FindId(roofId), new Point3d(205, 5, -5), Plane.WorldXY, null, true, true, Guid.Empty);
            var flatDim = doc.Objects.FindId(flatId).Geometry as LinearDimension;
            Check(flatDim != null && flatDim.Plane.ZAxis.Z > 0.99999 && flatDim.Plane.XAxis.X > 0.99999, "view-plane placement stays flat, pointing downhill in plan");
            Eq(flatDim?.PlainUserText, "100.00%", "view-plane slope text");

            // an edge / curve host: the arrow runs along the edge
            var edgeId = doc.Objects.AddLine(new Point3d(300, 0, 0), new Point3d(310, 0, -1));
            Guid edgeSpot;
            using (SkunkService.Suppress())
                edgeSpot = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Percent)", doc.Objects.FindId(edgeId), new Point3d(305, 0, -0.5), Plane.WorldXY, null, true, true, Guid.Empty);
            var edgeDim = doc.Objects.FindId(edgeSpot).Geometry as LinearDimension;
            Eq(edgeDim?.PlainUserText, "10.00%", "slope of an edge");
            var along = new Vector3d(10, 0, -1);
            along.Unitize();
            Check(edgeDim != null && edgeDim.Plane.XAxis * along > 0.99999, "arrow runs down the edge (" + edgeDim?.Plane.XAxis + ")");

            // Rise / 12" type
            Guid r;
            using (SkunkService.Suppress())
                r = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Rise/12\")", doc.Objects.FindId(hid), new Point3d(5, 5, -0.2), Plane.WorldXY, null, true, true, Guid.Empty);
            Eq((doc.Objects.FindId(r).Geometry as LinearDimension)?.PlainUserText, "1/2\" / 12\"", "slope as rise over 12\"");
        }

        /// <summary>A slope arrow's grips: definition point 1 (tail), 2 (head), and its ends on the dimension line.</summary>
        static (Point3d tail, Point3d head, Point3d tailOnLine, Point3d headOnLine) SlopeEnds(RhinoDoc doc, Guid id)
        {
            var d = doc.Objects.FindId(id)?.Geometry as LinearDimension;
            if (d == null || !d.Get3dPoints(out var tail, out var head, out var a1, out var a2, out var _, out var _))
                return (Point3d.Unset, Point3d.Unset, Point3d.Unset, Point3d.Unset);
            return (tail, head, a1, a2);
        }

        /// <summary>
        /// Stand-in for a grip edit in Rhino: replace the arrow with one through the given definition
        /// points (its dimension line moved by <paramref name="lineShift"/>), then let Skunk process it.
        /// </summary>
        static void EditSlope(RhinoDoc doc, Guid id, Point3d tail, Point3d head, Vector3d lineShift)
        {
            var old = (LinearDimension)doc.Objects.FindId(id).Geometry;
            var x = head - tail;
            x.Unitize();
            var y = old.Plane.YAxis - (old.Plane.YAxis * x) * x;
            y.Unitize();
            var plane = new Plane(tail + (head - tail) * 0.5, x, y);
            var style = doc.DimStyles.FindId(old.DimensionStyleId) ?? doc.DimStyles.Current;
            var dim = LinearDimension.Create(Rhino.Geometry.AnnotationType.Aligned, style, plane, x, tail, head, plane.Origin + lineShift, 0.0);
            dim.PlainText = old.PlainText;
            doc.Objects.Replace(id, dim, true);
            SkunkService.Instance.Touch(doc, new[] { id });
            SkunkService.Instance.ProcessNow(doc);
        }

        static SpotData Slope(RhinoDoc doc, Guid id) => ElementIO.Read<SpotData>(doc.Objects.FindId(id));

        static Point3d Location(RhinoDoc doc, Guid id) => Anchors.Evaluate(doc, Slope(doc, id).Anchor).Point;

        static void SlopeGripTests(RhinoDoc doc)
        {
            // a plane falling 2% toward +X
            var srf = NurbsSurface.CreateFromCorners(new Point3d(400, 0, 0), new Point3d(410, 0, -0.2), new Point3d(410, 10, -0.2), new Point3d(400, 10, 0));
            var hid = doc.Objects.AddSurface(srf);
            Guid id;
            using (SkunkService.Suppress())
                id = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Percent)", doc.Objects.FindId(hid), new Point3d(405, 5, -0.1), Plane.WorldXY, null, true, true, Guid.Empty);
            var d0 = Slope(doc, id);
            Check(d0 != null && d0.SlopeTop == null && d0.SlopeBottom == null && d0.Points?.Length == 15, "new slope arrow: type-length reach, its grip points recorded");
            var (tail, head, tailLine, headLine) = SlopeEnds(doc, id);
            Near(tail, tailLine, 1e-6, "the arrow's grips sit on its ends (no extension lines)");
            var along = head - tail;
            double length0 = along.Length;
            along.Unitize();
            var side = Vector3d.CrossProduct(Vector3d.ZAxis, along);
            side.Unitize();

            // drag the top grip 2' further up the slope, and a little sideways (the sideways part is ignored)
            EditSlope(doc, id, tail - along * 2 + side * 0.3, head, Vector3d.Zero);
            var d1 = Slope(doc, id);
            Near(d1.SlopeTop ?? -1, length0 / 2 + 2, 0.002, "top grip dragged 2' uphill: top reach");
            Near(d1.SlopeBottom ?? -1, length0 / 2, 0.002, "bottom reach unchanged");
            var (tail1, head1, _, _) = SlopeEnds(doc, id);
            Near(tail1, tail - along * 2, 0.002, "the top end lands on the slope line, so the arrow still follows the slope");
            Near(head1, head, 0.002, "the bottom end stays where it was");
            Eq((doc.Objects.FindId(id).Geometry as LinearDimension)?.PlainUserText, "2.00%", "a plane's slope does not depend on the arrow's length");

            // drag the bottom grip 1' further down
            EditSlope(doc, id, tail1, head1 + along * 1, Vector3d.Zero);
            Near(Slope(doc, id).SlopeBottom ?? -1, length0 / 2 + 1, 0.002, "bottom grip dragged 1' downhill: bottom reach");
            Near(Slope(doc, id).SlopeTop ?? -1, length0 / 2 + 2, 0.002, "top reach kept");

            // drag the arrow's line sideways: the arrow moves there, and measures there
            var loc0 = Location(doc, id);
            var (t2, h2, _, _) = SlopeEnds(doc, id);
            EditSlope(doc, id, t2, h2, side * 1.5);
            var loc2 = Location(doc, id);
            Near(loc2, loc0 + side * 1.5, 0.002, "arrow line dragged 1.5' sideways: its location moved with it");
            var (t3, h3, tl3, hl3) = SlopeEnds(doc, id);
            Check(t3.DistanceTo(tl3) < 1e-6 && h3.DistanceTo(hl3) < 1e-6, "rebuilt with its grips on its ends again");
            Near(t3.DistanceTo(h3), length0 + 3, 0.002, "its length is kept when it moves");

            // an arrow drawn by Skunk 1.1 (three plane points recorded) is upgraded, not read as an edit
            var older = Slope(doc, id);
            var odim = (LinearDimension)doc.Objects.FindId(id).Geometry;
            older.Points = ElementIO.Flatten(new[] { odim.Plane.Origin, odim.Plane.Origin + odim.Plane.XAxis, odim.Plane.Origin + odim.Plane.YAxis });
            ElementIO.Write(doc, doc.Objects.FindId(id), ElementKind.SpotSlope, older);
            SkunkService.Instance.Touch(doc, new[] { id });
            SkunkService.Instance.ProcessNow(doc);
            var upgraded = Slope(doc, id);
            Check(upgraded.Points?.Length == 15, "older arrow: grip points recorded after its first update");
            Near(Location(doc, id), loc2, 1e-6, "older arrow: location unchanged");
            Near(upgraded.SlopeTop ?? -1, length0 / 2 + 2, 1e-9, "older arrow: reach unchanged");

            // copy the arrow 3' along the plane: the copy is its own element and measures at its own place
            var copy = doc.Objects.Transform(id, Transform.Translation(0, -3, 0), false);
            SkunkService.Instance.TouchAdded(doc, new[] { copy });
            SkunkService.Instance.ProcessNow(doc);
            Check(Slope(doc, copy)?.Id == copy, "copied arrow is a new element");
            Near(Location(doc, copy), loc2 + new Vector3d(0, -3, 0), 0.002, "copy measures at its own location");
            Near(Location(doc, id), loc2, 1e-6, "original unchanged by the copy");

            // a ruled surface (hyperbolic paraboloid z = 0.05 (x - 500) y): the slope changes from place to place
            var hypar = NurbsSurface.CreateFromCorners(new Point3d(500, 0, 0), new Point3d(510, 0, 0), new Point3d(510, 10, 5), new Point3d(500, 10, 0));
            var rid = doc.Objects.AddSurface(hypar);
            Guid rs;
            using (SkunkService.Suppress())
                rs = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Percent)", doc.Objects.FindId(rid), new Point3d(505, 5, 1.25), Plane.WorldXY, null, true, true, Guid.Empty);
            Near(Slope(doc, rs).Value, Math.Sqrt(0.25 * 0.25 * 2), 0.001, "ruled surface: slope at (5, 5)");

            // move the arrow to (2, 8) on the surface: it measures there and turns to the fall line there
            doc.Objects.Transform(rs, Transform.Translation(-3, 3, -0.45), true);
            SkunkService.Instance.Touch(doc, new[] { rs });
            SkunkService.Instance.ProcessNow(doc);
            Near(Slope(doc, rs).Value, Math.Sqrt(0.4 * 0.4 + 0.1 * 0.1), 0.001, "arrow moved on the ruled surface: slope measured at its new location");
            Near(Location(doc, rs), new Point3d(502, 8, 0.8), 0.002, "its location is where it was moved to, on the surface");
            var rx = (doc.Objects.FindId(rs).Geometry as LinearDimension).Plane.XAxis;
            var plan = new Vector3d(rx.X, rx.Y, 0);
            plan.Unitize();
            var fall = new Vector3d(-0.4, -0.1, 0);
            fall.Unitize();
            Check(plan * fall > 0.999, "it turned to the fall line at its new location (" + plan + ")");

            // reach 3' uphill and 1' downhill: on a warped surface the arrow reads the slope between its ends
            var rd = Slope(doc, rs);
            rd.SlopeTop = 3;
            rd.SlopeBottom = 1;
            ElementIO.Write(doc, doc.Objects.FindId(rs), ElementKind.SpotSlope, rd);
            SkunkService.Instance.Touch(doc, new[] { rs });
            SkunkService.Instance.ProcessNow(doc);
            var (rt, rh, _, _) = SlopeEnds(doc, rs);
            double chord = Math.Abs(rt.Z - rh.Z) / Math.Sqrt((rt.X - rh.X) * (rt.X - rh.X) + (rt.Y - rh.Y) * (rt.Y - rh.Y));
            double value = Slope(doc, rs).Value;
            Near(value, chord, 1e-9, "value = rise / run between the arrow's two ends");
            // along the fall line u in plan, z(s) = z0 - m s + c s^2: between s = -a (top) and +b (bottom) the slope is m - c (b - a)
            double m = Math.Sqrt(0.17), cos = 1 / Math.Sqrt(1 + 0.17), c = 0.05 * fall.X * fall.Y;
            double expected = m - c * (1 * cos - 3 * cos);
            Near(value, expected, 0.001, "3' up / 1' down on the warped surface: the slope between its ends (" + m.ToString("0.####") + " at its location)");
            var percent = TypeTable.Get(doc).Find<SpotSlopeType>("Slope Arrow (Percent)");
            Eq((doc.Objects.FindId(rs).Geometry as LinearDimension)?.PlainUserText, UnitFormat.Slope(value, percent.Slope), "the label shows that value");
            var measured = SpotElement.MeasureSlope(new UpdateContext(doc), percent, Slope(doc, rs), Anchors.Evaluate(doc, Slope(doc, rs).Anchor));
            var rface = ((Brep)doc.Objects.FindId(rid).Geometry).Faces[0];
            bool OnFace(Point3d q) => rface.ClosestPoint(q, out double fu, out double fv) && q.DistanceTo(rface.PointAt(fu, fv)) < 0.002;
            Check(OnFace(measured.TopEnd) && OnFace(measured.BottomEnd), "both ends sit on the surface");
            Near((measured.Tail - measured.TopEnd).Length, (measured.Head - measured.BottomEnd).Length, 1e-9, "and the drawn arrow is the same small step above / beside both");

            // drop the bottom grip exactly where a 1.5' reach draws it: it reads back as 1.5' (no creep on a warped surface)
            var probe = Slope(doc, rs);
            probe.SlopeBottom = 1.5;
            var target = SpotElement.MeasureSlope(new UpdateContext(doc), percent, probe, Anchors.Evaluate(doc, probe.Anchor));
            var (gt, _, _, _) = SlopeEnds(doc, rs);
            EditSlope(doc, rs, gt, target.Head, Vector3d.Zero);
            Near(Slope(doc, rs).SlopeBottom ?? -1, 1.5, 0.002, "bottom grip dropped where a 1.5' reach draws it reads back as 1.5'");
            Near(Slope(doc, rs).SlopeTop ?? -1, 3, 1e-9, "the top reach is kept");
            Near(Location(doc, rs), new Point3d(502, 8, 0.8), 0.002, "an end grip does not move the arrow's location");
        }

        static void DimensionLayerTests(RhinoDoc doc)
        {
            var style = doc.DimStyles.Current;
            Guid Dim(double y) => doc.Objects.AddLinearDimension(LinearDimension.Create(Rhino.Geometry.AnnotationType.Aligned, style, Plane.WorldXY, Vector3d.XAxis,
                new Point3d(0, y, 0), new Point3d(5, y, 0), new Point3d(2.5, y + 1, 0), 0));
            string LayerOf(Guid id) => doc.Layers[doc.Objects.FindId(id).Attributes.LayerIndex].FullPath;
            void Added(params Guid[] ids)
            {
                SkunkService.Instance.TouchAdded(doc, ids);
                SkunkService.Instance.ProcessNow(doc);
            }
            string current = doc.Layers.CurrentLayer.FullPath;

            // on by default: a new Rhino dimension goes to the Skunk sublayer
            Check(DocStore.Settings(doc).DimensionsOnSkunkLayer, "Rhino dimensions go to a Skunk sublayer by default");
            var first = Dim(995);
            Added(first);
            Eq(LayerOf(first), "Skunk::Dimensions", "by default a new Rhino dimension goes to Skunk::Dimensions");

            // settings saved before it was the default (option off) switch over once
            var older = DocStore.Settings(doc);
            older.Version = 1;
            older.DimensionsOnSkunkLayer = false;
            DocStore.SaveSettings(doc, older);
            Check(DocStore.Settings(doc).DimensionsOnSkunkLayer, "settings from before switch to the new default");

            // turned off (and saved): Rhino's dimensions stay on the current layer
            var s = DocStore.Settings(doc);
            s.DimensionsOnSkunkLayer = false;
            DocStore.SaveSettings(doc, s);
            Check(!DocStore.Settings(doc).DimensionsOnSkunkLayer, "once turned off, it stays off");
            var before = Dim(1000);
            Added(before);
            Check(LayerOf(before) == current, "option off: a new Rhino dimension stays on the current layer (" + LayerOf(before) + ")");

            // on: new dimensions and leaders go to Skunk::Dimensions; text and Skunk's own annotations do not
            s.DimensionsOnSkunkLayer = true;
            DocStore.SaveSettings(doc, s);
            var dim = Dim(1010);
            var leader = doc.Objects.AddLeader(Leader.Create("note", Plane.WorldXY, style, new[] { new Point3d(0, 1020, 0), new Point3d(2, 1022, 0) }));
            var text = doc.Objects.AddText("label", new Plane(new Point3d(0, 1025, 0), Vector3d.ZAxis), 1, "Arial", false, false);
            var host = doc.Objects.AddSurface(NurbsSurface.CreateFromCorners(new Point3d(0, 1040, 0), new Point3d(10, 1040, -0.2), new Point3d(10, 1050, -0.2), new Point3d(0, 1050, 0)));
            Guid arrow;
            using (SkunkService.Suppress())
                arrow = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Percent)", doc.Objects.FindId(host), new Point3d(5, 1045, -0.1), Plane.WorldXY, null, true, true, Guid.Empty);
            Added(dim, leader, text, arrow);
            Eq(LayerOf(dim), "Skunk::Dimensions", "option on: a new Rhino dimension goes to the Skunk sublayer");
            Eq(LayerOf(leader), "Skunk::Dimensions", "and a new leader");
            Check(LayerOf(text) == current, "text stays where it is (" + LayerOf(text) + ")");
            Eq(LayerOf(arrow), "Skunk::Spot Slopes", "Skunk's own slope arrow keeps its layer");
            Eq(LayerOf(before), current, "a dimension made before stays until asked");

            // move the existing ones on request, then a sublayer of your own name
            int moved = DimensionLayer.MoveAll(doc, "Dimensions");
            Check(moved == 1 && LayerOf(before) == "Skunk::Dimensions", "existing Rhino dimensions moved on request (" + moved + ")");
            s.DimensionsLayer = "Dims";
            DocStore.SaveSettings(doc, s);
            var dim2 = Dim(1030);
            Added(dim2);
            Eq(LayerOf(dim2), "Skunk::Dims", "a sublayer of your own name");
            Eq(DimensionLayer.LayerName("A::B"), "Dimensions", "a layer path is not a sublayer name: the default is used");

            s.DimensionsOnSkunkLayer = true;
            s.DimensionsLayer = Keys.DimensionsLayer;
            DocStore.SaveSettings(doc, s);
        }

        static void KeyboardAliasTests()
        {
            Eq(KeyboardAliases.CommandOf("SkLevel"), "SkLevel", "a plain alias macro runs its command");
            Eq(KeyboardAliases.CommandOf("! _SkLevel"), "SkLevel", "so does '! _SkLevel'");
            Check(KeyboardAliases.CommandOf("_Line _Pause") == null, "a macro that does more is not counted as that command's alias");
            Check(KeyboardAliases.Defaults.Any(d => d.Alias == "LL" && d.Command == "SkLevel") &&
                  KeyboardAliases.Defaults.Any(d => d.Alias == "PI" && d.Command == "SkProperties") && !KeyboardAliases.Defaults.Any(d => d.Alias == "PP"),
                "Skunk suggests LL for Level and PI for Properties (not PP)");

            // a user's alias list: an old PP, an alias of their own, and PI already taken by another command
            var store = new MemoryAliasStore();
            store.Set("PP", "SkProperties");
            store.Set("C", "_Circle");
            store.Set("PI", "_Pipe");
            var o = KeyboardAliases.Apply(store, KeyboardAliases.DefaultTable(), replaceOthers: false);
            Eq(store.Macro("LL"), "SkLevel", "Skunk's aliases: LL runs Level");
            Check(store.Macro("PP") == null, "the old PP for Properties is removed");
            Eq(store.Macro("PI"), "_Pipe", "PI, which runs another command, is left alone without Replace");
            Check(o.Skipped.Count == 1 && o.Summary().Contains("PI"), "and reported (" + o.Summary().Replace("\n", " | ") + ")");
            Eq(store.Macro("C"), "_Circle", "aliases of other commands are untouched");
            o = KeyboardAliases.Apply(store, KeyboardAliases.DefaultTable(), replaceOthers: true);
            Eq(store.Macro("PI"), "SkProperties", "with Replace, PI is taken over for Properties");
            Check(o.Replaced.Count == 1, "and reported as replaced");

            // overrides: another alias, none, and two aliases swapped
            var wanted = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["SkLevel"] = "LV", ["SkSpotSlope"] = "" };
            KeyboardAliases.Apply(store, wanted, false);
            Check(store.Macro("LV") == "SkLevel" && store.Macro("LL") == null, "Level overridden to LV: LL goes");
            Check(store.Macro("SPS") == null, "Spot Slope cleared: no alias");
            KeyboardAliases.Apply(store, new System.Collections.Generic.Dictionary<string, string> { ["SkLevel"] = "PI", ["SkProperties"] = "LV" }, false);
            Check(store.Macro("PI") == "SkLevel" && store.Macro("LV") == "SkProperties", "two Skunk aliases swap without counting as conflicts");

            // bad input is reported, not applied
            o = KeyboardAliases.Apply(store, new System.Collections.Generic.Dictionary<string, string>
            {
                ["SkSpotElevation"] = "S E", ["SkSpotCoordinate"] = "X1", ["SkNorthArrow"] = "x1",
            }, false);
            Check(o.Invalid.Count == 2 && store.Macro("X1") == "SkSpotCoordinate", "a spaced alias and a duplicate (any case) are refused (" + o.Invalid.Count + ")");
            Eq(store.Macro("SPE"), "SkSpotElevation", "and that command keeps the alias it had");

            Check(CommandList.All.Length == 24 && CommandList.All.All(c => typeof(Skunk.SkunkEntry).GetMethod(c.Name) != null),
                "the dialog lists every Skunk command (" + CommandList.All.Length + ")");
            Check(KeyboardAliases.Describe(store).StartsWith("PI Level", StringComparison.Ordinal), "the Settings note lists the aliases (" + KeyboardAliases.Describe(store) + ")");
        }

        /// <summary>A console output as the snippet prints it (with console noise around the line).</summary>
        static string SiteLine(Point3d origin, Vector3d x, Vector3d y, Point3d pbpInternal, Point3d pbpShared, Point3d spInternal, Point3d spShared, bool? clipped = null)
        {
            string R(double d) => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            string P(Point3d p) => "[" + R(p.X) + ", " + R(p.Y) + ", " + R(p.Z) + "]";
            string V(Vector3d v) => P(new Point3d(v.X, v.Y, v.Z));
            return ">>> (snippet pasted)\n" + ImportedSite.Marker + " {\"v\": 1, \"units\": \"ft\", \"model\": \"Test Model\", \"location\": \"Internal\", \"version\": \"2026\", " +
                   "\"pbp\": {\"internal\": " + P(pbpInternal) + ", \"shared\": " + P(pbpShared) + "}, " +
                   "\"sp\": {\"internal\": " + P(spInternal) + ", \"shared\": " + P(spShared) + (clipped.HasValue ? ", \"clipped\": " + (clipped.Value ? "true" : "false") : "") + "}, " +
                   "\"total\": {\"origin\": " + P(origin) + ", \"x\": " + V(x) + ", \"y\": " + V(y) + ", \"z\": [0.0, 0.0, 1.0]}}\n>>> ";
        }

        static void SharedCoordinatesTests()
        {
            // a BIM model: true north 30 degrees east of project north, the survey point at internal (10, 20, 0)
            // with shared E 1000, N 5000, Elev 100, and a moved project base point (all decimal feet)
            double a = 30 * Math.PI / 180;
            var tn = new Vector3d(Math.Sin(a), Math.Cos(a), 0);
            var te = new Vector3d(Math.Cos(a), -Math.Sin(a), 0);
            var spInternal = new Point3d(10, 20, 0);
            var spShared = new Point3d(1000, 5000, 100);
            var origin = spInternal - (spShared.X * te + spShared.Y * tn + spShared.Z * Vector3d.ZAxis);   // shared origin, internally
            Point3d SharedOf(Point3d p)
            {
                var v = p - origin;
                return new Point3d(v * te, v * tn, v.Z);
            }
            var pbpInternal = new Point3d(-4, 6, 0);
            var pbpShared = SharedOf(pbpInternal);
            var anywhere = new Point3d(123.4, -56.7, 8.9);

            // the line, read out of the pasted console output (transform given shared -> internal, as documented)
            Check(ImportedSite.TryParse(SiteLine(origin, te, tn, pbpInternal, pbpShared, spInternal, spShared, true), out var site, out var err),
                "the line is read out of the pasted console output" + (err.Length > 0 ? " (" + err + ")" : ""));
            Near(site.Mismatch, 0, 1e-9, "the transform reproduces both points' own shared values");
            Eq(site.Model, "Test Model", "the model's name comes along");

            var doc = RhinoDoc.Create(null);
            doc.AdjustModelUnitSystem(UnitSystem.Feet, false);
            doc.ModelAbsoluteTolerance = 0.001;
            doc.ModelDistanceDisplayMode = Rhino.UI.DistanceDisplayMode.FeetInches;
            doc.ModelDistanceDisplayPrecision = 3;

            // the Rhino model sits at the BIM model's internal origin
            site.Apply(doc, ImportBasis.InternalOrigin);
            var m = SiteModel.Get(doc);
            Near(m.Project.Origin, pbpInternal, 1e-9, "internal origin: the project base point is where the model has it");
            Near(m.ProjectBasePointShared, pbpShared, 1e-6, "and its shared coordinates are the model's");
            Near(m.Shared(spInternal), spShared, 1e-6, "the survey point reads E 1000, N 5000, Elev 100");
            Near(m.AngleToTrueNorthDegrees, 30, 1e-9, "true north 30° east of project north");
            Near(m.Shared(anywhere), SharedOf(anywhere), 1e-6, "any point's shared coordinates match the model's");
            Check(m.Data.SpClipped, "the survey point's clipped state comes along");

            // ... at its project base point (Rhino origin = base point, axes along project north)
            site.Apply(doc, ImportBasis.ProjectBasePoint);
            m = SiteModel.Get(doc);
            Near(m.Project.Origin, Point3d.Origin, 1e-9, "project base point: the base point is Rhino's origin");
            Near(m.Shared(anywhere), SharedOf(anywhere + (pbpInternal - Point3d.Origin)), 1e-6, "and shared coordinates still match the model's");
            Near(m.AngleToTrueNorthDegrees, 30, 1e-9, "with the same angle to true north");

            // ... in its shared coordinates (Rhino's world is the shared system)
            site.Apply(doc, ImportBasis.SharedCoordinates);
            m = SiteModel.Get(doc);
            Near(m.Shared(anywhere), anywhere, 1e-6, "shared coordinates: Skunk's shared coordinates are Rhino's own");
            Near(m.Project.Origin, pbpShared, 1e-6, "the project base point sits at its shared coordinates");
            Near(m.AngleToTrueNorthDegrees, 30, 1e-9, "and project north is turned 30° from true north (Rhino's Y)");

            // a transform given the other way round (internal -> shared) is recognised from the points
            var reversed = SiteLine(SharedOf(Point3d.Origin), new Vector3d(te.X, tn.X, 0), new Vector3d(te.Y, tn.Y, 0), pbpInternal, pbpShared, spInternal, spShared);
            Check(ImportedSite.TryParse(reversed, out var site2, out _) && site2.Mismatch < 1e-9, "a transform given internal-to-shared is recognised");
            site2.Apply(doc, ImportBasis.InternalOrigin);
            Near(SiteModel.Get(doc).Shared(anywhere), SharedOf(anywhere), 1e-6, "and gives the same shared coordinates");

            // values that disagree are reported, console output without the line is refused
            var off = SiteLine(origin, te, tn, pbpInternal, pbpShared + new Vector3d(5, 0, 0), spInternal, spShared);
            Check(ImportedSite.TryParse(off, out var site3, out _) && Math.Abs(site3.Mismatch - 5) < 1e-6, "a 5' disagreement is measured (" + site3?.Mismatch + ")");
            Check(site3 != null && site3.Describe(doc, ImportBasis.InternalOrigin).Contains("Warning"), "and the preview warns about it");
            Check(!ImportedSite.TryParse("Traceback (most recent call last):\nNameError: name 'doc' is not defined", out _, out var why) && why.Contains(ImportedSite.Marker),
                "console output without the line is refused with a hint");

            // a metric Rhino model: the feet come in as metres
            var metric = RhinoDoc.Create(null);
            metric.AdjustModelUnitSystem(UnitSystem.Meters, false);
            site.Apply(metric, ImportBasis.InternalOrigin);
            var mm = SiteModel.Get(metric);
            Near(mm.Survey.Origin, new Point3d(10 * 0.3048, 20 * 0.3048, 0), 1e-9, "metric model: the survey point lands where the model has it, in metres");
            Near(mm.Data.SpNorth, 5000 * 0.3048, 1e-9, "and its shared north is in metres");
            metric.Dispose();

            Check(ImportedSite.Snippet.Contains("print('" + ImportedSite.Marker) && ImportedSite.Snippet.Contains("GetTotalTransform") && !ImportedSite.Snippet.Contains("\""),
                "the snippet prints the " + ImportedSite.Marker + " line (single-quoted Python, pastes into any console)");
            doc.Dispose();
        }

        /// <summary>The "site" mode: read a pasted console output file (e.g. a stand-in run of the snippet) and apply it to a fresh model.</summary>
        static int SiteFileCheck(string path)
        {
            var text = System.IO.File.ReadAllText(path);
            if (!ImportedSite.TryParse(text, out var site, out var error))
            {
                Console.WriteLine("FAIL " + error);
                return 1;
            }
            var doc = RhinoDoc.Create(null);
            doc.AdjustModelUnitSystem(UnitSystem.Feet, false);
            doc.ModelDistanceDisplayMode = Rhino.UI.DistanceDisplayMode.FeetInches;
            doc.ModelDistanceDisplayPrecision = 3;
            Console.WriteLine(site.Describe(doc, ImportBasis.InternalOrigin));
            site.Apply(doc, ImportBasis.InternalOrigin);
            var m = SiteModel.Get(doc);
            Near(m.ProjectBasePointShared, site.PbpShared, 1e-6, "the project base point's shared coordinates match the model's");
            Near(m.Shared(site.SpInternal), site.SpShared, 1e-6, "the survey point's shared coordinates match the model's");
            Console.WriteLine("angle to true north: " + UnitFormat.AngleEastWest(m.AngleToTrueNorthDegrees));
            doc.Dispose();
            return s_failures == 0 ? 0 : 1;
        }

        static void UnitsTests()
        {
            var doc = RhinoDoc.Create(null);
            doc.AdjustModelUnitSystem(UnitSystem.Feet, false);
            doc.ModelAbsoluteTolerance = 0.001;
            string L(double model) => Skunk.Commands.Kit.Length(doc, model);
            void Display(Rhino.UI.DistanceDisplayMode mode, int precision)
            {
                doc.ModelDistanceDisplayMode = mode;
                doc.ModelDistanceDisplayPrecision = precision;
            }

            // lengths read the way Document Properties > Units says
            Display(Rhino.UI.DistanceDisplayMode.FeetInches, 4);
            Eq(L(3.2), "3'-2 3/8\"", "feet, shown as feet and inches to 1/16\"");
            Display(Rhino.UI.DistanceDisplayMode.Decimal, 2);
            Eq(L(3.2), "3.20'", "feet, shown as decimals to 0.00");
            doc.AdjustModelUnitSystem(UnitSystem.Inches, false);
            Display(Rhino.UI.DistanceDisplayMode.Fractional, 3);
            Eq(L(38.4), "38 3/8\"", "inches, shown as fractions to 1/8\"");
            doc.AdjustModelUnitSystem(UnitSystem.Millimeters, false);
            Display(Rhino.UI.DistanceDisplayMode.Decimal, 1);
            Eq(L(975.36), "975.4", "millimetres to 0.0");
            doc.AdjustModelUnitSystem(UnitSystem.Meters, false);
            Display(Rhino.UI.DistanceDisplayMode.Decimal, 3);
            Eq(L(0.97536), "0.975", "metres to 0.000");
            Eq(UnitFormat.Describe(DocUnits.ModelLengthStyle(doc)), "Meters, 0.000", "Type Properties names what Rhino document units mean here");

            // type sizes (paper) follow Rhino's page units
            doc.AdjustPageUnitSystem(UnitSystem.Millimeters, false);
            Eq(UnitFormat.PaperSize(2.38125, DocUnits.Paper(doc)), "2.38 mm", "type sizes in millimetre page units");
            doc.AdjustPageUnitSystem(UnitSystem.Inches, false);
            doc.PageDistanceDisplayMode = Rhino.UI.DistanceDisplayMode.Fractional;
            Eq(UnitFormat.PaperSize(2.38125, DocUnits.Paper(doc)), "3/32\"", "type sizes in fractional inch page units");
            doc.PageDistanceDisplayMode = Rhino.UI.DistanceDisplayMode.Decimal;
            Eq(UnitFormat.PaperSize(2.38125, DocUnits.Paper(doc)), "0.094\"", "type sizes in decimal inch page units");

            // new types follow Rhino; an older library moves its stock formats over and keeps chosen ones
            doc.AdjustModelUnitSystem(UnitSystem.Feet, false);
            Display(Rhino.UI.DistanceDisplayMode.FeetInches, 3);
            var fresh = TypeLibrary.Defaults(true);
            Check(fresh.SpotElevation.All(t => t.Units.Format == LengthFormat.Project) && fresh.SpotCoordinate.All(t => t.Units.Format == LengthFormat.Project) &&
                  fresh.Level.All(t => t.Units.Format == LengthFormat.Project), "new types follow Rhino's document units");
            var older = TypeLibrary.Defaults(true);
            older.UnitsVersion = 0;
            older.SpotElevation[0].Units = LengthStyle.Imperial();
            older.SpotCoordinate[0].Units = new LengthStyle { Format = LengthFormat.DecimalFeet, Precision = 2, ShowUnitSymbol = true };
            older.Level[0].Units = new LengthStyle { Format = LengthFormat.FeetFractionalInches, FractionDenominator = 32 };
            TypeTable.Save(doc, older);
            var read = TypeTable.Get(doc);
            Check(read.SpotElevation[0].Units.Format == LengthFormat.Project && read.SpotCoordinate[0].Units.Format == LengthFormat.Project,
                "an older library's stock formats now follow Rhino");
            Check(read.Level[0].Units.Format == LengthFormat.FeetFractionalInches && read.Level[0].Units.FractionDenominator == 32, "a format chosen in a type stays");
            TypeTable.Save(doc, TypeLibrary.Defaults(true));

            // a spot, a slope with its own reach, and a level with an attached object
            var box = doc.Objects.AddBrep(new Box(Plane.WorldXY, new Interval(0, 10), new Interval(0, 10), new Interval(0, 10)).ToBrep());
            var ramp = doc.Objects.AddSurface(NurbsSurface.CreateFromCorners(new Point3d(20, 0, 2), new Point3d(30, 0, 1), new Point3d(30, 10, 1), new Point3d(20, 10, 2)));
            Guid spot, slope, level;
            using (SkunkService.Suppress())
            {
                spot = SpotElement.Create(doc, ElementKind.SpotElevation, "Target Filled (Project)", doc.Objects.FindId(box), new Point3d(5, 5, 10), Plane.WorldXY, new Point3d(8, 7, 10), true, true, Guid.Empty);
                slope = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Percent)", doc.Objects.FindId(ramp), new Point3d(25, 5, 1.5), Plane.WorldXY, null, true, true, Guid.Empty);
                level = LevelElement.Create(doc, "", "Level 2", 10, LevelElement.DefaultFootprint(doc, 10));
            }
            var attached = doc.Objects.AddBrep(new Box(Plane.WorldXY, new Interval(40, 42), new Interval(0, 2), new Interval(10, 12)).ToBrep());
            LevelElement.Attach(doc, doc.Objects.FindId(attached), level);
            var sd = Slope(doc, slope);
            sd.SlopeTop = 2;
            sd.SlopeBottom = 1;
            ElementIO.Write(doc, doc.Objects.FindId(slope), ElementKind.SpotSlope, sd);
            var all = new[] { box, ramp, spot, slope, level, attached };
            void Refresh()
            {
                // what Rhino's object and document-properties events report after a units change
                SkunkService.Instance.Touch(doc, all);
                SkunkService.Instance.ContextChanged(doc);
                SkunkService.Instance.ProcessNow(doc);
            }
            Refresh();
            string SpotText() => ElementIO.NormalizeText((doc.Objects.FindId(spot).Geometry as Leader)?.PlainText);
            Point3d Corner() => doc.Objects.FindId(attached).Geometry.GetBoundingBox(true).Min;
            Point3d Times(Point3d p, double f) => new Point3d(p.X * f, p.Y * f, p.Z * f);
            Eq(SpotText(), "EL 10'-0\"", "spot elevation, feet and inches");

            // changing how Rhino shows lengths re-labels everything
            Display(Rhino.UI.DistanceDisplayMode.Decimal, 2);
            Refresh();
            Eq(SpotText(), "EL 10.00'", "Rhino set to decimal feet: the spot follows");
            Display(Rhino.UI.DistanceDisplayMode.FeetInches, 3);
            Refresh();
            Eq(SpotText(), "EL 10'-0\"", "and back to feet and inches");

            // feet -> inches, scaling the model: Rhino scales every object, Skunk scales what it stores
            var offset0 = ElementIO.Read<SpotData>(doc.Objects.FindId(spot)).Offset;
            var location0 = Location(doc, slope);
            var corner0 = Corner();
            doc.AdjustModelUnitSystem(UnitSystem.Inches, true);
            SkunkService.Instance.UnitsScaled(doc, 12);   // what Rhino's UnitsChangedWithScaling event reports
            Refresh();
            Eq(SpotText(), "EL 10'-0\"", "model scaled to inches: the spot still reads 10'-0\"");
            Near(Slope(doc, slope).SlopeTop ?? -1, 24, 1e-9, "the slope's reach was scaled with the units (2' = 24\")");
            Near(Location(doc, slope), Times(location0, 12), 1e-6, "the slope stays where it was (the scaling is not read as an edit)");
            Near(Slope(doc, slope).Value, 0.1, 1e-6, "and still reads 10%");
            Near(ElementIO.Read<SpotData>(doc.Objects.FindId(spot)).Offset[0], offset0[0] * 12, 1e-9, "the leader offset was scaled");
            Near(ElementIO.Read<LevelData>(doc.Objects.FindId(level)).Z, 120, 1e-9, "the level's stored elevation was scaled");
            Near(Corner(), Times(corner0, 12), 1e-6, "the attached object was scaled once, not moved again with its level");

            // back to feet, scaling, while Skunk was not watching (no event): the recorded units tell
            doc.AdjustModelUnitSystem(UnitSystem.Feet, true);
            Refresh();
            Eq(SpotText(), "EL 10'-0\"", "scaled back to feet unseen: the spot reads 10'-0\"");
            Near(Slope(doc, slope).SlopeTop ?? -1, 2, 1e-9, "the reach is back to 2' (the change was found from the recorded units)");
            Near(Location(doc, slope), location0, 1e-6, "the slope is back where it was");
            Near(Corner(), corner0, 1e-6, "the attached object is back where it was, moved once");

            // feet -> millimetres WITHOUT scaling: every number stays, and so do Skunk's
            doc.AdjustModelUnitSystem(UnitSystem.Millimeters, false);
            Display(Rhino.UI.DistanceDisplayMode.Decimal, 1);
            Refresh();
            Near(Slope(doc, slope).SlopeTop ?? -1, 2, 1e-9, "units changed without scaling: the reach keeps its number");
            Near(Corner(), corner0, 1e-6, "nothing moved");
            Eq(SpotText(), "EL 10.0 mm", "the spot now reads millimetres (the type keeps its unit symbol setting)");
            doc.Dispose();
        }

        static void MeshTests(RhinoDoc doc)
        {
            // 5% terrain, rising toward +X
            var mesh = Mesh.CreateFromPlane(Plane.WorldXY, new Interval(0, 20), new Interval(0, 20), 10, 10);
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];
                mesh.Vertices.SetVertex(i, v.X, v.Y, 0.05 * v.X);
            }
            mesh.Normals.ComputeNormals();
            var mid = doc.Objects.AddMesh(mesh);
            Guid id, sl;
            using (SkunkService.Suppress())
            {
                id = SpotElement.Create(doc, ElementKind.SpotElevation, "Target Filled (Project)", doc.Objects.FindId(mid), new Point3d(5, 5, 0.25), Plane.WorldXY, null, true, true, Guid.Empty);
                sl = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Percent)", doc.Objects.FindId(mid), new Point3d(5, 5, 0.25), Plane.WorldXY, null, true, true, Guid.Empty);
            }
            Eq((doc.Objects.FindId(id).Geometry as Leader)?.PlainText, "EL 0'-3\"", "spot on a terrain mesh");
            var dim = doc.Objects.FindId(sl).Geometry as LinearDimension;
            Eq(dim?.PlainUserText, "5.00%", "terrain slope");
            OnSurface(dim, new Point3d(5, 5, 0.25), new Vector3d(-0.05, 0, 1), -Vector3d.XAxis, "terrain mesh");
            doc.Objects.Transform(mid, Transform.Translation(0, 0, 10), true);
            SkunkService.Instance.Touch(doc, new[] { mid });
            SkunkService.Instance.ProcessNow(doc);
            Eq((doc.Objects.FindId(id).Geometry as Leader)?.PlainText, "EL 10'-3\"", "spot followed the terrain");
        }

        static void NorthArrowTests(RhinoDoc doc)
        {
            Guid id;
            using (SkunkService.Suppress())
                id = NorthArrowElement.Create(doc, new Point3d(50, 50, 0), true, "");
            Check(id != Guid.Empty, "north arrow created");
            var inst = doc.Objects.FindId(id) as InstanceObject;
            var up = Vector3d.YAxis;
            up.Transform(inst.InstanceXform);
            up.Unitize();
            var tn = SiteModel.Get(doc).TrueNorth;
            Near(up * tn, 1, 1e-6, "north arrow points to true north");
            var site = SiteModel.Get(doc);
            site.RotateTrueNorth(-20, RotationPivot.SurveyPoint);
            site.Save(doc);
            SkunkService.Instance.ContextChanged(doc);
            SkunkService.Instance.ProcessNow(doc);
            inst = doc.Objects.FindId(id) as InstanceObject;
            up = Vector3d.YAxis;
            up.Transform(inst.InstanceXform);
            up.Unitize();
            Near(up * SiteModel.Get(doc).TrueNorth, 1, 1e-6, "north arrow re-oriented after Rotate True North");
            Near(inst.InsertionPoint, new Point3d(50, 50, 0), 1e-9, "north arrow stays in place");
        }

        /// <summary>
        /// The compiled plug-in, inspected by reflection only (it is NOT loaded as a Rhino plug-in, so
        /// nothing gets registered in the user's Rhino): id, start-up loading, commands and their ids.
        /// </summary>
        static void PluginTests(string rhpPath, string rhprojPath)
        {
            Console.WriteLine("== Compiled plug-in " + rhpPath);
            var asm = System.Reflection.Assembly.LoadFrom(System.IO.Path.GetFullPath(rhpPath));
            var project = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(rhprojPath)).RootElement;
            string projectId = project.GetProperty("id").GetString();
            var guid = asm.GetCustomAttributes(typeof(System.Runtime.InteropServices.GuidAttribute), false)
                .Cast<System.Runtime.InteropServices.GuidAttribute>().FirstOrDefault()?.Value;
            Check(string.Equals(guid, projectId, StringComparison.OrdinalIgnoreCase), "plug-in id = Script Editor project id (only one Skunk loads): " + guid);
            Eq(asm.GetName().Version?.ToString(3), project.GetProperty("identity").GetProperty("version").GetString(), "plug-in version = project version");

            var types = asm.GetTypes();
            var plugins = types.Where(t => !t.IsAbstract && typeof(Rhino.PlugIns.PlugIn).IsAssignableFrom(t)).ToList();
            Check(plugins.Count == 1, "one PlugIn class: " + string.Join(", ", plugins.Select(t => t.FullName)));
            if (plugins.Count == 1)
            {
                var p = (Rhino.PlugIns.PlugIn)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(plugins[0]);
                Check(p.LoadTime == Rhino.PlugIns.PlugInLoadTime.AtStartup, "loads when Rhino starts: " + p.LoadTime);
            }

            var codeIds = project.GetProperty("codes").EnumerateArray().ToDictionary(c => c.GetProperty("title").GetString(), c => c.GetProperty("id").GetString());
            var commands = types.Where(t => !t.IsAbstract && typeof(Rhino.Commands.Command).IsAssignableFrom(t)).ToList();
            var names = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var t in commands)
            {
                var c = (Rhino.Commands.Command)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
                names[c.EnglishName] = t.GUID.ToString();
            }
            Check(commands.Count == ScriptsCheck.Count, "commands: " + commands.Count + " (Script Editor project: " + ScriptsCheck.Count + ")");
            var missing = ScriptsCheck.Names.Where(n => !names.ContainsKey(n)).ToList();
            Check(missing.Count == 0, "every Script Editor command is in the plug-in" + (missing.Count > 0 ? ": missing " + string.Join(", ", missing) : ""));
            var badIds = names.Where(kv => !codeIds.TryGetValue(kv.Key, out var id) || !string.Equals(id, kv.Value, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            Check(badIds.Count == 0, "command ids match the rhproj" + (badIds.Count > 0 ? ": " + string.Join(", ", badIds) : ""));
            Check(asm.GetManifestResourceNames().Contains("Skunk.Plugin.Resources.Skunk.ico"), "plug-in icon embedded");
            var rui = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(rhpPath), "Skunk.rui");
            Check(System.IO.File.Exists(rui), "Skunk.rui next to Skunk.rhp (Rhino loads it with the plug-in)");
        }

        static void CopyTests(RhinoDoc doc)
        {
            // a host copied together with its spot: the copied spot belongs to the copied host
            var box = new Box(new Plane(new Point3d(100, 0, 0), Vector3d.ZAxis), new Interval(0, 4), new Interval(0, 4), new Interval(0, 4)).ToBrep();
            var hostId = doc.Objects.AddBrep(box);
            Guid spot;
            using (SkunkService.Suppress())
                spot = SpotElement.Create(doc, ElementKind.SpotElevation, "Target Filled (Project)", doc.Objects.FindId(hostId), new Point3d(102, 2, 4), Plane.WorldXY, new Point3d(105, 5, 4), true, true, Guid.Empty);
            var xf = Transform.Translation(0, 20, 6);
            var hostCopy = doc.Objects.Transform(hostId, xf, false);
            var spotCopy = doc.Objects.Transform(spot, xf, false);
            SkunkService.Instance.TouchAdded(doc, new[] { hostCopy, spotCopy });
            SkunkService.Instance.ProcessNow(doc);
            var copyData = ElementIO.Read<SpotData>(doc.Objects.FindId(spotCopy));
            Check(copyData.Anchor.Host == hostCopy, "copied spot re-hosted on the copied host");
            Eq((doc.Objects.FindId(spotCopy).Geometry as Leader)?.PlainText, "EL 10'-0\"", "copied spot measures the copied host");
            Check(ElementIO.Read<SpotData>(doc.Objects.FindId(spot)).Anchor.Host == hostId, "original spot keeps its host");
            doc.Objects.Transform(hostCopy, Transform.Translation(0, 0, 1), true);
            SkunkService.Instance.Touch(doc, new[] { hostCopy });
            SkunkService.Instance.ProcessNow(doc);
            Eq((doc.Objects.FindId(spotCopy).Geometry as Leader)?.PlainText, "EL 11'-0\"", "copied spot follows the copied host");

            // a level copied together with an object attached to it: the copy is attached to the copied level
            var footprint = new Polyline(new[] { new Point3d(0, 0, 0), new Point3d(50, 0, 0), new Point3d(50, 50, 0), new Point3d(0, 50, 0), new Point3d(0, 0, 0) });
            Guid level;
            using (SkunkService.Suppress())
                level = LevelElement.Create(doc, "", "Copy Source", 40, footprint);
            var slab = doc.Objects.AddBrep(new Box(new Plane(new Point3d(0, 0, 40), Vector3d.ZAxis), new Interval(0, 10), new Interval(0, 10), new Interval(-1, 0)).ToBrep());
            LevelElement.Attach(doc, doc.Objects.FindId(slab), level);
            var up = Transform.Translation(0, 0, 12);
            var levelCopy = doc.Objects.Transform(level, up, false);
            var slabCopy = doc.Objects.Transform(slab, up, false);
            SkunkService.Instance.TouchAdded(doc, new[] { levelCopy, slabCopy });
            SkunkService.Instance.ProcessNow(doc);
            var copyInfo = LevelElement.Info(doc, doc.Objects.FindId(levelCopy));
            Check(copyInfo != null && copyInfo.Name != "Copy Source" && Math.Abs(copyInfo.Z - 52) < 1e-9, "copied level gets a new name: " + copyInfo?.Name);
            Check(copyInfo != null && copyInfo.Data.Heads.Count == 2 && copyInfo.Data.Heads.Values.All(h => ElementIO.Find(doc, h) != null), "copied level has its own heads");
            Check(LevelElement.AttachedLevel(doc.Objects.FindId(slabCopy)) == levelCopy, "copied slab attached to the copied level");
            Check(LevelElement.AttachedLevel(doc.Objects.FindId(slab)) == level, "original slab stays on the original level");
            using (SkunkService.Suppress())
                LevelElement.SetElevationZ(new UpdateContext(doc), levelCopy, 55);
            Near(doc.Objects.FindId(slabCopy).Geometry.GetBoundingBox(true).Max.Z, 55, 1e-9, "copied slab follows the copied level");
            Near(doc.Objects.FindId(slab).Geometry.GetBoundingBox(true).Max.Z, 40, 1e-9, "original slab did not move");
        }

        static void LevelOptionTests(RhinoDoc doc)
        {
            var footprint = new Polyline(new[] { new Point3d(0, 0, 0), new Point3d(40, 0, 0), new Point3d(40, 30, 0), new Point3d(0, 30, 0), new Point3d(0, 0, 0) });
            Guid id;
            using (SkunkService.Suppress())
                id = LevelElement.Create(doc, "", "Options", 70, footprint);
            var obj = doc.Objects.FindId(id);
            Check(obj.Attributes.LinetypeSource == ObjectLinetypeSource.LinetypeFromLayer, "level line pattern by layer by default");

            // type line pattern: every level of the type uses it
            var lib = TypeTable.Get(doc);
            var lt = lib.Find<LevelType>(ElementIO.Read<LevelData>(obj).Type);
            lt.LinePattern = "Center";
            lt.LinePatternScale = 2.5;
            TypeTable.Save(doc, lib);
            SkunkService.Instance.ContextChanged(doc);
            SkunkService.Instance.ProcessNow(doc);
            obj = doc.Objects.FindId(id);
            var center = doc.Linetypes.FindName("Center");
            Check(center != null, "Rhino's standard linetypes loaded on demand");
            Check(obj.Attributes.LinetypeSource == ObjectLinetypeSource.LinetypeFromObject && center != null && obj.Attributes.LinetypeIndex == center.Index,
                "level drawn with the type's line pattern (Center)");
            Near(obj.Attributes.LinetypePatternScale, 2.5, 1e-12, "line pattern scale");

            // instance override
            using (SkunkService.Suppress())
            {
                var d = ElementIO.Read<LevelData>(obj);
                d.LinePattern = "Hidden";
                ElementIO.Write(doc, obj, ElementKind.Level, d);
                LevelElement.Update(new UpdateContext(doc), doc.Objects.FindId(id), UpdateReason.Context);
            }
            obj = doc.Objects.FindId(id);
            Check(obj.Attributes.LinetypeIndex == doc.Linetypes.FindName("Hidden")?.Index, "this level's own line pattern (Hidden) overrides the type");
            using (SkunkService.Suppress())
            {
                var d = ElementIO.Read<LevelData>(obj);
                d.LinePattern = Styles.ByLayer;
                ElementIO.Write(doc, obj, ElementKind.Level, d);
                LevelElement.Update(new UpdateContext(doc), doc.Objects.FindId(id), UpdateReason.Context);
            }
            Check(doc.Objects.FindId(id).Attributes.LinetypeSource == ObjectLinetypeSource.LinetypeFromLayer, "per-level By Layer");
            lt.LinePattern = "";
            lt.LinePatternScale = 1;
            TypeTable.Save(doc, lib);

            // which end of the level line shows the head
            string HeadKeys() => string.Join(",", ElementIO.Read<LevelData>(doc.Objects.FindId(id)).Heads.Keys.OrderBy(k => k));
            Eq(HeadKeys(), "0:2,1:2", "default: right end on the south and east elevations");
            using (SkunkService.Suppress())
                LevelElement.SetHeadEnds(new UpdateContext(doc), id, LevelElement.HeadEnds.Left);
            Eq(HeadKeys(), "0:1,1:1", "heads at the left end");
            var head = doc.Objects.FindId(ElementIO.Read<LevelData>(doc.Objects.FindId(id)).Heads["0:1"]);
            Near(SpotElement.Fingerprint(head.Geometry)[0], new Point3d(0, 0, 70), 1e-9, "left head at the start of the south edge");
            Check(SpotElement.Fingerprint(head.Geometry)[1].X < 0, "left head extends to the left");
            using (SkunkService.Suppress())
                LevelElement.SetHeadEnds(new UpdateContext(doc), id, LevelElement.HeadEnds.Both);
            Eq(HeadKeys(), "0:1,0:2,1:1,1:2", "heads at both ends");
            Check(LevelElement.CurrentHeadEnds(ElementIO.Read<LevelData>(doc.Objects.FindId(id))) == LevelElement.HeadEnds.Both, "head ends read back as Both");
            using (SkunkService.Suppress())
                LevelElement.SetHeadEnds(new UpdateContext(doc), id, LevelElement.HeadEnds.None);
            Eq(HeadKeys(), "", "no heads");
            using (SkunkService.Suppress())
                LevelElement.SetHeadEnds(new UpdateContext(doc), id, LevelElement.HeadEnds.Right);
            Eq(HeadKeys(), "0:2,1:2", "heads back at the right end (default edges)");
        }

        static void PropertyTests(RhinoDoc doc)
        {
            // what the Properties palette shows for each kind of element, built headlessly
            void Rows(string label, System.Collections.Generic.List<Skunk.UI.Param> rows, params string[] expect)
            {
                var names = new System.Collections.Generic.HashSet<string>(rows.Select(r => r.Name));
                var missing = expect.Where(e => !names.Contains(e)).ToList();
                Check(rows.Count > 0 && missing.Count == 0, label + ": " + rows.Count + " properties" + (missing.Count > 0 ? " - missing " + string.Join(", ", missing) : ""));
            }
            System.Collections.Generic.List<RhinoObject> Pick(ElementKind k) => ElementIO.All(doc, k).Take(2).ToList();
            Rows("spot elevation", Skunk.UI.PropertyModel.Spots(doc, ElementKind.SpotElevation, Pick(ElementKind.SpotElevation)),
                "Host", "Elevation Origin", "Display Elevations", "Elevation", "Shared N/S", "Project Elevation", "Internal X, Y, Z", "Layer", "Object Name");
            Rows("spot coordinate", Skunk.UI.PropertyModel.Spots(doc, ElementKind.SpotCoordinate, Pick(ElementKind.SpotCoordinate)),
                "Coordinate Origin", "N/S", "E/W", "Shared E/W");
            Rows("spot slope", Skunk.UI.PropertyModel.Spots(doc, ElementKind.SpotSlope, Pick(ElementKind.SpotSlope)),
                "Placement", "Arrow Top (uphill)", "Arrow Bottom (downhill)", "Slope", "Rise / Run", "Fall Direction (true north)", "Annotation Plane");
            Rows("level", Skunk.UI.PropertyModel.Level(doc, Pick(ElementKind.Level)[0]),
                "Elevation", "Elevation (Shared)", "Story Height", "Line Pattern", "Heads At", "Width (E/W)", "Extents Area", "Structural", "Relative Spot Elevations");
            Rows("north arrow", Skunk.UI.PropertyModel.NorthArrow(doc, Pick(ElementKind.NorthArrow)), "Points To", "Rotation", "Size");
            Rows("project (nothing selected)", Skunk.UI.PropertyModel.Location(doc, true, true),
                "Angle to True North", "Angle to True North (CCW)", "Shared Origin (internal)", "True North (azimuth)", "Model Units", "Live Updates");
            var plain = doc.Objects.FindId(s_attached);
            Rows("ordinary object", Skunk.UI.PropertyModel.Attached(doc, new System.Collections.Generic.List<RhinoObject> { plain }),
                "Base Level", "Base Offset", "Top Offset", "Height", "Top Elevation (Shared)", "Object Type");
        }

        static void TypeTests(RhinoDoc doc)
        {
            // change the units format of the spot elevation type: every spot of that type re-formats
            var lib = TypeTable.Get(doc);
            var t = lib.Find<SpotElevationType>("Target Filled (Project)");
            t.Units = new LengthStyle { Format = LengthFormat.DecimalFeet, Precision = 2 };
            t.ElevationIndicator = "+";
            TypeTable.Save(doc, lib);
            SkunkService.Instance.ContextChanged(doc);
            SkunkService.Instance.ProcessNow(doc);
            Eq((doc.Objects.FindId(s_spot).Geometry as Leader)?.PlainText, "+5.00'", "type change re-formats existing spots");
            var ds = doc.DimStyles.FindName(Styles.DimStyleName(t));
            Check(ds != null, "type has its own annotation style: " + Styles.DimStyleName(t));
        }
    }
}
