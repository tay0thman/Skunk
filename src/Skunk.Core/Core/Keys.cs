using System;

namespace Skunk.Core
{
    /// <summary>Names used in the document: user-text keys, document strings, layers, blocks.</summary>
    public static class Keys
    {
        // object user text
        public const string Kind = "Skunk:Kind";
        public const string Data = "Skunk:Data";
        public const string Owner = "Skunk:Owner";
        public const string Level = "Skunk:Level";
        public const string LevelOffset = "Skunk:LevelOffset";
        /// <summary>On a layout detail: the sides of it that grid bubbles show on ("Top, Left"); none = every bubble.</summary>
        public const string DetailGridBubbles = "Skunk:GridBubbles";

        // document strings (section, entry)
        public const string Section = "Skunk";
        public const string SiteEntry = "Site";
        public const string TypesEntry = "Types";
        public const string SettingsEntry = "Settings";

        // layers: Skunk's sublayers sit under one top-level layer (see Layers.Migrate for models from earlier builds)
        public const string RootLayer = "__Annotations__";
        /// <summary>The top-level layer's name in earlier Skunk builds.</summary>
        public const string OldRootLayer = "Skunk";
        public const string LevelsLayer = "Levels";
        public const string LevelHeadsLayer = "Level Heads";
        public const string SpotElevationsLayer = "Spot Elevations";
        public const string SpotCoordinatesLayer = "Spot Coordinates";
        public const string SpotSlopesLayer = "Spot Slopes";
        public const string SiteLayer = "Site";
        public const string NorthArrowsLayer = "North Arrows";
        public const string PlanCutsLayer = "Plan Cuts";
        public const string DatumBoxesLayer = "Datum Boxes";
        public const string GridsLayer = "Grids";
        public const string GridHeadsLayer = "Grid Heads";
        public const string GridElevationsLayer = "Grid Elevations";
        /// <summary>Default sublayer for Rhino's own dimensions (the "Rhino dimensions on a Skunk sublayer" option).</summary>
        public const string DimensionsLayer = "Dimensions";

        // blocks used as arrowheads / symbols
        public const string BlockPrefix = "Skunk ";
        public const string LinetypeDatum = "Skunk Datum";
        public const string DimStylePrefix = "Skunk ";

        // named construction planes
        public const string DefaultPbpCPlane = "Skunk: Project Base Point";
        public const string DefaultSpCPlane = "Skunk: Survey Point (True North)";

        public const string ProductName = "Skunk";
    }

    /// <summary>The kinds of Skunk elements stored in <see cref="Keys.Kind"/>.</summary>
    public enum ElementKind
    {
        None = 0,
        Level,
        LevelHead,
        SpotElevation,
        SpotCoordinate,
        SpotSlope,
        NorthArrow,
        ProjectBasePoint,
        SurveyPoint,
        PlanCut,
        /// <summary>A printed level line (one per elevation direction with heads; the footprint itself does not print).</summary>
        LevelLine,
        /// <summary>A datum box: one plan outline that holds the extents of the levels in it.</summary>
        DatumBox,
        /// <summary>A grid line (a datum in plan, named 1, 2, 3 or A, B, C).</summary>
        Grid,
        /// <summary>A grid bubble, owned by its grid: the circle (a leader) or its label (a text).</summary>
        GridHead,
        /// <summary>A grid as elevations show it, owned by its grid: a vertical line or one of its bubbles' parts.</summary>
        GridElevation,
    }

    public static class ElementKinds
    {
        public static ElementKind Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
                return ElementKind.None;
            return Enum.TryParse(text, false, out ElementKind k) ? k : ElementKind.None;
        }

        public static string DisplayName(ElementKind k)
        {
            switch (k)
            {
                case ElementKind.Level: return "Level";
                case ElementKind.LevelHead: return "Level Head";
                case ElementKind.SpotElevation: return "Spot Elevation";
                case ElementKind.SpotCoordinate: return "Spot Coordinate";
                case ElementKind.SpotSlope: return "Spot Slope";
                case ElementKind.NorthArrow: return "North Arrow";
                case ElementKind.ProjectBasePoint: return "Project Base Point";
                case ElementKind.SurveyPoint: return "Survey Point";
                case ElementKind.PlanCut: return "Plan Cut";
                case ElementKind.LevelLine: return "Level Line";
                case ElementKind.DatumBox: return "Datum Box";
                case ElementKind.Grid: return "Grid";
                case ElementKind.GridHead: return "Grid Head";
                case ElementKind.GridElevation: return "Grid (Elevation)";
                default: return "";
            }
        }

        /// <summary>A level's head or printed line: derived from the level, and it stands for the level when picked.</summary>
        public static bool IsLevelPart(ElementKind k) => k == ElementKind.LevelHead || k == ElementKind.LevelLine;

        /// <summary>A grid's bubble or elevation part: derived from the grid, and it stands for the grid when picked.</summary>
        public static bool IsGridPart(ElementKind k) => k == ElementKind.GridHead || k == ElementKind.GridElevation;

        public static bool IsSpot(ElementKind k) =>
            k == ElementKind.SpotElevation || k == ElementKind.SpotCoordinate || k == ElementKind.SpotSlope;
    }
}
