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
            if (args.Length > 0 && args[0] == "print")
            {
                // what layout details print: a run of its own (see PrintTests)
                using (new Rhino.Runtime.InProcess.RhinoCore(StartFlags(), Rhino.Runtime.InProcess.WindowStyle.NoWindow))
                {
                    SkunkService.Ensure();
                    PrintTests();
                    Console.WriteLine();
                    Console.WriteLine(s_failures == 0 ? $"ALL {s_checks} CHECKS PASSED" : $"{s_failures} OF {s_checks} CHECKS FAILED");
                    return s_failures == 0 ? 0 : 1;
                }
            }
            if (args.Length > 1 && args[0] == "docinfo")
            {
                // read-only: open each model headless, print its units and annotation scale settings, never save
                using (new Rhino.Runtime.InProcess.RhinoCore(StartFlags(), Rhino.Runtime.InProcess.WindowStyle.NoWindow))
                {
                    foreach (var file in args.Skip(1))
                    {
                        var d = RhinoDoc.OpenHeadless(file);
                        if (d == null)
                        {
                            Console.WriteLine(file + ": could not open");
                            continue;
                        }
                        Console.WriteLine("{0}: units={1} display={2} page={3} | Rhino model space scaling={4} text scale={5} hatch scale={6} " +
                                          "layout scaling={7} | Skunk scale={8} ({9}) imperial={10}",
                                          System.IO.Path.GetFileName(file), d.ModelUnitSystem, d.ModelDistanceDisplayMode, d.PageUnitSystem,
                                          d.ModelSpaceAnnotationScalingEnabled, d.ModelSpaceTextScale, d.ModelSpaceHatchScale,
                                          d.LayoutSpaceAnnotationScalingEnabled, DocUnits.AnnotationScale(d), DocUnits.Describe(DocUnits.AnnotationScale(d)),
                                          DocUnits.IsImperial(d));
                        // what Rhino multiplies annotation by in model views: each style's own DimensionScale
                        var scales = d.DimStyles.Where(s => !s.IsDeleted).GroupBy(s => s.DimensionScale)
                            .Select(g => g.Key.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " x" + g.Count() +
                                         " (" + string.Join(", ", g.Take(3).Select(s => s.Name)) + (g.Count() > 3 ? ", ..." : "") + ")");
                        Console.WriteLine("    style dimension scales: " + string.Join("; ", scales) + " | current style: " + d.DimStyles.Current?.Name);
                        foreach (var st in d.DimStyles.Where(x => !x.IsDeleted).Take(8))
                            Console.WriteLine("      " + st.Name + ": text height " + st.TextHeight.ToString("0.#####") + ", arrow " + st.ArrowLength.ToString("0.#####") +
                                              ", text gap " + st.TextGap.ToString("0.#####") + ", model space scale " + st.DimensionScale);
                        d.Dispose();
                    }
                    return 0;
                }
            }
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
                        bool loadedNow = Skunk.UI.SkunkToolbar.Load(out _, out bool openedNow);
                        Console.WriteLine("Skunk command finds it loaded and leaves it alone: " + (loadedNow && !openedNow) + " (headless: no window to show it in)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Skunk command show (headless): " + ex.GetType().Name + " - " + ex.Message);
                    }
                    // Rhino writes back what it read: the separators (spacer items) must survive, or its reader dropped them
                    int Spacers(string file) => System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText(file), "button_style=\"spacer\"").Count;
                    // the toolbar's items in order (separators marked), to see they also stay in place
                    string Order(string file)
                    {
                        var text = System.IO.File.ReadAllText(file);
                        int a = text.IndexOf("<tool_bars>", StringComparison.Ordinal), b = text.IndexOf("</tool_bars>", StringComparison.Ordinal);
                        var items = System.Text.RegularExpressions.Regex.Matches(a < 0 || b < a ? "" : text.Substring(a, b - a), "<tool_bar_item guid=\"([^\"]+)\"( button_style=\"spacer\")?");
                        return string.Join(" ", items.Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[2].Success ? "|" : m.Groups[1].Value.Substring(0, 8)));
                    }
                    var saved = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SkunkCheck_saved_" + System.IO.Path.GetFileName(args[1]));
                    bool wrote = tf.SaveAs(saved) && System.IO.File.Exists(saved);
                    int before = Spacers(args[1]), after = wrote ? Spacers(saved) : -1;
                    bool sameOrder = wrote && Order(saved) == Order(args[1]);
                    Console.WriteLine("separators: " + before + " in the file, " + after + " after Rhino read and saved it" + (sameOrder ? ", every button and separator in place" : ", ORDER CHANGED"));
                    Console.WriteLine("closed: " + tf.Close(false));
                    try { System.IO.File.Delete(copy); System.IO.File.Delete(saved); } catch (System.IO.IOException) { }
                    return groups == 1 && toolbars == 1 && knowsGroup && after == before && sameOrder ? 0 : 1;
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
            Check(ScriptsCheck.Count == 29, "all " + ScriptsCheck.Count + " Script Editor command scripts compile against Skunk.Core");
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
                Console.WriteLine("== Spot slopes on lines and polylines");
                CurveSlopeTests(doc);
                Console.WriteLine("== Spot slopes placed in side views (arrow and text in the CPlane they were placed in)");
                SideViewSlopeTests(doc);
                Console.WriteLine("== Go To Level (the dialog's level list)");
                GoToLevelTests();
                Console.WriteLine("== Mesh terrain");
                MeshTests(doc);
                Console.WriteLine("== North arrow / plan cut");
                NorthArrowTests(doc);
                Console.WriteLine("== Copies");
                CopyTests(doc);
                Console.WriteLine("== Level line pattern and heads");
                LevelOptionTests(doc);
                Console.WriteLine("== Level lines (the footprint does not print)");
                LevelLineTests(doc);
                Console.WriteLine("== Datum boxes (one outline for the extents of many levels)");
                DatumBoxTests(doc);
                Console.WriteLine("== Grids");
                GridTests(doc);
                Console.WriteLine("== Grids in elevations");
                GridElevationTests(doc);
                Console.WriteLine("== Grid names (type into a bubble, the grids after follow on, renumbering, copies named in turn)");
                GridNameTests(doc);
                Console.WriteLine("== Grid bubbles by the sides of a view (plans, elevations, layout details with sides of their own)");
                GridBubbleTests(doc);
                Console.WriteLine("== Properties palette content");
                PropertyTests(doc);
                Console.WriteLine("== Type edits");
                TypeTests(doc);
                Console.WriteLine("== Rhino dimensions on a Skunk sublayer");
                DimensionLayerTests(doc);
                Console.WriteLine("== Keyboard aliases (an in-memory alias list, never Rhino's)");
                KeyboardAliasTests();
                Console.WriteLine("== Start with Rhino (an in-memory startup list, never Rhino's) and the toolbar kept in the package folder (temporary folders)");
                StartupTests();
                Console.WriteLine("== The Skunk Attributes panel (what Rhino needs to register and dock it)");
                AttributesPanelTests();
                Console.WriteLine("== About Skunk");
                var about = Skunk.UI.AboutDialog.Lines();
                Check(about.Length == 4 && about[0].StartsWith(Skunk.Commands.SkunkInfo.Title) && about[1].Contains(Skunk.UI.AboutDialog.ProjectPage) &&
                      about[2].StartsWith("Installed: ") && about[3].StartsWith("Toolbar file: "),
                      "About Skunk names the version, the author, the project page, where it is installed and its toolbar file: " + about[0]);
                var aboutResources = typeof(Skunk.UI.AboutDialog).Assembly.GetManifestResourceNames();
                Check(aboutResources.Contains("Skunk.Core.Resources.Skunk.About.light.png") && aboutResources.Contains("Skunk.Core.Resources.Skunk.About.dark.png"),
                      "...with the Skunk logo, light and dark, in Skunk.Core");
                Console.WriteLine("== The annotation scale on Skunk's styles (what Rhino draws model views with)");
                AnnotationScaleTests(doc);
                doc.Dispose();
                Console.WriteLine("== Units follow Rhino's document units");
                UnitsTests();
                Console.WriteLine("== Print size: level heads and level lines at any scale");
                PrintSizeTests();
                Console.WriteLine("== Level heads filled by default");
                FilledHeadTests();
                Console.WriteLine("== Import shared coordinates");
                SharedCoordinatesTests();
                Console.WriteLine("== Project location: the Rhino model's geographic location (its Earth anchor point and Sun) and Rhino's north");
                GeoLocationTests();
                Console.WriteLine("== Skunk's layers under __Annotations__ (models from earlier builds: \"Skunk\" renamed, or merged into an __Annotations__ of their own)");
                LayerMigrationTests();
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
            Near(SiteModel.ClockwiseAngle(Vector3d.YAxis, GeoLocation.RhinoNorth(doc)), 30, 1e-6,
                "Rhino's north (its Sun and Earth anchor point) followed true north: in a new model the two agree");

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
            Eq(LayerOf(first), "__Annotations__::Dimensions", "by default a new Rhino dimension goes to __Annotations__::Dimensions");

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

            // on: new dimensions and leaders go to __Annotations__::Dimensions; text and Skunk's own annotations do not
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
            Eq(LayerOf(dim), "__Annotations__::Dimensions", "option on: a new Rhino dimension goes to the Skunk sublayer");
            Eq(LayerOf(leader), "__Annotations__::Dimensions", "and a new leader");
            Check(LayerOf(text) == current, "text stays where it is (" + LayerOf(text) + ")");
            Eq(LayerOf(arrow), "__Annotations__::Spot Slopes", "Skunk's own slope arrow keeps its layer");
            Eq(LayerOf(before), current, "a dimension made before stays until asked");

            // move the existing ones on request, then a sublayer of your own name
            int moved = DimensionLayer.MoveAll(doc, "Dimensions");
            Check(moved == 1 && LayerOf(before) == "__Annotations__::Dimensions", "existing Rhino dimensions moved on request (" + moved + ")");
            s.DimensionsLayer = "Dims";
            DocStore.SaveSettings(doc, s);
            var dim2 = Dim(1030);
            Added(dim2);
            Eq(LayerOf(dim2), "__Annotations__::Dims", "a sublayer of your own name");
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

            Check(CommandList.All.Length == ScriptsCheck.Count && CommandList.All.All(c => typeof(Skunk.SkunkEntry).GetMethod(c.Name) != null),
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

        /// <summary>
        /// The Skunk Attributes panel, as Rhino needs it to register, dock and bring back a panel: a public class with a
        /// GuidAttribute (its fixed id), a public constructor without parameters, Rhino's IPanel callbacks, and its tab
        /// icons as .ico resources in Skunk.Core. By reflection only: headless Rhino has no Eto platform to make the
        /// control, and registering a panel is the user's Rhino's business.
        /// </summary>
        static void AttributesPanelTests()
        {
            var t = typeof(Skunk.UI.SkunkAttributesPanel);
            var guid = t.GetCustomAttributes(typeof(System.Runtime.InteropServices.GuidAttribute), false).OfType<System.Runtime.InteropServices.GuidAttribute>().FirstOrDefault();
            Check(guid != null && new Guid(guid.Value) == new Guid("766a1bbd-ba43-5642-9313-4be87be9acaf") && Skunk.UI.SkunkAttributesPanel.PanelId == t.GUID,
                  "the panel has its fixed id (Rhino keeps where it is docked under it)");
            Check(t.IsPublic && t.GetConstructor(Type.EmptyTypes) != null && typeof(Rhino.UI.IPanel).IsAssignableFrom(t) && typeof(Eto.Forms.Panel).IsAssignableFrom(t),
                  "it is a public Eto panel with a constructor Rhino can call and Rhino's panel callbacks (shown, hidden, closing)");
            Eq(Skunk.UI.SkunkAttributesPanel.Caption, "Skunk Attributes", "its tab reads Skunk Attributes");
            foreach (var name in new[] { Skunk.UI.SkunkAttributesPanel.IconLight, Skunk.UI.SkunkAttributesPanel.IconDark })
            {
                using (var stream = t.Assembly.GetManifestResourceStream(name))
                {
                    var sizes = new System.Collections.Generic.List<int>();
                    if (stream != null)
                    {
                        var bytes = new byte[stream.Length];
                        stream.Read(bytes, 0, bytes.Length);
                        int count = bytes.Length > 6 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0 ? bytes[4] | bytes[5] << 8 : 0;
                        for (int i = 0; i < count && 6 + 16 * i < bytes.Length; i++)
                            sizes.Add(bytes[6 + 16 * i] == 0 ? 256 : bytes[6 + 16 * i]);
                    }
                    Check(sizes.Contains(16) && sizes.Contains(24) && sizes.Contains(32) && sizes.Contains(48),
                          "its tab icon " + name.Replace("Skunk.Core.Resources.", "") + " is in Skunk.Core, sizes " + string.Join(", ", sizes));
                }
            }
        }

        /// <summary>
        /// Start Skunk with Rhino adds and removes the Skunk command in Rhino's startup list and leaves the rest alone.
        /// In-memory only: the real list is the user's Rhino settings (RhinoStartupStore must never be used here).
        /// </summary>
        static void StartupTests()
        {
            var store = new MemoryStartupStore();
            Check(!StartWithRhino.IsOn(store) && StartWithRhino.Set(store, true) && store.Value == "_Skunk" && StartWithRhino.IsOn(store),
                  "an empty startup list gets _Skunk");
            Check(!StartWithRhino.Set(store, true) && store.Value == "_Skunk", "switched on twice, it is there once");
            store.Value = "_Grasshopper\n_-RunScript (Hello)";
            StartWithRhino.Set(store, true);
            Eq(store.Value, "_Grasshopper\n_-RunScript (Hello)\n_Skunk", "Skunk goes after the user's own startup commands");
            StartWithRhino.Set(store, false);
            Eq(store.Value, "_Grasshopper\n_-RunScript (Hello)", "switched off, only Skunk goes; the rest stays as it was");
            store.Value = "!_Skunk _Other";
            Check(StartWithRhino.IsOn(store), "!_Skunk counts as on");
            StartWithRhino.Set(store, false);
            Eq(store.Value, "_Other", "...and is removed");
            store.Value = "_SkunkWorks";
            Check(!StartWithRhino.IsOn(store), "another command that starts with Skunk does not count");

            ToolbarPackageTests();
        }

        /// <summary>
        /// The Skunk toolbar file: written by Skunk from the toolbar embedded in the engine into the package folder
        /// (packages\9.0\Skunk\Skunk.rui; beside the plug-in in a development build). No package carries one since 1.6.6:
        /// Rhino opens a plug-in's own toolbar file late, and up to 1.6.5 it took Skunk's toolbar out of the user's own
        /// toolbar at every start. Temporary folders laid out like a Yak install, with stand-ins for Rhino's toolbars and
        /// its record - never Rhino's own (headless Rhino runs on the user's settings).
        /// </summary>
        static void ToolbarPackageTests()
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SkunkCheck_toolbar_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                string pkg = System.IO.Path.Combine(root, "packages", "9.0", "Skunk");
                string V(string version) => System.IO.Path.Combine(pkg, version);
                string Rhp(string version) => System.IO.Path.Combine(V(version), "Skunk.rhp");
                string stable = System.IO.Path.Combine(pkg, "Skunk.rui");
                string stamp = System.IO.Path.Combine(pkg, "Skunk.rui.version");
                bool Same(string a, string b) => Skunk.UI.SkunkToolbar.SamePath(a, b);
                byte[] Bytes(string path) => System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : new byte[0];
                const string generatedId = "c879d7c5-a20d-50fc-9afe-ae95427fd409";
                const string rhinoId = "d4a72693-78ba-4a95-91c4-0930ad3ffae7";
                // a toolbar file with these buttons (its group is Skunk's)
                byte[] Toolbar(string fileId, string indent, params string[] macros)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<RhinoUI major_ver=\"5\" minor_ver=\"0\" guid=\"" + fileId + "\" localize=\"False\">\n");
                    sb.Append(indent + "<tool_bar_groups><tool_bar_group guid=\"" + generatedId + "\"><text><locale_1033>Skunk</locale_1033></text></tool_bar_group></tool_bar_groups>\n");
                    foreach (var m in macros)
                        sb.Append(indent + "<macro_item><text><locale_1033>" + m.Trim('!', ' ', '_') + "</locale_1033></text><script>" + m + "</script></macro_item>\n");
                    sb.Append("</RhinoUI>\n");
                    return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                }
                // what Yak does: the new version in its own folder, manifest.txt naming it, the old version folder deleted
                void Install(string version, string previous)
                {
                    System.IO.Directory.CreateDirectory(V(version));
                    System.IO.File.WriteAllText(Rhp(version), "plug-in " + version);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(pkg, "manifest.txt"), version + "\n");
                    if (previous != null)
                        System.IO.Directory.Delete(V(previous), true);
                }

                // the toolbar Skunk writes is the one gen_project generated, embedded in the engine
                var embedded = Skunk.UI.SkunkToolbar.Embedded();
                string embeddedButtons = embedded == null ? "" : Skunk.UI.SkunkToolbar.Buttons(embedded);
                Check(embedded != null && Skunk.UI.SkunkToolbar.FileId(embedded) == generatedId && embeddedButtons.Contains("_SkAbout") &&
                      embeddedButtons.Contains("_SkGridBubbles"), "the Skunk toolbar is embedded in the engine (as generated, the About button too)");

                Check(Skunk.UI.PackagePlace.Of(System.IO.Path.Combine(root, "dev", "build", "Skunk.rhp"), System.IO.File.Exists) == null,
                      "a plug-in outside a Yak package (a development build) has no package folder");
                Install("1.6.6", null);
                Check(Same(Skunk.UI.SkunkToolbar.Target(Rhp("1.6.6"), System.IO.File.Exists), stable),
                      "a Yak install's toolbar file is in the package folder (updates leave it, an uninstall removes it)");

                // which toolbar container shows after Skunk opens the file: never a second one beside a toolbar already loaded
                Check(Skunk.UI.SkunkToolbar.ShowAfterOpen(false, false) && Skunk.UI.SkunkToolbar.ShowAfterOpen(true, true) &&
                      !Skunk.UI.SkunkToolbar.ShowAfterOpen(true, false),
                      "the Skunk toolbar is shown when it was lost or showing, and left alone when loaded but not in its own container (a tab elsewhere, or hidden)");

                // a first run: no toolbar file yet, none open; Rhino recorded a plug-in toolbar file for Skunk
                var v1 = Toolbar(generatedId, "  ", "! _SkLevel", "! _SkGrid");
                var host = new FakeToolbarHost();
                var record = new Skunk.UI.MemoryToolbarRecord { Value = System.IO.Path.Combine(V("1.6.6"), "Skunk.rui") };
                string said = Skunk.UI.SkunkToolbar.Ensure(Rhp("1.6.6"), v1, host, record);
                Check(Bytes(stable).SequenceEqual(v1) && host.Opened.Count == 1 && Same(host.Opened[0], stable) && host.ShowAsked.Count == 1 && host.ShowAsked[0] &&
                      said != null && said.Contains("opens with Rhino"), "first run: Skunk writes its toolbar file into the package folder, opens and shows it");
                Check(record.Value == "", "...and Skunk has no plug-in toolbar file for Rhino to open late (Rhino's record cleared)");

                // every later start: Rhino opened the file itself, before building its toolbars - nothing is touched
                host.ShowAsked.Clear();
                said = Skunk.UI.SkunkToolbar.Ensure(Rhp("1.6.6"), v1, host, record);
                Check(said == null && host.Closed.Count == 0 && host.Opened.Count == 1 && host.ShowAsked.Count == 0,
                      "a later start, the toolbar file open: nothing closed, written or opened - a toolbar docked in the user's own toolbar stays there");

                // Rhino saved the file back its own way (its file id, other order and spacing, same buttons) and the stamp is
                // an older Skunk's: left alone, the stamp brought up to date
                var resaved = Toolbar(rhinoId, "        ", "! _SkGrid", "! _SkLevel");
                System.IO.File.WriteAllBytes(stable, resaved);
                System.IO.File.WriteAllText(stamp, "1.6.5");
                said = Skunk.UI.SkunkToolbar.Ensure(Rhp("1.6.6"), v1, host, record);
                Check(said == null && host.Closed.Count == 0 && Bytes(stable).SequenceEqual(resaved) && System.IO.File.ReadAllText(stamp).Trim() != "1.6.5",
                      "a toolbar Rhino saved back with the same buttons is left alone (only the stamp is updated)");

                // an update with new buttons, the toolbar docked as a tab in a toolbar of the user's (its own container
                // hidden): closed, rewritten with the file id Rhino knows it by, and opened again without showing a second one
                Install("1.6.7", "1.6.6");
                var v2 = Toolbar(generatedId, "  ", "! _SkLevel", "! _SkGrid", "! _SkAbout");
                host.Visible = false;
                said = Skunk.UI.SkunkToolbar.Ensure(Rhp("1.6.7"), v2, host, record);
                Check(host.Closed.Count == 1 && Same(host.Closed[0], stable) && host.Opened.Count == 1 && Same(host.Opened[0], stable) &&
                      host.ShowAsked.Count == 1 && !host.ShowAsked[0] && !host.Visible,
                      "new buttons: the toolbar file is closed, rewritten and opened again where it was (no second toolbar)");
                Check(Skunk.UI.SkunkToolbar.FileId(Bytes(stable)) == rhinoId && Skunk.UI.SkunkToolbar.Buttons(Bytes(stable)) == Skunk.UI.SkunkToolbar.Buttons(v2) &&
                      said != null && said.Contains("this version's buttons"), "...with the new buttons and the file id Rhino's layout knows it by");

                // an older package's own toolbar file open as well (up to 1.6.5 every package carried one): it is closed,
                // Skunk's toolbar file kept open and untouched
                string oldOwn = System.IO.Path.Combine(root, "old", "1.6.5", "Skunk.rui");
                host.Closed.Clear();
                host.ShowAsked.Clear();
                host.Opened.Add(oldOwn);
                said = Skunk.UI.SkunkToolbar.Ensure(Rhp("1.6.7"), v2, host, record);
                Check(host.Closed.Count == 1 && Same(host.Closed[0], oldOwn) && host.Opened.Count == 1 && Same(host.Opened[0], stable) && host.ShowAsked.Count == 0,
                      "an older package's own toolbar file is closed; Skunk's stays open, untouched");

                // the user closed the toolbar file: the next session opens and shows it again
                host.Opened.Clear();
                host.Closed.Clear();
                host.ShowAsked.Clear();
                said = Skunk.UI.SkunkToolbar.Ensure(Rhp("1.6.7"), v2, host, record);
                Check(said == "the Skunk toolbar is back." && host.Opened.Count == 1 && Same(host.Opened[0], stable) && host.Closed.Count == 0 &&
                      host.ShowAsked.Count == 1 && host.ShowAsked[0], "no toolbar file open: it is opened and shown, nothing written");

                // Rhino refuses to open it: Skunk says where the file is
                host.Opened.Clear();
                host.Refuse = true;
                said = Skunk.UI.SkunkToolbar.Ensure(Rhp("1.6.7"), v2, host, record);
                Check(said != null && said.Contains(stable) && said.Contains("Toolbars > File > Open"), "a toolbar Rhino did not open: Skunk names the file to open");
                host.Refuse = false;

                var plan = Skunk.UI.SkunkToolbar.Plan(true, true, new[] { oldOwn, stable }, stable);
                Check(!plan.Write && !plan.Open && plan.Close.Count == 1 && Same(plan.Close[0], oldOwn),
                      "two open files hold the Skunk toolbar: the other is closed, Skunk's kept");
                plan = Skunk.UI.SkunkToolbar.Plan(true, false, new[] { stable }, stable);
                Check(plan.Write && plan.Open && plan.Close.Count == 1 && Same(plan.Close[0], stable),
                      "new buttons: Skunk's own file is closed before it is rewritten (Rhino writes an open toolbar back when it closes)");

                // a development build: the toolbar file beside the plug-in, written and opened when nothing holds the group
                string dev = System.IO.Path.Combine(root, "dev", "build");
                System.IO.Directory.CreateDirectory(dev);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dev, "Skunk.rhp"), "plug-in");
                var devHost = new FakeToolbarHost();
                var devRecord = new Skunk.UI.MemoryToolbarRecord();
                said = Skunk.UI.SkunkToolbar.Ensure(System.IO.Path.Combine(dev, "Skunk.rhp"), v1, devHost, devRecord);
                Check(Bytes(System.IO.Path.Combine(dev, "Skunk.rui")).SequenceEqual(v1) && devHost.Opened.Count == 1 &&
                      Same(devHost.Opened[0], System.IO.Path.Combine(dev, "Skunk.rui")) && devRecord.Value == null,
                      "a development build writes its toolbar beside the plug-in and opens it");
            }
            finally
            {
                try
                {
                    System.IO.Directory.Delete(root, true);
                }
                catch (System.IO.IOException)
                {
                }
            }
        }

        /// <summary>
        /// The annotation scale and Rhino's annotation scaling. With Rhino's model space and layout space scaling on
        /// (every Rhino 9 template) Skunk's style sizes are paper sizes in page units - a layout detail prints them at
        /// that size at any detail scale (measured with Rhino's own detail scale, GetDimensionScale) - and each Skunk
        /// style's model space scale is the annotation scale in model units per page unit, so model views show them at
        /// the annotation scale. The document's own text scale makes no difference. Models of their own (a feet model
        /// with inch pages, as Rhino's feet template has), disposed after; then every Skunk style of the main test model.
        /// </summary>
        static void AnnotationScaleTests(RhinoDoc main)
        {
            RhinoDoc Model(UnitSystem units, UnitSystem page, bool modelScaling, bool layoutScaling)
            {
                var d = RhinoDoc.Create(null);
                d.AdjustModelUnitSystem(units, false);
                d.AdjustPageUnitSystem(page, false);
                d.ModelSpaceAnnotationScalingEnabled = modelScaling;
                d.LayoutSpaceAnnotationScalingEnabled = layoutScaling;
                d.ModelSpaceTextScale = 1;
                SkunkService.Instance.ProcessNow(d);
                return d;
            }
            GridType lastType = null;
            TextEntity Bubble(RhinoDoc d, string name)
            {
                Guid id;
                using (SkunkService.Suppress())
                    id = GridElement.Create(new UpdateContext(d), "", name, new Line(Point3d.Origin, new Point3d(0, 60, 0)), Guid.Empty, false, true);
                SkunkService.Instance.ProcessNow(d);
                var info = GridElement.Info(d, d.Objects.FindId(id));
                lastType = info?.Type;
                return info != null && info.Data.Heads.TryGetValue(GridElement.BubbleKey(2), out var head) ? ElementIO.Find(d, head)?.Geometry as TextEntity : null;
            }
            DimensionStyle StyleOf(RhinoDoc d, AnnotationBase a) => a == null ? null : d.DimStyles.FindId(a.DimensionStyleId);
            void Rescale(RhinoDoc d, double scale)
            {
                var s = DocStore.Settings(d);
                s.AnnotationScale = scale;
                DocStore.SaveSettings(d, s);
                SkunkService.Instance.ContextChanged(d);
                SkunkService.Instance.ProcessNow(d);
            }
            var made = new System.Collections.Generic.List<RhinoDoc>();
            try
            {
                var feet = Model(UnitSystem.Feet, UnitSystem.Inches, true, true);
                made.Add(feet);
                Check(Math.Abs(DocUnits.AnnotationScale(feet) - 96) < 1e-9 && DocUnits.RhinoScalesAnnotation(feet),
                      "a feet model like Rhino's template (inch pages, model space and layout scaling on): Skunk's scale is 1/8\" = 1'-0\" and Rhino scales it");
                var bubble = Bubble(feet, "1");
                var bs = StyleOf(feet, bubble);
                Check(bs != null && Math.Abs(bs.TextHeight - 0.1875) < 1e-9 && Math.Abs(bs.DimensionScale - 8) < 1e-9 && Math.Abs(bubble.DimensionScale - 8) < 1e-9,
                      "a grid bubble's style holds its 3/16\" text as a paper size in page units (0.1875) and model space scale 8 (96 / 12 feet per inch)");
                Check(bs != null && Math.Abs(bs.TextHeight * bs.DimensionScale - 1.5) < 1e-9,
                      "...so model views show the text 1'-6\" tall: 3/16\" at 1/8\" = 1'-0\", readable without zooming in");

                // a model saved by an earlier build (its styles in feet at model space scale 96) is put right when it opens: a full update
                var saved = StyleOf(feet, bubble);
                var old = saved.Duplicate();
                old.DimensionScale = 96;
                old.TextHeight = 0.015625;
                feet.DimStyles.Modify(old, saved.Index, true);
                SkunkService.Instance.FullUpdate(feet);
                SkunkService.Instance.ProcessNow(feet);
                var fixedStyle = StyleOf(feet, bubble);
                Check(Math.Abs(fixedStyle.DimensionScale - 8) < 1e-9 && Math.Abs(fixedStyle.TextHeight - 0.1875) < 1e-9,
                      "an older model's Skunk styles (feet sizes at model space scale 96, as the first 1.6.4 build left them) are put right when the model opens");

                feet.ModelSpaceTextScale = 96;
                SkunkService.Instance.ContextChanged(feet);
                SkunkService.Instance.ProcessNow(feet);
                Check(Math.Abs(StyleOf(feet, Bubble(feet, "2")).DimensionScale - 8) < 1e-9, "the document's own text scale makes no difference (Rhino does not draw styles with it)");
                Rescale(feet, 48);
                var quarter = StyleOf(feet, Bubble(feet, "3"));
                Check(quarter != null && Math.Abs(quarter.DimensionScale - 4) < 1e-9 && Math.Abs(quarter.TextHeight - 0.1875) < 1e-9,
                      "1/4\" = 1'-0\" chosen in SkSettings: model space scale 4, the paper size unchanged (text 9\" tall in model views)");

                var mm = Model(UnitSystem.Millimeters, UnitSystem.Millimeters, true, true);
                made.Add(mm);
                var ms = StyleOf(mm, Bubble(mm, "A"));
                Check(ms != null && lastType != null && Math.Abs(ms.DimensionScale - 100) < 1e-9 && Math.Abs(ms.TextHeight - lastType.TextSizeMM) < 1e-9,
                      "a millimetre model with millimetre pages: 1:100 is model space scale 100 (as Rhino's Millimeter Architectural), the text its paper size in mm (" +
                      ms?.TextHeight.ToString("0.###") + ")");
                var m = Model(UnitSystem.Meters, UnitSystem.Millimeters, true, true);
                made.Add(m);
                var mst = StyleOf(m, Bubble(m, "A"));
                Check(mst != null && lastType != null && Math.Abs(mst.DimensionScale - 0.1) < 1e-9 && Math.Abs(mst.TextHeight - lastType.TextSizeMM) < 1e-9,
                      "a metre model with millimetre pages: 1:100 is model space scale 0.1 (as Rhino's Meters Small), the text its paper size in mm (" +
                      mst?.TextHeight.ToString("0.###") + ")");

                var off = Model(UnitSystem.Feet, UnitSystem.Inches, false, false);
                made.Add(off);
                var os = StyleOf(off, Bubble(off, "1"));
                Check(os != null && Math.Abs(os.DimensionScale - 1) < 1e-9 && Math.Abs(os.TextHeight - 1.5) < 1e-9,
                      "Rhino's scaling off: model space scale 1, Skunk puts the scale in the sizes (3/16\" text 1'-6\" tall)");
                var half = Model(UnitSystem.Feet, UnitSystem.Inches, true, false);
                made.Add(half);
                var hst = StyleOf(half, Bubble(half, "1"));
                Check(!DocUnits.RhinoScalesAnnotation(half) && hst != null && Math.Abs(hst.DimensionScale - 1) < 1e-9 && Math.Abs(hst.TextHeight - 1.5) < 1e-9,
                      "model space scaling on but layout scaling off: Skunk scales the sizes itself too (layouts would not scale page sizes)");
            }
            finally
            {
                foreach (var d in made)
                    d.Dispose();
            }

            // the main test model: every Skunk style carries the model space scale for its units
            double want = DocUnits.StyleModelSpaceScale(main);
            var styles = main.DimStyles.Where(st => !st.IsDeleted && st.Name.StartsWith(Keys.DimStylePrefix, StringComparison.Ordinal)).ToList();
            var wrong = styles.Where(st => Math.Abs(st.DimensionScale - want) > 1e-9).Select(st => st.Name + " " + st.DimensionScale).ToList();
            Check(styles.Count > 5 && wrong.Count == 0, "all " + styles.Count + " Skunk styles of the test model have model space scale " + want.ToString("0.#####") +
                                                        (wrong.Count == 0 ? "" : " - not: " + string.Join(", ", wrong)));
        }

        /// <summary>
        /// What layout details print, in a run of its own (SkunkCheck print): headless Rhino finds a detail's scale only in
        /// the first model with layouts, so with the other test models open it would answer the style's own scale. With
        /// Rhino's annotation scaling (every Rhino 9 template) grid bubbles and level heads print at their type size at any
        /// detail scale - Rhino's detail scale (GetDimensionScale) times the style's page-unit sizes, at the detail's page /
        /// model ratio - and model views show them at the annotation scale. A feet model with inch pages, then a millimetre
        /// one with millimetre pages.
        /// </summary>
        static void PrintTests()
        {
            var cases = new (UnitSystem Units, UnitSystem Page, double Size, double PageW, double PageH, double Margin, (double Model, double Paper, string Label)[] Sheets)[]
            {
                (UnitSystem.Feet, UnitSystem.Inches, 60, 17, 11, 1,
                 new[] { (1.0, 0.125, "1/8\" = 1'-0\""), (1.0, 0.25, "1/4\" = 1'-0\""), (1.0, 0.0625, "1/16\" = 1'-0\"") }),
                (UnitSystem.Millimeters, UnitSystem.Millimeters, 18000, 420, 297, 20,
                 new[] { (100.0, 1.0, "1:100"), (50.0, 1.0, "1:50"), (200.0, 1.0, "1:200") }),
            };
            foreach (var c in cases)
            {
                var d = RhinoDoc.Create(null);
                try
                {
                    d.AdjustModelUnitSystem(c.Units, false);
                    d.AdjustPageUnitSystem(c.Page, false);
                    d.ModelSpaceAnnotationScalingEnabled = true;
                    d.LayoutSpaceAnnotationScalingEnabled = true;
                    SkunkService.Instance.ProcessNow(d);
                    Guid grid, level;
                    double s = c.Size;
                    var footprint = new Polyline(new[] { new Point3d(0, 0, 0), new Point3d(s, 0, 0), new Point3d(s, s * 0.75, 0), new Point3d(0, s * 0.75, 0), new Point3d(0, 0, 0) });
                    using (SkunkService.Suppress())
                    {
                        grid = GridElement.Create(new UpdateContext(d), "", "1", new Line(Point3d.Origin, new Point3d(0, s, 0)), Guid.Empty, false, true);
                        level = LevelElement.Create(d, "", "Level 1", s / 6, footprint);
                    }
                    SkunkService.Instance.FullUpdate(d);
                    SkunkService.Instance.ProcessNow(d);
                    var gi = GridElement.Info(d, d.Objects.FindId(grid));
                    var bubble = (TextEntity)ElementIO.Find(d, gi.Data.Heads[GridElement.BubbleKey(2)]).Geometry;
                    var bs = d.DimStyles.FindId(bubble.DimensionStyleId);
                    var li = LevelElement.Info(d, d.Objects.FindId(level));
                    var head = (Leader)d.Objects.FindId(li.Data.Heads.Values.First()).Geometry;
                    var hs = d.DimStyles.FindId(head.DimensionStyleId);
                    double mmToPage = RhinoMath.UnitScale(UnitSystem.Millimeters, c.Page);
                    string unit = c.Page == UnitSystem.Inches ? "\"" : " mm";
                    Near(bs.TextHeight * bs.DimensionScale, DocUnits.PaperToModel(d, gi.Type.TextSizeMM), 1e-9,
                         c.Units + " model: model views show a grid bubble's name at the annotation scale (style size times its model space scale " +
                         bs.DimensionScale.ToString("0.###") + ")");
                    foreach (var sheet in c.Sheets)
                    {
                        var page = d.Views.AddPageView("Skunk " + sheet.Model + "-" + sheet.Paper, c.PageW, c.PageH);
                        var det = page?.AddDetailView("Plan", new Point2d(c.Margin, c.Margin), new Point2d(c.PageH - c.Margin, c.PageH - c.Margin),
                                                      Rhino.Display.DefinedViewportProjection.Top);
                        if (det == null)
                        {
                            Check(false, "a layout with a detail in a headless " + c.Units + " model");
                            break;
                        }
#pragma warning disable CS0618
                        det.DetailGeometry.SetScale(sheet.Model, c.Units, sheet.Paper, c.Page);
#pragma warning restore CS0618
                        det.CommitChanges();
                        det = (DetailViewObject)d.Objects.FindId(det.Id);
                        double factor = AnnotationBase.GetDimensionScale(d, bs, det.Viewport);
                        double Printed(DimensionStyle st, double length) =>
                            length * AnnotationBase.GetDimensionScale(d, st, det.Viewport) * det.DetailGeometry.PageToModelRatio * RhinoMath.UnitScale(c.Units, c.Page);
                        string at = c.Units + " model, detail at " + sheet.Label + " (Rhino's detail scale " + factor.ToString("0.###") + "): ";
                        Near(Printed(bs, bs.TextHeight), gi.Type.TextSizeMM * mmToPage, 1e-9, at + "a grid bubble's name prints at its type size (" + unit.Trim() + ")");
                        Near(Printed(bs, bs.TextHeight + 2 * bs.MaskOffset), gi.Type.BubbleSizeMM * mmToPage, 1e-9, at + "...and the bubble across");
                        Near(Printed(hs, hs.TextHeight), li.Type.TextSizeMM * mmToPage, 1e-9, at + "a level head's text");
                        Near(Printed(hs, hs.LeaderArrowLength), li.Type.SymbolSizeMM * mmToPage, 1e-9, at + "...and its symbol");
                    }
                }
                finally
                {
                    d.Dispose();
                }
            }
        }

        /// <summary>Stands in for Rhino's toolbar files in <see cref="ToolbarPackageTests"/>.</summary>
        sealed class FakeToolbarHost : Skunk.UI.IToolbarHost
        {
            public readonly System.Collections.Generic.List<string> Opened = new System.Collections.Generic.List<string>();
            public readonly System.Collections.Generic.List<string> Closed = new System.Collections.Generic.List<string>();
            public bool Refuse;
            /// <summary>The Skunk group's own container shows (the user may have it as a tab elsewhere, or hidden).</summary>
            public bool Visible;
            /// <summary>Whether each open asked to show the group's own container.</summary>
            public readonly System.Collections.Generic.List<bool> ShowAsked = new System.Collections.Generic.List<bool>();

            public System.Collections.Generic.IList<string> OpenFiles() => Opened.ToList();

            public void Close(string path)
            {
                Closed.Add(path);
                Opened.RemoveAll(p => Skunk.UI.SkunkToolbar.SamePath(p, path));
            }

            public bool Open(string path, bool show)
            {
                if (Refuse)
                    return false;
                Opened.Add(path);
                ShowAsked.Add(show);
                if (show)
                    Visible = true;
                return true;
            }

            public bool Shown() => Opened.Count > 0 && Visible;
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
            Guid datumBox;
            using (SkunkService.Suppress())
                datumBox = DatumBoxElement.Create(new UpdateContext(doc), LevelElement.Info(doc, doc.Objects.FindId(level)).Footprint, new[] { level });
            var attached = doc.Objects.AddBrep(new Box(Plane.WorldXY, new Interval(40, 42), new Interval(0, 2), new Interval(10, 12)).ToBrep());
            LevelElement.Attach(doc, doc.Objects.FindId(attached), level);
            var sd = Slope(doc, slope);
            sd.SlopeTop = 2;
            sd.SlopeBottom = 1;
            ElementIO.Write(doc, doc.Objects.FindId(slope), ElementKind.SpotSlope, sd);
            Guid unitGrid;
            using (SkunkService.Suppress())
                unitGrid = GridElement.Create(new UpdateContext(doc), "", null, new Line(new Point3d(60, -10, 0), new Point3d(60, 30, 0)));
            var all = new[] { box, ramp, spot, slope, level, attached, datumBox, unitGrid };
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
            Check(ElementIO.Read<LevelData>(doc.Objects.FindId(level)).Box == datumBox, "the level is still in its datum box");
            {
                var gi = GridElement.Info(doc, doc.Objects.FindId(unitGrid));
                var bubble = ElementIO.Find(doc, gi.Data.Heads[GridElement.BubbleKey(2)]);
                Check(gi.Line.From.DistanceTo(new Point3d(720, -120, 0)) < 1e-6 && gi.Line.To.DistanceTo(new Point3d(720, 360, 0)) < 1e-6 &&
                      GridElement.Center(bubble.Geometry).DistanceTo(gi.Line.To) < 1e-6, "a grid was scaled once, its bubble still on its end");
            }
            Near(ElementIO.Read<DatumBoxData>(doc.Objects.FindId(datumBox)).Top, 120 + DatumBoxElement.Margin(doc), 1e-6, "the datum box still reaches 1 m over the level, now in inches");

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

        static void SideViewSlopeTests(RhinoDoc doc)
        {
            const string type = "Slope Arrow (Percent)";
            var percent = TypeTable.Get(doc).Find<SpotSlopeType>(type);
            Check(percent != null && percent.Placement == SlopePlacement.OnHost, "the slope type follows its host (placement On host)");
            // Rhino's Front CPlane (normal -Y, toward the viewer) and Right CPlane (normal +X)
            var front = new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis);
            var right = new Plane(Point3d.Origin, Vector3d.YAxis, Vector3d.ZAxis);
            LinearDimension Dim(Guid id) => doc.Objects.FindId(id)?.Geometry as LinearDimension;
            bool InPlane(Guid id, Vector3d normal) => Dim(id) != null && Math.Abs(Dim(id).Plane.ZAxis * normal) > 0.9999;
            Guid Place(Guid host, Point3d point, Plane plane)
            {
                using (SkunkService.Suppress())
                    return SpotElement.Create(doc, ElementKind.SpotSlope, type, doc.Objects.FindId(host), point, plane, null, true, true, Guid.Empty);
            }

            // a 20% profile drawn in an elevation, its slope placed in the Front view
            var profile = doc.Objects.AddLine(new Point3d(800, 0, 0), new Point3d(810, 0, 2));
            var f = Place(profile, new Point3d(805, 0, 1), front);
            Check(f != Guid.Empty && SpotElement.PlacedInSideView(Slope(doc, f)), "a slope placed in the Front view knows it was placed in a side view");
            Check(InPlane(f, Vector3d.YAxis), "its arrow and text lie in the Front CPlane, so the Front view shows them (plane normal " + Dim(f)?.Plane.ZAxis + ")");
            var along = new Vector3d(10, 0, 2);
            along.Unitize();
            Check(Dim(f) != null && Math.Abs(Dim(f).Plane.XAxis * along) > 0.9999, "the arrow runs along the profile");
            Eq(Dim(f)?.PlainUserText, "20.00%", "and reads its slope");
            var row = Skunk.UI.PropertyModel.Spots(doc, ElementKind.SpotSlope, new System.Collections.Generic.List<RhinoObject> { doc.Objects.FindId(f) })
                .FirstOrDefault(r => r.Name == "Placement");
            Eq(row?.Text, "Side view: in the CPlane it was placed in", "the panel says why it lies there");

            // the same profile in plan: it follows the host as before (a plane the Front view sees edge-on)
            var p = Place(profile, new Point3d(803, 0, 0.6), Plane.WorldXY);
            Check(!SpotElement.PlacedInSideView(Slope(doc, p)) && Dim(p) != null && Math.Abs(Dim(p).Plane.ZAxis * Vector3d.YAxis) < 1e-6,
                  "placed in plan, it follows the host as before (its plane contains the Front view's line of sight)");

            // moved along in the elevation, it stays in the Front CPlane and on the profile
            doc.Objects.Transform(f, Transform.Translation(2, 0, 0.4), true);
            SkunkService.Instance.Touch(doc, new[] { f });
            SkunkService.Instance.ProcessNow(doc);
            Check(InPlane(f, Vector3d.YAxis) && Math.Abs(Location(doc, f).X - 807) < 1e-6, "moved along the profile, it stays in the Front CPlane");
            Eq(Dim(f)?.PlainUserText, "20.00%", "...still reading 20%");

            // a 10% profile in the Right view
            var side = doc.Objects.AddLine(new Point3d(900, 0, 0), new Point3d(900, 10, 1));
            var r = Place(side, new Point3d(900, 5, 0.5), right);
            Check(InPlane(r, Vector3d.XAxis), "a slope placed in the Right view lies in the Right CPlane");
            Eq(Dim(r)?.PlainUserText, "10.00%", "...and reads 10%");

            // a roof seen from the side: 20% along X, placed in the Front view
            var roof = doc.Objects.AddBrep(Brep.CreateFromCornerPoints(new Point3d(1000, 0, 0), new Point3d(1010, 0, 2), new Point3d(1010, 10, 2), new Point3d(1000, 10, 0), doc.ModelAbsoluteTolerance));
            var s = Place(roof, new Point3d(1005, 5, 1), front);
            Check(InPlane(s, Vector3d.YAxis), "on a roof seen from the side, the arrow and text lie in the Front CPlane");
            Eq(Dim(s)?.PlainUserText, "20.00%", "...and read the roof's 20%");

            // up or down off the host line: by default just above it; moved up or down it stays where it was put
            var upLine = new Vector3d(-2, 0, 10);   // across the 20% profile, upward
            upLine.Unitize();
            double Above(Point3d pt) => (pt - new Point3d(800, 0, 0)) * upLine;
            double ArrowAbove(Guid id)
            {
                var d = Dim(id);
                if (d == null || !d.Get3dPoints(out Point3d d1, out Point3d d2, out _, out _, out _, out _))
                    return double.NaN;
                return (Above(d1) + Above(d2)) / 2;
            }
            double typeOffset = DocUnits.PaperToModel(doc, percent.OffsetFromModelMM);
            var u = Place(profile, new Point3d(802, 0, 0.4), front);
            Near(ArrowAbove(u), typeOffset, 1e-6, "a side-view arrow sits above its host line by the type's offset");
            Check(Dim(u) != null && Dim(u).Plane.YAxis * Vector3d.ZAxis > 0, "...its text on the far side of the arrow from the line (the dimension plane's Y points up)");
            doc.Objects.Transform(u, Transform.Translation(0, 0, 3), true);
            SkunkService.Instance.Touch(doc, new[] { u });
            SkunkService.Instance.ProcessNow(doc);
            double raised = typeOffset + 3 * (Vector3d.ZAxis * upLine);
            Near(ArrowAbove(u), raised, 1e-6, "moved up 3', it stays up (" + ArrowAbove(u).ToString("0.###") + "' above the line) instead of snapping back");
            Check(InPlane(u, Vector3d.YAxis) && Dim(u)?.PlainUserText == "20.00%", "...in the Front CPlane, still reading 20%");
            Check(Slope(doc, u).SlopeOffsetMM.HasValue && Math.Abs(DocUnits.PaperToModel(doc, Slope(doc, u).SlopeOffsetMM.Value) - raised) < 1e-6,
                  "...that distance kept as its offset (in paper mm, so it holds at any scale)");
            var arrowPanel = Skunk.UI.PropertyModel.Spots(doc, ElementKind.SpotSlope, new System.Collections.Generic.List<RhinoObject> { doc.Objects.FindId(u) });
            var offsetRow = arrowPanel.FirstOrDefault(x => x.Name == "Offset From Host");
            Eq(offsetRow?.Text, Skunk.Commands.Kit.Length(doc, raised), "the panel shows the offset");

            doc.Objects.Transform(u, Transform.Translation(0, 0, -6), true);
            SkunkService.Instance.Touch(doc, new[] { u });
            SkunkService.Instance.ProcessNow(doc);
            Check(ArrowAbove(u) < 0 && Dim(u) != null && Dim(u).Plane.YAxis * Vector3d.ZAxis < 0,
                  "moved below the line it stays below (" + ArrowAbove(u).ToString("0.###") + "'), its text below the arrow, away from the line");
            uint serial = doc.Objects.FindId(u).RuntimeSerialNumber;
            SkunkService.Instance.ContextChanged(doc);
            SkunkService.Instance.ProcessNow(doc);
            Check(doc.Objects.FindId(u).RuntimeSerialNumber == serial && Dim(u).Plane.YAxis * Vector3d.ZAxis < 0 && ArrowAbove(u) < 0,
                  "...and the next update leaves it as it is (not redrawn, nothing flips back)");

            Check(offsetRow?.OnText != null && offsetRow.OnText("1'"), "an offset typed in the panel...");
            Near(ArrowAbove(u), 1, 1e-6, "...puts the arrow 1' above the line");
            var flip = Slope(doc, u);
            flip.FlipSlope = true;
            ElementIO.Write(doc, doc.Objects.FindId(u), ElementKind.SpotSlope, flip);
            SkunkService.Instance.ContextChanged(doc);
            SkunkService.Instance.ProcessNow(doc);
            Near(ArrowAbove(u), 1, 1e-6, "flipped to point uphill, it stays on its side of the line");
            Check(offsetRow.OnText(""), "the offset cleared in the panel...");
            Near(ArrowAbove(u), typeOffset, 1e-6, "...goes back to the type's");
        }

        static void GoToLevelTests()
        {
            var levels = new System.Collections.Generic.List<LevelInfo>
            {
                new LevelInfo { Name = "Level 1", Z = 0 }, new LevelInfo { Name = "Level 2", Z = 12 }, new LevelInfo { Name = "Roof", Z = 24 },
            };
            Check(Skunk.Commands.DatumCommands.CurrentLevel(levels, 12, 0.001) == 1, "Go To Level selects the level the CPlane is at");
            Check(Skunk.Commands.DatumCommands.CurrentLevel(levels, 5, 0.001) == 0, "...and the first level when the CPlane is at none");
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

        /// <summary>
        /// Level heads and level lines print at their type size in every view and detail, whatever the scale
        /// or the model units: a head's symbol, tail (the leader's landing), gap and text are dimension style
        /// lengths that Rhino scales together, and Skunk Datum is a paper pattern like Rhino's own linetypes.
        /// </summary>
        static void PrintSizeTests()
        {
            const double mmPerFoot = 304.8;
            // Rhino keeps linetype patterns in paper millimetres, so Skunk Datum is the same in every unit system
            foreach (var units in new[] { UnitSystem.Feet, UnitSystem.Inches, UnitSystem.Millimeters, UnitSystem.Meters })
            {
                var d = RhinoDoc.Create(null);
                d.AdjustModelUnitSystem(units, false);
                Check(Layers.FindLinetype(d, Keys.LinetypeDatum) == null && d.Linetypes.FindName(Keys.LinetypeDatum)?.Index == -1,
                    "a new " + units + " model has no Skunk Datum (and Rhino's FindName answers Continuous, index -1, anyway)");
                Styles.EnsureDatumLinetype(d);
                var lt = Layers.FindLinetype(d, Keys.LinetypeDatum);
                if (lt == null)
                {
                    Check(false, "Skunk Datum added to a new " + units + " model");
                    d.Dispose();
                    continue;
                }
                var segments = Enumerable.Range(0, lt.SegmentCount).Select(i => { lt.GetSegment(i, out double l, out bool solid); return (l, solid); }).ToArray();
                Check(segments.Length == 4 && Math.Abs(segments[0].l - 19.05) < 1e-6 && segments[0].solid && Math.Abs(segments[1].l - 3.175) < 1e-6 && !segments[1].solid &&
                      Math.Abs(segments[2].l - 3.175) < 1e-6 && segments[2].solid && Math.Abs(segments[3].l - 3.175) < 1e-6 && !segments[3].solid && !lt.AlwaysModelDistances,
                      "Skunk Datum prints 3/4\" dash, 1/8\" gap, 1/8\" dash, 1/8\" gap with a model in " + units + ": '" + lt.PatternString(false) + "' in");
                d.Dispose();
            }

            var doc = RhinoDoc.Create(null);
            doc.AdjustModelUnitSystem(UnitSystem.Feet, false);
            doc.ModelAbsoluteTolerance = 0.001;
            doc.AdjustPageUnitSystem(UnitSystem.Inches, false);
            doc.ModelSpaceAnnotationScalingEnabled = true;
            doc.LayoutSpaceAnnotationScalingEnabled = true;
            // a model saved with the old Skunk Datum (model units times the 1/8" scale: a 6 mm dash on paper)
            var old = new Linetype { Name = Keys.LinetypeDatum };
            double u = DocUnits.FromMeters(doc, 0.001) * 96;
            old.AppendSegment(19.05 * u, true);
            old.AppendSegment(3.175 * u, false);
            old.AppendSegment(3.175 * u, true);
            old.AppendSegment(3.175 * u, false);
            doc.Linetypes.Add(old);
            Near(doc.Linetypes.LinetypeScale, 1, 1e-12, "a new model starts at Rhino's linetype display scale 1");
            var footprint = new Polyline(new[] { new Point3d(0, 0, 0), new Point3d(40, 0, 0), new Point3d(40, 30, 0), new Point3d(0, 30, 0), new Point3d(0, 0, 0) });
            Guid id;
            using (SkunkService.Suppress())
                id = LevelElement.Create(doc, "", "Level 1", 10, footprint);
            SkunkService.Instance.FullUpdate(doc);   // as when the model is opened
            SkunkService.Instance.ProcessNow(doc);
            var datum = Layers.FindLinetype(doc, Keys.LinetypeDatum);
            Check(Styles.IsDatumPattern(datum), "the old Skunk Datum is back to the paper pattern: '" + datum?.PatternString(true) + "' mm");
            Check(doc.Layers[Layers.LevelsLayerIndex(doc)].LinetypeIndex == datum?.Index, "the Levels layer draws Skunk Datum");
            Near(doc.Linetypes.LinetypeScale, 96, 1e-9, "model views show linetypes at the annotation scale (1/8\" = 1'-0\")");

            // a model where Skunk Datum was never added (1.4.1 and earlier): its Continuous Levels layer gets it, once
            {
                var d = RhinoDoc.Create(null);
                d.AdjustModelUnitSystem(UnitSystem.Feet, false);
                Layers.Ensure(d, Keys.LevelsLayer, System.Drawing.Color.Gray);
                SkunkService.Instance.FullUpdate(d);
                SkunkService.Instance.ProcessNow(d);
                var dl = Layers.FindLinetype(d, Keys.LinetypeDatum);
                int li = Layers.LevelsLayerIndex(d);
                Check(dl != null && Styles.IsDatumPattern(dl) && d.Layers[li].LinetypeIndex == dl.Index, "a Levels layer left Continuous gets Skunk Datum when the model opens");
                var layer = d.Layers[li];
                layer.LinetypeIndex = -1;
                d.Layers.Modify(layer, li, true);
                SkunkService.Instance.FullUpdate(d);
                SkunkService.Instance.ProcessNow(d);
                Check(d.Layers[li].LinetypeIndex == -1, "...once: Continuous chosen afterwards stays");
                d.Dispose();
            }

            var ctx = new UpdateContext(doc);
            var info = LevelElement.Info(doc, doc.Objects.FindId(id));
            var type = info.Type;
            double stub = LevelElement.HeadStub(ctx);
            Check(info.Data.Heads.Count == 2, "the level has its two default heads");
            void HeadsArePaperSized(string when, double styleScale)
            {
                info = LevelElement.Info(doc, doc.Objects.FindId(id));
                foreach (var kv in info.Data.Heads)
                {
                    var leader = (Leader)doc.Objects.FindId(kv.Value).Geometry;
                    var style = doc.DimStyles.FindId(leader.DimensionStyleId);
                    var pts = leader.Points3D;
                    bool stubOk = pts.Length == 2 && pts[0].DistanceTo(pts[1]) <= stub + 1e-12;
                    bool landing = leader.LeaderHasLanding && style.LeaderHasLanding;
                    // style lengths per paper millimetre: page units when Rhino scales annotation, else model units at the scale
                    double unit = DocUnits.RhinoScalesAnnotation(doc) ? RhinoMath.UnitScale(UnitSystem.Millimeters, doc.PageUnitSystem) : styleScale / mmPerFoot;
                    double tail = Styles.HeadTailMM(type) * unit, symbol = type.SymbolSizeMM * unit, text = type.TextSizeMM * unit;
                    bool sizes = Math.Abs(style.LeaderLandingLength - tail) < 1e-9 && Math.Abs(style.LeaderArrowLength - symbol) < 1e-9 && Math.Abs(style.TextHeight - text) < 1e-9;
                    bool scale = Math.Abs(style.DimensionScale - DocUnits.StyleModelSpaceScale(doc)) < 1e-9 && Math.Abs(leader.DimensionScale - style.DimensionScale) < 1e-9;
                    // Rhino lays the whole head out in the style's units at one scale per view or detail (Explode does not:
                    // it scales the lines and not the text): the text starts at the landing's end plus the gap
                    double textStarts = style.LeaderLandingLength + style.TextGap;
                    Check(stubOk && landing && sizes && scale && textStarts >= symbol * 0.5 - 1e-12,
                        when + ", head " + kv.Key + ": stub segment " + stubOk + ", tail is the landing " + landing + ", style sizes on paper " + sizes +
                        ", one model space scale " + scale + ", text starts " + (textStarts / unit).ToString("0.##") + " mm out (paper), past the symbol's " +
                        (type.SymbolSizeMM * 0.5).ToString("0.##") + " mm radius");
                }
            }
            HeadsArePaperSized("Rhino scaling at 1:96", 1);

            // a head drawn the old way (a model-length tail, no landing) is redrawn when the model opens
            var southId = info.Data.Heads["0:2"];
            var south = (Leader)doc.Objects.FindId(southId).Geometry;
            var oldPts = new[] { south.Points3D[0], south.Points3D[0] + Vector3d.XAxis * DocUnits.PaperToModel(doc, type.HeadLengthMM) };
            var oldHead = Leader.Create(south.PlainText, south.Plane, doc.DimStyles.FindId(south.DimensionStyleId), oldPts);
            oldHead.LeaderHasLanding = false;
            using (SkunkService.Suppress())
                doc.Objects.Replace(southId, oldHead);
            SkunkService.Instance.FullUpdate(doc);
            SkunkService.Instance.ProcessNow(doc);
            HeadsArePaperSized("after redrawing an old head", 1);

            // Skunk's scale changes without an edit of its own (no event - an Undo of SkSettings, say): Skunk notices,
            // heads keep their paper sizes and their style's model space scale follows, the north arrow (a block) is
            // resized, and model views follow
            Guid arrow;
            using (SkunkService.Suppress())
                arrow = NorthArrowElement.Create(doc, new Point3d(60, 60, 0), true, "");
            double arrow96 = ElementIO.Read<NorthArrowData>(doc.Objects.FindId(arrow)).Size;
            var southBefore = ((Leader)doc.Objects.FindId(LevelElement.Info(doc, doc.Objects.FindId(id)).Data.Heads["0:2"]).Geometry).Points3D;
            SkunkService.Instance.ProcessNow(doc);
            Check(!SkunkService.Instance.CheckAnnotationScale(doc), "no refresh while the scale stays the same");
            void SkunkScale(double scale)
            {
                var st = DocStore.Settings(doc);
                st.AnnotationScale = scale;
                DocStore.SaveSettings(doc, st);
            }
            SkunkScale(192);
            Check(SkunkService.Instance.CheckAnnotationScale(doc), "Skunk's scale changed (1:96 -> 1:192) is noticed");
            SkunkService.Instance.ProcessNow(doc);
            Check(!SkunkService.Instance.CheckAnnotationScale(doc), "...and refreshed once");
            HeadsArePaperSized("Rhino scaling at 1:192", 1);
            var headStyle = doc.DimStyles.FindId(((Leader)doc.Objects.FindId(LevelElement.Info(doc, doc.Objects.FindId(id)).Data.Heads["0:2"]).Geometry).DimensionStyleId);
            Near(headStyle.DimensionScale, 192 * RhinoMath.UnitScale(doc.PageUnitSystem, doc.ModelUnitSystem), 1e-9,
                 "the level heads' style carries the new model space scale (1:192 in model units per page unit: Rhino draws them 192 times their paper size in model views)");
            var southAfter = ((Leader)doc.Objects.FindId(LevelElement.Info(doc, doc.Objects.FindId(id)).Data.Heads["0:2"]).Geometry).Points3D;
            Check(southAfter[0].DistanceTo(southBefore[0]) < 1e-9 && southAfter[1].DistanceTo(southBefore[1]) < 1e-9, "head geometry does not depend on the scale");
            Near(ElementIO.Read<NorthArrowData>(doc.Objects.FindId(arrow)).Size, arrow96 * 2, 1e-9, "the north arrow follows 1:96 -> 1:192");
            Near(doc.Linetypes.LinetypeScale, 192, 1e-9, "model views follow the new scale");

            // a linetype display scale the user set stays theirs
            doc.Linetypes.LinetypeScale = 48;
            SkunkScale(96);
            SkunkService.Instance.CheckAnnotationScale(doc);
            SkunkService.Instance.ProcessNow(doc);
            Near(doc.Linetypes.LinetypeScale, 48, 1e-9, "a linetype display scale the user set is left alone");
            Check(!DocStore.Settings(doc).LinetypeScaleFollows, "...and Skunk stops looking after it (Settings can hand it back)");

            // with Rhino's scaling off, Skunk's own scale goes into the style: all parts of the head together
            doc.ModelSpaceAnnotationScalingEnabled = false;
            var settings = DocStore.Settings(doc);
            settings.AnnotationScale = 96;
            DocStore.SaveSettings(doc, settings);
            Check(SkunkService.Instance.CheckAnnotationScale(doc), "turning Rhino's annotation scaling off is noticed");
            SkunkService.Instance.ProcessNow(doc);
            HeadsArePaperSized("Skunk's own scale 1:96", 96);
            doc.Dispose();
        }

        /// <summary>New libraries fill level heads; an older model's stock project type follows once, a type of the user's own does not.</summary>
        static void FilledHeadTests()
        {
            var fresh = TypeLibrary.Defaults(true);
            Check(fresh.Level.Find(t => t.Name == TypeLibrary.StockProjectLevel)?.HeadSymbol == SymbolKind.TargetFilled && new LevelType().HeadSymbol == SymbolKind.TargetFilled,
                "level heads are filled by default");
            var doc = RhinoDoc.Create(null);
            doc.AdjustModelUnitSystem(UnitSystem.Feet, false);
            var older = TypeLibrary.Defaults(true);
            older.SymbolsVersion = 0;
            older.Level.Find(t => t.Name == TypeLibrary.StockProjectLevel).HeadSymbol = SymbolKind.Target;
            older.Level.Add(new LevelType { Name = "Office Hollow", HeadSymbol = SymbolKind.Target });
            TypeTable.Save(doc, older);
            var read = TypeTable.Get(doc);
            Check(read.Level.Find(t => t.Name == TypeLibrary.StockProjectLevel).HeadSymbol == SymbolKind.TargetFilled, "an older model's stock project level type (hollow target) is filled now");
            Check(read.Level.Find(t => t.Name == "Office Hollow").HeadSymbol == SymbolKind.Target, "a level type of the user's own keeps its hollow target");
            read.Level.Find(t => t.Name == TypeLibrary.StockProjectLevel).HeadSymbol = SymbolKind.Target;
            TypeTable.Save(doc, read);
            Check(TypeTable.Get(doc).Level.Find(t => t.Name == TypeLibrary.StockProjectLevel).HeadSymbol == SymbolKind.Target, "a hollow target chosen afterwards stays");
            doc.Dispose();
        }

        /// <summary>
        /// Level lines: the footprint shows in model views but does not print; one line per elevation direction
        /// with heads prints instead, so an elevation shows one dashed line. (Seen edge-on, the footprint's front
        /// and back edges print on top of each other with their patterns running opposite ways: almost solid.)
        /// </summary>
        static void LevelLineTests(RhinoDoc doc)
        {
            var footprint = new Polyline(new[] { new Point3d(0, 200, 0), new Point3d(40, 200, 0), new Point3d(40, 230, 0), new Point3d(0, 230, 0), new Point3d(0, 200, 0) });
            Guid id;
            using (SkunkService.Suppress())
                id = LevelElement.Create(doc, "", "Lines", 90, footprint);
            LevelInfo Info() => LevelElement.Info(doc, doc.Objects.FindId(id));
            RhinoObject LineObj(string key) => Info().Data.Lines.TryGetValue(key, out var g) ? ElementIO.Find(doc, g) : null;
            bool Runs(string key, Point3d a, Point3d b) => LineObj(key)?.Geometry is LineCurve lc && lc.PointAtStart.DistanceTo(a) < 1e-9 && lc.PointAtEnd.DistanceTo(b) < 1e-9;
            bool Prints(RhinoObject o) => !(o.Attributes.PlotWeightSource == ObjectPlotWeightSource.PlotWeightFromObject && o.Attributes.PlotWeight < 0);

            var level = doc.Objects.FindId(id);
            Check(!Prints(level), "the footprint shows in model views but does not print");
            Check(Info().Data.Lines.Count == 2, "one printed level line per head direction (south, east): " + Info().Data.Lines.Count);
            Check(Runs("0", new Point3d(0, 200, 90), new Point3d(40, 200, 90)), "the south line runs along the south edge at the level");
            Check(Runs("1", new Point3d(40, 200, 90), new Point3d(40, 230, 90)), "the east line runs along the east edge");
            var south = LineObj("0");
            Check(ElementIO.KindOf(south) == ElementKind.LevelLine && south.Attributes.LayerIndex == Layers.LevelsLayerIndex(doc) &&
                  south.Attributes.LinetypeSource == level.Attributes.LinetypeSource && south.Attributes.LinetypeIndex == level.Attributes.LinetypeIndex && Prints(south),
                  "a level line is on __Annotations__::Levels with the level's line pattern, and it prints");

            // heads on all four sides: north is the same elevation direction as south, so still two lines
            using (SkunkService.Suppress())
            {
                var o = doc.Objects.FindId(id);
                var d = ElementIO.Read<LevelData>(o);
                for (int e = 0; e < d.End2.Count; e++)
                    d.End2[e] = true;
                ElementIO.Write(doc, o, ElementKind.Level, d);
                LevelElement.Update(new UpdateContext(doc), doc.Objects.FindId(id), UpdateReason.Context);
            }
            Check(Info().Data.Heads.Count == 4 && Info().Data.Lines.Count == 2 && Runs("0", new Point3d(0, 200, 90), new Point3d(40, 200, 90)),
                "heads on all four sides: four heads, still one line per elevation direction");

            // a level line moved up moves the level; moved sideways it snaps back
            var lineId = Info().Data.Lines["0"];
            doc.Objects.Transform(lineId, Transform.Translation(0, 0, 2), true);
            SkunkService.Instance.Touch(doc, new[] { lineId });
            SkunkService.Instance.ProcessNow(doc);
            Near(Info().Z, 92, 1e-9, "a level line moved up 2' moves the level");
            Check(Runs("0", new Point3d(0, 200, 92), new Point3d(40, 200, 92)), "...and the line goes with it");
            doc.Objects.Transform(lineId, Transform.Translation(5, 3, 0), true);
            SkunkService.Instance.Touch(doc, new[] { lineId });
            SkunkService.Instance.ProcessNow(doc);
            Check(Runs("0", new Point3d(0, 200, 92), new Point3d(40, 200, 92)), "a level line moved sideways snaps back");

            // deleted, a level line comes back (it is part of the level); a copy of one is removed
            var deleted = Info().Data.Lines["0"];
            doc.Objects.Delete(deleted, true);
            using (SkunkService.Suppress())
                LevelElement.LineDeleted(new UpdateContext(doc), id, 0, deleted);
            Check(LineObj("0") != null && LineObj("0").Id != deleted && Runs("0", new Point3d(0, 200, 92), new Point3d(40, 200, 92)), "a deleted level line comes back");
            var copy = doc.Objects.Add(LineObj("0").Geometry.Duplicate(), LineObj("0").Attributes.Duplicate());
            SkunkService.Instance.TouchAdded(doc, new[] { copy });
            SkunkService.Instance.ProcessNow(doc);
            Check(ElementIO.Find(doc, copy) == null, "a copied level line is removed (lines come from their level)");

            // the level's own line pattern applies to its lines too
            using (SkunkService.Suppress())
            {
                var o = doc.Objects.FindId(id);
                var d = ElementIO.Read<LevelData>(o);
                d.LinePattern = "Hidden";
                ElementIO.Write(doc, o, ElementKind.Level, d);
                LevelElement.Update(new UpdateContext(doc), doc.Objects.FindId(id), UpdateReason.Context);
            }
            var hidden = Layers.FindLinetype(doc, "Hidden");
            Check(hidden != null && LineObj("0").Attributes.LinetypeIndex == hidden.Index && LineObj("1").Attributes.LinetypeIndex == hidden.Index, "a level's line pattern (Hidden) draws its lines");

            // deleting the level deletes its lines and heads
            var data = Info().Data;
            var parts = data.Lines.Values.Concat(data.Heads.Values).ToList();
            doc.Objects.Delete(id, true);
            using (SkunkService.Suppress())
                LevelElement.Deleted(new UpdateContext(doc), id, data);
            Check(parts.All(p => ElementIO.Find(doc, p) == null), "deleting the level deletes its lines and heads");
        }

        /// <summary>
        /// Datum boxes: one outline holds the extents of the levels in it. Rotate, stretch or move the box and they
        /// follow; a head dragged sideways (or a footprint edited in plan) takes its level out; the box fits the
        /// heights of its levels, goes back to its outline when it is made into something else, and never prints.
        /// </summary>
        static void DatumBoxTests(RhinoDoc doc)
        {
            var svc = SkunkService.Instance;
            void Run(params Guid[] ids)
            {
                svc.Touch(doc, ids);
                svc.ProcessNow(doc);
            }
            LevelInfo L(Guid id) => LevelElement.Info(doc, doc.Objects.FindId(id));
            Polyline Plan(Guid id) => LevelElement.Normalize(L(id).Footprint, 0);
            bool Same(Polyline x, Polyline y) => DatumBoxElement.SamePlan(x, y, 1e-6);
            double BoxZ(Guid id) => ((Curve)doc.Objects.FindId(id).Geometry).PointAtStart.Z;
            double BoxTop(Guid id) => ElementIO.Read<DatumBoxData>(doc.Objects.FindId(id)).Top;
            double margin = DatumBoxElement.Margin(doc);

            var fp = new Polyline(new[] { new Point3d(0, 300, 0), new Point3d(50, 300, 0), new Point3d(50, 340, 0), new Point3d(0, 340, 0), new Point3d(0, 300, 0) });
            Guid a, b;
            using (SkunkService.Suppress())
            {
                a = LevelElement.Create(doc, "", "Box A", 200, fp);
                b = LevelElement.Create(doc, "", "Box B", 212, fp);
            }

            // a box around levels that share their extents takes that outline: nothing moves
            var fit = DatumBoxElement.FitOutline(doc, new[] { L(a), L(b) });
            Check(Same(fit, LevelElement.Normalize(fp, 0)), "a box around levels with the same extents takes their outline");
            Guid box;
            using (SkunkService.Suppress())
                box = DatumBoxElement.Create(new UpdateContext(doc), fit, new[] { a, b });
            var bo = doc.Objects.FindId(box);
            Check(ElementIO.KindOf(bo) == ElementKind.DatumBox && L(a).Data.Box == box && L(b).Data.Box == box, "datum box created with both levels in it");
            Eq(bo.Attributes.Name, "Datum Box 1", "box name");
            Near(BoxZ(box), 200 - margin, 1e-9, "the box sits just under its lowest level");
            Near(BoxTop(box), 212 + margin, 1e-9, "its top (drawn on screen) is just over its highest level");
            Check(bo.Attributes.PlotWeightSource == ObjectPlotWeightSource.PlotWeightFromObject && bo.Attributes.PlotWeight < 0 &&
                  doc.Layers[bo.Attributes.LayerIndex].FullPath == Keys.RootLayer + "::" + Keys.DatumBoxesLayer, "the box is on __Annotations__::Datum Boxes and never prints");

            // rotate the box: both levels turn at their own elevations, heads and level lines with their sides
            var center = new Point3d(25, 320, 0);
            var turn = Transform.Rotation(RhinoMath.ToRadians(30), Vector3d.ZAxis, center);
            doc.Objects.Transform(box, turn, true);
            Run(box);
            var rotated = LevelElement.Normalize(fp, 0);
            rotated.Transform(turn);
            Check(Same(Plan(a), rotated) && Same(Plan(b), rotated), "rotating the box rotates both levels");
            Near(L(a).Z, 200, 1e-9, "level A keeps its elevation");
            Near(L(b).Z, 212, 1e-9, "level B keeps its elevation");
            var side = rotated[1] - rotated[0];
            side.Unitize();
            var facing = Vector3d.CrossProduct(side, Vector3d.ZAxis);
            var headPlane = (doc.Objects.FindId(L(a).Data.Heads["0:2"]).Geometry as Leader).Plane;
            Check((headPlane.ZAxis - facing).Length < 1e-6, "the heads turned with their sides");
            var levelLine = doc.Objects.FindId(L(a).Data.Lines["0"]).Geometry as LineCurve;
            Check(levelLine != null && Vector3d.CrossProduct(levelLine.Line.UnitTangent, side).Length < 1e-6, "...and so did the level lines");

            // stretch the box along its first side, as the gumball does: both levels stretch
            var frame = new Plane(center, side, Vector3d.CrossProduct(Vector3d.ZAxis, side));
            var stretch = Transform.Scale(frame, 1.5, 1, 1);
            doc.Objects.Transform(box, stretch, true);
            Run(box);
            var stretched = new Polyline(rotated);
            stretched.Transform(stretch);
            Check(Same(Plan(a), stretched) && Same(Plan(b), stretched), "stretching the box stretches both levels");
            Near(Plan(a)[0].DistanceTo(Plan(a)[1]), 75, 1e-6, "...to 75' along the side");

            // moved up, the box goes back under its lowest level; its levels stay
            doc.Objects.Transform(box, Transform.Translation(0, 0, 40), true);
            Run(box);
            Near(BoxZ(box), 200 - margin, 1e-9, "a box moved up goes back under its lowest level");
            Check(Same(Plan(a), stretched) && Same(Plan(b), stretched), "...and its levels stay as they were");

            // a level moved up stays in its box, and the box grows over it
            doc.Objects.Transform(b, Transform.Translation(0, 0, 6), true);
            Run(b);
            Near(L(b).Z, 218, 1e-9, "level B moved up 6'");
            Check(L(b).Data.Box == box && Same(Plan(b), stretched), "...and stays in its box");
            Near(BoxTop(box), 218 + margin, 1e-9, "the box grows to stay over it");

            // a new level on top (Previous extents) joins the box
            Guid c;
            using (SkunkService.Suppress())
                c = LevelElement.Create(doc, "", "Box C", 230, LevelElement.DefaultFootprint(doc, 230), DatumBoxElement.DefaultBox(doc));
            Check(L(c).Data.Box == box && Same(Plan(c), stretched), "a new level above joins the top level's box, with its outline");
            Near(BoxTop(box), 230 + margin, 1e-9, "...and the box grows over it");

            // level A's head dragged 5' outward along its side: A leaves the box, and that end moves with the head
            var pa = Plan(a);
            var along = pa[1] - pa[0];
            along.Unitize();
            var hid = L(a).Data.Heads["0:2"];
            doc.Objects.Transform(hid, Transform.Translation(along * 5), true);
            Run(hid);
            Check(L(a).Data.Box == Guid.Empty, "a head dragged sideways takes its level out of the box");
            Near(Plan(a)[0].DistanceTo(Plan(a)[1]), 80, 1e-6, "...and that end of the level moved with it (75' + 5')");
            Near(Plan(a)[0].DistanceTo(pa[0]), 0, 1e-6, "...while the other end stayed");
            Near(L(a).Z, 200, 1e-9, "...and the level did not move up or down");
            Check(L(b).Data.Box == box && Same(Plan(b), stretched) && L(c).Data.Box == box, "the other levels stay in the box");
            Near(BoxZ(box), 218 - margin, 1e-9, "the box now fits the levels still in it");

            // the box moved: the levels in it follow, the one that left does not
            var aBefore = Plan(a);
            doc.Objects.Transform(box, Transform.Translation(10, 0, 0), true);
            Run(box);
            var shifted = new Polyline(stretched);
            shifted.Transform(Transform.Translation(10, 0, 0));
            Check(Same(Plan(b), shifted) && Same(Plan(c), shifted), "moving the box moves the levels still in it");
            Check(Same(Plan(a), aBefore), "...and not the level that left");

            // a head dragged mostly up moves its level and keeps it in the box (the sideways drift is ignored)
            var hb = L(b).Data.Heads["0:2"];
            doc.Objects.Transform(hb, Transform.Translation(along * 0.3 + new Vector3d(0, 0, 4)), true);
            Run(hb);
            Near(L(b).Z, 222, 1e-9, "a head dragged mostly up moves its level up");
            Check(L(b).Data.Box == box && Same(Plan(b), shifted), "...and the level stays in the box");

            // A joins again (its Datum Box property): it takes the box's outline, heads on the same sides
            using (SkunkService.Suppress())
                DatumBoxElement.Attach(new UpdateContext(doc), a, box);
            Check(L(a).Data.Box == box && Same(Plan(a), shifted), "a level that joins the box again takes its outline");
            Check(L(a).Data.Heads.Count == 2 && L(a).Data.Heads.ContainsKey("0:2") && L(a).Data.Heads.ContainsKey("1:2"), "...with its heads on the sides they were on");

            // a level drawn from another corner keeps its heads on its north and west sides when it joins
            var fpD = new Polyline(new[] { new Point3d(50, 340, 0), new Point3d(0, 340, 0), new Point3d(0, 300, 0), new Point3d(50, 300, 0), new Point3d(50, 340, 0) });
            Guid dl;
            using (SkunkService.Suppress())
                dl = LevelElement.Create(doc, "", "Box D", 206, fpD);
            Check(L(dl).Data.Heads.ContainsKey("0:2") && L(dl).Data.Heads.ContainsKey("1:2"), "level D has heads on its first two sides (north and west)");
            using (SkunkService.Suppress())
                DatumBoxElement.Attach(new UpdateContext(doc), dl, box);
            var dd = L(dl).Data;
            Check(dd.Box == box && dd.Heads.Count == 2 && dd.Heads.ContainsKey("2:2") && dd.Heads.ContainsKey("3:2"),
                  "joining the box, level D keeps its heads on its north and west sides (the box's 3rd and 4th): " + string.Join(", ", dd.Heads.Keys));

            // level B's footprint edited in plan (a corner dragged): B leaves the box and keeps the edit
            var plB = new Polyline(L(b).Footprint);
            var corner = plB[2] + new Vector3d(3, 3, 0);
            plB[2] = corner;
            doc.Objects.Replace(b, new PolylineCurve(plB));
            Run(b);
            Check(L(b).Data.Box == Guid.Empty && L(b).Footprint[2].DistanceTo(corner) < 1e-6, "a level whose footprint is edited in plan leaves the box and keeps the edit");

            // the box made into something else (a circle) goes back to its outline; its levels stay
            var boxPlan = DatumBoxElement.Info(doc, doc.Objects.FindId(box)).Outline;
            doc.Objects.Replace(box, new ArcCurve(new Circle(new Point3d(35, 320, 0), 10)));
            Run(box);
            Check(doc.Objects.FindId(box).Geometry is PolylineCurve && Same(DatumBoxElement.Info(doc, doc.Objects.FindId(box)).Outline, boxPlan) && Same(Plan(c), shifted),
                  "a box turned into a circle goes back to its outline, and its levels stay");

            // a rotation typed in the Properties palette turns the box and its levels about its middle
            using (SkunkService.Suppress())
                DatumBoxElement.SetRotation(new UpdateContext(doc), box, 45);
            var turned = DatumBoxElement.Info(doc, doc.Objects.FindId(box)).Outline;
            Near(DatumBoxElement.Rotation(doc, turned), 45, 1e-6, "a typed rotation turns the box");
            Check(Same(Plan(c), turned) && Same(Plan(a), turned), "...and the levels in it");
            var choice = Skunk.UI.PropertyModel.Level(doc, doc.Objects.FindId(c)).FirstOrDefault(r => r.Name == "Datum Box");
            Check(choice != null && choice.Index > 0 && choice.Choices[choice.Index] == "Datum Box 1", "a level's Datum Box property shows its box");

            // copied together, a box and a level in it: the copy of the level goes into the copy of the box
            var xf = Transform.Translation(200, 0, 0);
            var boxCopy = doc.Objects.Transform(box, xf, false);
            var cCopy = doc.Objects.Transform(c, xf, false);
            svc.TouchAdded(doc, new[] { boxCopy, cCopy });
            svc.ProcessNow(doc);
            Check(L(cCopy).Data.Box == boxCopy && L(c).Data.Box == box, "a level copied with its box goes into the copied box");
            Eq(doc.Objects.FindId(boxCopy).Attributes.Name, "Datum Box 2", "the copied box gets the next name");

            // the box deleted: its levels keep their extents, on their own
            var keep = Plan(c);
            doc.Objects.Delete(box, true);
            svc.FullUpdate(doc);
            svc.ProcessNow(doc);
            Check(L(c).Data.Box == Guid.Empty && L(a).Data.Box == Guid.Empty && Same(Plan(c), keep), "deleting the box leaves its levels where they were, on their own");
        }

        /// <summary>
        /// Grids: names in series, a bubble (a filled circle and a label) centred on each end that has one, bubbles
        /// dragged along a grid moving that end, datum boxes setting the ends of the grids in them, plan cuts that
        /// never clip grids, copies, renames and deletions.
        /// </summary>
        static void GridTests(RhinoDoc doc)
        {
            var svc = SkunkService.Instance;
            void Run(params Guid[] ids)
            {
                svc.Touch(doc, ids);
                svc.ProcessNow(doc);
            }
            GridInfo G(Guid id) => GridElement.Info(doc, doc.Objects.FindId(id));
            RhinoObject Part(Guid id, string key) => G(id).Data.Heads.TryGetValue(key, out var p) ? ElementIO.Find(doc, p) : null;
            int PlanParts(Guid id) => G(id).Data.Heads.Keys.Count(k => !k.StartsWith("e", StringComparison.Ordinal));
            Guid Make(string name, Point3d a, Point3d b, bool? end1 = null, bool? end2 = null)
            {
                using (SkunkService.Suppress())
                    return GridElement.Create(new UpdateContext(doc), "", name, new Line(a, b), Guid.Empty, end1, end2);
            }

            // names in series
            string[,] series = { { "1", "2" }, { "9", "10" }, { "09", "10" }, { "A", "B" }, { "Z", "AA" }, { "AZ", "BA" }, { "A9", "A10" }, { "B.1", "B.2" }, { "Grid A", "Grid B" }, { "Main", "Main 2" } };
            bool seriesOk = true;
            for (int i = 0; i < series.GetLength(0); i++)
                if (GridElement.Increment(series[i, 0]) != series[i, 1])
                {
                    seriesOk = false;
                    Console.WriteLine("    " + series[i, 0] + " -> " + GridElement.Increment(series[i, 0]) + " (expected " + series[i, 1] + ")");
                }
            Check(seriesOk, "grid names run on: 1, 2 / 9, 10 / A, B / Z, AA / AZ, BA / A9, A10 / B.1, B.2 / Grid A, Grid B / Main, Main 2");

            // a plan cut made before there are any grids: the first grid brings it up to date
            Guid cutBefore;
            using (SkunkService.Suppress())
                cutBefore = PlanCutElement.Ensure(doc, LevelElement.All(doc)[0].Id, Guid.NewGuid());

            // the first grid: 1, a bubble at End 2 (the type's default), circle and label centred on the end
            var g1 = Make(null, new Point3d(500, 0, 0), new Point3d(500, 80, 0));
            Eq(G(g1).Name, "1", "the first grid is 1");
            Check(PlanParts(g1) == 1 && Part(g1, "2t") != null && Part(g1, "2") == null && Part(g1, "1t") == null, "a bubble at End 2 (one text), none at End 1");
            var bubble = Part(g1, "2t");
            var bubbleText = bubble.Geometry as TextEntity;
            Check(bubbleText != null, "the bubble is a text: the grid's name");
            Near(GridElement.Center(bubble.Geometry), new Point3d(500, 80, 0), 1e-9, "the bubble is centred on End 2");
            Eq(bubbleText?.PlainText, "1", "it reads the grid's name");
            Check(bubbleText?.Justification == TextJustification.MiddleCenter, "the text is centred on its point, so the bubble stays centred at any scale");
            Check(bubble.Attributes.DisplayOrder > 0, "drawn over the grid line, so its circle hides the line");
            var style = doc.DimStyles.FindId(bubbleText.DimensionStyleId);
            Check(style != null && style.DrawTextMask && style.MaskFrameType == DimensionStyle.MaskFrame.CircleFrame &&
                  style.MaskColorSource == DimensionStyle.MaskType.MaskColor && style.MaskColor.ToArgb() == System.Drawing.Color.White.ToArgb(),
                  "the circle is the text's own mask (white, with an outline): it hides the line and can never cover the name");
            Near((style.TextHeight + 2 * style.MaskOffset) / style.TextHeight, 12.7 / 4.7625, 1e-9,
                 "the circle is the bubble size across (1/2\" around 3/16\" text), style sizes that scale together per view and detail");
            Check(doc.InstanceDefinitions.Find(Styles.GridBubbleBlock) == null, "no bubble block: the circle is the text's");
            var lineObj = doc.Objects.FindId(g1);
            Check(lineObj.Geometry is LineCurve && lineObj.Attributes.LayerIndex == doc.Layers.FindByFullPath(Keys.RootLayer + "::" + Keys.GridsLayer, -1) &&
                  doc.Layers[lineObj.Attributes.LayerIndex].LinetypeIndex == Layers.FindLinetype(doc, Keys.LinetypeDatum)?.Index,
                  "the grid line is on __Annotations__::Grids, drawn with Skunk Datum");

            // the next grids: 2 with bubbles at both ends, then A and B
            var g2 = Make(null, new Point3d(530, 0, 0), new Point3d(530, 80, 0), true, true);
            var ga = Make("A", new Point3d(480, 10, 0), new Point3d(560, 10, 0));
            var gb = Make(null, new Point3d(480, 40, 0), new Point3d(560, 40, 0));
            Eq(G(g2).Name, "2", "the next grid is 2");
            Check(PlanParts(g2) == 2, "bubbles at both ends (one text each)");
            Eq(G(gb).Name, "B", "after grid A comes B");

            // a bubble dragged along its grid moves that end; dragged across, it snaps back
            var c2 = Part(g1, "2t");
            doc.Objects.Transform(c2.Id, Transform.Translation(0, 15, 0), true);
            Run(c2.Id);
            Near(G(g1).Line.To, new Point3d(500, 95, 0), 1e-9, "a bubble dragged 15' along its grid takes that end along");
            Near(G(g1).Line.From, new Point3d(500, 0, 0), 1e-9, "...and the other end stays");
            Near(GridElement.Center(Part(g1, "2t").Geometry), new Point3d(500, 95, 0), 1e-9, "...and the bubble stays there, on the new end");
            var c3 = Part(g1, "2t");
            doc.Objects.Transform(c3.Id, Transform.Translation(7, 0, 0), true);
            Run(c3.Id);
            Near(G(g1).Line.To, new Point3d(500, 95, 0), 1e-9, "a bubble dragged across its grid changes nothing");
            Near(GridElement.Center(Part(g1, "2t").Geometry), new Point3d(500, 95, 0), 1e-9, "...it snaps back to the end");

            // the grid moved: its bubbles go along; renamed in Rhino's Properties: the bubble follows, and so does the
            // grid after it (grid 2, to its right: the names run left to right)
            doc.Objects.Transform(g1, Transform.Translation(-5, 0, 0), true);
            Run(g1);
            Near(GridElement.Center(Part(g1, "2t").Geometry), new Point3d(495, 95, 0), 1e-9, "a grid moved takes its bubble along");
            var o1 = doc.Objects.FindId(g1);
            var at = o1.Attributes.Duplicate();
            at.Name = "1A";
            doc.Objects.ModifyAttributes(o1, at, true);
            Run(g1);
            Eq((Part(g1, "2t").Geometry as TextEntity)?.PlainText, "1A", "renamed in Rhino's Properties, the bubble shows the new name");
            Eq(G(g2).Name, "1B", "...and grid 2 after it follows on");
            Eq((Part(g2, "2t").Geometry as TextEntity)?.PlainText, "1B", "...its bubbles too");
            Check(G(ga).Name == "A" && G(gb).Name == "B", "...while the grids across them keep their names");

            // a deleted bubble switches that end off
            var gone = Part(g2, "1t");
            doc.Objects.Delete(gone.Id, true);
            using (SkunkService.Suppress())
                GridElement.HeadDeleted(new UpdateContext(doc), g2, 1, true, GridElement.PlanBubble, gone.Id);
            Check(!G(g2).Data.End1 && Part(g2, "1t") == null && Part(g2, "2t") != null, "deleting a bubble switches that end off");

            // plan cuts, made before or after the grids, leave the grid layers unclipped: grids show on every floor
            Guid cutAfter;
            using (SkunkService.Suppress())
                cutAfter = PlanCutElement.Ensure(doc, LevelElement.All(doc)[0].Id, Guid.NewGuid());
            int gridsLayer = doc.Layers.FindByFullPath(Keys.RootLayer + "::" + Keys.GridsLayer, -1);
            int headsLayer = doc.Layers.FindByFullPath(Keys.RootLayer + "::" + Keys.GridHeadsLayer, -1);
            bool Unclipped(Guid cutId)
            {
                if (!(doc.Objects.FindId(cutId)?.Geometry is ClippingPlaneSurface cps))
                    return false;
                cps.GetClipParticipation(out var ids, out var layers, out bool exclusion);
                var set = (layers ?? Enumerable.Empty<int>()).ToList();
                return exclusion && set.Contains(gridsLayer) && set.Contains(headsLayer) && !(ids ?? Enumerable.Empty<Guid>()).Any();
            }
            Check(Unclipped(cutBefore), "a plan cut made before the first grid leaves __Annotations__::Grids and __Annotations__::Grid Heads unclipped");
            Check(Unclipped(cutAfter), "...and so does one made after");
            doc.Objects.Delete(cutBefore, true);
            doc.Objects.Delete(cutAfter, true);

            // a copy gets the next name and bubbles of its own; deleting a grid deletes its bubbles
            var copy = doc.Objects.Transform(g2, Transform.Translation(30, 0, 0), false);
            svc.TouchAdded(doc, new[] { copy });
            svc.ProcessNow(doc);
            Check(G(copy).Name == "C" && PlanParts(copy) == 1 && !G(copy).Data.Heads.Values.Intersect(G(g2).Data.Heads.Values).Any(),
                  "a copied grid gets the next name and its own bubble: " + G(copy).Name);
            var copyData = G(copy).Data;
            var copyParts = copyData.Heads.Values.ToList();
            doc.Objects.Delete(copy, true);
            using (SkunkService.Suppress())
                GridElement.Deleted(new UpdateContext(doc), copy, copyData);
            Check(copyParts.All(p => ElementIO.Find(doc, p) == null), "deleting a grid deletes its bubbles");

            // a datum box sets the ends of the grids in it; the grids keep their places
            var outline = new Polyline(new[] { new Point3d(470, -10, 0), new Point3d(570, -10, 0), new Point3d(570, 110, 0), new Point3d(470, 110, 0), new Point3d(470, -10, 0) });
            Guid box;
            using (SkunkService.Suppress())
                box = DatumBoxElement.Create(new UpdateContext(doc), outline, Array.Empty<Guid>(), "Grid Box", new[] { g1, g2, ga });
            Check(G(g1).Data.Box == box && G(g2).Data.Box == box && G(ga).Data.Box == box && G(gb).Data.Box == Guid.Empty, "grids 1, 2 and A joined the box, B did not");
            Near(G(g1).Line.From, new Point3d(495, -10, 0), 1e-9, "grid 1 starts on the box's south side");
            Near(G(g1).Line.To, new Point3d(495, 110, 0), 1e-9, "...and ends on its north side");
            Near(G(ga).Line.From, new Point3d(470, 10, 0), 1e-9, "grid A starts on the west side");
            Near(G(ga).Line.To, new Point3d(570, 10, 0), 1e-9, "...and ends on the east side");
            Near(GridElement.Center(Part(ga, "2t").Geometry), new Point3d(570, 10, 0), 1e-9, "...with its bubble on the box");
            Check(DatumBoxElement.GridIds(doc, box).Count == 3, "the box knows its three grids");

            // the box stretched east: grid A's end follows, grid 1 stays where it is
            doc.Objects.Transform(box, Transform.Scale(new Plane(new Point3d(470, -10, 0), Vector3d.XAxis, Vector3d.YAxis), 1.5, 1, 1), true);
            Run(box);
            Near(G(ga).Line.To, new Point3d(620, 10, 0), 1e-9, "the box stretched east: grid A's end follows");
            Near(G(g1).Line.From, new Point3d(495, -10, 0), 1e-9, "...and grid 1 keeps its place (the box sets ends, not positions)");
            Near(G(gb).Line.To, new Point3d(560, 40, 0), 1e-9, "...and grid B, not in the box, is untouched");

            // the box turned: the grids keep their lines, their ends move onto the turned outline
            doc.Objects.Transform(box, Transform.Rotation(RhinoMath.ToRadians(10), Vector3d.ZAxis, new Point3d(545, 50, 0)), true);
            Run(box);
            var turned = DatumBoxElement.Info(doc, doc.Objects.FindId(box)).Outline;
            var turnedCurve = new PolylineCurve(turned);
            bool OnOutline(Point3d p) => turnedCurve.ClosestPoint(new Point3d(p.X, p.Y, 0), out double tt) && turnedCurve.PointAt(tt).DistanceTo(new Point3d(p.X, p.Y, 0)) < 1e-6;
            var la = G(ga).Line;
            Check(OnOutline(la.From) && OnOutline(la.To) && Math.Abs(la.From.Y - 10) < 1e-9 && Math.Abs(la.To.Y - 10) < 1e-9,
                  "the box turned 10 degrees: grid A stays on its line, its ends on the turned outline");

            // a grid moved inside the box stays in it, its ends back on the outline
            doc.Objects.Transform(g1, Transform.Translation(10, 0, 0), true);
            Run(g1);
            var l1 = G(g1).Line;
            Check(G(g1).Data.Box == box && Math.Abs(l1.From.X - 505) < 1e-9 && Math.Abs(l1.To.X - 505) < 1e-9 && OnOutline(l1.From) && OnOutline(l1.To),
                  "a grid moved inside the box stays in it, its ends on the outline");

            // a bubble dragged along the grid takes the grid out of the box and keeps that end
            var aBefore = G(ga).Line;
            var dirA = aBefore.Direction;
            dirA.Unitize();
            var bubbleA = Part(ga, "2t");
            doc.Objects.Transform(bubbleA.Id, Transform.Translation(dirA * -20), true);
            Run(bubbleA.Id);
            Check(G(ga).Data.Box == Guid.Empty, "a bubble dragged along its grid takes the grid out of the box");
            Near(G(ga).Line.To, aBefore.To - dirA * 20, 1e-9, "...and that end stays where the bubble was dropped");
            Near(G(ga).Line.From, aBefore.From, 1e-9, "...and the other end stays");

            // one end of a grid dragged along it (a grip edit) also takes the grid out
            var l2 = G(g2).Line;
            var d2 = l2.Direction;
            d2.Unitize();
            doc.Objects.Replace(g2, new LineCurve(l2.From, l2.To + d2 * 12));
            Run(g2);
            Check(G(g2).Data.Box == Guid.Empty && G(g2).Line.To.DistanceTo(l2.To + d2 * 12) < 1e-9, "an end dragged along the grid takes the grid out of the box and stays");

            // the box changes again: only the grid still in it follows
            var aKeep = G(ga).Line;
            doc.Objects.Transform(box, Transform.Translation(0, 5, 0), true);
            Run(box);
            var moved =DatumBoxElement.Info(doc, doc.Objects.FindId(box)).Outline;
            var movedCurve = new PolylineCurve(moved);
            bool OnMoved(Point3d p) => movedCurve.ClosestPoint(new Point3d(p.X, p.Y, 0), out double tt) && movedCurve.PointAt(tt).DistanceTo(new Point3d(p.X, p.Y, 0)) < 1e-6;
            Check(OnMoved(G(g1).Line.From) && OnMoved(G(g1).Line.To), "the box moved: the grid still in it follows");
            Check(G(ga).Line.From.DistanceTo(aKeep.From) < 1e-9 && G(ga).Line.To.DistanceTo(aKeep.To) < 1e-9, "...and the grids that left do not");

            // joining again (the Datum Box property) puts the ends back on the outline
            using (SkunkService.Suppress())
                GridElement.Attach(new UpdateContext(doc), ga, box);
            Check(G(ga).Data.Box == box && OnMoved(G(ga).Line.From) && OnMoved(G(ga).Line.To), "a grid that joins the box again gets its ends on the outline");
            var gridChoice = Skunk.UI.PropertyModel.Grid(doc, doc.Objects.FindId(ga)).FirstOrDefault(r => r.Name == "Datum Box");
            Check(gridChoice != null && gridChoice.Index > 0 && gridChoice.Choices[gridChoice.Index] == "Grid Box", "a grid's Datum Box property shows its box");

            // the box deleted: its grids keep their ends, on their own
            var g1Keep = G(g1).Line;
            doc.Objects.Delete(box, true);
            svc.FullUpdate(doc);
            svc.ProcessNow(doc);
            Check(G(g1).Data.Box == Guid.Empty && G(g1).Line.From.DistanceTo(g1Keep.From) < 1e-9 && G(g1).Line.To.DistanceTo(g1Keep.To) < 1e-9,
                  "deleting the box leaves its grids where they were, on their own");
        }

        /// <summary>
        /// Grids in elevations: beyond each end a vertical line from just under the lowest level to just over the
        /// highest (a datum box's height in a box), with its bubble facing out; the type's Elevation Bubbles; parts that
        /// snap back, come back and are not copied; and the layout details that show each part.
        /// </summary>
        static void GridElevationTests(RhinoDoc doc)
        {
            var svc = SkunkService.Instance;
            void Run(params Guid[] ids)
            {
                svc.Touch(doc, ids);
                svc.ProcessNow(doc);
            }
            GridInfo G(Guid id) => GridElement.Info(doc, doc.Objects.FindId(id));
            RhinoObject Part(Guid id, string key) => G(id).Data.Heads.TryGetValue(key, out var p) ? ElementIO.Find(doc, p) : null;
            LineCurve LineOf(Guid id, string key) => Part(id, key)?.Geometry as LineCurve;

            Guid g;
            using (SkunkService.Suppress())
                g = GridElement.Create(new UpdateContext(doc), "", "E1", new Line(new Point3d(700, 0, 0), new Point3d(700, 60, 0)));
            var zs = LevelElement.All(doc).Select(l => l.Z).ToList();
            double margin = DatumBoxElement.Margin(doc);
            double bottom = zs.Min() - margin, top = zs.Max() + margin;
            var type = TypeTable.Get(doc).Find<GridType>(G(g).Data.Type);
            double offset = GridElement.ElevationOffset(doc, type);

            // a vertical line beyond each end, from just under the lowest level to just over the highest
            var e1 = LineOf(g, "e1");
            var e2 = LineOf(g, "e2");
            Check(e1 != null && e2 != null && ElementIO.KindOf(Part(g, "e1")) == ElementKind.GridElevation &&
                  Part(g, "e1").Attributes.LayerIndex == doc.Layers.FindByFullPath(Keys.RootLayer + "::" + Keys.GridElevationsLayer, -1),
                  "a vertical grid line beyond each end, on __Annotations__::Grid Elevations");
            Near(e1.PointAtStart, new Point3d(700, -offset, bottom), 1e-6, "beyond End 1 by a bubble diameter, from just under the lowest level");
            Near(e1.PointAtEnd, new Point3d(700, -offset, top), 1e-6, "...to just over the highest");
            Near(e2.PointAtStart, new Point3d(700, 60 + offset, bottom), 1e-6, "beyond End 2 likewise");
            Check(offset >= DocUnits.PaperToModel(doc, type.BubbleSizeMM) - 1e-9, "seen from above, the elevation bubble clears the plan bubble");

            // its bubble at the top faces out, reading left to right for someone beyond that end looking along the grid
            var t1 = Part(g, "e1ut")?.Geometry as TextEntity;
            var t2 = Part(g, "e2ut")?.Geometry as TextEntity;
            Check(t1 != null && t2 != null && Part(g, "e1u") == null && Part(g, "e1dt") == null, "a bubble at the top (the type's default, one text), none at the bottom");
            Near(GridElement.Center(t1), new Point3d(700, -offset, top), 1e-6, "the bubble is centred on the top of the line");
            Eq(t1?.PlainText, "E1", "it reads the grid's name");
            Check(t1 != null && (t1.Plane.ZAxis - new Vector3d(0, -1, 0)).Length < 1e-9 && (t1.Plane.XAxis - Vector3d.XAxis).Length < 1e-9,
                  "beyond End 1 it faces south, east to the right");
            Check(t2 != null && (t2.Plane.ZAxis - Vector3d.YAxis).Length < 1e-9 && (t2.Plane.XAxis + Vector3d.XAxis).Length < 1e-9,
                  "beyond End 2 it faces north, west to the right");

            // bubble circles are solid: each bubble draws Continuous itself, the elevation one too, though its layer is
            // the dashed datum one; the elevation line keeps the datum pattern
            bool SolidBubble(RhinoObject o) => o != null && o.Attributes.LinetypeSource == ObjectLinetypeSource.LinetypeFromObject && o.Attributes.LinetypeIndex == -1;
            Check(SolidBubble(Part(g, "e1ut")) && SolidBubble(Part(g, "e2ut")) && SolidBubble(Part(g, "2t")),
                  "the bubbles' circles are solid (Continuous), in elevations and in plan");
            Check(Part(g, "e1").Attributes.LinetypeSource != ObjectLinetypeSource.LinetypeFromObject || Part(g, "e1").Attributes.LinetypeIndex >= 0,
                  "...while the elevation line keeps the datum pattern");
            var dashed = Part(g, "e1ut").Attributes.Duplicate();
            dashed.LinetypeSource = ObjectLinetypeSource.LinetypeFromLayer;
            doc.Objects.ModifyAttributes(Part(g, "e1ut"), dashed, true);
            svc.ContextChanged(doc);
            svc.ProcessNow(doc);
            Check(SolidBubble(Part(g, "e1ut")), "a bubble from an earlier build (dashed by its layer) draws solid after the next update");

            // the highest level moved up: the lines reach up with it
            var highest = LevelElement.All(doc).Last();
            doc.Objects.Transform(highest.Id, Transform.Translation(0, 0, 10), true);
            Run(highest.Id);
            Near(LineOf(g, "e1").PointAtEnd.Z, top + 10, 1e-6, "the highest level moved up 10': the grid's elevation lines reach up with it");
            doc.Objects.Transform(highest.Id, Transform.Translation(0, 0, -10), true);
            Run(highest.Id);

            // the type's Elevation Bubbles: both ends of the line, then none (the lines stay)
            void Bubbles(GridElevationBubbles b)
            {
                var lib = TypeTable.Get(doc);
                lib.Grid.First(x => x.Name == type.Name).ElevationBubbles = b;
                TypeTable.Save(doc, lib);
                svc.ContextChanged(doc);
                svc.ProcessNow(doc);
            }
            Bubbles(GridElevationBubbles.Both);
            Check(Part(g, "e1ut") != null && Part(g, "e1dt") != null && Part(g, "e2dt") != null, "Elevation Bubbles = Both: a bubble at the top and the bottom");
            Near(GridElement.Center(Part(g, "e1dt").Geometry), new Point3d(700, -offset, bottom), 1e-6, "...the bottom one centred on the bottom of the line");
            Bubbles(GridElevationBubbles.None);
            Check(Part(g, "e1ut") == null && Part(g, "e1dt") == null && LineOf(g, "e1") != null, "Elevation Bubbles = None: no bubbles, the lines stay");
            Bubbles(GridElevationBubbles.Top);

            // elevation parts follow the grid: moved they snap back, deleted they come back, copied they go
            var line = Part(g, "e1");
            doc.Objects.Transform(line.Id, Transform.Translation(4, 0, 2), true);
            Run(line.Id);
            Near(LineOf(g, "e1").PointAtStart, new Point3d(700, -offset, bottom), 1e-6, "an elevation line moved snaps back");
            var bub = Part(g, "e1ut");
            doc.Objects.Delete(bub.Id, true);
            using (SkunkService.Suppress())
                GridElement.HeadDeleted(new UpdateContext(doc), g, 1, true, GridElement.ElevationTop, bub.Id);
            Check(Part(g, "e1ut") != null && Part(g, "e1ut").Id != bub.Id, "a deleted elevation bubble comes back");
            var copied = doc.Objects.Add(Part(g, "e2").Geometry.Duplicate(), Part(g, "e2").Attributes.Duplicate());
            svc.TouchAdded(doc, new[] { copied });
            svc.ProcessNow(doc);
            Check(ElementIO.Find(doc, copied) == null, "a copied elevation line is removed (elevation parts come from their grid)");

            // in a datum box the lines span the box (here one with no levels: from the box up 3 m)
            var outline = new Polyline(new[] { new Point3d(690, -20, 0), new Point3d(710, -20, 0), new Point3d(710, 80, 0), new Point3d(690, 80, 0), new Point3d(690, -20, 0) });
            Guid box;
            using (SkunkService.Suppress())
                box = DatumBoxElement.Create(new UpdateContext(doc), outline, Array.Empty<Guid>(), "Elevation Box", new[] { g });
            Near(LineOf(g, "e1").PointAtStart, new Point3d(700, -20 - offset, 0), 1e-6, "in a datum box the line starts at the box's bottom, beyond the grid's new end");
            Near(LineOf(g, "e1").PointAtEnd.Z, DocUnits.FromMeters(doc, 3.0), 1e-6, "...and reaches the box's top");

            // which layout details show which part
            GridViews.Detail D(Vector3d dir, bool parallel = true)
            {
                dir.Unitize();
                return new GridViews.Detail { ViewportId = Guid.NewGuid(), Direction = dir, Parallel = parallel };
            }
            var plan = D(-Vector3d.ZAxis);
            var south = D(Vector3d.YAxis);
            var east = D(-Vector3d.XAxis);
            var axo = D(new Vector3d(1, 1, -1));
            var near = D(new Vector3d(Math.Sin(RhinoMath.ToRadians(12)), Math.Cos(RhinoMath.ToRadians(12)), 0));
            var off = D(new Vector3d(Math.Sin(RhinoMath.ToRadians(25)), Math.Cos(RhinoMath.ToRadians(25)), 0));
            Check(GridViews.Shows(plan, false, Vector3d.Unset) && !GridViews.Shows(plan, true, -Vector3d.YAxis), "a plan detail shows the plan grid and no elevation parts");
            Check(!GridViews.Shows(south, false, Vector3d.Unset), "an elevation detail shows no plan grid");
            Check(GridViews.Shows(south, true, -Vector3d.YAxis) && !GridViews.Shows(south, true, Vector3d.YAxis),
                  "the south elevation (looking north) shows the parts beyond the south ends, not those beyond the north ends");
            Check(!GridViews.Shows(east, true, -Vector3d.YAxis) && GridViews.Shows(east, true, Vector3d.XAxis),
                  "the east elevation shows east-west grids' east parts, not north-south grids");
            Check(GridViews.Shows(axo, false, Vector3d.Unset) && !GridViews.Shows(axo, true, -Vector3d.YAxis), "a 3D detail shows the plan grid only");
            Check(GridViews.Shows(near, true, -Vector3d.YAxis) && !GridViews.Shows(off, true, -Vector3d.YAxis), "looking within 15 degrees of along the grid counts; 25 degrees does not");
            Check(!GridViews.Shows(D(Vector3d.YAxis, false), true, -Vector3d.YAxis), "a perspective detail shows no elevation parts");

            // a real layout (headless Rhino can make one) with plan, south, east and 3D details: the idle check notices
            // them and the service sets each grid part's Hide in Detail overrides
            var page = doc.Views.AddPageView("Skunk grid views", 17, 11);
            var planDetail = page?.AddDetailView("Plan", new Point2d(1, 1), new Point2d(5, 5), Rhino.Display.DefinedViewportProjection.Top);
            var southDetail = page?.AddDetailView("South", new Point2d(6, 1), new Point2d(10, 5), Rhino.Display.DefinedViewportProjection.Front);
            var eastDetail = page?.AddDetailView("East", new Point2d(11, 1), new Point2d(15, 5), Rhino.Display.DefinedViewportProjection.Right);
            var threeD = page?.AddDetailView("3D", new Point2d(1, 6), new Point2d(5, 10), Rhino.Display.DefinedViewportProjection.Perspective);
            Check(planDetail != null && southDetail != null && eastDetail != null && threeD != null && GridViews.Details(doc).Count >= 4, "a layout with plan, south, east and 3D details");
            if (planDetail != null && southDetail != null && eastDetail != null && threeD != null)
            {
                Check(svc.CheckGridViews(doc), "the idle check notices the new details");
                svc.ProcessNow(doc);
                bool Hidden(RhinoObject o, DetailViewObject d) => o.Attributes.HasHideInDetailOverrideSet(d.Viewport.Id);
                var gridObj = doc.Objects.FindId(g);
                Check(!Hidden(gridObj, planDetail) && Hidden(gridObj, southDetail) && Hidden(gridObj, eastDetail) && !Hidden(gridObj, threeD),
                      "the grid line shows in the plan and 3D details, not in the elevations");
                Check(!Hidden(Part(g, "2t"), planDetail) && Hidden(Part(g, "2t"), southDetail), "...and so does its plan bubble");
                Check(Hidden(Part(g, "e1"), planDetail) && !Hidden(Part(g, "e1"), southDetail) && !Hidden(Part(g, "e1ut"), southDetail) && Hidden(Part(g, "e2"), southDetail) &&
                      Hidden(Part(g, "e2ut"), southDetail) && Hidden(Part(g, "e1"), eastDetail) && Hidden(Part(g, "e1"), threeD),
                      "elevation parts: only the south elevation shows them, only those beyond the grid's south end");
                Check(GridViews.Apply(doc, new[] { g }, GridViews.Details(doc)) == 0, "applied again, nothing changes");
                Check(!svc.CheckGridViews(doc), "the idle check stays quiet while no detail changes");

                // the south detail turned round to look from the north: the parts beyond the other end show instead
                var vp = southDetail.Viewport;
                var target = vp.CameraTarget;
                vp.SetCameraLocations(target, target + Vector3d.YAxis * 500);
                southDetail.CommitViewportChanges();
                Check(svc.CheckGridViews(doc), "the idle check notices a turned detail");
                svc.ProcessNow(doc);
                Check(!Hidden(Part(g, "e2"), southDetail) && Hidden(Part(g, "e1"), southDetail), "...and that detail now shows the parts beyond the north end");
                page.Close();
            }

            // tidy: the box and the grid go (the box first: the grid keeps its ends)
            doc.Objects.Delete(box, true);
            var gd = G(g).Data;
            doc.Objects.Delete(g, true);
            using (SkunkService.Suppress())
                GridElement.Deleted(new UpdateContext(doc), g, gd);
            Check(gd.Heads.Values.All(p => ElementIO.Find(doc, p) == null), "deleting a grid deletes its elevation parts too");
        }

        /// <summary>
        /// Grid names: the bubble is a text you type into (the grid takes the name, the grids after it follow on), names in
        /// between rename one grid, names stay unique by swapping, renumbering in a picked order or along a direction,
        /// copies named in turn, and 1.6.0 bubbles (a circle leader under the text) tidied away. These grids run at 30
        /// degrees, parallel only to each other.
        /// </summary>
        static void GridNameTests(RhinoDoc doc)
        {
            var svc = SkunkService.Instance;
            void Run(params Guid[] ids)
            {
                svc.Touch(doc, ids);
                svc.ProcessNow(doc);
            }
            GridInfo G(Guid id) => GridElement.Info(doc, doc.Objects.FindId(id));
            RhinoObject Bubble(Guid id) => G(id).Data.Heads.TryGetValue(GridElement.BubbleKey(2), out var p) ? ElementIO.Find(doc, p) : null;
            string Shown(Guid id) => ElementIO.NormalizeText((Bubble(id)?.Geometry as TextEntity)?.PlainText ?? "");
            string Names(params Guid[] ids) => string.Join(",", ids.Select(i => G(i).Name));

            // pure rules first
            Check(GridElement.CompareNames("9", "10") < 0 && GridElement.CompareNames("Z", "AA") < 0 && GridElement.CompareNames("A.2", "A.10") < 0 &&
                  GridElement.CompareNames("A", "1") == null && GridElement.CompareNames("A.1", "B.1") == null,
                  "series order: 9 before 10, Z before AA, A.2 before A.10; A and 1, A.1 and B.1 are different series");
            Check(GridElement.FirstInSeries(new[] { "3", "6", "4", "5" }) == "3" && GridElement.FirstInSeries(new[] { "C", "A", "B" }) == "A",
                  "the first name of a set: 3 of 3, 6, 4, 5; A of C, A, B");
            Check(GridElement.InBetween("2.5", "2") && GridElement.InBetween("2A", "2") && GridElement.InBetween("A.1", "A") && GridElement.InBetween("A1", "A") &&
                  !GridElement.InBetween("12", "1") && !GridElement.InBetween("B", "A") && !GridElement.InBetween("AA", "A"),
                  "names in between: 2.5 and 2A after 2, A.1 and A1 after A; not 12 after 1, B or AA after A");
            Eq(GridElement.CleanName("  C \r\n D  "), "C D", "a typed name is one line, single spaces");

            var u = new Vector3d(Math.Cos(RhinoMath.ToRadians(30)), Math.Sin(RhinoMath.ToRadians(30)), 0);
            var across = new Vector3d(u.Y, -u.X, 0);
            var origin = new Point3d(1200, 300, 0);
            Line At(double i) => new Line(origin + across * (i * 25), origin + across * (i * 25) + u * 80);
            Guid Make(string name, double i)
            {
                using (SkunkService.Suppress())
                    return GridElement.Create(new UpdateContext(doc), "", name, At(i), Guid.Empty, false, true);
            }

            // an array of copies, reported out of order, is named in turn going away from the original
            var g0 = Make("1", 0);
            var c3 = doc.Objects.Transform(g0, Transform.Translation(across * 75), false);
            var c1 = doc.Objects.Transform(g0, Transform.Translation(across * 25), false);
            var c2 = doc.Objects.Transform(g0, Transform.Translation(across * 50), false);
            svc.TouchAdded(doc, new[] { c3, c1, c2 });
            svc.ProcessNow(doc);
            Eq(Names(g0, c1, c2, c3), "1,2,3,4", "three copies reported out of order are named in turn, nearest the original first");
            Eq(Shown(c2), "3", "...and their bubbles show it");

            // a name typed into a bubble renames the grid, and the grids after it follow on
            var part = Bubble(c1);
            var typed = (TextEntity)part.Geometry.Duplicate();
            typed.PlainText = " A \n";
            doc.Objects.Replace(part.Id, typed);
            Run(part.Id);
            Eq(Names(g0, c1, c2, c3), "1,A,B,C", "A typed into grid 2's bubble: grid 2 is A, the grids after it B and C, the one before stays 1");
            Eq(Shown(c1) + Shown(c3), "AC", "...and the bubbles show the new names");
            Check(Bubble(c1)?.Id == part.Id, "...the edited bubble is the same object (its detail overrides stay)");

            // a name in between renames that grid alone
            part = Bubble(c2);
            typed = (TextEntity)part.Geometry.Duplicate();
            typed.PlainText = "A.5";
            doc.Objects.Replace(part.Id, typed);
            Run(part.Id);
            Eq(Names(g0, c1, c2, c3), "1,A,A.5,C", "A.5 typed after grid A renames that grid alone");
            using (SkunkService.Suppress())
                GridElement.Rename(new UpdateContext(doc), c2, "B");
            Eq(Names(g0, c1, c2, c3), "1,A,B,C", "renamed back to B (the Name property): the grid after it already follows");

            // the same name typed with bold or a size of its own: the bubble is made again from the grid
            part = Bubble(c2);
            var bold = (TextEntity)part.Geometry.Duplicate();
            bold.RichText = @"{\rtf1\deff0{\fonttbl{\f0 Arial;}}\f0 \fs23{\f0\b B\par}}";
            bold.TextHeight = bold.TextHeight * 2;
            doc.Objects.Replace(part.Id, bold);
            Run(part.Id);
            var clean = Bubble(c2)?.Geometry as TextEntity;
            Check(clean != null && G(c2).Name == "B" && !clean.RichText.Contains("\\b") && Math.Abs(clean.TextHeight - (Bubble(c1).Geometry as TextEntity).TextHeight) < 1e-12,
                  "a bubble made bold and bigger by hand goes back to the type's look");

            // names stay unique: a grid that has a name the renumbering needs swaps with it
            System.Collections.Generic.List<GridRename> renamed = null;
            using (SkunkService.Suppress())
                renamed = GridElement.Renumber(new UpdateContext(doc), new[] { c1 }, "C");
            Eq(Names(g0, c1, c2, c3), "1,C,B,A", "grid A renumbered C: grid C, not picked, takes A");
            Check(renamed.Count == 2 && renamed.Any(r => r.Id == c3 && r.From == "C" && r.To == "A"), "...and the result says so");

            // renumbering in the order a direction crosses the grids
            var ordered = GridElement.AlongDirection(doc, new[] { g0, c1, c2, c3 }, origin + across * 100 + u * 40, origin - across * 10 + u * 40);
            Check(ordered.SequenceEqual(new[] { c3, c2, c1, g0 }), "a direction drawn back across the grids orders them last to first");
            using (SkunkService.Suppress())
                GridElement.Renumber(new UpdateContext(doc), ordered, "1");
            Eq(Names(g0, c1, c2, c3), "4,3,2,1", "renumbered from 1 along it");
            using (SkunkService.Suppress())
                GridElement.Renumber(new UpdateContext(doc), new[] { g0, c1, c2, c3 }, "A");
            Eq(Names(g0, c1, c2, c3), "A,B,C,D", "renumbered from A in the order picked");

            // renamed to a name another grid has: they swap, and the names run on in the grids' own direction
            using (SkunkService.Suppress())
                GridElement.Rename(new UpdateContext(doc), c3, "A");
            Eq(Names(g0, c1, c2, c3), "D,B,C,A", "the last grid renamed A: no grid comes after it, and grid A takes the name it gave up");

            // a 1.6.0 bubble (a circle leader with the text drawn over it) is tidied: the leader goes, the text stays
            var label = Bubble(g0);
            var labelText = (TextEntity)label.Geometry;
            var circle = Leader.Create("", labelText.Plane, doc.DimStyles.FindId(labelText.DimensionStyleId),
                                       new[] { labelText.Plane.Origin, labelText.Plane.Origin + u * 0.004 });
            var circleAttrs = ElementIO.With(new ObjectAttributes(), ElementKind.GridHead, new GridHeadData { Grid = g0, End = 2, Label = false });
            var circleId = doc.Objects.AddLeader(circle, circleAttrs);
            var oldData = G(g0).Data;
            oldData.Heads["2"] = circleId;
            ElementIO.Write(doc, doc.Objects.FindId(g0), ElementKind.Grid, oldData);
            var la = label.Attributes.Duplicate();
            la.DisplayOrder = 2;
            doc.Objects.ModifyAttributes(label, la, true);
            int blockIndex = doc.InstanceDefinitions.Add(Styles.GridBubbleBlock, "", Point3d.Origin, new GeometryBase[] { new ArcCurve(new Circle(0.5)) });
            Check(circleId != Guid.Empty && blockIndex >= 0, "a grid set up as 1.6.0 left it (a circle leader under the text, the bubble block)");
            Run(g0);
            Check(ElementIO.Find(doc, circleId) == null && !G(g0).Data.Heads.ContainsKey("2") && Bubble(g0)?.Id == label.Id && Bubble(g0).Attributes.DisplayOrder == 1,
                  "...the next update removes the circle leader and keeps the text (now the whole bubble)");
            using (SkunkService.Suppress())
                Styles.DimStyle(doc, TypeTable.Get(doc).Find<GridType>(G(g0).Data.Type));
            Check(doc.InstanceDefinitions.Find(Styles.GridBubbleBlock) == null, "...and the unused bubble block goes");

            // tidy: these grids go
            foreach (var id in new[] { g0, c1, c2, c3 })
            {
                var gd = G(id).Data;
                doc.Objects.Delete(id, true);
                using (SkunkService.Suppress())
                    GridElement.Deleted(new UpdateContext(doc), id, gd);
            }
        }

        /// <summary>
        /// Grid bubbles by the sides of a view (SkGridBubbles): a plan sets the grids' ends by where they point on the
        /// screen, a view looking level their elevation bubbles; a layout detail keeps sides of its own and shows only
        /// those bubbles (adding any it needs), and ShowAll gives it every bubble back. A bubble just made gets its
        /// detail overrides at once.
        /// </summary>
        static void GridBubbleTests(RhinoDoc doc)
        {
            var svc = SkunkService.Instance;
            GridInfo G(Guid id) => GridElement.Info(doc, doc.Objects.FindId(id));
            RhinoObject Part(Guid id, string key) => G(id).Data.Heads.TryGetValue(key, out var p) ? ElementIO.Find(doc, p) : null;
            string Ends(params Guid[] ids) => string.Join(" ", ids.Select(i => (G(i).Data.End1 ? "1" : "-") + (G(i).Data.End2 ? "2" : "-")));
            Guid Make(string name, Point3d a, Point3d b)
            {
                using (SkunkService.Suppress())
                    return GridElement.Create(new UpdateContext(doc), "", name, new Line(a, b), Guid.Empty, false, true);
            }
            int Sides(Guid[] ids, GridBubbleSides sides, Vector3d right, Vector3d up, bool addOnly = false)
            {
                using (SkunkService.Suppress())
                    return GridElement.SetBubbleSides(new UpdateContext(doc), ids, sides, right, up, addOnly);
            }

            // which side of a view a grid end is on
            var X = Vector3d.XAxis;
            var Y = Vector3d.YAxis;
            Check(GridViews.SideOf(Y, X, Y) == GridBubbleSides.Top && GridViews.SideOf(-Y, X, Y) == GridBubbleSides.Bottom &&
                  GridViews.SideOf(X, X, Y) == GridBubbleSides.Right && GridViews.SideOf(-X, X, Y) == GridBubbleSides.Left,
                  "a plan view: north ends are on the top, south the bottom, east the right, west the left");
            Check(GridViews.SideOf(Y, Y, -X) == GridBubbleSides.Right && GridViews.SideOf(-X, Y, -X) == GridBubbleSides.Top,
                  "the view turned a quarter (north to the right): north ends are on its right, west ends on its top");
            Check(GridViews.SideOf(new Vector3d(1, 3, 0), X, Y) == GridBubbleSides.Top && GridViews.SideOf(new Vector3d(3, -1, 0), X, Y) == GridBubbleSides.Right,
                  "a skewed grid's end is on the side it points to most");

            // four grids, each with a bubble at End 2: V1 runs south to north, V2 north to south, H1 west to east, H2 east to west
            var o = new Point3d(1500, 0, 0);
            var v1 = Make("V1", o, o + new Vector3d(0, 60, 0));
            var v2 = Make("V2", o + new Vector3d(20, 60, 0), o + new Vector3d(20, 0, 0));
            var h1 = Make("H1", o + new Vector3d(-10, 20, 0), o + new Vector3d(40, 20, 0));
            var h2 = Make("H2", o + new Vector3d(40, 40, 0), o + new Vector3d(-10, 40, 0));
            var all = new[] { v1, v2, h1, h2 };
            Eq(Ends(all), "-2 -2 -2 -2", "four grids with a bubble at End 2");

            // in a plan view: top and left
            int changed = Sides(all, GridBubbleSides.Top | GridBubbleSides.Left, X, Y);
            Eq(Ends(all), "-2 1- 1- -2", "top and left of a plan: the north ends of V1 and V2, the west ends of H1 and H2");
            Check(changed == 2 && Part(v2, "1t") != null && Part(v2, "2t") == null, "...two grids changed, their bubbles moved to the other end");
            Sides(all, GridBubbleSides.All, X, Y);
            Eq(Ends(all), "12 12 12 12", "all four sides: both ends");
            Sides(all, GridBubbleSides.None, X, Y);
            Eq(Ends(all), "-- -- -- --", "no side: no bubbles");
            Sides(all, GridBubbleSides.Top, Y, -X);
            Eq(Ends(all), "-- -- 1- -2", "the view turned a quarter (north to the right): its top is west, so only the east-west grids' west ends");

            // the dialog's diagram shows what the command then does to the grids, for any sides and any turn of the view
            string Shown(GridBubbleSides s, Vector3d r, Vector3d u) =>
                string.Join(" ", GridBubbleDiagram.Plan(all.Select(G), r, u, s).Grids.Select(gr => (gr.A.Bubble ? "1" : "-") + (gr.B.Bubble ? "2" : "-")));
            bool matches = true;
            foreach (var s in new[] { GridBubbleSides.Top | GridBubbleSides.Left, GridBubbleSides.Bottom | GridBubbleSides.Right, GridBubbleSides.Top,
                                      GridBubbleSides.All, GridBubbleSides.None })
                foreach (var (r, u) in new[] { (X, Y), (Y, -X), (-X, -Y) })
                {
                    string shown = Shown(s, r, u);
                    Sides(all, s, r, u);
                    if (shown != Ends(all))
                    {
                        matches = false;
                        Console.WriteLine("    diagram " + shown + ", grids " + Ends(all) + " (" + s + ", right " + r + ")");
                    }
                }
            Check(matches, "the dialog's diagram shows every grid end's bubble just as the command then sets it (5 side choices, 3 turns of the view)");
            Sides(all, GridBubbleSides.Top, Y, -X);
            var turned = GridBubbleDiagram.Plan(new[] { G(v1) }, Y, -X, GridBubbleSides.Right).Grids[0];
            Check(turned.B.Side == GridBubbleSides.Right && turned.B.X > turned.A.X && Math.Abs(turned.B.Y - turned.A.Y) < 1e-9 && turned.B.Bubble && !turned.A.Bubble,
                  "the view turned a quarter: the diagram lays V1 across with its north end on the right, where the bubble goes");
            var standing = GridBubbleDiagram.Elevation(new[] { G(v2), G(v1) }, X, GridBubbleSides.Top | GridBubbleSides.Left);
            Check(standing.LookingLevel && standing.Grids.Count == 2 && standing.Grids[0].Name == "V1" &&
                  standing.Grids.All(gr => gr.B.Bubble && !gr.A.Bubble && gr.B.Y > gr.A.Y && gr.A.X == gr.B.X),
                  "looking level the diagram stands the grids up left to right (V1, V2) with only the top bubbles (left does not apply)");
            var sample = GridBubbleDiagram.Plan(new GridInfo[0], X, Y, GridBubbleSides.Top | GridBubbleSides.Left);
            Check(sample.Sample && sample.Grids.Count == 6 && sample.BubbleCount == 6,
                  "no grids in the view: the diagram is a sample plan (1, 2, 3 and A, B, C), here with 6 bubbles on the top and left");
            sample.Apply(GridBubbleSides.All);
            Check(sample.BubbleCount == 12, "...ticking every side shows a bubble at all 12 ends");

            // a view looking level: the top and bottom of the elevation lines, the grid's own choice
            using (SkunkService.Suppress())
                GridElement.SetElevationSides(new UpdateContext(doc), new[] { v1 }, GridBubbleSides.Top | GridBubbleSides.Bottom, false);
            Check(G(v1).Data.ElevationBubbles == GridElevationBubbles.Both && Part(v1, "e1ut") != null && Part(v1, "e1dt") != null,
                  "looking level, top and bottom: V1's elevation lines get bubbles at both ends (its own Elevation Bubbles: Both)");
            using (SkunkService.Suppress())
                GridElement.SetElevationSides(new UpdateContext(doc), new[] { v1 }, GridBubbleSides.Bottom, false);
            Check(G(v1).Data.ElevationBubbles == GridElevationBubbles.Bottom && Part(v1, "e1ut") == null && Part(v1, "e1dt") != null, "...bottom only");
            var ebRow = Skunk.UI.PropertyModel.Grid(doc, doc.Objects.FindId(v1)).FirstOrDefault(r => r.Name == "Elevation Bubbles");
            Check(ebRow != null && ebRow.Choices[ebRow.Index] == "Bottom", "the palette shows the grid's own Elevation Bubbles");
            using (SkunkService.Suppress())
                GridElement.SetElevationBubbles(new UpdateContext(doc), v1, null);
            Check(!G(v1).Data.ElevationBubbles.HasValue && Part(v1, "e1ut") != null && Part(v1, "e1dt") == null, "...and By Type puts the type's back (top)");

            // layout details: one keeps top and left, one shows every bubble, an elevation keeps only the top
            Sides(all, GridBubbleSides.None, X, Y);
            using (SkunkService.Suppress())
                foreach (var id in all)
                    GridElement.SetEnds(new UpdateContext(doc), id, false, true);
            using (SkunkService.Suppress())
                GridElement.SetElevationBubbles(new UpdateContext(doc), v1, GridElevationBubbles.Both);
            var page = doc.Views.AddPageView("Skunk bubble sides", 17, 11);
            var planA = page?.AddDetailView("Plan A", new Point2d(1, 1), new Point2d(8, 10), Rhino.Display.DefinedViewportProjection.Top);
            var planB = page?.AddDetailView("Plan B", new Point2d(9, 1), new Point2d(16, 5), Rhino.Display.DefinedViewportProjection.Top);
            var south = page?.AddDetailView("South", new Point2d(9, 6), new Point2d(16, 10), Rhino.Display.DefinedViewportProjection.Front);
            Check(planA != null && planB != null && south != null, "a layout with two plan details and a south elevation");
            if (planA != null && planB != null && south != null)
            {
                svc.CheckGridViews(doc);
                svc.ProcessNow(doc);
                bool Hidden(Guid grid, string key, DetailViewObject d) => Part(grid, key)?.Attributes.HasHideInDetailOverrideSet(d.Viewport.Id) ?? true;
                DetailViewObject Fresh(DetailViewObject d) => doc.Objects.FindId(d.Id) as DetailViewObject;
                void DetailSides(DetailViewObject d, GridBubbleSides? sides)
                {
                    using (SkunkService.Suppress())
                    {
                        var ctx = new UpdateContext(doc);
                        GridViews.SetSides(doc, Fresh(d), sides);
                        var info = GridViews.Of(Fresh(d));
                        if (sides.HasValue && GridViews.IsElevation(info))
                            GridElement.SetElevationSides(ctx, new[] { v1 }, sides.Value, true);
                        else if (sides.HasValue)
                            GridElement.SetBubbleSides(ctx, all, sides.Value, info.Right, info.Up, true);
                        GridViews.Apply(doc, all, GridViews.Details(doc));
                    }
                }
                string before = GridViews.Signature(GridViews.Details(doc));
                DetailSides(planA, GridBubbleSides.Top | GridBubbleSides.Left);
                Check(GridViews.SidesOf(Fresh(planA)) == (GridBubbleSides.Top | GridBubbleSides.Left) && GridViews.Signature(GridViews.Details(doc)) != before,
                      "plan A keeps its sides (top and left), and the details' signature changes (the idle check re-applies)");
                Eq(Ends(all), "-2 12 12 -2", "...grids that had no bubble on its top or left get one (V2 north, H1 west); none lose one");
                Check(!Hidden(v1, "2t", planA) && !Hidden(v2, "1t", planA) && Hidden(v2, "2t", planA) && !Hidden(h1, "1t", planA) && Hidden(h1, "2t", planA) && !Hidden(h2, "2t", planA),
                      "...plan A shows the bubbles on its top and left only");
                Check(!Hidden(v2, "2t", planB) && !Hidden(h1, "2t", planB) && !Hidden(v2, "1t", planB), "...plan B shows every bubble");
                Check(Hidden(v2, "1t", south), "...the new bubbles are hidden in the elevation, like every plan part");

                DetailSides(south, GridBubbleSides.Top);
                Check(!Hidden(v1, "e1ut", south) && Hidden(v1, "e1dt", south), "the south elevation keeps the top: V1's bottom elevation bubble is hidden there");
                DetailSides(planA, null);
                Check(GridViews.SidesOf(Fresh(planA)) == null && !Hidden(v2, "2t", planA) && !Hidden(h1, "2t", planA), "ShowAll: plan A shows every bubble again");

                // a bubble switched on later is hidden in the elevation detail at once
                using (SkunkService.Suppress())
                    GridElement.SetEnds(new UpdateContext(doc), h2, true, true);
                Check(Part(h2, "1t") != null && Hidden(h2, "1t", south) && !Hidden(h2, "1t", planB), "a bubble switched on later gets its detail overrides at once");
                page.Close();
            }

            // tidy: these grids go
            foreach (var id in all)
            {
                var gd = G(id).Data;
                doc.Objects.Delete(id, true);
                using (SkunkService.Suppress())
                    GridElement.Deleted(new UpdateContext(doc), id, gd);
            }
        }

        /// <summary>Spot slopes on curves: a line, and polyline segments - an arrow never reads across a corner.</summary>
        static void CurveSlopeTests(RhinoDoc doc)
        {
            var plId = doc.Objects.AddPolyline(new[] { new Point3d(600, 0, 0), new Point3d(610, 0, 1), new Point3d(620, 0, 4) });
            Guid a, b;
            using (SkunkService.Suppress())
            {
                a = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Percent)", doc.Objects.FindId(plId), new Point3d(609, 0, 0.9), Plane.WorldXY, null, true, true, Guid.Empty);
                b = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Percent)", doc.Objects.FindId(plId), new Point3d(615, 0, 2.5), Plane.WorldXY, null, true, true, Guid.Empty);
            }
            Check(a != Guid.Empty && b != Guid.Empty, "spot slopes placed on a polyline");
            var ad = Slope(doc, a);
            ad.SlopeTop = 3;   // uphill past the corner at 610
            ad.SlopeBottom = 2;
            ElementIO.Write(doc, doc.Objects.FindId(a), ElementKind.SpotSlope, ad);
            SkunkService.Instance.Touch(doc, new[] { a });
            SkunkService.Instance.ProcessNow(doc);
            Near(Slope(doc, a).Value, 0.1, 1e-9, "polyline: an arrow reaching past the corner reads its own segment");
            Eq((doc.Objects.FindId(a).Geometry as LinearDimension)?.PlainUserText, "10.00%", "...and labels it");
            var percent = TypeTable.Get(doc).Find<SpotSlopeType>("Slope Arrow (Percent)");
            var m = SpotElement.MeasureSlope(new UpdateContext(doc), percent, Slope(doc, a), Anchors.Evaluate(doc, Slope(doc, a).Anchor));
            Near(m.TopEnd, new Point3d(610, 0, 1), 1e-6, "its uphill end stops at the corner");
            Near(Slope(doc, b).Value, 0.3, 1e-9, "polyline: the other segment reads 30%");

            // moved onto the other segment, the arrow reads that one
            doc.Objects.Transform(a, Transform.Translation(6, 0, 1.6), true);
            SkunkService.Instance.Touch(doc, new[] { a });
            SkunkService.Instance.ProcessNow(doc);
            Near(Slope(doc, a).Value, 0.3, 1e-6, "an arrow moved onto the 30% segment reads 30%");

            // a level line reads 0%, and a line works like a single segment
            var flat = doc.Objects.AddLine(new Point3d(700, 0, 5), new Point3d(710, 0, 5));
            var ramp = doc.Objects.AddLine(new Point3d(720, 0, 0), new Point3d(724, 0, 1));
            Guid f, r;
            using (SkunkService.Suppress())
            {
                f = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Percent)", doc.Objects.FindId(flat), new Point3d(705, 0, 5), Plane.WorldXY, null, true, true, Guid.Empty);
                r = SpotElement.Create(doc, ElementKind.SpotSlope, "Slope Arrow (Percent)", doc.Objects.FindId(ramp), new Point3d(722, 0, 0.5), Plane.WorldXY, null, true, true, Guid.Empty);
            }
            Eq((doc.Objects.FindId(f).Geometry as LinearDimension)?.PlainUserText, "0.00%", "a flat line reads 0%");
            var rd = Slope(doc, r);
            rd.SlopeTop = 10;
            rd.SlopeBottom = 10;
            ElementIO.Write(doc, doc.Objects.FindId(r), ElementKind.SpotSlope, rd);
            SkunkService.Instance.Touch(doc, new[] { r });
            SkunkService.Instance.ProcessNow(doc);
            Near(Slope(doc, r).Value, 0.25, 1e-9, "an arrow longer than its line reads the line (25%)");
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
            // the engine's sources are compiled in, not its resources: the panel's tab icons need their own entries
            Check(asm.GetManifestResourceNames().Contains(Skunk.UI.SkunkAttributesPanel.IconLight) &&
                  asm.GetManifestResourceNames().Contains(Skunk.UI.SkunkAttributesPanel.IconDark), "the Skunk Attributes panel's tab icons embedded (light and dark)");
            Check(asm.GetManifestResourceNames().Contains(Skunk.UI.SkunkToolbar.Resource), "the Skunk toolbar embedded (Skunk writes it where Rhino opens it)");
            Check(asm.GetManifestResourceNames().Contains("Skunk.Core.Resources.Skunk.About.light.png") &&
                  asm.GetManifestResourceNames().Contains("Skunk.Core.Resources.Skunk.About.dark.png"), "the About dialog's logo embedded (light and dark)");
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
            var datumType = Layers.FindLinetype(doc, Keys.LinetypeDatum);
            Check(datumType != null && doc.Layers[Layers.LevelsLayerIndex(doc)].LinetypeIndex == datumType.Index,
                "the Levels layer of a new model draws Skunk Datum (not Continuous)");

            // type line pattern: every level of the type uses it
            var lib = TypeTable.Get(doc);
            var lt = lib.Find<LevelType>(ElementIO.Read<LevelData>(obj).Type);
            lt.LinePattern = "Center";
            lt.LinePatternScale = 2.5;
            TypeTable.Save(doc, lib);
            SkunkService.Instance.ContextChanged(doc);
            SkunkService.Instance.ProcessNow(doc);
            obj = doc.Objects.FindId(id);
            var center = Layers.FindLinetype(doc, "Center");
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
            var hidden = Layers.FindLinetype(doc, "Hidden");
            Check(hidden != null && obj.Attributes.LinetypeIndex == hidden.Index, "this level's own line pattern (Hidden) overrides the type");
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
                "Elevation", "Elevation (Shared)", "Story Height", "Line Pattern", "Heads At", "Datum Box", "Width (E/W)", "Extents Area", "Structural", "Relative Spot Elevations");
            Rows("datum box", Skunk.UI.PropertyModel.DatumBox(doc, Pick(ElementKind.DatumBox)[0]), "Levels", "Grids", "Length", "Width", "Rotation", "Extents Area", "Name", "Layer");
            Rows("grid", Skunk.UI.PropertyModel.Grid(doc, Pick(ElementKind.Grid)[0]), "Bubble at End 1", "Bubble at End 2", "Datum Box", "Length", "Direction", "Name", "Layer");
            Rows("grid type", Skunk.UI.PropertyModel.Type(doc, TypeTable.Get(doc).Grid[0], () => { }),
                "Bubble Size", "Plan View Symbols End 1 (Default)", "Plan View Symbols End 2 (Default)", "Line Pattern", "Text Size", "Color");
            Rows("north arrow", Skunk.UI.PropertyModel.NorthArrow(doc, Pick(ElementKind.NorthArrow)), "Points To", "Rotation", "Size");
            Rows("project (nothing selected)", Skunk.UI.PropertyModel.Location(doc, true, true),
                "Geographic Location", "Rhino North", "Angle to True North", "Angle to True North (CCW)", "Shared Origin (internal)", "True North (azimuth)", "Model Units", "Live Updates");
            // a Place button in the header of each point's group (drawn there, not as a row of its own)
            var project = Skunk.UI.PropertyModel.Location(doc, true, true);
            var place = project.Where(r => r.Editor == Skunk.UI.ParamEditor.GroupButton).ToList();
            Check(place.Count == 2 && place.All(r => r.Text == "Place" && r.OnClick != null) &&
                  place.Any(r => r.Group == "Project Base Point" && r.Tooltip.Contains("SkProjectBasePoint")) &&
                  place.Any(r => r.Group == "Survey Point" && r.Tooltip.Contains("SkSurveyPoint")),
                  "Place buttons in the Project Base Point and Survey Point headers (SkProjectBasePoint, SkSurveyPoint)");
            var pbpOnly = Skunk.UI.PropertyModel.Location(doc, true, false).Where(r => r.Editor == Skunk.UI.ParamEditor.GroupButton).ToList();
            Check(pbpOnly.Count == 1 && pbpOnly[0].Group == "Project Base Point", "a selected project base point marker shows its own Place button only");
            var plain = doc.Objects.FindId(s_attached);
            Rows("ordinary object", Skunk.UI.PropertyModel.Attached(doc, new System.Collections.Generic.List<RhinoObject> { plain }),
                "Base Level", "Base Offset", "Top Offset", "Height", "Top Elevation (Shared)", "Object Type");
        }

        static void LayerMigrationTests()
        {
            int Add(RhinoDoc d, string name, Guid parent)
            {
                var l = new Layer { Name = name, ParentLayerId = parent };
                return d.Layers.Add(l);
            }
            Guid Dot(RhinoDoc d, int layer) => d.Objects.AddPoint(new Point3d(1, 2, 3), new ObjectAttributes { LayerIndex = layer });
            string PathOf(RhinoDoc d, Guid id) => d.Layers[d.Objects.FindId(id).Attributes.LayerIndex].FullPath;
            bool Has(RhinoDoc d, string path) => d.Layers.FindByFullPath(path, -1) >= 0;

            // a new model: Skunk's layers go under __Annotations__
            var fresh = RhinoDoc.Create(null);
            try
            {
                int levels = Layers.Ensure(fresh, Keys.LevelsLayer, System.Drawing.Color.Black);
                Eq(fresh.Layers[levels].FullPath, "__Annotations__::Levels", "a new model: Skunk's layers are under __Annotations__");
                Check(!Has(fresh, "Skunk"), "...and there is no Skunk layer");
            }
            finally
            {
                fresh.Dispose();
            }

            // a model from an earlier build: its "Skunk" layer is renamed; sublayers, objects and layer settings stay
            var old = RhinoDoc.Create(null);
            try
            {
                int root = Add(old, "Skunk", Guid.Empty);
                old.Layers[root].Color = System.Drawing.Color.FromArgb(70, 70, 70);
                int grids = Add(old, "Grids", old.Layers[root].Id);
                old.Layers[grids].IsLocked = true;
                int site = Add(old, "Site", old.Layers[root].Id);
                old.Layers[site].IsVisible = false;
                var dot = Dot(old, grids);
                Check(Layers.Find(old, Keys.GridsLayer) == grids, "before the rename Skunk still finds its old sublayer (reads only)");
                Check(Layers.Migrate(old), "a model with a Skunk layer is migrated");
                Check(Has(old, "__Annotations__::Grids") && !Has(old, "Skunk"), "...the Skunk layer is now __Annotations__, its sublayer kept");
                Check(old.Layers[grids].IsLocked && !old.Layers[site].IsVisible && Has(old, "__Annotations__::Site"),
                      "...its sublayers as they were: a locked one still locked, a hidden one still hidden");
                Eq(PathOf(old, dot), "__Annotations__::Grids", "...and the objects are on it");
                Check(!Layers.Migrate(old), "migrating again changes nothing");
                Check(Layers.Find(old, Keys.GridsLayer) == grids && Layers.Ensure(old, Keys.GridsLayer, System.Drawing.Color.Black) == grids,
                      "...and Skunk finds and keeps using the same layer");
            }
            finally
            {
                old.Dispose();
            }

            // a model with an __Annotations__ layer of its own and an old Skunk layer: Skunk's sublayers move in; one that
            // is there already takes the objects; the empty Skunk layer goes
            var both = RhinoDoc.Create(null);
            try
            {
                int mine = Add(both, "__Annotations__", Guid.Empty);
                int myText = Add(both, "Text", both.Layers[mine].Id);
                int myLevels = Add(both, "Levels", both.Layers[mine].Id);
                int skunk = Add(both, "Skunk", Guid.Empty);
                int spots = Add(both, "Spot Elevations", both.Layers[skunk].Id);
                int skunkLevels = Add(both, "Levels", both.Layers[skunk].Id);
                var spot = Dot(both, spots);
                var level = Dot(both, skunkLevels);
                var text = Dot(both, myText);
                Check(Layers.Migrate(both), "an __Annotations__ of the model's own and a Skunk layer: migrated");
                Eq(PathOf(both, spot), "__Annotations__::Spot Elevations", "...Skunk's sublayer moved into __Annotations__ with its objects");
                Eq(PathOf(both, level), "__Annotations__::Levels", "...a sublayer it already had takes the objects of Skunk's");
                Check(both.Objects.FindId(level).Attributes.LayerIndex == myLevels, "...(the model's own Levels layer)");
                Eq(PathOf(both, text), "__Annotations__::Text", "...the model's own sublayers stay as they are");
                Check(!Has(both, "Skunk") && !Has(both, "Skunk::Levels"), "...and the emptied Skunk layers are gone");
            }
            finally
            {
                both.Dispose();
            }

            // the old layer is the current layer and an __Annotations__ exists: it stays (a current layer cannot be deleted)
            var current = RhinoDoc.Create(null);
            try
            {
                int mine = Add(current, "__Annotations__", Guid.Empty);
                int skunk = Add(current, "Skunk", Guid.Empty);
                current.Layers.SetCurrentLayerIndex(skunk, true);
                Layers.Migrate(current);
                Check(Has(current, "Skunk") && current.Layers.CurrentLayerIndex == skunk && current.Layers.FindByFullPath("__Annotations__", -1) == mine,
                      "an old Skunk layer that is the current layer stays (it cannot be deleted); nothing is lost");
            }
            finally
            {
                current.Dispose();
            }
        }

        static void GeoLocationTests()
        {
            var doc = RhinoDoc.Create(null);
            doc.AdjustModelUnitSystem(UnitSystem.Feet, false);
            doc.ModelAbsoluteTolerance = 0.001;
            doc.ModelDistanceDisplayMode = Rhino.UI.DistanceDisplayMode.FeetInches;
            doc.ModelDistanceDisplayPrecision = 3;
            string Row(System.Collections.Generic.List<Skunk.UI.Param> rows, string name) => rows.FirstOrDefault(r => r.Name == name)?.Text;
            System.Collections.Generic.List<Skunk.UI.Param> Panel() => Skunk.UI.PropertyModel.Location(doc, true, true);
            // clockwise from world Y (project north in this model), like "Angle to True North"
            double RhinoAngle() => SiteModel.ClockwiseAngle(Vector3d.YAxis, GeoLocation.RhinoNorth(doc));
            Vector3d Toward(double clockwiseDegrees)
            {
                double r = clockwiseDegrees * Math.PI / 180;
                return new Vector3d(Math.Sin(r), Math.Cos(r), 0);
            }
            void Save(SiteModel s)
            {
                using (SkunkService.Suppress())
                    s.Save(doc);
            }
            try
            {
                // a new Rhino model: no location, Rhino's north along world Y - the panel says so
                var g = GeoLocation.Read(doc);
                Check(!g.IsSet && GeoLocation.SameNorth(g.North, Vector3d.YAxis), "a new Rhino model has no geographic location; Rhino's north is world Y");
                var rows = Panel();
                Check(rows.Count > 0 && rows[0].Group == "Location", "Project Location starts with the geographic location");
                Eq(Row(rows, "Geographic Location"), "Not set in this Rhino model", "a location that is not set");
                Eq(Row(rows, "Rhino North"), "0.00° (same as true north)", "Rhino's north against true north");

                // the location set in Rhino (EarthAnchorPoint, or the Sun panel): Skunk reads it from the model
                using (var a = doc.EarthAnchorPoint)
                {
                    a.EarthBasepointLatitude = 39.7392;
                    a.EarthBasepointLongitude = -104.9903;
                    a.EarthBasepointElevation = 1609.344;
                    a.Name = "Sample Place";
                    a.Description = "100 Sample Street";
                    a.ModelBasePoint = new Point3d(100, 200, 0);
                    a.ModelNorth = Toward(30);
                    a.ModelEast = Toward(120);
                    doc.EarthAnchorPoint = a;
                }
                g = GeoLocation.Read(doc);
                Check(g.IsSet, "the location set in Rhino is read from the model");
                rows = Panel();
                Eq(Row(rows, "Place"), "Sample Place", "place");
                Eq(Row(rows, "Address"), "100 Sample Street", "address");
                Eq(Row(rows, "Latitude"), "39.739200° N", "latitude");
                Eq(Row(rows, "Longitude"), "104.990300° W", "longitude");
                Eq(Row(rows, "Elevation"), Skunk.Commands.Kit.Length(doc, 5280), "elevation in the model's length format (Rhino keeps meters: 1609.344 m)");
                Check(Row(rows, "Anchor Point (internal)")?.StartsWith(Skunk.Commands.Kit.Length(doc, 100) + ",") == true, "the model point at that latitude and longitude: " + Row(rows, "Anchor Point (internal)"));
                Check(Row(rows, "Geographic Location") == null, "no 'not set' row once it is set");

                // a model without a Skunk site starts with true north along Rhino's north; the first processing stores it
                Near(SiteModel.Get(doc).AngleToTrueNorthDegrees, 30, 1e-9, "a new Skunk site starts with true north along Rhino's north (30° East)");
                Eq(Row(Panel(), "Rhino North"), "30.00° East (same as true north)", "the two agree");
                SkunkService.Instance.ContextChanged(doc);
                SkunkService.Instance.ProcessNow(doc);
                Check(DocStore.Get<SiteData>(doc, Keys.SiteEntry) != null, "the first processing stores the site");
                Near(SiteModel.Get(doc).AngleToTrueNorthDegrees, 30, 1e-9, "...with Rhino's north as true north");
                Near(RhinoAngle(), 30, 1e-6, "...and Rhino's north as it was");

                // while they agree, Rhino's north (its Sun too) follows true north; the location stays
                var site = SiteModel.Get(doc);
                site.SetAngleToTrueNorth(60, RotationPivot.ProjectBasePoint);
                Save(site);
                Near(RhinoAngle(), 60, 1e-6, "Rhino's north follows true north while they agree (60° East)");
                Near(doc.RenderSettings.Sun.North, 30, 1e-6, "the Sun's north too (degrees from world X: 60° East of Y is 30°)");
                g = GeoLocation.Read(doc);
                Check(g.IsSet && Math.Abs(g.Latitude - 39.7392) < 1e-9 && Math.Abs(g.Longitude + 104.9903) < 1e-9 && g.Name == "Sample Place",
                    "the location did not move when Rhino's north turned");
                Check(Math.Abs(doc.RenderSettings.Sun.Latitude - 39.7392) < 1e-9 && Math.Abs(doc.RenderSettings.Sun.Longitude + 104.9903) < 1e-9, "the Sun has the model's location");

                // a north set apart in Rhino stays apart; the panel shows both
                Check(GeoLocation.SetRhinoNorth(doc, Toward(10)), "Rhino's north set apart (10° East)");
                site = SiteModel.Get(doc);
                site.SetAngleToTrueNorth(75, RotationPivot.ProjectBasePoint);
                Save(site);
                Near(RhinoAngle(), 10, 1e-6, "a north set apart in Rhino stays when true north turns (75° East)");
                Eq(Row(Panel(), "Rhino North"), "10.00° East (true north is 75.00° East)", "the panel shows both");
                string summary = GeoLocation.Read(doc).Summary(doc, SiteModel.Get(doc));
                Check(summary.Contains("Sample Place, 39.739200° N, 104.990300° W, elevation") && summary.Contains("10.00° East (true north is 75.00° East)"), "-SkLocation's line: " + summary);

                // Location > True North from Rhino: true north turns onto Rhino's north about the project base point (away
                // from the survey point, which has shared values of its own)
                site = SiteModel.Get(doc);
                site.MoveProjectBasePoint(new Point3d(50, 30, 0));
                site.Data.SpEast = 1000;
                site.Data.SpNorth = 5000;
                Save(site);
                Near(RhinoAngle(), 10, 1e-6, "moving the base points leaves Rhino's north");
                site = SiteModel.Get(doc);
                var pbpShared = site.ProjectBasePointShared;
                var originShared = site.Shared(Point3d.Origin);
                GeoLocation.UseRhinoNorth(doc, site, RotationPivot.ProjectBasePoint);
                Save(site);
                site = SiteModel.Get(doc);
                Near(site.AngleToTrueNorthDegrees, 10, 1e-6, "True North from Rhino: true north is Rhino's north (10° East)");
                Near(site.ProjectBasePointShared, pbpShared, 1e-6, "...turned about the project base point (its shared coordinates stay)");
                Check(site.Shared(Point3d.Origin).DistanceTo(originShared) > 1, "...while the internal origin's shared coordinates turn about it");

                // Location > Rhino's North from True North
                GeoLocation.SetRhinoNorth(doc, Toward(-20));
                Check(GeoLocation.SetRhinoNorth(doc, SiteModel.Get(doc).TrueNorth) && Math.Abs(RhinoAngle() - 10) < 1e-6, "Rhino's North from True North: back to 10° East");

                // true north turned on its named CPlane (Rhino's Named CPlanes panel): saved, and Rhino's north follows
                site = SiteModel.Get(doc);
                var turned = new Plane(site.Survey.Origin, Toward(115), Toward(25));
                NamedPlanes.Set(doc, site.Data.SpCPlane, turned);
                SkunkService.Instance.ContextChanged(doc);
                SkunkService.Instance.ProcessNow(doc);
                Near(SiteModel.Get(doc).AngleToTrueNorthDegrees, 25, 1e-6, "a true north turned on its named CPlane (25° East)");
                Near(SiteModel.PlaneFrom(DocStore.Get<SiteData>(doc, Keys.SiteEntry).SpPlane).Value.YAxis.X, Toward(25).X, 1e-9, "...is saved as the last known plane");
                Near(RhinoAngle(), 25, 1e-6, "...and Rhino's north follows it");

                // latitude and longitude of model points (Rhino's model-to-earth transform)
                Check(GeoLocation.TryLatLong(doc, new Point3d(100, 200, 0), out double lat, out double lon) && Math.Abs(lat - 39.7392) < 1e-7 && Math.Abs(lon + 104.9903) < 1e-7,
                    "the anchor point is at the model's latitude and longitude: " + lat.ToString("0.######") + ", " + lon.ToString("0.######"));
                GeoLocation.TryLatLong(doc, new Point3d(100, 200, 0) + GeoLocation.RhinoNorth(doc) * 1000, out double lat2, out double lon2);
                Check(lat2 - lat > 0.0027 && lat2 - lat < 0.00278 && Math.Abs(lon2 - lon) < 1e-7,
                    "1000 ft along Rhino's north is due north, about 0.00274° of latitude: " + (lat2 - lat).ToString("0.#######") + ", " + (lon2 - lon).ToString("0.#######"));
                Eq(GeoLocation.LatLongText(doc, new Point3d(100, 200, 0)), "39.739200° N, 104.990300° W", "a point's latitude and longitude in words");

                // a location changed in Rhino raises no event Skunk sees: the idle check refreshes the panel
                SkunkService.Instance.CheckGeoLocation(doc);
                Check(!SkunkService.Instance.CheckGeoLocation(doc), "an unchanged location asks for no refresh");
                using (var a = doc.EarthAnchorPoint)
                {
                    a.Name = "Renamed Place";
                    doc.EarthAnchorPoint = a;
                }
                Check(SkunkService.Instance.CheckGeoLocation(doc), "a location renamed in Rhino refreshes the panel");

                // the Location tab's Set in Rhino opens Rhino's document location: Document Properties > Location (world map)
                string page = Skunk.UI.LocationDialog.RhinoLocationPage;
                Check(page == "_-DocumentPropertiesPage _Location" && Rhino.Commands.Command.IsCommand("DocumentPropertiesPage")
                      && Rhino.Commands.Command.LookupCommandId("DocumentPropertiesPage", true) != Guid.Empty
                      && !Rhino.Commands.Command.IsCommand("SkunkNoSuchCommand"),
                    "Set in Rhino opens Rhino's document location with a Rhino command: " + page);

                // what Skunk relies on in Rhino 9: a location set on the Sun reaches the model's location
                var sun = doc.RenderSettings.Sun;
                sun.BeginChange(Rhino.Render.RenderContent.ChangeContexts.Program);
                sun.Latitude = 51.5;
                sun.Longitude = -0.12;
                sun.EndChange();
                g = GeoLocation.Read(doc);
                Check(Math.Abs(g.Latitude - 51.5) < 1e-9 && Math.Abs(g.Longitude + 0.12) < 1e-9, "a location set on Rhino's Sun is the model's location (Rhino writes it to the anchor point)");
            }
            finally
            {
                doc.Dispose();
            }

            // a model without a location: Rhino's north turns, the location stays not set
            var bare = RhinoDoc.Create(null);
            try
            {
                Check(GeoLocation.SetRhinoNorth(bare, new Vector3d(1, 1, 0)), "Rhino's north set in a model without a location");
                var g = GeoLocation.Read(bare);
                Check(!g.IsSet && Math.Abs(SiteModel.ClockwiseAngle(Vector3d.YAxis, g.North) - 45) < 1e-6, "...turns (45° East) and the location stays not set");
                Check(!GeoLocation.TryLatLong(bare, Point3d.Origin, out _, out _), "no latitude and longitude without a location");
            }
            finally
            {
                bare.Dispose();
            }
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
