using System;
using System.Linq;
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
using Skunk.Types;

namespace Skunk.Commands
{
    /// <summary>Annotate > Dimension: spot elevation, spot coordinate, spot slope; plus north arrows.</summary>
    internal static class AnnotateCommands
    {
        static readonly string[] Displays = { "Actual", "Top", "Bottom", "TopAndBottom" };
        static readonly string[] PickModes = { "Object", "Point" };

        public static Result SpotElevation(RhinoDoc doc, RunMode mode) => Spot(doc, ElementKind.SpotElevation);

        public static Result SpotCoordinate(RhinoDoc doc, RunMode mode) => Spot(doc, ElementKind.SpotCoordinate);

        public static Result SpotSlope(RhinoDoc doc, RunMode mode) => Spot(doc, ElementKind.SpotSlope);

        /// <summary>
        /// Place spots one after another: pick a point on a surface, polysurface, mesh,
        /// SubD, curve or point, then the leader end. Command-line options act as the options bar.
        /// </summary>
        static Result Spot(RhinoDoc doc, ElementKind kind)
        {
            var lib = TypeTable.Get(doc);
            var names = Kit.TypeNames(lib, kind);
            int typeIndex = Kit.IndexOf(names, Kit.LastType(kind));
            var leader = new OptionToggle(true, "No", "Yes");
            var shoulder = new OptionToggle(true, "No", "Yes");
            var directionUp = new OptionToggle(false, "Down", "Up");
            int display = -1;
            int pickMode = 0;
            Guid relativeLevel = Guid.Empty;
            int created = 0;
            string what = ElementKinds.DisplayName(kind).ToLowerInvariant();

            while (true)
            {
                var type = lib.Find(kind, names[typeIndex]);
                if (display < 0 && type is SpotElevationType se0)
                    display = (int)se0.Display;
                bool relative = type is SpotElevationType set && set.ElevationOrigin == CoordinateBase.Relative;

                RhinoObject host = null;
                Point3d point;
                GetResult r;
                int optType, optDisplay = -1, optRel = -1, optPick;
                var levels = relative ? LevelElement.All(doc) : null;
                if (PickModes[pickMode] == "Object")
                {
                    var go = new GetObject();
                    go.SetCommandPrompt($"Select a point on a surface, mesh, edge or curve for the {what}" + (created > 0 ? " (Enter when done)" : ""));
                    go.GeometryFilter = Kit.HostTypes;
                    go.SetCustomGeometryFilter(Kit.HostFilter);
                    go.SubObjectSelect = true;
                    go.EnablePreSelect(false, true);
                    go.DeselectAllBeforePostSelect = true;
                    go.AcceptNothing(true);
                    optType = go.AddOptionList("Type", names.Select(Kit.OptionValue), typeIndex);
                    if (kind != ElementKind.SpotSlope)
                    {
                        go.AddOptionToggle("Leader", ref leader);
                        go.AddOptionToggle("Shoulder", ref shoulder);
                    }
                    if (kind == ElementKind.SpotElevation)
                        optDisplay = go.AddOptionList("Display", Displays, Math.Max(0, display));
                    if (kind == ElementKind.SpotSlope)
                        go.AddOptionToggle("Direction", ref directionUp);
                    if (relative && levels.Count > 0)
                        optRel = go.AddOptionList("RelativeBase", levels.Select(l => Kit.OptionValue(l.Name)), Math.Max(0, levels.FindIndex(l => l.Id == relativeLevel)));
                    optPick = go.AddOptionList("PickMode", PickModes, pickMode);
                    r = go.Get();
                    if (r == GetResult.Option)
                    {
                        int i = go.OptionIndex();
                        if (i == optType) typeIndex = go.Option().CurrentListOptionIndex;
                        else if (i == optDisplay) display = go.Option().CurrentListOptionIndex;
                        else if (i == optRel) relativeLevel = levels[go.Option().CurrentListOptionIndex].Id;
                        else if (i == optPick) pickMode = go.Option().CurrentListOptionIndex;
                        continue;
                    }
                    if (r != GetResult.Object)
                        break;
                    var objRef = go.Object(0);
                    host = objRef.Object();
                    point = objRef.SelectionPoint();
                    if (!point.IsValid)
                    {
                        var bb = host.Geometry.GetBoundingBox(true);
                        var a = Anchors.Capture(host, bb.Center);
                        point = Anchors.Evaluate(doc, a).Point;
                    }
                }
                else
                {
                    var gp = new GetPoint();
                    gp.SetCommandPrompt($"Point for the {what} (snaps to objects; a point on an object hosts the spot)" + (created > 0 ? " (Enter when done)" : ""));
                    gp.AcceptNothing(true);
                    optType = gp.AddOptionList("Type", names.Select(Kit.OptionValue), typeIndex);
                    if (kind != ElementKind.SpotSlope)
                    {
                        gp.AddOptionToggle("Leader", ref leader);
                        gp.AddOptionToggle("Shoulder", ref shoulder);
                    }
                    if (kind == ElementKind.SpotElevation)
                        optDisplay = gp.AddOptionList("Display", Displays, Math.Max(0, display));
                    if (kind == ElementKind.SpotSlope)
                        gp.AddOptionToggle("Direction", ref directionUp);
                    if (relative && levels.Count > 0)
                        optRel = gp.AddOptionList("RelativeBase", levels.Select(l => Kit.OptionValue(l.Name)), Math.Max(0, levels.FindIndex(l => l.Id == relativeLevel)));
                    optPick = gp.AddOptionList("PickMode", PickModes, pickMode);
                    r = gp.Get();
                    if (r == GetResult.Option)
                    {
                        int i = gp.OptionIndex();
                        if (i == optType) typeIndex = gp.Option().CurrentListOptionIndex;
                        else if (i == optDisplay) display = gp.Option().CurrentListOptionIndex;
                        else if (i == optRel) relativeLevel = levels[gp.Option().CurrentListOptionIndex].Id;
                        else if (i == optPick) pickMode = gp.Option().CurrentListOptionIndex;
                        continue;
                    }
                    if (r != GetResult.Point)
                        break;
                    point = gp.Point();
                    var on = gp.PointOnObject();
                    host = on?.Object();
                    if (host != null && !Anchors.CanHost(host))
                        host = null;
                    if (host == null && kind == ElementKind.SpotSlope)
                    {
                        Log.Warn("a spot slope needs a surface, mesh or curve under the point - snap to one, or use PickMode=Object");
                        continue;
                    }
                }

                type = lib.Find(kind, names[typeIndex]);
                Kit.SetLastType(kind, type.Name);
                var plane = Kit.ViewPlane(doc, point);
                Point3d? end = null;
                if (kind != ElementKind.SpotSlope && leader.CurrentValue)
                {
                    var gl = new GetPoint();
                    gl.SetCommandPrompt("Leader end (text location) - Enter for the default");
                    gl.SetBasePoint(point, true);
                    gl.DrawLineFromPoint(point, true);
                    gl.Constrain(plane, false);
                    gl.AcceptNothing(true);
                    var rl = gl.Get();
                    if (rl == GetResult.Point)
                        end = gl.Point();
                    else if (rl != GetResult.Nothing)
                        break;
                }
                if (relative && relativeLevel == Guid.Empty)
                    relativeLevel = NearestLevelBelow(doc, point);

                Guid id;
                using (SkunkService.Suppress())
                {
                    id = SpotElement.Create(doc, kind, type.Name, host, point, plane, end, leader.CurrentValue, shoulder.CurrentValue, relativeLevel);
                    if (id != Guid.Empty)
                        ApplyInstanceOptions(doc, id, kind, type, display, directionUp.CurrentValue);
                }
                if (id == Guid.Empty)
                {
                    Log.Error("the " + what + " could not be created");
                    continue;
                }
                created++;
                doc.Objects.UnselectAll();
                doc.Views.Redraw();
            }
            if (created > 0)
                Log.Info(created + " " + what + (created == 1 ? "" : "s") + " placed.");
            return created > 0 ? Result.Success : Result.Cancel;
        }

