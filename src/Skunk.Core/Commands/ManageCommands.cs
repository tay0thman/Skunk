using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using Skunk.Core;
using Skunk.Elements;
using Skunk.Service;
using Skunk.Site;
using Skunk.Units;

namespace Skunk.Commands
{
    /// <summary>Manage: project location, coordinates and position, plus view and settings commands.</summary>
    internal static class ManageCommands
    {
        static string L(RhinoDoc doc, double v) => Kit.Length(doc, v);

        static void Report(RhinoDoc doc, string what)
        {
            var site = SiteModel.Get(doc);
            var pbp = site.ProjectBasePointShared;
            Log.Info($"{what}: project base point N {L(doc, pbp.Y)}, E {L(doc, pbp.X)}, Elev {L(doc, pbp.Z)}; " +
                     $"angle to true north {UnitFormat.AngleEastWest(site.AngleToTrueNorthDegrees)}; " +
                     $"survey point N {L(doc, site.Data.SpNorth)}, E {L(doc, site.Data.SpEast)}, Elev {L(doc, site.Data.SpElevation)} ({(site.Data.SpClipped ? "clipped" : "unclipped")}).");
        }

        static void SaveSite(RhinoDoc doc, SiteModel site)
        {
            using (SkunkService.Suppress())
                site.Save(doc);
            SkunkService.Instance?.ContextChanged(doc);
            SkunkService.Instance?.ProcessNow(doc);
            doc.Views.Redraw();
        }

        /// <summary>SkLocation: the Location / Coordinates dialog (-SkLocation prints the values).</summary>
        public static Result Location(RhinoDoc doc, RunMode mode)
        {
            if (mode == RunMode.Scripted)
            {
                Log.Info("Project location: " + GeoLocation.Read(doc).Summary(doc, SiteModel.Get(doc)) + ".");
                Report(doc, "Location");
                return Result.Success;
            }
            UI.LocationDialog.Show(doc);
            return Result.Success;
        }

        /// <summary>SkProjectBasePoint: move the project base point, or type its shared coordinates / angle to true north.</summary>
        public static Result ProjectBasePoint(RhinoDoc doc, RunMode mode)
        {
            var site = SiteModel.Get(doc);
            var shared = site.ProjectBasePointShared;
            var ns = new OptionDouble(shared.Y);
            var ew = new OptionDouble(shared.X);
            var el = new OptionDouble(shared.Z);
            var angle = new OptionDouble(site.AngleToTrueNorthDegrees, -360, 360);
            var gp = new GetPoint();
            gp.SetCommandPrompt("New project base point location (the model does not move), or edit its values");
            gp.AddOptionDouble("NorthSouth", ref ns);
            gp.AddOptionDouble("EastWest", ref ew);
            gp.AddOptionDouble("Elevation", ref el);
            gp.AddOptionDouble("AngleToTrueNorthEast", ref angle);
            gp.AcceptNothing(true);
            gp.SetBasePoint(site.Project.Origin, true);
            bool changed = false;
            while (true)
            {
                var r = gp.Get();
                if (r == GetResult.Point)
                {
                    site.MoveProjectBasePoint(gp.Point());
                    changed = true;
                    break;
                }
                if (r == GetResult.Option)
                {
                    site.SetProjectBasePointShared(ew.CurrentValue, ns.CurrentValue, el.CurrentValue);
                    site.SetAngleToTrueNorth(angle.CurrentValue, RotationPivot.ProjectBasePoint);
                    changed = true;
                    continue;
                }
                if (r == GetResult.Nothing)
                    break;
                return Result.Cancel;
            }
            if (changed)
            {
                SaveSite(doc, site);
                Report(doc, "Project base point");
            }
            return Result.Success;
        }

