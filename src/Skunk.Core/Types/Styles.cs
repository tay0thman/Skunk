using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Skunk.Core;

namespace Skunk.Types
{
    /// <summary>
    /// Creates the document resources Skunk elements are drawn with: layers, the datum linetype,
    /// symbol blocks used as arrowheads, and one dimension style per Skunk type.
    /// </summary>
    public static class Styles
    {
        // ---- layers -----------------------------------------------------------------------------

        public static int Layer(RhinoDoc doc, ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Level:
                    return Layers.Ensure(doc, Keys.LevelsLayer, Color.FromArgb(60, 60, 60), 0.13, EnsureDatumLinetype(doc));
                case ElementKind.LevelHead:
                    return Layers.Ensure(doc, Keys.LevelHeadsLayer, Color.FromArgb(60, 60, 60), 0.18);
                case ElementKind.SpotElevation:
                    return Layers.Ensure(doc, Keys.SpotElevationsLayer, Color.FromArgb(0, 70, 140), 0.18);
                case ElementKind.SpotCoordinate:
                    return Layers.Ensure(doc, Keys.SpotCoordinatesLayer, Color.FromArgb(0, 110, 90), 0.18);
                case ElementKind.SpotSlope:
                    return Layers.Ensure(doc, Keys.SpotSlopesLayer, Color.FromArgb(150, 60, 0), 0.18);
                case ElementKind.NorthArrow:
                    return Layers.Ensure(doc, Keys.NorthArrowsLayer, Color.Black, 0.25);
                case ElementKind.PlanCut:
                    return Layers.Ensure(doc, Keys.PlanCutsLayer, Color.FromArgb(200, 120, 0), -1);
                case ElementKind.ProjectBasePoint:
                case ElementKind.SurveyPoint:
                default:
                    // plot weight -1 = "No print": base points never print
                    return Layers.Ensure(doc, Keys.SiteLayer, Color.FromArgb(40, 90, 200), -1);
            }
        }

        /// <summary>Center-style datum linetype (long dash, gap, short dash, gap) sized for the annotation scale.</summary>
        public static string EnsureDatumLinetype(RhinoDoc doc)
        {
            double scale = DocUnits.AnnotationScale(doc);
            double u = DocUnits.FromMeters(doc, 0.001) * scale;
            var segments = new[] { 19.05 * u, -3.175 * u, 3.175 * u, -3.175 * u };
            var existing = doc.Linetypes.FindName(Keys.LinetypeDatum);
            if (existing != null)
            {
                if (Math.Abs(existing.PatternLength - (19.05 + 3 * 3.175) * u) > 1e-9 * Math.Max(1, u))
                {
                    var lt = new Linetype { Name = Keys.LinetypeDatum };
                    foreach (var s in segments)
                        lt.AppendSegment(Math.Abs(s), s > 0);
                    doc.Linetypes.Modify(lt, existing.Index, true);
                }
                return Keys.LinetypeDatum;
            }
            var linetype = new Linetype { Name = Keys.LinetypeDatum };
            foreach (var s in segments)
                linetype.AppendSegment(Math.Abs(s), s > 0);
            doc.Linetypes.Add(linetype);
            return Keys.LinetypeDatum;
        }

        // ---- symbol blocks ----------------------------------------------------------------------

        /// <summary>Block name for a symbol drawn as a user arrowhead, or null when Rhino has a built-in one.</summary>
        static string BlockName(SymbolKind s)
        {
            switch (s)
            {
                case SymbolKind.TargetFilled: return Keys.BlockPrefix + "Target Filled";
                case SymbolKind.Target: return Keys.BlockPrefix + "Target";
                case SymbolKind.Circle: return Keys.BlockPrefix + "Circle";
                case SymbolKind.Triangle: return Keys.BlockPrefix + "Triangle";
                default: return null;
            }
        }

        /// <summary>
        /// Arrow settings for a symbol: built-in arrowheads where Rhino has one, otherwise a unit-size
        /// block (diameter 1 around the insertion point, scaled by the arrow size).
        /// </summary>
        public static void ArrowFor(RhinoDoc doc, SymbolKind s, out DimensionStyle.ArrowType type, out Guid blockId)
        {
            blockId = Guid.Empty;
            switch (s)
            {
                case SymbolKind.None: type = DimensionStyle.ArrowType.None; return;
                case SymbolKind.Dot: type = DimensionStyle.ArrowType.Dot; return;
                case SymbolKind.Arrow: type = DimensionStyle.ArrowType.SolidTriangle; return;
                case SymbolKind.Tick: type = DimensionStyle.ArrowType.Tick; return;
            }
            blockId = EnsureSymbolBlock(doc, s);
            type = blockId == Guid.Empty ? DimensionStyle.ArrowType.Dot : DimensionStyle.ArrowType.UserBlock;
        }