        static void ApplyInstanceOptions(RhinoDoc doc, Guid id, ElementKind kind, SkunkType type, int display, bool up)
        {
            var obj = doc.Objects.FindId(id);
            var data = ElementIO.Read<SpotData>(obj);
            if (data == null)
                return;
            bool changed = false;
            if (kind == ElementKind.SpotElevation && type is SpotElevationType set && display >= 0 && (ElevationDisplay)display != set.Display)
            {
                data.Display = (ElevationDisplay)display;
                changed = true;
            }
            if (kind == ElementKind.SpotSlope && type is SpotSlopeType sst && up != (sst.Direction == SlopeDirection.Up))
            {
                data.FlipSlope = true;
                changed = true;
            }
            if (!changed)
                return;
            ElementIO.Write(doc, obj, kind, data);
            SpotElement.Update(new UpdateContext(doc), doc.Objects.FindId(id), UpdateReason.Context);
        }

        static Guid NearestLevelBelow(RhinoDoc doc, Point3d p)
        {
            var levels = LevelElement.All(doc);
            var below = levels.Where(l => l.Z <= p.Z + doc.ModelAbsoluteTolerance).OrderByDescending(l => l.Z).FirstOrDefault();
            return (below ?? levels.FirstOrDefault())?.Id ?? Guid.Empty;
        }

