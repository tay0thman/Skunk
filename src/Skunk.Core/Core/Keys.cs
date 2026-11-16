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

        // document strings (section, entry)
        public const string Section = "Skunk";
        public const string SiteEntry = "Site";
        public const string TypesEntry = "Types";
        public const string SettingsEntry = "Settings";

        // layers
        public const string RootLayer = "Skunk";
        public const string LevelsLayer = "Levels";
        public const string LevelHeadsLayer = "Level Heads";
        public const string SpotElevationsLayer = "Spot Elevations";
        public const string SpotCoordinatesLayer = "Spot Coordinates";
        public const string SpotSlopesLayer = "Spot Slopes";
        public const string SiteLayer = "Site";
        public const string NorthArrowsLayer = "North Arrows";
        public const string PlanCutsLayer = "Plan Cuts";
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
                default: return "";
            }
        }

        public static bool IsSpot(ElementKind k) =>
            k == ElementKind.SpotElevation || k == ElementKind.SpotCoordinate || k == ElementKind.SpotSlope;
    }
}