        public static Guid EnsureSymbolBlock(RhinoDoc doc, SymbolKind s)
        {
            string name = BlockName(s);
            if (name == null)
                return Guid.Empty;
            var existing = doc.InstanceDefinitions.Find(name);
            if (existing != null && !existing.IsDeleted)
                return existing.Id;

            var geometry = new List<GeometryBase>();
            var o = Point3d.Origin;
            const double r = 0.5;
            switch (s)
            {
                case SymbolKind.Target:
                case SymbolKind.TargetFilled:
                    geometry.Add(new ArcCurve(new Circle(Plane.WorldXY, r)));
                    geometry.Add(new LineCurve(new Point3d(-r, 0, 0), new Point3d(r, 0, 0)));
                    geometry.Add(new LineCurve(new Point3d(0, -r, 0), new Point3d(0, r, 0)));
                    if (s == SymbolKind.TargetFilled)
                    {
                        geometry.AddRange(Quadrant(doc, 0));
                        geometry.AddRange(Quadrant(doc, 2));
                    }
                    break;
                case SymbolKind.Circle:
                    geometry.Add(new ArcCurve(new Circle(Plane.WorldXY, r)));
                    break;
                case SymbolKind.Triangle:
                    {
                        // filled triangle pointing at the insertion point from above (spot elevation triangle)
                        var tri = new Polyline(new[] { o, new Point3d(-0.6, 1.0, 0), new Point3d(0.6, 1.0, 0), o });
                        var crv = new PolylineCurve(tri);
                        geometry.Add(crv);
                        var h = SolidHatch(doc, crv);
                        if (h != null)
                            geometry.Add(h);
                        break;
                    }
            }

            var attributes = new List<ObjectAttributes>();
            foreach (var _ in geometry)
            {
                attributes.Add(new ObjectAttributes
                {
                    ColorSource = ObjectColorSource.ColorFromParent,
                    PlotColorSource = ObjectPlotColorSource.PlotColorFromParent,
                    PlotWeightSource = ObjectPlotWeightSource.PlotWeightFromParent,
                    LayerIndex = 0,
                });
            }
            int index = doc.InstanceDefinitions.Add(name, "Skunk symbol (unit size, scaled as an arrowhead)", o, geometry, attributes);
            return index >= 0 ? doc.InstanceDefinitions[index].Id : Guid.Empty;
        }

        static IEnumerable<GeometryBase> Quadrant(RhinoDoc doc, int quadrant)
        {
            const double r = 0.5;
            double a0 = quadrant * Math.PI / 2.0;
            var arc = new Arc(new Circle(Plane.WorldXY, r), new Interval(a0, a0 + Math.PI / 2.0));
            var pc = new PolyCurve();
            pc.Append(new Line(Point3d.Origin, arc.StartPoint));
            pc.Append(arc);
            pc.Append(new Line(arc.EndPoint, Point3d.Origin));
            var h = SolidHatch(doc, pc);
            if (h != null)
                yield return h;
        }

        static Hatch SolidHatch(RhinoDoc doc, Curve boundary)
        {
            int pattern = SolidPatternIndex(doc);
            if (pattern < 0)
                return null;
            var hatches = Hatch.Create(boundary, pattern, 0, 1, doc.ModelAbsoluteTolerance > 0 ? Math.Min(doc.ModelAbsoluteTolerance, 1e-4) : 1e-4);
            return hatches != null && hatches.Length > 0 ? hatches[0] : null;
        }

        static int SolidPatternIndex(RhinoDoc doc)
        {
            var solid = doc.HatchPatterns.FindName("Solid");
            if (solid != null)
                return solid.Index;
            return doc.HatchPatterns.Add(HatchPattern.Defaults.Solid);
        }

        // ---- dimension styles -------------------------------------------------------------------

        public static string CategoryName(ElementKind k)
        {
            switch (k)
            {
                case ElementKind.SpotElevation: return "Spot Elevation";
                case ElementKind.SpotCoordinate: return "Spot Coordinate";
                case ElementKind.SpotSlope: return "Spot Slope";
                case ElementKind.Level:
                case ElementKind.LevelHead: return "Level Head";
                case ElementKind.NorthArrow: return "North Arrow";
                default: return k.ToString();
            }
        }

        public const string ByLayer = "<By Layer>";
        public const string ByType = "<By Type>";

