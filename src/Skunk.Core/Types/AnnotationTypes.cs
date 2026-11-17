using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Rhino;
using Skunk.Core;
using Skunk.Site;
using Skunk.Units;

namespace Skunk.Types
{
    /// <summary>Symbols drawn at a spot / level head ("Symbol" / "Leader Arrowhead").</summary>
    public enum SymbolKind
    {
        None = 0,
        TargetFilled,
        Target,
        Circle,
        Triangle,
        Dot,
        Arrow,
        Tick,
    }

    /// <summary>Spot elevation "Display Elevations".</summary>
    public enum ElevationDisplay
    {
        Actual = 0,
        Top,
        Bottom,
        TopAndBottom,
    }

    /// <summary>Spot coordinate "Top Value" / "Bottom Value".</summary>
    public enum CoordinateValue
    {
        None = 0,
        NorthSouth,
        EastWest,
        Elevation,
    }

    /// <summary>Spot slope "Slope Direction".</summary>
    public enum SlopeDirection
    {
        Down = 0,
        Up,
    }

    /// <summary>Where a spot slope arrow is drawn.</summary>
    public enum SlopePlacement
    {
        /// <summary>On the host: in the surface's tangent plane down the fall line, or along the edge.</summary>
        OnHost = 0,
        /// <summary>Flat in the plane the spot was placed in (the view's CPlane), like a plan annotation.</summary>
        ViewPlane,
    }

    /// <summary>Grid "Elevation Bubbles": where a grid's bubbles are in elevations and sections.</summary>
    public enum GridElevationBubbles
    {
        Top = 0,
        Bottom,
        Both,
        None,
    }

    /// <summary>Spot "Text Location": Above Leader / Below Leader / In-line with Leader.</summary>
    public enum SpotTextLocation
    {
        AboveLeader = 0,
        BelowLeader,
        InLine,
    }

    /// <summary>
    /// Shared type parameters ("Type Properties"). Paper sizes are millimetres on the printed
    /// sheet; Skunk converts them with the annotation scale (the view scale).
    /// </summary>
    public abstract class SkunkType
    {
        public string Name = "";
        public double TextSizeMM = 2.38125;
        public string Font = "Arial";
        public bool Bold;
        public bool Italic;
        public double TextGapMM = 0.79375;
        /// <summary>"Text Background": Opaque (mask) or Transparent.</summary>
        public bool OpaqueBackground;
        /// <summary>Text / symbol colour (ARGB); 0 = by layer.</summary>
        public int Color;
        /// <summary>Print width in millimetres; 0 = by layer.</summary>
        public double LineWeightMM;
        public string Comments = "";

        [JsonIgnore]
        public abstract ElementKind Kind { get; }
    }

    public sealed class SpotElevationType : SkunkType
    {
        public SymbolKind Symbol = SymbolKind.TargetFilled;
        public double SymbolSizeMM = 3.175;
        public double ShoulderMM = 3.175;
        public SpotTextLocation TextLocation = SpotTextLocation.InLine;
        public CoordinateBase ElevationOrigin = CoordinateBase.ProjectBasePoint;
        public string OriginCPlane = "";
        /// <summary>Default for new spots; each spot can override it ("Display Elevations" is an instance parameter, SpotData.Display).</summary>
        public ElevationDisplay Display = ElevationDisplay.Actual;
        public string ElevationIndicator = "EL ";
        public string TopIndicator = "T.O. ";
        public string BottomIndicator = "B.O. ";
        public bool IndicatorAsPrefix = true;
        public LengthStyle Units = new LengthStyle();

        [JsonIgnore]
        public override ElementKind Kind => ElementKind.SpotElevation;
    }

    public sealed class SpotCoordinateType : SkunkType
    {
        public SymbolKind Symbol = SymbolKind.Target;
        public double SymbolSizeMM = 3.175;
        public double ShoulderMM = 3.175;
        public SpotTextLocation TextLocation = SpotTextLocation.InLine;
        public CoordinateBase CoordinateOrigin = CoordinateBase.SurveyPoint;
        public string OriginCPlane = "";
        public CoordinateValue TopValue = CoordinateValue.NorthSouth;
        public CoordinateValue BottomValue = CoordinateValue.EastWest;
        public bool IncludeElevation;
        public string NorthIndicator = "N ";
        public string EastIndicator = "E ";
        public string ElevationIndicator = "EL ";
        public bool IndicatorAsPrefix = true;
        public LengthStyle Units = new LengthStyle();

