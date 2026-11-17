using Rhino.Commands;
using Skunk.Commands;

namespace Skunk
{
    /// <summary>
    /// Entry points called by the Script Editor command scripts (ScriptEditor/*.cs):
    ///     __rhino_command_result__ = SkunkEntry.SkLevel(__rhino_doc__, __rhino_runmode__, __rhino_command__);
    /// The parameters are object so the scripts can pass their globals straight through.
    /// Every entry point starts the Skunk event service first (it keeps levels and spots parametric).
    /// </summary>
    public static class SkunkEntry
    {
        // Architecture > Datum
        public static Result SkLevel(object doc, object mode, object command) => Kit.Run(doc, mode, "SkLevel", DatumCommands.Level);
        public static Result SkLevels(object doc, object mode, object command) => Kit.Run(doc, mode, "SkLevels", DatumCommands.Levels);
        public static Result SkDatumBox(object doc, object mode, object command) => Kit.Run(doc, mode, "SkDatumBox", DatumCommands.DatumBox);
        public static Result SkGrid(object doc, object mode, object command) => Kit.Run(doc, mode, "SkGrid", DatumCommands.Grid);
        public static Result SkRenumberGrids(object doc, object mode, object command) => Kit.Run(doc, mode, "SkRenumberGrids", DatumCommands.RenumberGrids);
        public static Result SkGridBubbles(object doc, object mode, object command) => Kit.Run(doc, mode, "SkGridBubbles", DatumCommands.GridBubbles);
        public static Result SkGoToLevel(object doc, object mode, object command) => Kit.Run(doc, mode, "SkGoToLevel", DatumCommands.GoToLevel);
        public static Result SkAttachToLevel(object doc, object mode, object command) => Kit.Run(doc, mode, "SkAttachToLevel", DatumCommands.Attach);
        public static Result SkDetachFromLevel(object doc, object mode, object command) => Kit.Run(doc, mode, "SkDetachFromLevel", DatumCommands.Detach);

        // Annotate > Dimension
        public static Result SkSpotElevation(object doc, object mode, object command) => Kit.Run(doc, mode, "SkSpotElevation", AnnotateCommands.SpotElevation);
        public static Result SkSpotCoordinate(object doc, object mode, object command) => Kit.Run(doc, mode, "SkSpotCoordinate", AnnotateCommands.SpotCoordinate);
        public static Result SkSpotSlope(object doc, object mode, object command) => Kit.Run(doc, mode, "SkSpotSlope", AnnotateCommands.SpotSlope);
        public static Result SkNorthArrow(object doc, object mode, object command) => Kit.Run(doc, mode, "SkNorthArrow", AnnotateCommands.NorthArrow);
        public static Result SkEditType(object doc, object mode, object command) => Kit.Run(doc, mode, "SkEditType", AnnotateCommands.EditType);
        public static Result SkProperties(object doc, object mode, object command) => Kit.Run(doc, mode, "SkProperties", AnnotateCommands.Properties);

        // Manage > Project Location / Coordinates / Position
        public static Result SkLocation(object doc, object mode, object command) => Kit.Run(doc, mode, "SkLocation", ManageCommands.Location);
        public static Result SkProjectBasePoint(object doc, object mode, object command) => Kit.Run(doc, mode, "SkProjectBasePoint", ManageCommands.ProjectBasePoint);
        public static Result SkSurveyPoint(object doc, object mode, object command) => Kit.Run(doc, mode, "SkSurveyPoint", ManageCommands.SurveyPoint);
        public static Result SkSpecifyCoordinates(object doc, object mode, object command) => Kit.Run(doc, mode, "SkSpecifyCoordinates", ManageCommands.SpecifyCoordinates);
        public static Result SkReportCoordinates(object doc, object mode, object command) => Kit.Run(doc, mode, "SkReportCoordinates", ManageCommands.ReportCoordinates);
        public static Result SkImportSharedCoordinates(object doc, object mode, object command) => Kit.Run(doc, mode, "SkImportSharedCoordinates", ManageCommands.ImportSharedCoordinates);
        public static Result SkRotateTrueNorth(object doc, object mode, object command) => Kit.Run(doc, mode, "SkRotateTrueNorth", ManageCommands.RotateTrueNorth);
        public static Result SkRotateProjectNorth(object doc, object mode, object command) => Kit.Run(doc, mode, "SkRotateProjectNorth", ManageCommands.RotateProjectNorth);
        public static Result SkUpdate(object doc, object mode, object command) => Kit.Run(doc, mode, "SkUpdate", ManageCommands.Update);
        public static Result SkSettings(object doc, object mode, object command) => Kit.Run(doc, mode, "SkSettings", ManageCommands.Settings);
        public static Result Skunk(object doc, object mode, object command) => Kit.Run(doc, mode, "Skunk", ManageCommands.Start);
        public static Result SkAbout(object doc, object mode, object command) => Kit.Run(doc, mode, "SkAbout", ManageCommands.About);

        // View
        public static Result SkOrientView(object doc, object mode, object command) => Kit.Run(doc, mode, "SkOrientView", ManageCommands.OrientView);
        public static Result SkShowSite(object doc, object mode, object command) => Kit.Run(doc, mode, "SkShowSite", ManageCommands.ShowSite);
    }
}