        /// <summary>Rhino's standard linetypes (LinetypeTable.LoadDefaultLinetypes adds them).</summary>
        public static readonly string[] RhinoDefaultLinetypes = { "Continuous", "Border", "Center", "DashDot", "Dashed", "Dots", "Hidden" };

        /// <summary>Linetypes to offer for a level line: Skunk Datum, Rhino's standard ones, and the model's own.</summary>
        public static List<string> LinetypeNames(RhinoDoc doc)
        {
            var names = new List<string> { Keys.LinetypeDatum };
            foreach (var n in RhinoDefaultLinetypes)
                if (!names.Contains(n))
                    names.Add(n);
            var extra = new List<string>();
            foreach (var lt in doc.Linetypes)
            {
                if (lt == null || lt.IsDeleted || string.IsNullOrEmpty(lt.Name))
                    continue;
                if (!names.Contains(lt.Name) && !extra.Contains(lt.Name))
                    extra.Add(lt.Name);
            }
            extra.Sort(StringComparer.OrdinalIgnoreCase);
            names.AddRange(extra);
            return names;
        }

        /// <summary>
        /// Linetype index for a name (-1 = Continuous); loads Skunk Datum or Rhino's standard linetypes
        /// when the model doesn't have them yet. Returns false for "by layer" or an unknown name.
        /// </summary>
        public static bool TryLinetype(RhinoDoc doc, string name, out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(name) || name == ByLayer || name == ByType)
                return false;
            if (string.Equals(name, "Continuous", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name == Keys.LinetypeDatum)
                EnsureDatumLinetype(doc);
            var lt = doc.Linetypes.FindName(name);
            if (lt == null && Array.IndexOf(RhinoDefaultLinetypes, name) >= 0)
            {
                doc.Linetypes.LoadDefaultLinetypes();
                lt = doc.Linetypes.FindName(name);
            }
            if (lt == null || lt.IsDeleted)
                return false;
            index = lt.Index;
            return true;
        }

        public static string DimStyleName(SkunkType t) =>
            Keys.DimStylePrefix + CategoryName(t.Kind) + " - " + Sanitize(t.Name);