        [JsonIgnore]
        public override ElementKind Kind => ElementKind.SpotCoordinate;
    }

    public sealed class SpotSlopeType : SkunkType
    {
        public SlopeStyle Slope = new SlopeStyle();
        /// <summary>"Leader Line Length": the arrow length on paper.</summary>
        public double ArrowLengthMM = 12.7;
        /// <summary>"Offset From Model": gap between the reference and the arrow.</summary>
        public double OffsetFromModelMM = 1.5875;
        public SlopeDirection Direction = SlopeDirection.Down;
        /// <summary>On the host (follows the slope, the default) or flat in the view plane.</summary>
        public SlopePlacement Placement = SlopePlacement.OnHost;
        public SymbolKind Arrowhead = SymbolKind.Arrow;
        public double ArrowSizeMM = 2.38125;

        [JsonIgnore]
        public override ElementKind Kind => ElementKind.SpotSlope;
    }

    public sealed class LevelType : SkunkType
    {
        /// <summary>Level type "Elevation Base".</summary>
        public CoordinateBase ElevationBase = CoordinateBase.ProjectBasePoint;
        public string BaseCPlane = "";
        public SymbolKind HeadSymbol = SymbolKind.TargetFilled;
        public double SymbolSizeMM = 6.35;
        /// <summary>The head's tail past the end of the level line, on paper (at least the symbol's radius; see Styles.HeadTailMM).</summary>
        public double HeadLengthMM = 6.35;
        /// <summary>"Symbol at End 1 Default" / "Symbol at End 2 Default".</summary>
        public bool End1Default;
        public bool End2Default = true;
        /// <summary>Which footprint edges get heads by default (south, east, north, west for a rectangle).</summary>
        public bool[] EdgesDefault = { true, true, false, false };
        public bool ShowElevation = true;
        /// <summary>
        /// Level type "Line Pattern": a linetype name, or empty / "&lt;By Layer&gt;" for the
        /// __Annotations__::Levels layer's linetype (Skunk Datum). Rhino's default linetypes are loaded on demand.
        /// </summary>
        public string LinePattern = "";
        /// <summary>Per-object linetype pattern scale for the level line.</summary>
        public double LinePatternScale = 1.0;
        public LengthStyle Units = new LengthStyle();

        [JsonIgnore]
        public override ElementKind Kind => ElementKind.Level;
    }

    public sealed class GridType : SkunkType
    {
        /// <summary>Bubble diameter on paper.</summary>
        public double BubbleSizeMM = 12.7;
        /// <summary>"Plan View Symbols End 1 (Default)" / "End 2 (Default)".</summary>
        public bool End1Default;
        public bool End2Default = true;
        /// <summary>Grid line linetype: a linetype name, or empty for the __Annotations__::Grids layer's (Skunk Datum).</summary>
        public string LinePattern = "";
        public double LinePatternScale = 1.0;
        /// <summary>Bubbles of the grid in elevations and sections (top, bottom, both or none).</summary>
        public GridElevationBubbles ElevationBubbles = GridElevationBubbles.Top;

        [JsonIgnore]
        public override ElementKind Kind => ElementKind.Grid;
    }

    public sealed class NorthArrowType : SkunkType
    {
        public double SizeMM = 19.05;
        [JsonIgnore]
        public override ElementKind Kind => ElementKind.NorthArrow;
    }

    /// <summary>All types of a document (stored as JSON in the document strings "Skunk/Types").</summary>
    public sealed class TypeLibrary
    {
        public int Version = 1;
        /// <summary>2: length formats follow Rhino's document units unless a type chose its own (see TypeTable.Migrate).</summary>
        public int UnitsVersion;
        /// <summary>2: level heads are filled by default (see TypeTable.Migrate).</summary>
        public int SymbolsVersion;
        public List<SpotElevationType> SpotElevation = new List<SpotElevationType>();
        public List<SpotCoordinateType> SpotCoordinate = new List<SpotCoordinateType>();
        public List<SpotSlopeType> SpotSlope = new List<SpotSlopeType>();
        public List<LevelType> Level = new List<LevelType>();
        public List<NorthArrowType> NorthArrow = new List<NorthArrowType>();
        public List<GridType> Grid = new List<GridType>();

