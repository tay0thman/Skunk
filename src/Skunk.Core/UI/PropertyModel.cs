using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Skunk.Commands;
using Skunk.Core;
using Skunk.Elements;
using Skunk.Service;
using Skunk.Site;
using Skunk.Types;
using Skunk.Units;

namespace Skunk.UI
{
    /// <summary>Builds the parameter lists shown by the Properties palette and the Type Properties dialog.</summary>
    internal static partial class PropertyModel
    {
        // ---- value helpers ----------------------------------------------------------------------

        public static string Len(RhinoDoc doc, double model) => Kit.Length(doc, model);

        public static bool ParseLen(RhinoDoc doc, string text, out double model)
        {
            model = 0;
            if (!UnitFormat.TryParseLength(text, DocUnits.MetersPerUnit(doc), out double m))
                return false;
            model = DocUnits.FromMeters(doc, m);
            return true;
        }

        static bool ParseDouble(string text, out double v) =>
            double.TryParse((text ?? "").Trim().TrimEnd('°'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

        static string Num(double v, int decimals = 2) => UnitFormat.Decimal(v, decimals, false, false, false);

        public static string Edge(LevelInfo level, SiteModel site, int edge)
        {
            var pl = level.Footprint;
            var u = pl[edge + 1] - pl[edge];
            u.Z = 0;
            u.Unitize();
            var n = Vector3d.CrossProduct(u, Vector3d.ZAxis);
            double north = n * site.ProjectNorth, east = n * site.ProjectEast;
            if (north > 0.92) return "North";
            if (north < -0.92) return "South";
            if (east > 0.92) return "East";
            if (east < -0.92) return "West";
            return "Edge " + (edge + 1);
        }

        // ---- type parameters ----------------------------------------------------------------------

        static readonly string[] Fractions = { "1/2\"", "1/4\"", "1/8\"", "1/16\"", "1/32\"", "1/64\"", "1/128\"", "1/256\"" };
        static readonly int[] FractionDen = { 2, 4, 8, 16, 32, 64, 128, 256 };
        static readonly string[] Decimals = { "0", "0.0", "0.00", "0.000", "0.0000", "0.00000", "0.000000" };

        /// <summary>Parameters of a type. <paramref name="changed"/> runs after each edit (preview / dirty flag).</summary>
        public static List<Param> Type(RhinoDoc doc, SkunkType t, Action changed)
        {
            var paper = DocUnits.Paper(doc);   // Rhino's page (layout) units
            var list = new List<Param>();
            Param Paper(string group, string name, Func<double> get, Action<double> set, string tip = "") =>
                Param.Edit(group, name, UnitFormat.PaperSize(get(), paper), s =>
                {
                    if (!UnitFormat.TryParsePaperSize(s, paper, out double mm))
                        return false;
                    set(mm);
                    changed();
                    return true;
                }, tip);
            Param Text(string group, string name, Func<string> get, Action<string> set, string tip = "") =>
                Param.Edit(group, name, get(), s => { set(s); changed(); return true; }, tip);
            Param Bool(string group, string name, Func<bool> get, Action<bool> set, string tip = "") =>
                Param.Check(group, name, get(), v => { set(v); changed(); }, tip);
            Param Enum<T>(string group, string name, Func<T> get, Action<T> set, string tip = "") where T : struct, System.Enum =>
                Param.Choice(group, name, EtoKit.Words(EtoKit.Names<T>()), EtoKit.IndexOf(get()), i => { set(EtoKit.ValueAt<T>(i)); changed(); }, tip);

            switch (t)
            {
                case SpotElevationType se:
                    list.Add(Enum("Graphics", "Symbol", () => se.Symbol, v => se.Symbol = v));
                    list.Add(Paper("Graphics", "Symbol Size", () => se.SymbolSizeMM, v => se.SymbolSizeMM = v));
                    list.Add(Paper("Graphics", "Leader Shoulder", () => se.ShoulderMM, v => se.ShoulderMM = v));
                    break;
                case SpotCoordinateType sc:
                    list.Add(Enum("Graphics", "Symbol", () => sc.Symbol, v => sc.Symbol = v));
                    list.Add(Paper("Graphics", "Symbol Size", () => sc.SymbolSizeMM, v => sc.SymbolSizeMM = v));
                    list.Add(Paper("Graphics", "Leader Shoulder", () => sc.ShoulderMM, v => sc.ShoulderMM = v));
                    break;
                case SpotSlopeType ss:
                    list.Add(Paper("Graphics", "Leader Line Length", () => ss.ArrowLengthMM, v => ss.ArrowLengthMM = v, "Length of the slope arrow on paper"));
                    list.Add(Paper("Graphics", "Offset From Model", () => ss.OffsetFromModelMM, v => ss.OffsetFromModelMM = v));
                    list.Add(Enum("Graphics", "Arrowhead", () => ss.Arrowhead, v => ss.Arrowhead = v));
                    list.Add(Paper("Graphics", "Arrowhead Size", () => ss.ArrowSizeMM, v => ss.ArrowSizeMM = v));
                    list.Add(Enum("Graphics", "Slope Direction", () => ss.Direction, v => ss.Direction = v));
                    list.Add(Param.Choice("Graphics", "Placement", new[] { "On host (follows the slope)", "View plane (flat)" },
                        ss.Placement == SlopePlacement.OnHost ? 0 : 1, i => { ss.Placement = i == 0 ? SlopePlacement.OnHost : SlopePlacement.ViewPlane; changed(); },
                        "On host: the arrow lies on the surface down the fall line (or along the edge). View plane: flat in the CPlane it was placed in."));
                    break;
                case LevelType lt:
                    list.Add(Enum("Graphics", "Symbol", () => lt.HeadSymbol, v => lt.HeadSymbol = v));
                    list.Add(Paper("Graphics", "Symbol Size", () => lt.SymbolSizeMM, v => lt.SymbolSizeMM = v));
                    list.Add(Paper("Graphics", "Head Length", () => lt.HeadLengthMM, v => lt.HeadLengthMM = v, "Leader from the level line end to the head text"));
                    list.Add(Bool("Graphics", "Symbol at End 1 Default", () => lt.End1Default, v => lt.End1Default = v));
                    list.Add(Bool("Graphics", "Symbol at End 2 Default", () => lt.End2Default, v => lt.End2Default = v));
                    string[] sides = { "South", "East", "North", "West" };
                    for (int i = 0; i < 4; i++)
                    {
                        int k = i;
                        list.Add(Bool("Graphics", "Heads on " + sides[k] + " Elevation", () => lt.EdgesDefault != null && k < lt.EdgesDefault.Length && lt.EdgesDefault[k], v =>
                        {
                            var e = (lt.EdgesDefault ?? new bool[4]).ToList();
                            while (e.Count < 4) e.Add(false);
                            e[k] = v;
                            lt.EdgesDefault = e.ToArray();
                        }, "Default for new levels with rectangular extents"));
                    }
                    list.Add(Bool("Graphics", "Show Elevation", () => lt.ShowElevation, v => lt.ShowElevation = v));
                    {
                        var patterns = new List<string> { Styles.ByLayer };
                        patterns.AddRange(Styles.LinetypeNames(doc));
                        string current = string.IsNullOrWhiteSpace(lt.LinePattern) ? Styles.ByLayer : lt.LinePattern;
                        if (!patterns.Contains(current))
                            patterns.Add(current);
                        list.Add(Param.Choice("Graphics", "Line Pattern", patterns.ToArray(), patterns.IndexOf(current), i =>
                        {
                            lt.LinePattern = patterns[i] == Styles.ByLayer ? "" : patterns[i];
                            changed();
                        }, "Linetype of the level line; By Layer = the Skunk::Levels layer (Skunk Datum)"));
                        list.Add(Param.Edit("Graphics", "Line Pattern Scale", Num(lt.LinePatternScale, 3), t =>
                        {
                            if (!ParseDouble(t, out double sc) || sc <= 0)
                                return false;
                            lt.LinePatternScale = sc;
                            changed();
                            return true;
                        }, "Per-object linetype scale of the level line"));
                    }
                    break;
                case NorthArrowType na:
                    list.Add(Paper("Graphics", "Size", () => na.SizeMM, v => na.SizeMM = v));
                    break;
            }
            list.Add(Param.Color("Graphics", "Color", t.Color, v => { t.Color = v; changed(); }));
            list.Add(Param.Edit("Graphics", "Line Weight (mm)", t.LineWeightMM <= 0 ? "By Layer" : Num(t.LineWeightMM), s =>
            {
                if (s.Trim().Equals("by layer", StringComparison.OrdinalIgnoreCase) || s.Trim() == "0")
                    t.LineWeightMM = 0;
                else if (ParseDouble(s, out double w) && w >= 0)
                    t.LineWeightMM = w;
                else
                    return false;
                changed();
                return true;
            }, "Print width in millimetres, or By Layer"));

            if (!(t is NorthArrowType))
            {
                list.Add(Paper("Text", "Text Size", () => t.TextSizeMM, v => t.TextSizeMM = v));
                list.Add(Paper("Text", "Text Offset", () => t.TextGapMM, v => t.TextGapMM = v));
                list.Add(Text("Text", "Text Font", () => t.Font, v => t.Font = v));
                list.Add(Bool("Text", "Bold", () => t.Bold, v => t.Bold = v));
                list.Add(Bool("Text", "Italic", () => t.Italic, v => t.Italic = v));
                list.Add(Param.Choice("Text", "Text Background", new[] { "Transparent", "Opaque" }, t.OpaqueBackground ? 1 : 0, i => { t.OpaqueBackground = i == 1; changed(); }));
            }
            switch (t)
            {
                case SpotElevationType se:
                    list.Add(Enum("Text", "Text Location", () => se.TextLocation, v => se.TextLocation = v));
                    list.Add(Enum("Constraints", "Elevation Origin", () => se.ElevationOrigin, v => se.ElevationOrigin = v));
                    list.Add(Text("Constraints", "Origin CPlane", () => se.OriginCPlane, v => se.OriginCPlane = v, "Named CPlane used when Elevation Origin = Named CPlane"));
                    list.Add(Enum("Constraints", "Display Elevations (default)", () => se.Display, v => se.Display = v, "New spots start with this; each spot can change it"));
                    list.Add(Text("Text", "Elevation Indicator", () => se.ElevationIndicator, v => se.ElevationIndicator = v));
                    list.Add(Text("Text", "Top Indicator", () => se.TopIndicator, v => se.TopIndicator = v));
                    list.Add(Text("Text", "Bottom Indicator", () => se.BottomIndicator, v => se.BottomIndicator = v));
                    list.Add(Param.Choice("Text", "Indicator as Prefix / Suffix", new[] { "Prefix", "Suffix" }, se.IndicatorAsPrefix ? 0 : 1, i => { se.IndicatorAsPrefix = i == 0; changed(); }));
                    AddUnits(doc, list, () => se.Units, v => se.Units = v, changed);
                    break;
                case SpotCoordinateType sc:
                    list.Add(Enum("Text", "Text Location", () => sc.TextLocation, v => sc.TextLocation = v));
                    list.Add(Enum("Constraints", "Coordinate Origin", () => sc.CoordinateOrigin, v => sc.CoordinateOrigin = v));
                    list.Add(Text("Constraints", "Origin CPlane", () => sc.OriginCPlane, v => sc.OriginCPlane = v, "Named CPlane used when Coordinate Origin = Named CPlane"));
                    list.Add(Enum("Text", "Top Value", () => sc.TopValue, v => sc.TopValue = v));
                    list.Add(Enum("Text", "Bottom Value", () => sc.BottomValue, v => sc.BottomValue = v));
                    list.Add(Bool("Text", "Include Elevation", () => sc.IncludeElevation, v => sc.IncludeElevation = v));
                    list.Add(Text("Text", "North / South Indicator", () => sc.NorthIndicator, v => sc.NorthIndicator = v));
                    list.Add(Text("Text", "East / West Indicator", () => sc.EastIndicator, v => sc.EastIndicator = v));
                    list.Add(Text("Text", "Elevation Indicator", () => sc.ElevationIndicator, v => sc.ElevationIndicator = v));
                    list.Add(Param.Choice("Text", "Indicator as Prefix / Suffix", new[] { "Prefix", "Suffix" }, sc.IndicatorAsPrefix ? 0 : 1, i => { sc.IndicatorAsPrefix = i == 0; changed(); }));
                    AddUnits(doc, list, () => sc.Units, v => sc.Units = v, changed);
                    break;
                case SpotSlopeType ss:
                    {
                        var formats = EtoKit.Names<SlopeFormat>();
                        list.Add(Param.Choice("Units", "Slope Units", formats.Select(f => UnitFormat.Describe((SlopeFormat)System.Enum.Parse(typeof(SlopeFormat), f))).ToArray(),
                            EtoKit.IndexOf(ss.Slope.Format), i => { ss.Slope.Format = EtoKit.ValueAt<SlopeFormat>(i); changed(); }));
                        list.Add(Param.Choice("Units", "Rounding (decimal)", Decimals, Math.Max(0, Math.Min(6, ss.Slope.Precision)), i => { ss.Slope.Precision = i; changed(); }));
                        list.Add(Param.Choice("Units", "Rounding (rise fraction)", Fractions, Math.Max(0, Array.IndexOf(FractionDen, ss.Slope.FractionDenominator)), i => { ss.Slope.FractionDenominator = FractionDen[i]; changed(); }));
                        list.Add(Bool("Units", "Show Unit Symbol", () => ss.Slope.ShowUnitSymbol, v => ss.Slope.ShowUnitSymbol = v));
                        list.Add(Bool("Units", "Suppress Trailing Zeros", () => ss.Slope.SuppressTrailingZeros, v => ss.Slope.SuppressTrailingZeros = v));
                        break;
                    }
                case LevelType lt:
                    list.Add(Enum("Constraints", "Elevation Base", () => lt.ElevationBase, v => lt.ElevationBase = v == CoordinateBase.Relative ? CoordinateBase.ProjectBasePoint : v,
                        "Measure from the project base point, the survey point, the internal origin or a named CPlane"));
                    list.Add(Text("Constraints", "Base CPlane", () => lt.BaseCPlane, v => lt.BaseCPlane = v, "Named CPlane used when Elevation Base = Named CPlane"));
                    AddUnits(doc, list, () => lt.Units, v => lt.Units = v, changed);
                    break;
            }
            list.Add(Text("Identity Data", "Type Comments", () => t.Comments, v => t.Comments = v));
            return list;
        }

        static void AddUnits(RhinoDoc doc, List<Param> list, Func<LengthStyle> get, Action<LengthStyle> set, Action changed)
        {
            var u = get() ?? new LengthStyle();
            set(u);
            var formats = EtoKit.Names<LengthFormat>();
            string rhino = UnitFormat.Describe(DocUnits.ModelLengthStyle(doc));
            string Label(string name)
            {
                var f = (LengthFormat)System.Enum.Parse(typeof(LengthFormat), name);
                return f == LengthFormat.Project ? UnitFormat.Describe(f) + " (" + rhino + ")" : UnitFormat.Describe(f);
            }
            const string fromRhino = "Not used with Rhino document units: the rounding then comes from Rhino (Document Properties > Units)";
            list.Add(Param.Choice("Units", "Units Format", formats.Select(Label).ToArray(),
                EtoKit.IndexOf(u.Format), i => { u.Format = EtoKit.ValueAt<LengthFormat>(i); changed(); },
                "Rhino document units follow Document Properties > Units (unit system, distance display and precision) and update with them"));
            list.Add(Param.Choice("Units", "Rounding (fractional)", Fractions, Math.Max(0, Array.IndexOf(FractionDen, u.FractionDenominator)), i => { u.FractionDenominator = FractionDen[i]; changed(); }, fromRhino));
            list.Add(Param.Choice("Units", "Rounding (decimal)", Decimals, Math.Max(0, Math.Min(6, u.Precision)), i => { u.Precision = i; changed(); }, fromRhino));
            list.Add(Param.Check("Units", "Show Unit Symbol", u.ShowUnitSymbol, v => { u.ShowUnitSymbol = v; changed(); }));
            list.Add(Param.Check("Units", "Suppress Trailing Zeros", u.SuppressTrailingZeros, v => { u.SuppressTrailingZeros = v; changed(); }));
            list.Add(Param.Check("Units", "Suppress 0 Feet", u.SuppressZeroFeet, v => { u.SuppressZeroFeet = v; changed(); }));
            list.Add(Param.Check("Units", "Suppress 0 Inches", u.SuppressZeroInches, v => { u.SuppressZeroInches = v; changed(); }));
            list.Add(Param.Check("Units", "Show + for Positive Values", u.ShowPlusForPositive, v => { u.ShowPlusForPositive = v; changed(); }));
            list.Add(Param.Check("Units", "Use Digit Grouping", u.UseDigitGrouping, v => { u.UseDigitGrouping = v; changed(); }));
            list.Add(Param.Check("Units", "Suppress Spaces", u.SuppressSpaces, v => { u.SuppressSpaces = v; changed(); }));
        }

        /// <summary>Text a type would produce for a sample value (Type Properties preview).</summary>
        public static string Sample(RhinoDoc doc, SkunkType t)
        {
            double mpu = DocUnits.MetersPerUnit(doc);
            double sample = DocUnits.IsImperial(doc) ? 100.5416667 * 0.3048 : 30.645;
            switch (t)
            {
                case SpotElevationType se:
                    return UnitFormat.Compose("", se.ElevationIndicator, UnitFormat.Length(sample, TypeTable.Resolve(doc, se.Units)), "", se.IndicatorAsPrefix);
                case SpotCoordinateType sc:
                    return UnitFormat.Compose("", sc.NorthIndicator, UnitFormat.Length(sample * 10, TypeTable.Resolve(doc, sc.Units)), "", sc.IndicatorAsPrefix) + "   " +
                           UnitFormat.Compose("", sc.EastIndicator, UnitFormat.Length(sample * 3, TypeTable.Resolve(doc, sc.Units)), "", sc.IndicatorAsPrefix);
                case SpotSlopeType ss:
                    return UnitFormat.Slope(0.25 / 12.0, ss.Slope) + "   " + UnitFormat.Slope(0.02, ss.Slope);
                case LevelType lt:
                    return "Level 2  " + UnitFormat.Length(sample / 10, TypeTable.Resolve(doc, lt.Units));
                default:
                    return "";
            }
        }
    }
}