        /// <summary>SkNorthArrow: a north arrow that keeps pointing to true (or project) north.</summary>
        public static Result NorthArrow(RhinoDoc doc, RunMode mode)
        {
            var lib = TypeTable.Get(doc);
            var names = Kit.TypeNames(lib, ElementKind.NorthArrow);
            int typeIndex = Kit.IndexOf(names, Kit.LastType(ElementKind.NorthArrow));
            var trueNorth = new OptionToggle(true, "Project", "True");
            var gp = new GetPoint();
            gp.SetCommandPrompt("North arrow location");
            int optType = gp.AddOptionList("Type", names.Select(Kit.OptionValue), typeIndex);
            gp.AddOptionToggle("North", ref trueNorth);
            while (true)
            {
                var r = gp.Get();
                if (r == GetResult.Option)
                {
                    if (gp.OptionIndex() == optType)
                        typeIndex = gp.Option().CurrentListOptionIndex;
                    continue;
                }
                if (r != GetResult.Point)
                    return Result.Cancel;
                break;
            }
            Kit.SetLastType(ElementKind.NorthArrow, names[typeIndex]);
            Guid id;
            using (SkunkService.Suppress())
                id = NorthArrowElement.Create(doc, gp.Point(), trueNorth.CurrentValue, names[typeIndex]);
            doc.Views.Redraw();
            return id == Guid.Empty ? Result.Failure : Result.Success;
        }

        /// <summary>SkEditType: Edit Type for the selected element's type (or a chosen category).</summary>
        public static Result EditType(RhinoDoc doc, RunMode mode)
        {
            var kind = ElementKind.SpotElevation;
            string typeName = null;
            var selected = doc.Objects.GetSelectedObjects(false, false).FirstOrDefault(o => ElementIO.KindOf(o) != ElementKind.None);
            if (selected != null)
            {
                kind = ElementIO.KindOf(selected);
                if (ElementKinds.IsLevelPart(kind))
                {
                    var hd = ElementIO.Read<HeadData>(selected);
                    selected = hd == null ? null : ElementIO.Find(doc, hd.Level);
                    kind = ElementKind.Level;
                }
                else if (ElementKinds.IsGridPart(kind))
                {
                    var gh = ElementIO.Read<GridHeadData>(selected);
                    selected = gh == null ? null : ElementIO.Find(doc, gh.Grid);
                    kind = ElementKind.Grid;
                }
                typeName = selected == null ? null : ReadTypeName(selected, kind);
            }
            if (kind == ElementKind.ProjectBasePoint || kind == ElementKind.SurveyPoint || kind == ElementKind.PlanCut || kind == ElementKind.DatumBox)
                kind = ElementKind.SpotElevation;
            UI.TypePropertiesDialog.Show(doc, kind, typeName ?? Kit.LastType(kind));
            return Result.Success;
        }

        static string ReadTypeName(RhinoObject o, ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Level: return ElementIO.Read<LevelData>(o)?.Type;
                case ElementKind.NorthArrow: return ElementIO.Read<NorthArrowData>(o)?.Type;
                case ElementKind.Grid: return ElementIO.Read<GridData>(o)?.Type;
                default: return ElementIO.Read<SpotData>(o)?.Type;
            }
        }

        /// <summary>SkProperties: the Skunk Attributes panel (opens it, or closes it when its tab is in front).</summary>
        public static Result Properties(RhinoDoc doc, RunMode mode)
        {
            UI.SkunkAttributesPanel.Toggle(doc);
            return Result.Success;
        }
    }
}