        public IEnumerable<SkunkType> Of(ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.SpotElevation: return SpotElevation;
                case ElementKind.SpotCoordinate: return SpotCoordinate;
                case ElementKind.SpotSlope: return SpotSlope;
                case ElementKind.Level:
                case ElementKind.LevelHead:
                case ElementKind.LevelLine: return Level;
                case ElementKind.NorthArrow: return NorthArrow;
                case ElementKind.Grid:
                case ElementKind.GridHead:
                case ElementKind.GridElevation: return Grid;
                default: return Enumerable.Empty<SkunkType>();
            }
        }

        public SkunkType Find(ElementKind kind, string name)
        {
            var list = Of(kind).ToList();
            return list.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault();
        }

        public T Find<T>(string name) where T : SkunkType => Find(KindOf<T>(), name) as T;

        public static ElementKind KindOf<T>() where T : SkunkType
        {
            if (typeof(T) == typeof(SpotElevationType)) return ElementKind.SpotElevation;
            if (typeof(T) == typeof(SpotCoordinateType)) return ElementKind.SpotCoordinate;
            if (typeof(T) == typeof(SpotSlopeType)) return ElementKind.SpotSlope;
            if (typeof(T) == typeof(LevelType)) return ElementKind.Level;
            if (typeof(T) == typeof(NorthArrowType)) return ElementKind.NorthArrow;
            if (typeof(T) == typeof(GridType)) return ElementKind.Grid;
            return ElementKind.None;
        }

        /// <summary>Add a copy of a type under a new unique name ("Duplicate...").</summary>
        public SkunkType Duplicate(SkunkType source, string newName)
        {
            var name = UniqueName(source.Kind, string.IsNullOrWhiteSpace(newName) ? source.Name + " 2" : newName);
            switch (source)
            {
                case SpotElevationType a: { var c = Json.Clone(a); c.Name = name; SpotElevation.Add(c); return c; }
                case SpotCoordinateType a: { var c = Json.Clone(a); c.Name = name; SpotCoordinate.Add(c); return c; }
                case SpotSlopeType a: { var c = Json.Clone(a); c.Name = name; SpotSlope.Add(c); return c; }
                case LevelType a: { var c = Json.Clone(a); c.Name = name; Level.Add(c); return c; }
                case NorthArrowType a: { var c = Json.Clone(a); c.Name = name; NorthArrow.Add(c); return c; }
                case GridType a: { var c = Json.Clone(a); c.Name = name; Grid.Add(c); return c; }
            }
            return null;
        }

        public bool Remove(SkunkType t)
        {
            switch (t)
            {
                case SpotElevationType a: return SpotElevation.Count > 1 && SpotElevation.Remove(a);
                case SpotCoordinateType a: return SpotCoordinate.Count > 1 && SpotCoordinate.Remove(a);
                case SpotSlopeType a: return SpotSlope.Count > 1 && SpotSlope.Remove(a);
                case LevelType a: return Level.Count > 1 && Level.Remove(a);
                case NorthArrowType a: return NorthArrow.Count > 1 && NorthArrow.Remove(a);
                case GridType a: return Grid.Count > 1 && Grid.Remove(a);
            }
            return false;
        }

        public string UniqueName(ElementKind kind, string wanted)
        {
            var names = new HashSet<string>(Of(kind).Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(wanted))
                return wanted;
            for (int i = 2; ; i++)
            {
                string n = wanted + " " + i;
                if (!names.Contains(n))
                    return n;
            }
        }

        /// <summary>Name of the stock level type new levels use.</summary>
        public const string StockProjectLevel = "Target Head (Project)";