        /// <summary>SkSurveyPoint: move the survey point (clipped: the shared system moves with it).</summary>
        public static Result SurveyPoint(RhinoDoc doc, RunMode mode)
        {
            var site = SiteModel.Get(doc);
            var clipped = new OptionToggle(site.Data.SpClipped, "Unclipped", "Clipped");
            var ns = new OptionDouble(site.Data.SpNorth);
            var ew = new OptionDouble(site.Data.SpEast);
            var el = new OptionDouble(site.Data.SpElevation);
            var gp = new GetPoint();
            gp.SetCommandPrompt("New survey point location, or edit its values");
            gp.AddOptionToggle("Clip", ref clipped);
            gp.AddOptionDouble("NorthSouth", ref ns);
            gp.AddOptionDouble("EastWest", ref ew);
            gp.AddOptionDouble("Elevation", ref el);
            gp.AcceptNothing(true);
            gp.SetBasePoint(site.Survey.Origin, true);
            bool changed = false;
            while (true)
            {
                var r = gp.Get();
                if (r == GetResult.Point)
                {
                    site.Data.SpClipped = clipped.CurrentValue;
                    site.MoveSurveyPoint(gp.Point(), clipped.CurrentValue);
                    changed = true;
                    break;
                }
                if (r == GetResult.Option)
                {
                    site.Data.SpClipped = clipped.CurrentValue;
                    site.Data.SpNorth = ns.CurrentValue;
                    site.Data.SpEast = ew.CurrentValue;
                    site.Data.SpElevation = el.CurrentValue;
                    changed = true;
                    continue;
                }
                if (r == GetResult.Nothing)
                    break;
                return Result.Cancel;
            }
            if (changed)
            {
                SaveSite(doc, site);
                Report(doc, "Survey point");
            }
            return Result.Success;
        }

        /// <summary>SkSpecifyCoordinates: give a picked point its shared coordinates and true north.</summary>
        public static Result SpecifyCoordinates(RhinoDoc doc, RunMode mode)
        {
            var gp = new GetPoint();
            gp.SetCommandPrompt("Point whose shared coordinates you know");
            if (gp.Get() != GetResult.Point)
                return Result.Cancel;
            var p = gp.Point();
            var site = SiteModel.Get(doc);
            var current = site.Shared(p);
            double n = current.Y, e = current.X, z = current.Z, a = site.AngleToTrueNorthDegrees;
            if (!AskNumber("North / South", ref n) || !AskNumber("East / West", ref e) || !AskNumber("Elevation", ref z) ||
                !AskNumber("Angle from project north to true north (degrees, + = East)", ref a))
                return Result.Cancel;
            site.SpecifyCoordinatesAtPoint(p, e, n, z, a);
            SaveSite(doc, site);
            Report(doc, "Coordinates specified");
            return Result.Success;
        }

        static bool AskNumber(string prompt, ref double value)
        {
            var gn = new GetNumber();
            gn.SetCommandPrompt(prompt);
            gn.SetDefaultNumber(value);
            gn.AcceptNothing(true);
            var r = gn.Get();
            if (r == GetResult.Number)
                value = gn.Number();
            return r == GetResult.Number || r == GetResult.Nothing;
        }

        /// <summary>SkReportCoordinates: report shared coordinates - pick points, read every system.</summary>
        public static Result ReportCoordinates(RhinoDoc doc, RunMode mode)
        {
            var site = SiteModel.Get(doc);
            var lines = new List<string>();
            var gp = new GetPoint();
            gp.SetCommandPrompt("Point to report (Enter when done)");
            gp.AcceptNothing(true);
            while (gp.Get() == GetResult.Point)
            {
                var p = gp.Point();
                var s = site.Shared(p);
                var pr = site.ProjectCoordinates(p);
                string line = $"Shared N {L(doc, s.Y)}  E {L(doc, s.X)}  Elev {L(doc, s.Z)}   |   Project N {L(doc, pr.Y)}  E {L(doc, pr.X)}  Elev {L(doc, pr.Z)}   |   Internal X {L(doc, p.X)}  Y {L(doc, p.Y)}  Z {L(doc, p.Z)}";
                RhinoApp.WriteLine(line);
                lines.Add(line);
            }
            if (lines.Count > 0 && mode == RunMode.Interactive)
                Rhino.UI.Dialogs.ShowTextDialog(string.Join(System.Environment.NewLine, lines), "Skunk - Reported coordinates");
            return Result.Success;
        }