        static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Default";
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if ("()[]{}".IndexOf(chars[i]) >= 0 && i == 0)
                    chars[i] = '_';
            return new string(chars).Trim();
        }

        /// <summary>Create or update the dimension style of a type and return it (document instance).</summary>
        public static DimensionStyle DimStyle(RhinoDoc doc, SkunkType t)
        {
            string name = DimStyleName(t);
            var existing = doc.DimStyles.FindName(name);
            var ds = existing != null ? existing.Duplicate() : new DimensionStyle();
            ds.Name = name;
            Configure(doc, ds, t);
            if (existing == null)
            {
                int index = doc.DimStyles.Add(ds, false);
                return index >= 0 ? doc.DimStyles[index] : doc.DimStyles.Current;
            }
            doc.DimStyles.Modify(ds, existing.Index, true);
            return doc.DimStyles[existing.Index];
        }

        static void Configure(RhinoDoc doc, DimensionStyle ds, SkunkType t)
        {
            double S(double mm) => DocUnits.PaperToStyle(doc, mm);
            ds.TextHeight = S(t.TextSizeMM);
            try
            {
                var font = Rhino.DocObjects.Font.FromQuartetProperties(string.IsNullOrWhiteSpace(t.Font) ? "Arial" : t.Font, t.Bold, t.Italic);
                if (font != null)
                    ds.Font = font;
            }
            catch (Exception ex)
            {
                Log.Debug("font " + t.Font + ": " + ex.Message);
            }
            ds.TextGap = S(t.TextGapMM);
            ds.DrawTextMask = t.OpaqueBackground;
            ds.MaskColorSource = DimensionStyle.MaskType.BackgroundColor;
            ds.MaskOffset = S(0.5);
            ds.DrawForward = true;
            ds.LeaderCurveType = DimensionStyle.LeaderCurveStyle.Polyline;
            ds.LeaderContentAngleType = DimensionStyle.LeaderContentAngleStyle.Horizontal;
            ds.LeaderTextVerticalAlignment = TextVerticalAlignment.Middle;
            ds.LeaderTextHorizontalAlignment = TextHorizontalAlignment.Auto;
            ds.LeaderTextOrientation = TextOrientation.InPlane;
            ds.TextOrientation = TextOrientation.InPlane;
            ds.LengthFactor = 1.0;

            switch (t)
            {
                case SpotElevationType se:
                    Leader(doc, ds, se.Symbol, S(se.SymbolSizeMM), S(se.ShoulderMM));
                    ds.LeaderTextVerticalAlignment = Vertical(se.TextLocation);
                    break;
                case SpotCoordinateType sc:
                    Leader(doc, ds, sc.Symbol, S(sc.SymbolSizeMM), S(sc.ShoulderMM));
                    ds.LeaderTextVerticalAlignment = Vertical(sc.TextLocation);
                    break;
                case LevelType lt:
                    Leader(doc, ds, lt.HeadSymbol, S(lt.SymbolSizeMM), 0);
                    ds.LeaderHasLanding = false;
                    break;
                case SpotSlopeType ss:
                    {
                        ArrowFor(doc, ss.Arrowhead, out var at, out var block);
                        ds.ArrowType1 = DimensionStyle.ArrowType.None;
                        ds.ArrowType2 = at;
                        if (block != Guid.Empty)
                            ds.ArrowBlockId2 = block;
                        ds.ArrowLength = S(ss.ArrowSizeMM);
                        ds.SuppressExtension1 = true;
                        ds.SuppressExtension2 = true;
                        ds.ExtensionLineExtension = 0;
                        ds.ExtensionLineOffset = 0;
                        ds.DimensionLineExtension = 0;
                        ds.FixedExtensionOn = false;
                        ds.DimTextLocation = DimensionStyle.TextLocation.AboveDimLine;
                        ds.DimTextOrientation = TextOrientation.InPlane;
                        ds.DimTextAngleType = DimensionStyle.LeaderContentAngleStyle.Aligned;
                        ds.TextHorizontalAlignment = TextHorizontalAlignment.Center;
                        ds.FitText = DimensionStyle.TextFit.TextInside;
                        ds.FitArrow = DimensionStyle.ArrowFit.ArrowsInside;
                        ds.ForceDimensionLineBetweenExtensionLines = true;
                        break;
                    }
                case NorthArrowType _:
                    break;
            }
        }

        /// <summary>Text location relative to the leader shoulder.</summary>
        static TextVerticalAlignment Vertical(SpotTextLocation location)
        {
            switch (location)
            {
                case SpotTextLocation.AboveLeader: return TextVerticalAlignment.Bottom;
                case SpotTextLocation.BelowLeader: return TextVerticalAlignment.Top;
                default: return TextVerticalAlignment.Middle;
            }
        }

        static void Leader(RhinoDoc doc, DimensionStyle ds, SymbolKind symbol, double symbolSize, double shoulder)
        {
            ArrowFor(doc, symbol, out var at, out var block);
            ds.LeaderArrowType = at;
            if (block != Guid.Empty)
                ds.LeaderArrowBlockId = block;
            ds.LeaderArrowLength = symbolSize;
            ds.LeaderHasLanding = shoulder > 0;
            ds.LeaderLandingLength = shoulder > 0 ? shoulder : symbolSize * 0.5;
        }

        /// <summary>Attributes of a new Skunk object of a given kind and type.</summary>
        public static ObjectAttributes Attributes(RhinoDoc doc, ElementKind kind, SkunkType type)
        {
            var a = doc.CreateDefaultAttributes();
            a.LayerIndex = Layer(doc, kind);
            if (type != null && type.Color != 0)
            {
                a.ColorSource = ObjectColorSource.ColorFromObject;
                a.ObjectColor = Color.FromArgb(type.Color);
                a.PlotColorSource = ObjectPlotColorSource.PlotColorFromObject;
                a.PlotColor = Color.FromArgb(type.Color);
            }
            if (type != null && type.LineWeightMM > 0)
            {
                a.PlotWeightSource = ObjectPlotWeightSource.PlotWeightFromObject;
                a.PlotWeight = type.LineWeightMM;
            }
            a.SetUserString(Keys.Kind, kind.ToString());
            return a;
        }

        /// <summary>Re-apply type colour / weight to an existing object's attributes.</summary>
        public static void ApplyTypeAttributes(ObjectAttributes a, SkunkType type)
        {
            if (type == null)
                return;
            if (type.Color != 0)
            {
                a.ColorSource = ObjectColorSource.ColorFromObject;
                a.ObjectColor = Color.FromArgb(type.Color);
                a.PlotColorSource = ObjectPlotColorSource.PlotColorFromObject;
                a.PlotColor = Color.FromArgb(type.Color);
            }
            else
            {
                a.ColorSource = ObjectColorSource.ColorFromLayer;
                a.PlotColorSource = ObjectPlotColorSource.PlotColorFromLayer;
            }
            if (type.LineWeightMM > 0)
            {
                a.PlotWeightSource = ObjectPlotWeightSource.PlotWeightFromObject;
                a.PlotWeight = type.LineWeightMM;
            }
            else
            {
                a.PlotWeightSource = ObjectPlotWeightSource.PlotWeightFromLayer;
            }
        }
    }
}