        /// <summary>Default types (US imperial or metric template). Every length follows Rhino's document units.</summary>
        public static TypeLibrary Defaults(bool imperial)
        {
            LengthStyle Units() => new LengthStyle { Format = LengthFormat.Project, ShowUnitSymbol = imperial };
            double text = imperial ? 2.38125 : 2.5;
            var lib = new TypeLibrary { UnitsVersion = TypeTable.CurrentUnitsVersion, SymbolsVersion = TypeTable.CurrentSymbolsVersion };
            lib.SpotElevation.Add(new SpotElevationType { Name = "Target Filled (Project)", Symbol = SymbolKind.TargetFilled, ElevationOrigin = CoordinateBase.ProjectBasePoint, Units = Units(), TextSizeMM = text });
            lib.SpotElevation.Add(new SpotElevationType { Name = "Target (Survey)", Symbol = SymbolKind.Target, ElevationOrigin = CoordinateBase.SurveyPoint, Units = Units(), TextSizeMM = text });
            lib.SpotElevation.Add(new SpotElevationType { Name = "Triangle (Relative)", Symbol = SymbolKind.Triangle, ElevationOrigin = CoordinateBase.Relative, ElevationIndicator = "", Units = Units(), TextSizeMM = text });
            lib.SpotElevation.Add(new SpotElevationType { Name = "Top & Bottom (Project)", Symbol = SymbolKind.Target, ElevationOrigin = CoordinateBase.ProjectBasePoint, Display = ElevationDisplay.TopAndBottom, Units = Units(), TextSizeMM = text });

            lib.SpotCoordinate.Add(new SpotCoordinateType { Name = "Target (Survey)", CoordinateOrigin = CoordinateBase.SurveyPoint, Units = Units(), TextSizeMM = text });
            lib.SpotCoordinate.Add(new SpotCoordinateType { Name = "Target (Project)", CoordinateOrigin = CoordinateBase.ProjectBasePoint, IncludeElevation = true, Units = Units(), TextSizeMM = text });

            lib.SpotSlope.Add(new SpotSlopeType { Name = "Slope Arrow (Percent)", Slope = new SlopeStyle { Format = SlopeFormat.Percent, Precision = 2 }, TextSizeMM = text });
            lib.SpotSlope.Add(new SpotSlopeType { Name = "Slope Arrow (Rise/12\")", Slope = new SlopeStyle { Format = SlopeFormat.RiseOver12Inches, FractionDenominator = 16 }, TextSizeMM = text });
            lib.SpotSlope.Add(new SpotSlopeType { Name = "Slope Arrow (1:x)", Slope = new SlopeStyle { Format = SlopeFormat.RatioOneToX, Precision = 0 }, TextSizeMM = text });

            lib.Level.Add(new LevelType { Name = StockProjectLevel, HeadSymbol = SymbolKind.TargetFilled, ElevationBase = CoordinateBase.ProjectBasePoint, Units = Units(), TextSizeMM = imperial ? 3.175 : 3.5 });
            lib.Level.Add(new LevelType { Name = "Target Head (Survey)", HeadSymbol = SymbolKind.TargetFilled, ElevationBase = CoordinateBase.SurveyPoint, Units = Units(), TextSizeMM = imperial ? 3.175 : 3.5 });
            lib.Level.Add(new LevelType { Name = "Triangle Head (Project)", HeadSymbol = SymbolKind.Triangle, ElevationBase = CoordinateBase.ProjectBasePoint, Units = Units(), TextSizeMM = imperial ? 3.175 : 3.5, SymbolSizeMM = 4.7625 });

            lib.NorthArrow.Add(new NorthArrowType { Name = "North Arrow 3/4\"", SizeMM = 19.05 });

            lib.Grid.Add(imperial
                ? new GridType { Name = "Circle Bubble 1/2\"", BubbleSizeMM = 12.7, TextSizeMM = 4.7625 }
                : new GridType { Name = "Circle Bubble 12 mm", BubbleSizeMM = 12, TextSizeMM = 4.5 });
            return lib;
        }
    }

    /// <summary>Document access to the type library.</summary>
    public static class TypeTable
    {
        public static TypeLibrary Get(RhinoDoc doc)
        {
            var lib = DocStore.Get<TypeLibrary>(doc, Keys.TypesEntry);
            if (lib == null)
            {
                lib = TypeLibrary.Defaults(DocUnits.IsImperial(doc));
                return lib;
            }
            var defaults = TypeLibrary.Defaults(DocUnits.IsImperial(doc));
            if (lib.SpotElevation == null || lib.SpotElevation.Count == 0) lib.SpotElevation = defaults.SpotElevation;
            if (lib.SpotCoordinate == null || lib.SpotCoordinate.Count == 0) lib.SpotCoordinate = defaults.SpotCoordinate;
            if (lib.SpotSlope == null || lib.SpotSlope.Count == 0) lib.SpotSlope = defaults.SpotSlope;
            if (lib.Level == null || lib.Level.Count == 0) lib.Level = defaults.Level;
            if (lib.NorthArrow == null || lib.NorthArrow.Count == 0) lib.NorthArrow = defaults.NorthArrow;
            if (lib.Grid == null || lib.Grid.Count == 0) lib.Grid = defaults.Grid;
            Migrate(lib);
            return lib;
        }

        public static void Save(RhinoDoc doc, TypeLibrary lib) => DocStore.Set(doc, Keys.TypesEntry, lib);

        /// <summary>Type libraries from this version on: lengths follow Rhino's document units by default.</summary>
        public const int CurrentUnitsVersion = 2;

        /// <summary>
        /// Libraries saved before lengths followed Rhino: types still on one of the old fixed defaults (feet
        /// and inches to 1/8", millimetres, decimal feet / metres for survey coordinates) switch to Rhino
        /// document units; a format the user chose stays. Runs on every read until the library is saved.
        /// </summary>
        static void Migrate(TypeLibrary lib)
        {
            if (lib.UnitsVersion < CurrentUnitsVersion)
            {
                void Fix(LengthStyle u)
                {
                    if (u != null && IsOldDefault(u))
                        u.Format = LengthFormat.Project;
                }
                foreach (var t in lib.SpotElevation) Fix(t.Units);
                foreach (var t in lib.SpotCoordinate) Fix(t.Units);
                foreach (var t in lib.Level) Fix(t.Units);
                lib.UnitsVersion = CurrentUnitsVersion;
            }
            // level heads are filled by default: the stock project type still on the old hollow target follows
            if (lib.SymbolsVersion < CurrentSymbolsVersion)
            {
                foreach (var t in lib.Level)
                    if (t.Name == TypeLibrary.StockProjectLevel && t.HeadSymbol == SymbolKind.Target)
                        t.HeadSymbol = SymbolKind.TargetFilled;
                lib.SymbolsVersion = CurrentSymbolsVersion;
            }
        }

        /// <summary>Type libraries from this version on: level heads are filled by default.</summary>
        public const int CurrentSymbolsVersion = 2;

        static bool IsOldDefault(LengthStyle u)
        {
            bool plain = !u.SuppressTrailingZeros && !u.SuppressLeadingZeros && !u.SuppressZeroFeet && !u.SuppressZeroInches &&
                         !u.ShowPlusForPositive && !u.UseDigitGrouping && u.SuppressSpaces;
            if (!plain)
                return false;
            switch (u.Format)
            {
                case LengthFormat.FeetFractionalInches: return u.FractionDenominator == 8 && u.ShowUnitSymbol;
                case LengthFormat.Millimeters: return u.Precision == 0 && !u.ShowUnitSymbol;
                case LengthFormat.DecimalFeet: return u.Precision == 2 && u.ShowUnitSymbol;
                case LengthFormat.Meters: return u.Precision == 3 && !u.ShowUnitSymbol;
                default: return false;
            }
        }

        /// <summary>
        /// The concrete format of a length style in this document. Rhino document units (Project) take
        /// the unit system, distance display and precision from Document Properties > Units, so they
        /// change when those change; the style keeps its own symbol, zero and sign settings.
        /// </summary>
        public static LengthStyle Resolve(RhinoDoc doc, LengthStyle style)
        {
            var s = (style ?? new LengthStyle()).Clone();
            if (s.Format != LengthFormat.Project)
                return s;
            var rhino = DocUnits.ModelLengthStyle(doc);
            s.Format = rhino.Format;
            s.Precision = rhino.Precision;
            s.FractionDenominator = rhino.FractionDenominator;
            return s;
        }
    }
}