        /// <summary>SkRotateTrueNorth: rotate the shared coordinate system; the model does not move.</summary>
        public static Result RotateTrueNorth(RhinoDoc doc, RunMode mode)
        {
            var site = SiteModel.Get(doc);
            var pivotSurvey = new OptionToggle(false, "ProjectBasePoint", "SurveyPoint");
            var absolute = new OptionToggle(false, "RotateBy", "SetTo");
            var gn = new GetNumber();
            gn.SetDefaultNumber(0);
            int optPick = -1;
            double? value = null;
            while (true)
            {
                gn.ClearCommandOptions();
                gn.SetCommandPrompt(absolute.CurrentValue
                    ? $"Angle from project north to true north (degrees, + = East; now {UnitFormat.AngleEastWest(site.AngleToTrueNorthDegrees)})"
                    : "Rotate true north by (degrees, + = clockwise)");
                gn.AddOptionToggle("Pivot", ref pivotSurvey);
                gn.AddOptionToggle("Mode", ref absolute);
                optPick = gn.AddOption("Pick");
                var r = gn.Get();
                if (r == GetResult.Option)
                {
                    if (gn.OptionIndex() == optPick)
                    {
                        var center = pivotSurvey.CurrentValue ? site.Survey.Origin : site.Project.Origin;
                        var gp = new GetPoint();
                        gp.SetCommandPrompt("Point toward the new true north");
                        gp.SetBasePoint(center, true);
                        gp.DrawLineFromPoint(center, true);
                        if (gp.Get() != GetResult.Point)
                            return Result.Cancel;
                        var dir = gp.Point() - center;
                        dir.Z = 0;
                        if (!dir.Unitize())
                            return Result.Cancel;
                        absolute = new OptionToggle(true, "RotateBy", "SetTo");
                        value = SiteModel.ClockwiseAngle(site.ProjectNorth, dir);
                        break;
                    }
                    continue;
                }
                if (r != GetResult.Number)
                    return Result.Cancel;
                value = gn.Number();
                break;
            }
            var pivot = pivotSurvey.CurrentValue ? RotationPivot.SurveyPoint : RotationPivot.ProjectBasePoint;
            if (absolute.CurrentValue)
                site.SetAngleToTrueNorth(value.Value, pivot);
            else
                site.RotateTrueNorth(value.Value, pivot);
            SaveSite(doc, site);
            Report(doc, "True north rotated");
            return Result.Success;
        }

