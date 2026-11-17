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
    /// <summary>
    /// Instance parameters of every Skunk element (Properties palette), in the familiar groups:
    /// Constraints, Graphics, Text, Dimensions (computed, read-only), Location (the point in every
    /// coordinate system), Extents, Identity Data, Other. Edits apply to the whole selection.
    /// </summary>
    internal static partial class PropertyModel
    {
        // ---- shared rows ------------------------------------------------------------------------------

        static Param RO(string group, string name, string text, string tip = "") => Param.ReadOnly(group, name, text, tip);

        static string LayerPath(RhinoDoc doc, RhinoObject o)
        {
            var layer = o.Attributes.LayerIndex >= 0 && o.Attributes.LayerIndex < doc.Layers.Count ? doc.Layers[o.Attributes.LayerIndex] : null;
            return layer?.FullPath ?? "";
        }

        /// <summary>Object name, layer and id rows (Identity Data).</summary>
        static void Identity(List<Param> list, RhinoDoc doc, List<RhinoObject> objects, bool editableName = true)
        {
            var o = objects[0];
            var ids = objects.Select(x => x.Id).ToList();
            if (editableName)
            {
                list.Add(Param.Edit("Identity Data", "Object Name", o.Attributes.Name ?? "", t =>
                {
                    Kit.Edit(doc, "Skunk Rename", ctx =>
                    {
                        foreach (var id in ids)
                        {
                            var x = ElementIO.Find(doc, id);
                            if (x == null)
                                continue;
                            var a = x.Attributes.Duplicate();
                            a.Name = t ?? "";
                            doc.Objects.ModifyAttributes(x, a, true);
                        }
                    });
                    return true;
                }, "Rhino object name"));
            }
            list.Add(RO("Identity Data", "Layer", objects.All(x => x.Attributes.LayerIndex == o.Attributes.LayerIndex) ? LayerPath(doc, o) : "(varies)"));
            list.Add(RO("Identity Data", "Element Id", objects.Count == 1 ? o.Id.ToString() : objects.Count + " elements"));
        }

        /// <summary>A point in shared, project and internal coordinates (Location group).</summary>
        static void PointRows(List<Param> list, RhinoDoc doc, SiteModel site, Point3d p, string group = "Location")
        {
            var sh = site.Shared(p);
            var pr = site.ProjectCoordinates(p);
            list.Add(RO(group, "Shared N/S", Len(doc, sh.Y), "Survey point / shared coordinates (true north axes)"));
            list.Add(RO(group, "Shared E/W", Len(doc, sh.X)));
            list.Add(RO(group, "Shared Elevation", Len(doc, sh.Z)));
            list.Add(RO(group, "Project N/S", Len(doc, pr.Y), "Relative to the project base point (project north axes)"));
            list.Add(RO(group, "Project E/W", Len(doc, pr.X)));
            list.Add(RO(group, "Project Elevation", Len(doc, pr.Z)));
            list.Add(RO(group, "Internal X, Y, Z", Len(doc, p.X) + ",  " + Len(doc, p.Y) + ",  " + Len(doc, p.Z), "Rhino world coordinates (from the internal origin)"));
        }

        static string PlaneName(Plane p)
        {
            var n = p.ZAxis;
            if (Math.Abs(n.Z) > 0.9999)
                return "Horizontal";
            if (Math.Abs(n.Z) < 1e-4)
                return "Vertical, facing " + UnitFormat.Compass(Math.Atan2(n.X, n.Y) * 180.0 / Math.PI);
            return "Inclined " + Num(Math.Acos(Math.Min(1, Math.Abs(n.Z))) * 180.0 / Math.PI, 1) + "°";
        }

        /// <summary>Azimuth (degrees clockwise from a north direction) of a plan direction.</summary>
        static double Azimuth(Vector3d north, Vector3d dir)
        {
            var d = new Vector3d(dir.X, dir.Y, 0);
            if (!d.Unitize())
                return double.NaN;
            double a = SiteModel.ClockwiseAngle(north, d);
            return a < 0 ? a + 360 : a;
        }

        static string Area(RhinoDoc doc, double modelArea)
        {
            double m2 = modelArea * Math.Pow(DocUnits.MetersPerUnit(doc), 2);
            if (DocUnits.IsImperial(doc))
                return UnitFormat.Decimal(m2 / (UnitFormat.MetersPerFoot * UnitFormat.MetersPerFoot), 0, true, false, false) + " sq ft";
            return UnitFormat.Decimal(m2, 2, true, false, false) + " m²";
        }

        // ---- spots --------------------------------------------------------------------------------------

        /// <summary>Parameters of the selected spots (edits apply to all of them).</summary>
        public static List<Param> Spots(RhinoDoc doc, ElementKind kind, List<RhinoObject> objects)
        {
            var list = new List<Param>();
            var first = objects[0];
            var data = ElementIO.Read<SpotData>(first) ?? new SpotData();
            var ids = objects.Select(o => o.Id).ToList();
            var ctx = new UpdateContext(doc);
            var type = ctx.Types.Find(kind, data.Type);
            var sample = Anchors.Evaluate(doc, Json.Clone(data.Anchor));
            var p = sample.Found ? sample.Point : data.Anchor.LastPoint;

            // Constraints
            var host = ElementIO.Find(doc, data.Anchor.Host);
            list.Add(RO("Constraints", "Host", host == null ? (data.Anchor.Host == Guid.Empty ? "(none - measures its own point)" : "(missing)") : HostName(host)));
            if (host != null)
                list.Add(RO("Constraints", "Host Layer", LayerPath(doc, host)));
            list.Add(RO("Constraints", "Attached To", data.Anchor.Kind == HostKind.BrepFace ? "Face " + (data.Anchor.Index + 1) :
                data.Anchor.Kind == HostKind.Mesh ? "Mesh face " + (data.Anchor.Index + 1) : data.Anchor.Kind.ToString()));
            if (kind == ElementKind.SpotElevation && type is SpotElevationType se)
            {
                list.Add(RO("Constraints", "Elevation Origin", Frames.Describe(se.ElevationOrigin, se.OriginCPlane), "Type parameter (Edit Type)"));
                if (se.ElevationOrigin == CoordinateBase.Relative)
                {
                    var levels = LevelElement.All(doc);
                    var names = levels.Select(l => l.Name).ToArray();
                    int idx = levels.FindIndex(l => l.Id == data.RelativeLevel);
                    list.Add(Param.Choice("Constraints", "Relative Base", names, idx, i => EditSpots(doc, ids, d => d.RelativeLevel = levels[i].Id)));
                }
            }
            if (kind == ElementKind.SpotCoordinate && type is SpotCoordinateType sc0)
                list.Add(RO("Constraints", "Coordinate Origin", Frames.Describe(sc0.CoordinateOrigin, sc0.OriginCPlane), "Type parameter (Edit Type)"));

            // Graphics
            if (kind != ElementKind.SpotSlope)
            {
                list.Add(Param.Check("Graphics", "Leader", data.Leader, v => EditSpots(doc, ids, d => d.Leader = v)));
                list.Add(Param.Check("Graphics", "Shoulder", data.Shoulder, v => EditSpots(doc, ids, d => d.Shoulder = v)));
            }
            if (kind == ElementKind.SpotElevation && type is SpotElevationType set)
            {
                var displays = EtoKit.Names<ElevationDisplay>();
                var current = data.Display ?? set.Display;
                list.Add(Param.Choice("Graphics", "Display Elevations", EtoKit.Words(displays), EtoKit.IndexOf(current),
                    i => EditSpots(doc, ids, d => d.Display = EtoKit.ValueAt<ElevationDisplay>(i))));
            }
            if (kind == ElementKind.SpotSlope && type is SpotSlopeType ss0)
            {
                list.Add(Param.Check("Graphics", "Flip Slope Direction", data.FlipSlope, v => EditSpots(doc, ids, d => d.FlipSlope = v),
                    "Arrow points " + (ss0.Direction == SlopeDirection.Down ? "uphill" : "downhill") + " when checked"));
                list.Add(RO("Graphics", "Placement",
                    SpotElement.PlacedInSideView(data) ? "Side view: in the CPlane it was placed in"
                    : ss0.Placement == SlopePlacement.OnHost ? "On host (follows the slope)" : "View plane (flat)",
                    "Type parameter (Edit Type). A slope placed in a side view (an elevation or section) lies in that view's CPlane, so its arrow and text show there."));
                if (sample.Found)
                {
                    var reach = SpotElement.MeasureSlope(ctx, ss0, data, sample);
                    list.Add(Param.Edit("Graphics", "Arrow Top (uphill)", Len(doc, reach.Top), t => EditReach(doc, ids, t, true),
                        "How far the arrow reaches uphill from its location. Drag its top grip, type a length, or clear the box for the type's arrow length."));
                    list.Add(Param.Edit("Graphics", "Arrow Bottom (downhill)", Len(doc, reach.Bottom), t => EditReach(doc, ids, t, false),
                        "How far the arrow reaches downhill from its location. Drag its bottom grip, type a length, or clear the box for the type's arrow length."));
                    list.Add(Param.Edit("Graphics", "Offset From Host", Len(doc, reach.Offset), t => EditSlopeOffset(doc, ids, t),
                        SpotElement.PlacedInSideView(data)
                            ? "How far the arrow sits above its host line (negative = below). Move the arrow up or down, type a length, or clear the box for the type's Offset From Model."
                            : "How far the arrow sits beside its location. Type a length, or clear the box for the type's Offset From Model."));
                }
            }
            if (first.Geometry is AnnotationBase ab)
                list.Add(RO("Graphics", "Annotation Plane", PlaneName(ab.Plane)));

            // Text
            string upper = kind == ElementKind.SpotSlope ? "Prefix" : "Single / Upper Value Prefix";
            string upperS = kind == ElementKind.SpotSlope ? "Suffix" : "Single / Upper Value Suffix";
            list.Add(Param.Edit("Text", upper, data.Prefix, t => { EditSpots(doc, ids, d => d.Prefix = t); return true; }));
            list.Add(Param.Edit("Text", upperS, data.Suffix, t => { EditSpots(doc, ids, d => d.Suffix = t); return true; }));
            if (kind != ElementKind.SpotSlope)
            {
                list.Add(Param.Edit("Text", "Lower Value Prefix", data.LowerPrefix, t => { EditSpots(doc, ids, d => d.LowerPrefix = t); return true; }));
                list.Add(Param.Edit("Text", "Lower Value Suffix", data.LowerSuffix, t => { EditSpots(doc, ids, d => d.LowerSuffix = t); return true; }));
            }
            list.Add(RO("Text", "Value", ElementIO.NormalizeText(SpotElement.CurrentText(first)).Replace("\n", "  |  ")));

            // Dimensions: the measured values, computed (read-only)
            if (p.IsValid)
            {
                switch (kind)
                {
                    case ElementKind.SpotElevation when type is SpotElevationType se2:
                        {
                            var frame = ctx.Frame(se2.ElevationOrigin, se2.OriginCPlane, data.RelativeLevel);
                            list.Add(RO("Dimensions", "Elevation", Len(doc, frame.Elevation(p)), "From the type's Elevation Origin"));
                            if (Anchors.TopBottom(doc, data.Anchor, p, out double top, out double bottom))
                            {
                                list.Add(RO("Dimensions", "Top Elevation", Len(doc, frame.Elevation(new Point3d(p.X, p.Y, top)))));
                                list.Add(RO("Dimensions", "Bottom Elevation", Len(doc, frame.Elevation(new Point3d(p.X, p.Y, bottom)))));
                                list.Add(RO("Dimensions", "Thickness", Len(doc, top - bottom)));
                            }
                            break;
                        }
                    case ElementKind.SpotCoordinate when type is SpotCoordinateType sc2:
                        {
                            var frame = ctx.Frame(sc2.CoordinateOrigin, sc2.OriginCPlane, Guid.Empty);
                            list.Add(RO("Dimensions", "N/S", Len(doc, frame.North(p)), "From the type's Coordinate Origin"));
                            list.Add(RO("Dimensions", "E/W", Len(doc, frame.East(p))));
                            list.Add(RO("Dimensions", "Elevation", Len(doc, frame.Elevation(p))));
                            break;
                        }
                }
                if (sample.Found && (!sample.Normal.IsZero || !sample.Tangent.IsZero))
                {
                    double r = Anchors.Slope(sample, out Vector3d down);
                    var arrow = kind == ElementKind.SpotSlope && type is SpotSlopeType ss3 ? SpotElement.MeasureSlope(ctx, ss3, data, sample) : null;
                    if (arrow == null)
                        list.Add(RO("Dimensions", "Slope", SlopeFormats(r)));
                    else
                    {
                        list.Add(RO("Dimensions", "Slope", SlopeFormats(arrow.Slope), "Between the arrow's two ends on the host"));
                        list.Add(RO("Dimensions", "Rise / Run", Len(doc, Math.Abs(arrow.Rise)) + " over " + Len(doc, arrow.Run),
                            "Height difference and plan distance between the arrow's ends"));
                        if (Math.Abs(arrow.Slope - r) > 5e-5)
                            list.Add(RO("Dimensions", "Slope at Location", SlopeFormats(r), "The surface's own slope at the arrow's location (the arrow's value spans its ends)"));
                    }
                    double azTrue = Azimuth(ctx.Site.TrueNorth, down);
                    double azProject = Azimuth(ctx.Site.ProjectNorth, down);
                    if (!double.IsNaN(azTrue) && r > 1e-9)
                    {
                        list.Add(RO("Dimensions", "Fall Direction (true north)", UnitFormat.Bearing(azTrue) + "   (" + Num(azTrue) + "°, " + UnitFormat.Compass(azTrue) + ")",
                            "Downhill direction, bearing from true north"));
                        list.Add(RO("Dimensions", "Fall Direction (project north)", UnitFormat.Bearing(azProject) + "   (" + Num(azProject) + "°)"));
                    }
                }
                PointRows(list, doc, ctx.Site, p);
            }

            // Identity Data
            list.Add(Param.Edit("Identity Data", "Comments", data.Comments, t => { EditSpots(doc, ids, d => d.Comments = t); return true; }));
            if (type != null && !string.IsNullOrEmpty(type.Comments))
                list.Add(RO("Identity Data", "Type Comments", type.Comments));
            Identity(list, doc, objects);
            if (objects.Count > 1)
                list.Add(RO("Other", "Selected", objects.Count + " elements (edits apply to all)"));
            return list;
        }

        static string HostName(RhinoObject o)
        {
            string name = string.IsNullOrEmpty(o.Attributes.Name) ? "" : " \"" + o.Attributes.Name + "\"";
            return o.ShortDescription(false) + name;
        }

        static string SlopeFormats(double r) =>
            UnitFormat.Slope(r, new SlopeStyle { Format = SlopeFormat.Percent, Precision = 2 }) + "   " +
            UnitFormat.Slope(r, new SlopeStyle { Format = SlopeFormat.DecimalDegrees, Precision = 2 }) + "   " +
            UnitFormat.Slope(r, new SlopeStyle { Format = SlopeFormat.RatioOneToX, Precision = 1 }) + "   " +
            UnitFormat.Slope(r, new SlopeStyle { Format = SlopeFormat.RiseOver12Inches, FractionDenominator = 16 });

        /// <summary>Set a slope arrow's top / bottom reach from typed text; empty text = the type's arrow length.</summary>
        static bool EditReach(RhinoDoc doc, List<Guid> ids, string text, bool top)
        {
            double? reach = null;
            var s = (text ?? "").Trim();
            if (s.Length > 0)
            {
                if (!ParseLen(doc, s, out double model) || model <= 0)
                    return false;
                reach = model;
            }
            EditSpots(doc, ids, d =>
            {
                if (top)
                    d.SlopeTop = reach;
                else
                    d.SlopeBottom = reach;
            });
            return true;
        }

        /// <summary>Set a slope arrow's offset from its host line from typed text (kept in paper mm); empty text = the type's.</summary>
        static bool EditSlopeOffset(RhinoDoc doc, List<Guid> ids, string text)
        {
            double? mm = null;
            var s = (text ?? "").Trim();
            if (s.Length > 0)
            {
                if (!ParseLen(doc, s, out double model))
                    return false;
                mm = model / DocUnits.PaperToModel(doc, 1.0);
            }
            EditSpots(doc, ids, d => d.SlopeOffsetMM = mm);
            return true;
        }

        static void EditSpots(RhinoDoc doc, List<Guid> ids, Action<SpotData> change)
        {
            Kit.Edit(doc, "Skunk Properties", ctx =>
            {
                foreach (var id in ids)
                {
                    var obj = ElementIO.Find(doc, id);
                    var data = obj == null ? null : ElementIO.Read<SpotData>(obj);
                    if (data == null)
                        continue;
                    change(data);
                    ElementIO.Write(doc, obj, ElementIO.KindOf(obj), data);
                    SpotElement.Update(ctx, doc.Objects.FindId(id), UpdateReason.Context);
                }
            });
        }

        // ---- levels -------------------------------------------------------------------------------------

        static readonly string[] HeadEndNames = { "Right end", "Left end", "Both ends", "None" };

        /// <summary>Parameters of a level.</summary>
        public static List<Param> Level(RhinoDoc doc, RhinoObject obj)
        {
            var list = new List<Param>();
            var info = LevelElement.Info(doc, obj);
            if (info == null)
                return list;
            var ctx = new UpdateContext(doc);
            var site = ctx.Site;
            var type = info.Type;
            var all = LevelElement.All(doc);
            int at = all.FindIndex(l => l.Id == info.Id);
            var above = at >= 0 && at + 1 < all.Count ? all[at + 1] : null;
            var below = at > 0 ? all[at - 1] : null;

            // Constraints
            double display = LevelElement.DisplayElevation(ctx, type, info.Z);
            list.Add(Param.Edit("Constraints", "Elevation", Len(doc, display), t =>
            {
                if (!ParseLen(doc, t, out double v))
                    return false;
                Kit.Edit(doc, "Skunk Level Elevation", c => LevelElement.SetElevationZ(c, info.Id, LevelElement.WorldZ(c, type, v)));
                return true;
            }, "Relative to the type's Elevation Base"));
            list.Add(RO("Constraints", "Elevation Base", Frames.Describe(type?.ElevationBase ?? CoordinateBase.ProjectBasePoint, type?.BaseCPlane), "Type parameter (Edit Type)"));
            var o = site.Project.Origin;
            list.Add(RO("Constraints", "Elevation (Project)", Len(doc, site.ProjectCoordinates(new Point3d(o.X, o.Y, info.Z)).Z)));
            list.Add(RO("Constraints", "Elevation (Shared)", Len(doc, site.Shared(new Point3d(o.X, o.Y, info.Z)).Z)));
            list.Add(RO("Constraints", "Internal Z", Len(doc, info.Z)));
            list.Add(RO("Constraints", "Story Above", above?.Name ?? "(none)"));

            // Dimensions
            list.Add(Param.Edit("Dimensions", "Computation Height", Len(doc, info.Data.ComputationHeight), t =>
            {
                if (!ParseLen(doc, t, out double v))
                    return false;
                EditLevel(doc, info.Id, d => d.ComputationHeight = v);
                return true;
            }));
            list.Add(RO("Dimensions", "Story Height", above != null ? Len(doc, above.Z - info.Z) : "(top level)", "To the next level above"));
            list.Add(RO("Dimensions", "Height Above Level Below", below != null ? Len(doc, info.Z - below.Z) + "  (" + below.Name + ")" : "(lowest level)"));

            // Graphics: line pattern and heads
            var patterns = new List<string> { Styles.ByType + " (" + (string.IsNullOrWhiteSpace(type?.LinePattern) ? Styles.ByLayer : type.LinePattern) + ")", Styles.ByLayer };
            patterns.AddRange(Styles.LinetypeNames(doc));
            string mine = info.Data.LinePattern;
            int patternIndex = string.IsNullOrEmpty(mine) ? 0 : patterns.IndexOf(mine);
            if (patternIndex < 0)
            {
                patterns.Add(mine);
                patternIndex = patterns.Count - 1;
            }
            list.Add(Param.Choice("Graphics", "Line Pattern", patterns.ToArray(), patternIndex, i =>
                EditLevel(doc, info.Id, d => d.LinePattern = i == 0 ? null : patterns[i]),
                "Linetype of this level's line; By Type uses the level type's Line Pattern"));
            var ends = LevelElement.CurrentHeadEnds(info.Data);
            list.Add(Param.Choice("Graphics", "Heads At", HeadEndNames, ends.HasValue ? (int)ends.Value : -1, i =>
                Kit.Edit(doc, "Skunk Level Heads", c => LevelElement.SetHeadEnds(c, info.Id, (LevelElement.HeadEnds)i)),
                "Which end of the level line shows the head, as seen in each elevation (End 1 = left, End 2 = right)"));

            // Extents: per side, and the size of the level
            for (int e = 0; e < info.Footprint.SegmentCount; e++)
            {
                int edge = e;
                string side = Edge(info, site, edge);
                bool left = edge < info.Data.End1.Count && info.Data.End1[edge];
                bool right = edge < info.Data.End2.Count && info.Data.End2[edge];
                list.Add(Param.Check("Extents", side + " elevation - left head", left, v => EditLevel(doc, info.Id, d => SetFlag(d.End1, edge, v)), "Show head at End 1"));
                list.Add(Param.Check("Extents", side + " elevation - right head", right, v => EditLevel(doc, info.Id, d => SetFlag(d.End2, edge, v)), "Show head at End 2"));
            }
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var pt in info.Footprint)
            {
                site.Project.ClosestParameter(pt, out double s, out double t);
                minX = Math.Min(minX, s); maxX = Math.Max(maxX, s);
                minY = Math.Min(minY, t); maxY = Math.Max(maxY, t);
            }
            var boxes = DatumBoxElement.All(doc);
            var boxNames = new[] { "(none)" }.Concat(boxes.Select(b => b.Name)).ToArray();
            int boxIndex = info.Data.Box == Guid.Empty ? 0 : boxes.FindIndex(b => b.Id == info.Data.Box) + 1;
            list.Add(Param.Choice("Extents", "Datum Box", boxNames, Math.Max(0, boxIndex), i =>
                Kit.Edit(doc, "Skunk Datum Box", c => DatumBoxElement.Attach(c, info.Id, i == 0 ? Guid.Empty : boxes[i - 1].Id)),
                "The datum box whose outline this level's extents follow; dragging one of its heads sideways takes it out"));
            list.Add(RO("Extents", "Width (E/W)", Len(doc, maxX - minX)));
            list.Add(RO("Extents", "Depth (N/S)", Len(doc, maxY - minY)));
            double area = 0;
            var fp = info.Footprint;
            for (int i = 0; i < fp.Count - 1; i++)
                area += fp[i].X * fp[i + 1].Y - fp[i + 1].X * fp[i].Y;
            list.Add(RO("Extents", "Extents Area", Area(doc, Math.Abs(area) / 2.0)));

            // Identity Data
            list.Add(Param.Edit("Identity Data", "Name", info.Name, t =>
            {
                if (string.IsNullOrWhiteSpace(t))
                    return false;
                Kit.Edit(doc, "Skunk Rename Level", c => LevelElement.Rename(c, info.Id, t));
                return true;
            }));
            list.Add(Param.Check("Identity Data", "Building Story", info.Data.BuildingStory, v => EditLevel(doc, info.Id, d => d.BuildingStory = v)));
            list.Add(Param.Check("Identity Data", "Structural", info.Data.Structural, v => EditLevel(doc, info.Id, d => d.Structural = v)));
            list.Add(Param.Edit("Identity Data", "Comments", info.Data.Comments, t => { EditLevel(doc, info.Id, d => d.Comments = t); return true; }));
            Identity(list, doc, new List<RhinoObject> { obj }, editableName: false);

            // Other
            var index = SkunkService.Instance?.Index(doc);
            list.Add(RO("Other", "Attached Objects", LevelElement.Attached(doc, info.Id).Length.ToString()));
            if (index != null)
                list.Add(RO("Other", "Relative Spot Elevations", index.RelativeTo(info.Id).Count().ToString()));
            list.Add(RO("Other", "Heads", info.Data.Heads.Count.ToString()));
            list.Add(RO("Other", "Plan Cuts", ElementIO.All(doc, ElementKind.PlanCut).Count(c => ElementIO.Read<PlanCutData>(c)?.Level == info.Id).ToString()));
            list.Add(RO("Other", "Named CPlane", string.IsNullOrEmpty(info.Data.CPlane) ? "(none)" : info.Data.CPlane));
            return list;
        }

        /// <summary>Parameters of a grid.</summary>
        public static List<Param> Grid(RhinoDoc doc, RhinoObject obj)
        {
            var list = new List<Param>();
            var info = GridElement.Info(doc, obj);
            if (info == null)
                return list;
            var id = info.Id;
            bool end1 = info.Data.End1, end2 = info.Data.End2;
            list.Add(Param.Check("Graphics", "Bubble at End 1", end1, v => Kit.Edit(doc, "Skunk Grid Bubbles", c => GridElement.SetEnds(c, id, v, end2)),
                "Plan View Symbols End 1: a bubble where the grid starts"));
            list.Add(Param.Check("Graphics", "Bubble at End 2", end2, v => Kit.Edit(doc, "Skunk Grid Bubbles", c => GridElement.SetEnds(c, id, end1, v)),
                "Plan View Symbols End 2: a bubble where the grid ends"));
            var own = info.Data.ElevationBubbles;
            var elevationChoices = new[] { "By Type (" + (info.Type?.ElevationBubbles ?? Skunk.Types.GridElevationBubbles.Top) + ")", "Top", "Bottom", "Both", "None" };
            list.Add(Param.Choice("Graphics", "Elevation Bubbles", elevationChoices, own.HasValue ? 1 + (int)own.Value : 0, i =>
                Kit.Edit(doc, "Skunk Grid Bubbles", c => GridElement.SetElevationBubbles(c, id, i == 0 ? (Skunk.Types.GridElevationBubbles?)null : (Skunk.Types.GridElevationBubbles)(i - 1))),
                "Where this grid's bubbles are in elevations and sections: its type's, or its own"));

            var boxes = DatumBoxElement.All(doc);
            var boxNames = new[] { "(none)" }.Concat(boxes.Select(b => b.Name)).ToArray();
            int boxIndex = info.Data.Box == Guid.Empty ? 0 : boxes.FindIndex(b => b.Id == info.Data.Box) + 1;
            list.Add(Param.Choice("Extents", "Datum Box", boxNames, Math.Max(0, boxIndex), i =>
                Kit.Edit(doc, "Skunk Datum Box", c => GridElement.Attach(c, id, i == 0 ? Guid.Empty : boxes[i - 1].Id)),
                "The datum box whose outline sets this grid's ends; dragging a bubble along the grid takes it out"));
            list.Add(RO("Extents", "Length", Len(doc, info.Line.Length)));
            var site = SiteModel.Get(doc);
            var dir = info.Line.Direction;
            double angle = RhinoMath.ToDegrees(Math.Atan2(dir * site.Project.YAxis, dir * site.Project.XAxis));
            list.Add(RO("Extents", "Direction", angle.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "\u00b0",
                "From End 1 to End 2, counter-clockwise from project east"));
            list.Add(RO("Extents", "Height", Len(doc, info.Line.From.Z), "The grid's height in the model (plan cuts never clip grids)"));

            list.Add(Param.Edit("Identity Data", "Name", info.Name, t =>
            {
                if (string.IsNullOrWhiteSpace(t))
                    return false;
                Kit.Edit(doc, "Skunk Rename Grid", c => GridElement.Rename(c, id, t));
                return true;
            }));
            list.Add(Param.Edit("Identity Data", "Comments", info.Data.Comments, t =>
            {
                Kit.Edit(doc, "Skunk Properties", c =>
                {
                    var o = ElementIO.Find(doc, id);
                    var d = o == null ? null : ElementIO.Read<GridData>(o);
                    if (d == null)
                        return;
                    d.Comments = t ?? "";
                    ElementIO.Write(doc, o, ElementKind.Grid, d);
                });
                return true;
            }));
            Identity(list, doc, new List<RhinoObject> { obj }, editableName: false);
            return list;
        }

        /// <summary>Parameters of a datum box.</summary>
        public static List<Param> DatumBox(RhinoDoc doc, RhinoObject obj)
        {
            var list = new List<Param>();
            var info = DatumBoxElement.Info(doc, obj);
            if (info == null)
                return list;
            var levels = LevelElement.All(doc).Where(l => l.Data.Box == info.Id).ToList();
            list.Add(RO("Extents", "Levels", levels.Count == 0 ? "(none)" : string.Join(", ", levels.Select(l => l.Name)),
                "The levels whose extents follow this box. A level leaves when one of its heads is dragged sideways; its Datum Box property puts it back"));
            var grids = GridElement.All(doc).Where(g => g.Data.Box == info.Id).ToList();
            list.Add(RO("Extents", "Grids", grids.Count == 0 ? "(none)" : string.Join(", ", grids.Select(g => g.Name)),
                "The grids whose ends sit on this box. A grid leaves when one of its bubbles is dragged along it; its Datum Box property puts it back"));
            // size along its own sides, and how it is turned from project east
            var o = info.Outline;
            var x = o[1] - o[0];
            x.Z = 0;
            x.Unitize();
            var y = new Vector3d(-x.Y, x.X, 0);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in o)
            {
                var v = p - o[0];
                minX = Math.Min(minX, v * x); maxX = Math.Max(maxX, v * x);
                minY = Math.Min(minY, v * y); maxY = Math.Max(maxY, v * y);
            }
            list.Add(RO("Extents", "Length", Len(doc, maxX - minX), "Along its first side"));
            list.Add(RO("Extents", "Width", Len(doc, maxY - minY), "Across its first side"));
            double rotation = DatumBoxElement.Rotation(doc, o);
            list.Add(Param.Edit("Extents", "Rotation", rotation.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "\u00b0", t =>
            {
                var s = (t ?? "").Replace("\u00b0", "").Trim();
                if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double deg))
                    return false;
                Kit.Edit(doc, "Skunk Rotate Datum Box", c => DatumBoxElement.SetRotation(c, info.Id, deg));
                return true;
            }, "Angle of its first side from project east, counter-clockwise, in degrees: type one to turn the box (and its levels) about its middle"));
            double area = 0;
            for (int i = 0; i < o.Count - 1; i++)
                area += o[i].X * o[i + 1].Y - o[i + 1].X * o[i].Y;
            list.Add(RO("Extents", "Extents Area", Area(doc, Math.Abs(area) / 2.0)));

            list.Add(Param.Edit("Identity Data", "Name", info.Name, t =>
            {
                if (string.IsNullOrWhiteSpace(t))
                    return false;
                Kit.Edit(doc, "Skunk Rename Datum Box", c => DatumBoxElement.Rename(c, info.Id, t));
                return true;
            }));
            list.Add(Param.Edit("Identity Data", "Comments", info.Data.Comments, t =>
            {
                Kit.Edit(doc, "Skunk Properties", c =>
                {
                    var box = ElementIO.Find(doc, info.Id);
                    var d = box == null ? null : ElementIO.Read<DatumBoxData>(box);
                    if (d == null)
                        return;
                    d.Comments = t ?? "";
                    ElementIO.Write(doc, box, ElementKind.DatumBox, d);
                });
                return true;
            }));
            Identity(list, doc, new List<RhinoObject> { obj }, editableName: false);
            return list;
        }

        static void SetFlag(List<bool> flags, int index, bool value)
        {
            while (flags.Count <= index)
                flags.Add(false);
            flags[index] = value;
        }

        static void EditLevel(RhinoDoc doc, Guid id, Action<LevelData> change)
        {
            Kit.Edit(doc, "Skunk Properties", ctx =>
            {
                var obj = ElementIO.Find(doc, id);
                var data = obj == null ? null : ElementIO.Read<LevelData>(obj);
                if (data == null)
                    return;
                change(data);
                ElementIO.Write(doc, obj, ElementKind.Level, data);
                LevelElement.Update(ctx, doc.Objects.FindId(id), UpdateReason.Context);
            });
        }

        // ---- project base point / survey point / project --------------------------------------------------

        /// <summary>
        /// Project location parameters (shown when nothing is selected): the Rhino model's geographic location, the project
        /// base point, the survey point, the project. A selected marker shows its own point (the survey point with the
        /// geographic location).
        /// </summary>
        public static List<Param> Location(RhinoDoc doc, bool pbp, bool sp)
        {
            var list = new List<Param>();
            var site = SiteModel.Get(doc);
            if (sp)
                Geographic(list, doc, site);
            if (pbp)
            {
                list.Add(Param.GroupButton("Project Base Point", "Place", () => EtoKit.RunCommand("SkProjectBasePoint"),
                    "Place the project base point: pick its new location, or type its shared coordinates (SkProjectBasePoint; the model does not move)"));
                var shared = site.ProjectBasePointShared;
                list.Add(Param.Edit("Project Base Point", "N/S", Len(doc, shared.Y), t => EditSite(doc, t, (s, v) => { var c = s.ProjectBasePointShared; s.SetProjectBasePointShared(c.X, v, c.Z); })));
                list.Add(Param.Edit("Project Base Point", "E/W", Len(doc, shared.X), t => EditSite(doc, t, (s, v) => { var c = s.ProjectBasePointShared; s.SetProjectBasePointShared(v, c.Y, c.Z); })));
                list.Add(Param.Edit("Project Base Point", "Elev", Len(doc, shared.Z), t => EditSite(doc, t, (s, v) => { var c = s.ProjectBasePointShared; s.SetProjectBasePointShared(c.X, c.Y, v); })));
                double a = site.AngleToTrueNorthDegrees;
                list.Add(Param.Edit("Project Base Point", "Angle to True North", UnitFormat.AngleEastWest(a), t =>
                {
                    if (!TryParseAngle(t, out double deg))
                        return false;
                    var s = SiteModel.Get(doc);
                    s.SetAngleToTrueNorth(deg, RotationPivot.ProjectBasePoint);
                    SaveSite(doc, s);
                    return true;
                }, "e.g. 30 East, 12.5 W, or -12.5 (negative = West)"));
                double ccw = ((-a % 360) + 360) % 360;
                list.Add(RO("Project Base Point", "Angle to True North (CCW)", Num(ccw) + "°", "The same angle measured counter-clockwise from project north, 0 to 360°"));
                var o = site.Project.Origin;
                list.Add(RO("Project Base Point", "Internal X, Y, Z", Len(doc, o.X) + ",  " + Len(doc, o.Y) + ",  " + Len(doc, o.Z)));
                list.Add(RO("Project Base Point", "Project North (azimuth)", Num(Azimuth(Vector3d.YAxis, site.ProjectNorth)) + "° from world Y"));
                list.Add(RO("Project Base Point", "Project CPlane", site.Data.PbpCPlane));
            }
            if (sp)
            {
                list.Add(Param.GroupButton("Survey Point", "Place", () => EtoKit.RunCommand("SkSurveyPoint"),
                    "Place the survey point: pick its new location, or type its shared coordinates (SkSurveyPoint; clipped, the shared coordinates move with it)"));
                list.Add(Param.Edit("Survey Point", "N/S", Len(doc, site.Data.SpNorth), t => EditSite(doc, t, (s, v) => s.Data.SpNorth = v)));
                list.Add(Param.Edit("Survey Point", "E/W", Len(doc, site.Data.SpEast), t => EditSite(doc, t, (s, v) => s.Data.SpEast = v)));
                list.Add(Param.Edit("Survey Point", "Elev", Len(doc, site.Data.SpElevation), t => EditSite(doc, t, (s, v) => s.Data.SpElevation = v)));
                list.Add(Param.Check("Survey Point", "Clipped", site.Data.SpClipped, v =>
                {
                    var s = SiteModel.Get(doc);
                    s.Data.SpClipped = v;
                    SaveSite(doc, s);
                }, "Clipped: moving the survey point moves the shared coordinate system with it"));
                var o = site.Survey.Origin;
                list.Add(RO("Survey Point", "Internal X, Y, Z", Len(doc, o.X) + ",  " + Len(doc, o.Y) + ",  " + Len(doc, o.Z)));
                var origin = site.SharedOrigin;
                list.Add(RO("Survey Point", "Shared Origin (internal)", Len(doc, origin.X) + ",  " + Len(doc, origin.Y) + ",  " + Len(doc, origin.Z), "Where shared N 0, E 0, Elev 0 is"));
                list.Add(RO("Survey Point", "True North (azimuth)", Num(Azimuth(Vector3d.YAxis, site.TrueNorth)) + "° from world Y"));
                list.Add(RO("Survey Point", "True North CPlane", site.Data.SpCPlane));
            }
            if (pbp && sp)
            {
                list.Add(RO("Project", "Model Units", doc.ModelUnitSystem.ToString()));
                list.Add(RO("Project", "Annotation Scale", DocUnits.Describe(DocUnits.AnnotationScale(doc)) +
                    (DocUnits.RhinoScalesAnnotation(doc) ? " (Rhino scales Skunk's styles: paper sizes in layouts)" : " (Skunk scales the sizes)")));
                var index = SkunkService.Instance?.Index(doc);
                list.Add(RO("Project", "Levels", LevelElement.All(doc).Count.ToString()));
                if (index != null)
                {
                    list.Add(RO("Project", "Spots", index.Spots.Count.ToString()));
                    list.Add(RO("Project", "North Arrows", index.Arrows.Count.ToString()));
                }
                list.Add(RO("Project", "Live Updates", SkunkService.Instance?.Running == true ? "On" : "Off"));
            }
            return list;
        }

        /// <summary>
        /// The project's geographic location: the Rhino model's own (its Earth anchor point, which Rhino's Sun shares),
        /// read-only here - it is set in Rhino - and Rhino's north against true north.
        /// </summary>
        static void Geographic(List<Param> list, RhinoDoc doc, SiteModel site)
        {
            const string g = "Location";
            const string setIt = "Set it in Rhino: Document Properties > Location, with its world map (Location... > Location > Set in Rhino opens it).";
            var geo = GeoLocation.Read(doc);
            if (!geo.IsSet)
            {
                list.Add(RO(g, "Geographic Location", "Not set in this Rhino model", "The project location is the Rhino model's geographic location. " + setIt));
            }
            else
            {
                list.Add(RO(g, "Place", geo.Name.Length > 0 ? geo.Name : "(no name)", "The Rhino model's location name. " + setIt));
                if (geo.Description.Length > 0)
                    list.Add(RO(g, "Address", geo.Description, "The Rhino model's location description"));
                list.Add(RO(g, "Latitude", GeoLocation.FormatLatitude(geo.Latitude), "Latitude of the Rhino model's anchor point. " + setIt));
                list.Add(RO(g, "Longitude", GeoLocation.FormatLongitude(geo.Longitude), "Longitude of the Rhino model's anchor point. " + setIt));
                list.Add(RO(g, "Elevation", geo.Elevation(doc), "Elevation of the anchor point (Rhino keeps it in meters)"));
                var p = geo.ModelPoint;
                list.Add(RO(g, "Anchor Point (internal)", Len(doc, p.X) + ",  " + Len(doc, p.Y) + ",  " + Len(doc, p.Z), "The model point at this latitude and longitude"));
            }
            list.Add(RO(g, "Rhino North", geo.NorthText(site),
                "Rhino's north (its Sun and Earth anchor point), from project north. It follows true north while the two agree; Location... > Location matches them when they differ."));
        }

        public static bool TryParseAngle(string text, out double clockwiseDegrees)
        {
            clockwiseDegrees = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var t = text.Trim().ToLowerInvariant().Replace("°", " ");
            double sign = 1;
            if (t.EndsWith("west") || t.EndsWith(" w"))
            {
                sign = -1;
                t = t.EndsWith("west") ? t.Substring(0, t.Length - 4) : t.Substring(0, t.Length - 2);
            }
            else if (t.EndsWith("east") || t.EndsWith(" e"))
                t = t.EndsWith("east") ? t.Substring(0, t.Length - 4) : t.Substring(0, t.Length - 2);
            if (!ParseDouble(t.Trim(), out double v))
                return false;
            clockwiseDegrees = sign * v;
            return true;
        }

        static bool EditSite(RhinoDoc doc, string text, Action<SiteModel, double> change)
        {
            if (!ParseLen(doc, text, out double v))
                return false;
            var site = SiteModel.Get(doc);
            change(site, v);
            SaveSite(doc, site);
            return true;
        }

        static void SaveSite(RhinoDoc doc, SiteModel site)
        {
            Kit.Edit(doc, "Skunk Location", ctx => site.Save(doc), contextChanged: true);
        }

        // ---- north arrows, plan cuts -----------------------------------------------------------------

        /// <summary>North arrow parameters.</summary>
        public static List<Param> NorthArrow(RhinoDoc doc, List<RhinoObject> objects)
        {
            var list = new List<Param>();
            var data = ElementIO.Read<NorthArrowData>(objects[0]) ?? new NorthArrowData();
            var ids = objects.Select(o => o.Id).ToList();
            list.Add(Param.Choice("Graphics", "Points To", new[] { "True North", "Project North" }, data.TrueNorth ? 0 : 1, i =>
                Kit.Edit(doc, "Skunk Properties", ctx =>
                {
                    foreach (var id in ids)
                    {
                        var o = ElementIO.Find(doc, id);
                        var d = o == null ? null : ElementIO.Read<NorthArrowData>(o);
                        if (d == null)
                            continue;
                        d.TrueNorth = i == 0;
                        ElementIO.Write(doc, o, ElementKind.NorthArrow, d);
                        NorthArrowElement.Update(ctx, doc.Objects.FindId(id));
                    }
                })));
            if (objects[0] is InstanceObject inst)
            {
                var site = SiteModel.Get(doc);
                var up = Vector3d.YAxis;
                up.Transform(inst.InstanceXform);
                list.Add(RO("Graphics", "Rotation", Num(Azimuth(site.ProjectNorth, up)) + "° from project north"));
                list.Add(RO("Graphics", "Size", Len(doc, data.Size)));
                var p = inst.InsertionPoint;
                list.Add(RO("Location", "Internal X, Y, Z", Len(doc, p.X) + ",  " + Len(doc, p.Y) + ",  " + Len(doc, p.Z)));
            }
            Identity(list, doc, objects);
            return list;
        }

        /// <summary>Plan cut (Go To Level view range) parameters.</summary>
        public static List<Param> PlanCut(RhinoDoc doc, RhinoObject obj)
        {
            var list = new List<Param>();
            var data = ElementIO.Read<PlanCutData>(obj);
            var level = data == null ? null : LevelElement.Info(doc, ElementIO.Find(doc, data.Level));
            var settings = DocStore.Settings(doc);
            list.Add(RO("Constraints", "Level", level?.Name ?? "(missing)"));
            list.Add(RO("Constraints", "Cut Plane", Len(doc, DocUnits.FromMeters(doc, settings.CutHeightMeters)) + " above the level", "Skunk Settings > Plan cut height"));
            list.Add(RO("Constraints", "View Depth", Len(doc, DocUnits.FromMeters(doc, settings.ViewDepthMeters)) + " below the level", "Skunk Settings > View depth"));
            string view = "(not found)";
            foreach (var v in doc.Views)
                if (data != null && v.ActiveViewport.Id == data.Viewport)
                    view = v.ActiveViewport.Name;
            list.Add(RO("Constraints", "Clips View", view));
            Identity(list, doc, new List<RhinoObject> { obj });
            return list;
        }

        // ---- ordinary Rhino objects (level constraint) ----------------------------------------------------

        /// <summary>Base level / offsets for ordinary objects (base constraint / base offset).</summary>
        public static List<Param> Attached(RhinoDoc doc, List<RhinoObject> objects)
        {
            var list = new List<Param>();
            var levels = LevelElement.All(doc);
            var names = new[] { "(none)" }.Concat(levels.Select(l => l.Name)).ToArray();
            var current = LevelElement.AttachedLevel(objects[0]);
            bool same = objects.All(o => LevelElement.AttachedLevel(o) == current);
            int idx = same ? levels.FindIndex(l => l.Id == current) + 1 : -1;
            var ids = objects.Select(o => o.Id).ToList();
            list.Add(Param.Choice("Constraints", "Base Level", names, idx, i =>
            {
                Guid level = i == 0 ? Guid.Empty : levels[i - 1].Id;
                Kit.Edit(doc, "Skunk Attach", ctx =>
                {
                    foreach (var id in ids)
                    {
                        var o = ElementIO.Find(doc, id);
                        if (o != null)
                            LevelElement.Attach(doc, o, level);
                    }
                });
            }, "Attached objects move with their level"));
            var bb = BoundingBox.Empty;
            foreach (var o in objects)
                bb.Union(o.Geometry.GetBoundingBox(true));
            var levelInfo = levels.FirstOrDefault(l => l.Id == current);
            if (levelInfo != null && objects.Count == 1)
            {
                double offset = bb.Min.Z - levelInfo.Z;
                list.Add(Param.Edit("Constraints", "Base Offset", Len(doc, offset), t =>
                {
                    if (!ParseLen(doc, t, out double v))
                        return false;
                    Kit.Edit(doc, "Skunk Base Offset", ctx =>
                    {
                        var o = ElementIO.Find(doc, ids[0]);
                        double now = o.Geometry.GetBoundingBox(true).Min.Z - levelInfo.Z;
                        doc.Objects.Transform(o.Id, Transform.Translation(0, 0, v - now), true);
                        SkunkService.Instance?.Touch(doc, new[] { o.Id });
                    });
                    return true;
                }, "Distance from the level to the bottom of the object"));
                list.Add(RO("Constraints", "Top Offset", Len(doc, bb.Max.Z - levelInfo.Z), "Top of the object above its base level"));
            }
            if (bb.IsValid)
            {
                var site = SiteModel.Get(doc);
                var c = bb.Center;
                list.Add(RO("Dimensions", "Height", Len(doc, bb.Max.Z - bb.Min.Z)));
                list.Add(RO("Dimensions", "Bottom Elevation (Project)", Len(doc, site.ProjectCoordinates(new Point3d(c.X, c.Y, bb.Min.Z)).Z)));
                list.Add(RO("Dimensions", "Top Elevation (Project)", Len(doc, site.ProjectCoordinates(new Point3d(c.X, c.Y, bb.Max.Z)).Z)));
                list.Add(RO("Dimensions", "Bottom Elevation (Shared)", Len(doc, site.Shared(new Point3d(c.X, c.Y, bb.Min.Z)).Z)));
                list.Add(RO("Dimensions", "Top Elevation (Shared)", Len(doc, site.Shared(new Point3d(c.X, c.Y, bb.Max.Z)).Z)));
            }
            list.Add(RO("Identity Data", "Object Type", objects.Select(o => o.ShortDescription(false)).Distinct().Count() == 1 ? objects[0].ShortDescription(false) : "(varies)"));
            Identity(list, doc, objects);
            return list;
        }
    }
}