        /// <summary>
        /// SkRotateProjectNorth: rotate the model (every object) about the internal origin or the project base
        /// point; true north turns with it, so shared coordinates do not change.
        /// </summary>
        public static Result RotateProjectNorth(RhinoDoc doc, RunMode mode)
        {
            string[] choices = { "Clockwise90", "CounterClockwise90", "Rotate180", "Angle", "AlignTo" };
            var pivotPbp = new OptionToggle(false, "InternalOrigin", "ProjectBasePoint");
            var go = new GetOption();
            go.SetCommandPrompt("Rotate project north");
            int optRotate = go.AddOptionList("Rotation", choices, 0);
            go.AddOptionToggle("Pivot", ref pivotPbp);
            go.AcceptNothing(true);
            int choice = 0;
            while (true)
            {
                var r = go.Get();
                if (r == GetResult.Option)
                {
                    if (go.OptionIndex() == optRotate)
                    {
                        choice = go.Option().CurrentListOptionIndex;
                        break;
                    }
                    continue;
                }
                if (r == GetResult.Nothing)
                    break;
                return Result.Cancel;
            }
            var site = SiteModel.Get(doc);
            var center = pivotPbp.CurrentValue ? site.Project.Origin : Point3d.Origin;
            double clockwise;
            switch (choices[choice])
            {
                case "Clockwise90": clockwise = 90; break;
                case "CounterClockwise90": clockwise = -90; break;
                case "Rotate180": clockwise = 180; break;
                case "Angle":
                    {
                        clockwise = 0;
                        if (!AskNumber("Rotate the project by (degrees, + = clockwise)", ref clockwise))
                            return Result.Cancel;
                        break;
                    }
                default:
                    {
                        // align: the picked direction becomes project north (vertical on a plan view)
                        var g1 = new GetPoint();
                        g1.SetCommandPrompt("Start of a line that should point to project north");
                        if (g1.Get() != GetResult.Point)
                            return Result.Cancel;
                        var g2 = new GetPoint();
                        g2.SetCommandPrompt("End of the line");
                        g2.SetBasePoint(g1.Point(), true);
                        g2.DrawLineFromPoint(g1.Point(), true);
                        if (g2.Get() != GetResult.Point)
                            return Result.Cancel;
                        var d = g2.Point() - g1.Point();
                        d.Z = 0;
                        if (!d.Unitize())
                            return Result.Cancel;
                        clockwise = SiteModel.ClockwiseAngle(d, site.ProjectNorth);
                        break;
                    }
            }
            if (Math.Abs(clockwise) < 1e-9)
                return Result.Nothing;
            var xf = Transform.Rotation(-clockwise * Math.PI / 180.0, Vector3d.ZAxis, center);
            int moved = 0;
            using (SkunkService.Suppress())
            {
                var settings = new ObjectEnumeratorSettings { NormalObjects = true, LockedObjects = true, HiddenObjects = true, DeletedObjects = false, ReferenceObjects = false };
                var all = doc.Objects.GetObjectList(settings).ToList();
                foreach (var o in all)
                {
                    var k = ElementIO.KindOf(o);
                    if (k == ElementKind.ProjectBasePoint || k == ElementKind.SurveyPoint)
                        continue;
                    if (doc.Objects.Transform(o.Id, xf, true) != Guid.Empty)
                        moved++;
                }
                // the real world turns with the model: shared coordinates stay the same
                var survey = site.Survey;
                survey.Transform(xf);
                site.Survey = survey;
                if (!pivotPbp.CurrentValue)
                {
                    var project = site.Project;
                    var o = project.Origin;
                    o.Transform(xf);
                    project.Origin = o;
                    site.Project = project;
                }
            }
            SaveSite(doc, site);
            SkunkService.Instance?.FullUpdate(doc);
            SkunkService.Instance?.ProcessNow(doc);
            Log.Info($"Project rotated {Math.Abs(clockwise):0.##}° {(clockwise > 0 ? "clockwise" : "counter-clockwise")} ({moved} objects); shared coordinates unchanged.");
            Report(doc, "Location");
            return Result.Success;
        }

        /// <summary>SkOrientView: turn the active plan view so true north (or project north) points up.</summary>
        public static Result OrientView(RhinoDoc doc, RunMode mode)
        {
            var view = doc.Views.ActiveView;
            if (view == null)
                return Result.Failure;
            var trueNorth = new OptionToggle(true, "ProjectNorth", "TrueNorth");
            var go = new GetOption();
            go.SetCommandPrompt("Orientation (Enter to apply)");
            go.AddOptionToggle("Orientation", ref trueNorth);
            go.AcceptNothing(true);
            while (true)
            {
                var r = go.Get();
                if (r == GetResult.Option)
                    continue;
                if (r == GetResult.Nothing)
                    break;
                return Result.Cancel;
            }
            var site = SiteModel.Get(doc);
            var north = trueNorth.CurrentValue ? site.TrueNorth : site.ProjectNorth;
            var vp = view.ActiveViewport;
            var camDir = vp.CameraDirection;
            camDir.Unitize();
            if (Math.Abs(camDir * Vector3d.ZAxis) < 0.9)
            {
                Log.Warn("orientation applies to plan views (looking straight down)");
                return Result.Nothing;
            }
            vp.CameraUp = north;
            view.Redraw();
            Log.Info("View oriented to " + (trueNorth.CurrentValue ? "true north." : "project north."));
            return Result.Success;
        }

        /// <summary>SkShowSite: show / hide the internal origin, project base point and survey point.</summary>
        public static Result ShowSite(RhinoDoc doc, RunMode mode)
        {
            var settings = DocStore.Settings(doc);
            settings.ShowSiteMarkers = !settings.ShowSiteMarkers;
            DocStore.SaveSettings(doc, settings);
            int layer = Types.Styles.Layer(doc, ElementKind.SurveyPoint);
            var l = doc.Layers[layer];
            if (l.IsVisible != settings.ShowSiteMarkers)
                l.IsVisible = settings.ShowSiteMarkers;
            doc.Views.Redraw();
            Log.Info("Site points " + (settings.ShowSiteMarkers ? "shown." : "hidden."));
            return Result.Success;
        }

        /// <summary>SkUpdate: re-sync every Skunk element with its host, level and coordinate system.</summary>
        public static Result Update(RhinoDoc doc, RunMode mode)
        {
            SkunkService.Instance.FullUpdate(doc);
            int n = SkunkService.Instance.ProcessNow(doc);
            var index = SkunkService.Instance.Index(doc);
            Log.Info($"{index.Levels.Count} levels, {index.Spots.Count} spots, {index.Arrows.Count} north arrows checked; {n} object(s) updated.");
            doc.Views.Redraw();
            return Result.Success;
        }

        /// <summary>
        /// SkImportSharedCoordinates: match the shared coordinates of a BIM model - a Python snippet to run in
        /// the model's console, its printed line pasted back (see Site.ImportedSite).
        /// </summary>
        public static Result ImportSharedCoordinates(RhinoDoc doc, RunMode mode)
        {
            UI.SharedCoordinatesDialog.Show(doc);
            return Result.Success;
        }

        /// <summary>SkAbout: the About Skunk dialog (-SkAbout prints the same on the command line).</summary>
        public static Result About(RhinoDoc doc, RunMode mode)
        {
            if (mode == RunMode.Scripted)
            {
                foreach (var line in UI.AboutDialog.Lines())
                    RhinoApp.WriteLine(line);
                return Result.Success;
            }
            UI.AboutDialog.Show(doc);
            return Result.Success;
        }

        /// <summary>SkSettings: annotation scale, plan cut, behaviour, aliases.</summary>
        public static Result Settings(RhinoDoc doc, RunMode mode)
        {
            UI.SettingsDialog.Show(doc);
            return Result.Success;
        }

        /// <summary>
        /// Skunk: loads the plug-in (running any of its commands does), starts the event service, makes sure the Skunk
        /// toolbar is loaded and reports the state. Add it to Rhino's startup commands. A toolbar Rhino has loaded already is
        /// left where the user put it (shown or hidden), so start-up never shows a second one.
        /// </summary>
        public static Result Start(RhinoDoc doc, RunMode mode)
        {
            var index = SkunkService.Instance.Index(doc);
            bool loaded = UI.SkunkToolbar.Load(out _, out bool opened);
            Log.Info($"{SkunkInfo.Title} running - {index.Levels.Count} levels, {index.Spots.Count} spots, {index.Arrows.Count} north arrows in this model." +
                     (!loaded ? " Skunk.rui was not found next to the plug-in: open it with Toolbar > File > Open." : opened ? " The Skunk toolbar was opened." : ""));
            return Result.Success;
        }
    }

    /// <summary>Version information.</summary>
    public static class SkunkInfo
    {
        public static string Version => typeof(SkunkInfo).Assembly.GetName().Version?.ToString(3) ?? "?";

        public static string Title => "Skunk " + Version;
    }
}
