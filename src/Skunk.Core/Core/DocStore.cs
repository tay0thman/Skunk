using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Skunk.Units;

namespace Skunk.Core
{
    /// <summary>
    /// Document-level Skunk settings (stored as JSON in the document strings, section "Skunk").
    /// </summary>
    public sealed class SkunkSettings
    {
        /// <summary>2: Rhino dimensions go to the Skunk sublayer by default (see DocStore.Settings).</summary>
        public int Version = 2;
        /// <summary>Annotation scale used when Rhino's model-space annotation scaling is off (the view scale).</summary>
        public double AnnotationScale = 0;
        /// <summary>Create / update a named CPlane for every level.</summary>
        public bool LevelCPlanes = true;
        /// <summary>Plan-view cut height above the level (model metres) used by Go To Level (the view range cut plane).</summary>
        public double CutHeightMeters = 1.2192;
        /// <summary>View depth below the level (model metres) for the plan cut.</summary>
        public double ViewDepthMeters = 0.3048;
        /// <summary>Delete spot annotations when their host is deleted. Off: they keep the last position.</summary>
        public bool DeleteSpotsWithHost = true;
        /// <summary>Pixel size of the base point glyphs.</summary>
        public int MarkerPixelSize = 22;
        public bool ShowSiteMarkers = true;
        public bool ShowInternalOrigin = true;
        public bool ShowNorthArrows = true;
        /// <summary>Default level extents margin (fraction of the model size) when no level exists yet.</summary>
        public double ExtentsMargin = 0.15;
        /// <summary>Put Rhino's own new dimensions and leaders (not Skunk's annotations) on the Skunk sublayer below (on by default).</summary>
        public bool DimensionsOnSkunkLayer = true;
        /// <summary>Name of that sublayer of Skunk's top-level layer ("__Annotations__").</summary>
        public string DimensionsLayer = Keys.DimensionsLayer;
        /// <summary>
        /// Model units (metres per unit) Skunk's stored lengths are in. A different value later means the
        /// units changed while Skunk was not watching; the annotations then tell whether Rhino scaled them.
        /// </summary>
        public double MetersPerUnit;
        /// <summary>
        /// Keep Rhino's linetype display scale (model views only; prints never use it) at the annotation scale,
        /// so level lines look in model views the way they print. Turns itself off when the model has a value
        /// of its own (see LinetypeDisplay).
        /// </summary>
        public bool LinetypeScaleFollows = true;
        /// <summary>The linetype display scale Skunk set last (0 = none yet).</summary>
        public double LinetypeScaleSet;
        /// <summary>2: Skunk Datum and the Levels layer were checked once for the 1.4.1-and-earlier problems (Styles.RepairDatum).</summary>
        public int DatumVersion;

        public SkunkSettings Clone() => Json.Clone(this);
    }

    /// <summary>Read / write JSON values in the document strings, with a tiny per-document cache.</summary>
    public static class DocStore
    {
        sealed class Cached
        {
            public string Text;
            public object Value;
        }

        static readonly Dictionary<(uint, string), Cached> s_cache = new Dictionary<(uint, string), Cached>();

        public static string GetString(RhinoDoc doc, string entry)
        {
            if (doc == null)
                return null;
            return doc.Strings.GetValue(Keys.Section, entry);
        }

        public static void SetString(RhinoDoc doc, string entry, string value)
        {
            if (doc == null)
                return;
            string old = doc.Strings.GetValue(Keys.Section, entry);
            if (old == value)
                return;
            doc.Strings.SetString(Keys.Section, entry, value);
        }

        /// <summary>Read and deserialize (cached while the stored text is unchanged).</summary>
        public static T Get<T>(RhinoDoc doc, string entry) where T : class
        {
            string text = GetString(doc, entry);
            if (string.IsNullOrEmpty(text))
                return null;
            var key = (doc.RuntimeSerialNumber, entry);
            if (s_cache.TryGetValue(key, out var c) && c.Text == text && c.Value is T cached)
                return Json.Clone(cached);
            var value = Json.Read<T>(text);
            if (value != null)
                s_cache[key] = new Cached { Text = text, Value = Json.Clone(value) };
            return value;
        }

        public static void Set<T>(RhinoDoc doc, string entry, T value) where T : class
        {
            string text = Json.Write(value);
            SetString(doc, entry, text);
            s_cache[(doc.RuntimeSerialNumber, entry)] = new Cached { Text = text, Value = Json.Clone(value) };
        }

        public static void Forget(RhinoDoc doc)
        {
            if (doc == null)
                return;
            var remove = new List<(uint, string)>();
            foreach (var k in s_cache.Keys)
                if (k.Item1 == doc.RuntimeSerialNumber)
                    remove.Add(k);
            foreach (var k in remove)
                s_cache.Remove(k);
        }

        public static SkunkSettings Settings(RhinoDoc doc)
        {
            var s = Get<SkunkSettings>(doc, Keys.SettingsEntry);
            if (s == null)
            {
                s = new SkunkSettings();
                if (s.AnnotationScale <= 0)
                    s.AnnotationScale = DocUnits.DefaultViewScale(doc);
                if (!DocUnits.IsImperial(doc))
                {
                    s.CutHeightMeters = 1.2;
                    s.ViewDepthMeters = 0.3;
                }
            }
            if (s.AnnotationScale <= 0)
                s.AnnotationScale = DocUnits.DefaultViewScale(doc);
            if (s.Version < 2)
            {
                // settings saved before the dimension sublayer became the default (until they are saved again)
                s.DimensionsOnSkunkLayer = true;
                s.Version = 2;
            }
            return s;
        }

        public static void SaveSettings(RhinoDoc doc, SkunkSettings settings) => Set(doc, Keys.SettingsEntry, settings);
    }

    /// <summary>Document units helpers.</summary>
    public static class DocUnits
    {
        public static double MetersPerUnit(RhinoDoc doc) =>
            doc == null ? 1.0 : RhinoMath.UnitScale(doc.ModelUnitSystem, UnitSystem.Meters);

        public static double ToMeters(RhinoDoc doc, double modelLength) => modelLength * MetersPerUnit(doc);

        public static double FromMeters(RhinoDoc doc, double meters) => meters / MetersPerUnit(doc);

        public static bool IsImperial(RhinoDoc doc) => doc == null || IsImperial(doc.ModelUnitSystem);

        static bool IsImperial(UnitSystem units)
        {
            switch (units)
            {
                case UnitSystem.Inches:
                case UnitSystem.Feet:
                case UnitSystem.Yards:
                case UnitSystem.Miles:
                case UnitSystem.Mils:
                case UnitSystem.Microinches:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The length format Rhino shows for a unit system, distance display and display precision
        /// (Document Properties > Units). Rhino's rarer units fall back to the nearest format Skunk has
        /// (yards / miles as feet, kilometres as metres, decimetres as centimetres, microns as millimetres).
        /// </summary>
        public static LengthStyle RhinoLengthStyle(UnitSystem units, Rhino.UI.DistanceDisplayMode mode, int precision)
        {
            int p = Math.Max(0, Math.Min(7, precision));
            var s = new LengthStyle { Precision = p, FractionDenominator = 1 << p };
            switch (units)
            {
                case UnitSystem.Inches:
                case UnitSystem.Mils:
                case UnitSystem.Microinches:
                    s.Format = mode == Rhino.UI.DistanceDisplayMode.Decimal ? LengthFormat.DecimalInches
                        : mode == Rhino.UI.DistanceDisplayMode.Fractional ? LengthFormat.FractionalInches
                        : LengthFormat.FeetFractionalInches;
                    break;
                case UnitSystem.Feet:
                case UnitSystem.Yards:
                case UnitSystem.Miles:
                    s.Format = mode == Rhino.UI.DistanceDisplayMode.Decimal ? LengthFormat.DecimalFeet : LengthFormat.FeetFractionalInches;
                    break;
                case UnitSystem.Millimeters:
                case UnitSystem.Microns:
                case UnitSystem.Nanometers:
                case UnitSystem.Angstroms:
                    s.Format = LengthFormat.Millimeters;
                    break;
                case UnitSystem.Centimeters:
                case UnitSystem.Decimeters:
                    s.Format = LengthFormat.Centimeters;
                    break;
                default:
                    s.Format = LengthFormat.Meters;
                    break;
            }
            return s;
        }

        /// <summary>Rhino's model length display for this document (Document Properties > Units).</summary>
        public static LengthStyle ModelLengthStyle(RhinoDoc doc) => doc == null
            ? LengthStyle.Imperial()
            : RhinoLengthStyle(doc.ModelUnitSystem, doc.ModelDistanceDisplayMode, doc.ModelDistanceDisplayPrecision);

        /// <summary>How type sizes (paper sizes) are shown and typed: Rhino's page (layout) units and their display.</summary>
        public static PaperUnit Paper(RhinoDoc doc)
        {
            if (doc == null)
                return PaperUnit.FractionalInches;
            if (!IsImperial(doc.PageUnitSystem))
                return PaperUnit.Millimeters;
            return doc.PageDistanceDisplayMode == Rhino.UI.DistanceDisplayMode.Decimal ? PaperUnit.DecimalInches : PaperUnit.FractionalInches;
        }

        /// <summary>
        /// The annotation scale (the view scale) Skunk sizes its annotation with: Skunk's own setting (SkSettings),
        /// 1/8" = 1'-0" or 1:100 to begin with (<see cref="DefaultViewScale"/>). How Rhino uses it: see
        /// <see cref="RhinoScalesAnnotation"/> and <see cref="StyleModelSpaceScale"/>. RhinoDoc.ModelSpaceTextScale is a
        /// separate document value Rhino does not draw dimension styles with (1 in every Rhino 9 template), so Skunk
        /// neither reads nor sets it.
        /// </summary>
        public static double AnnotationScale(RhinoDoc doc)
        {
            if (doc == null)
                return 1;
            double scale = DocStore.Settings(doc).AnnotationScale;
            return scale > 0 ? scale : DefaultViewScale(doc);
        }

        /// <summary>
        /// Rhino scales annotation in model views and in layout details: its model space and layout space scaling are
        /// both on, as in every Rhino 9 template. Dimension style sizes are then paper sizes in page units - a layout
        /// detail prints them at that size at any detail scale (measured: a 1/8" = 1'-0" detail draws a style length
        /// times 8 feet per page inch) - and model views show them times the style's model space scale. Otherwise
        /// Skunk puts the scale in the sizes itself.
        /// </summary>
        public static bool RhinoScalesAnnotation(RhinoDoc doc) =>
            doc != null && doc.ModelSpaceAnnotationScalingEnabled && doc.LayoutSpaceAnnotationScalingEnabled;

        /// <summary>One page unit (a paper inch or millimetre) in model units.</summary>
        public static double PageUnitInModel(RhinoDoc doc)
        {
            if (doc == null)
                return 1;
            double s = RhinoMath.UnitScale(PageUnits(doc), doc.ModelUnitSystem);
            return s > 0 && !double.IsInfinity(s) ? s : 1;
        }

        /// <summary>
        /// The model space scale Skunk's styles carry. Rhino draws a style's page-unit sizes in model views times this
        /// number, read as model units, so it is the annotation scale times one page unit in model units: 1/8" = 1'-0"
        /// with inch pages is 96 / 12 = 8 in a feet model and 96 in an inch model; 1:100 is 100 in a millimetre model
        /// and 0.1 in a metre one (Rhino's own templates: Millimeter Architectural 100, Meters Small 0.1). 1 when Skunk
        /// scales the sizes itself.
        /// </summary>
        public static double StyleModelSpaceScale(RhinoDoc doc) =>
            RhinoScalesAnnotation(doc) ? AnnotationScale(doc) * PageUnitInModel(doc) : 1.0;

        /// <summary>The view scale a model starts with: 1/8" = 1'-0" (1:96) in feet and inches, 1:100 in metric units.</summary>
        public static double DefaultViewScale(RhinoDoc doc) => IsImperial(doc) ? 96 : 100;

        /// <summary>A paper length (mm) as a model length for geometry Skunk draws itself (leader offsets, arrows).</summary>
        public static double PaperToModel(RhinoDoc doc, double paperMillimeters) =>
            FromMeters(doc, paperMillimeters / 1000.0) * AnnotationScale(doc);

        /// <summary>
        /// A paper length (mm) as a dimension-style length (text height, arrow size): in page units when Rhino scales
        /// annotation (what layout details print), else the model length at the annotation scale.
        /// </summary>
        public static double PaperToStyle(RhinoDoc doc, double paperMillimeters)
        {
            if (RhinoScalesAnnotation(doc))
                return paperMillimeters * RhinoMath.UnitScale(UnitSystem.Millimeters, PageUnits(doc));
            return FromMeters(doc, paperMillimeters / 1000.0) * AnnotationScale(doc);
        }

        /// <summary>The page units, millimetres when the model has none set.</summary>
        static UnitSystem PageUnits(RhinoDoc doc)
        {
            var u = doc.PageUnitSystem;
            return u == UnitSystem.None || u == UnitSystem.Unset || u == UnitSystem.CustomUnits ? UnitSystem.Millimeters : u;
        }

        public static string Describe(double scale)
        {
            if (scale <= 0)
                return "?";
            double inchesPerFoot = 12.0 / scale;
            string[] known = { "1/32", "1/16", "3/32", "1/8", "3/16", "1/4", "3/8", "1/2", "3/4", "1", "1 1/2", "3" };
            foreach (var k in known)
            {
                double v = Parse(k);
                if (Math.Abs(v - inchesPerFoot) < 1e-6)
                    return k + "\" = 1'-0\"  (1:" + scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ")";
            }
            return "1:" + scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        static double Parse(string fraction)
        {
            double total = 0;
            foreach (var part in fraction.Split(' '))
            {
                int s = part.IndexOf('/');
                total += s > 0 ? double.Parse(part.Substring(0, s)) / double.Parse(part.Substring(s + 1)) : double.Parse(part);
            }
            return total;
        }
    }

    /// <summary>
    /// Layers, created on demand under one top-level layer, "__Annotations__" (Keys.RootLayer; "Skunk" in earlier builds,
    /// which <see cref="Migrate"/> puts right). A model that already has an "__Annotations__" layer gets Skunk's sublayers in it.
    /// </summary>
    public static class Layers
    {
        public static int Ensure(RhinoDoc doc, string childName, System.Drawing.Color color, double plotWeight = 0.0, string linetype = null)
        {
            Migrate(doc);
            int root = doc.Layers.FindByFullPath(Keys.RootLayer, -1);
            if (root < 0)
            {
                var parent = new Layer { Name = Keys.RootLayer, Color = System.Drawing.Color.FromArgb(70, 70, 70) };
                root = doc.Layers.Add(parent);
            }
            string full = Keys.RootLayer + "::" + childName;
            int index = doc.Layers.FindByFullPath(full, -1);
            if (index >= 0)
                return index;
            var layer = new Layer
            {
                Name = childName,
                ParentLayerId = doc.Layers[root].Id,
                Color = color,
                PlotWeight = plotWeight,
            };
            var lt = FindLinetype(doc, linetype);
            if (lt != null)
                layer.LinetypeIndex = lt.Index;
            return doc.Layers.Add(layer);
        }

        /// <summary>
        /// The linetype with this name, or null. Rhino's LinetypeTable.FindName never returns null: for a name
        /// it does not know it returns the Continuous linetype (index -1), so a null check alone always passes.
        /// </summary>
        public static Linetype FindLinetype(RhinoDoc doc, string name)
        {
            if (doc == null || string.IsNullOrWhiteSpace(name))
                return null;
            var lt = doc.Linetypes.FindName(name);
            return lt != null && lt.Index >= 0 && !lt.IsDeleted && string.Equals(lt.Name, name, StringComparison.OrdinalIgnoreCase) ? lt : null;
        }

        /// <summary>Index of the __Annotations__::Levels layer, or -1.</summary>
        public static int LevelsLayerIndex(RhinoDoc doc) => Find(doc, Keys.LevelsLayer);

        /// <summary>
        /// Index of one of Skunk's sublayers, or -1. Reads only: a model from an earlier build that has not been
        /// migrated yet still has it under "Skunk".
        /// </summary>
        public static int Find(RhinoDoc doc, string childName)
        {
            if (doc == null)
                return -1;
            int i = doc.Layers.FindByFullPath(Keys.RootLayer + "::" + childName, -1);
            return i >= 0 ? i : doc.Layers.FindByFullPath(Keys.OldRootLayer + "::" + childName, -1);
        }

        /// <summary>
        /// Earlier Skunk builds kept their layers under a top-level "Skunk" layer; it is "__Annotations__" now. The old
        /// layer is renamed; when the model already has an "__Annotations__" layer, Skunk's sublayers move into it
        /// instead - a sublayer it already has there takes the objects of Skunk's one - and the old layer goes once it is
        /// empty. Objects keep their layers' colours, linetypes and states. Runs when Skunk makes a layer and when a model
        /// opens (the service's full update). True when it changed anything.
        /// </summary>
        public static bool Migrate(RhinoDoc doc)
        {
            if (doc == null)
                return false;
            int old = doc.Layers.FindByFullPath(Keys.OldRootLayer, -1);
            if (old < 0)
                return false;
            int root = doc.Layers.FindByFullPath(Keys.RootLayer, -1);
            // a model's layers change as their properties are set (Rhino 9). Changing a layer makes Rhino apply its
            // sublayers' remembered (persistent) visibility and locking again: keep the states they have now
            var oldLayer = doc.Layers[old];
            var states = Descendants(doc, oldLayer.Id).Select(l => (l.Index, l.IsVisible, l.IsLocked)).ToList();
            if (root < 0)
            {
                oldLayer.Name = Keys.RootLayer;
                Restore(doc, states);
                return doc.Layers.FindByFullPath(Keys.RootLayer, -1) == old;
            }
            var rootId = doc.Layers[root].Id;
            bool changed = false;
            foreach (var child in Children(doc, oldLayer.Id))
            {
                int there = doc.Layers.FindByFullPath(Keys.RootLayer + "::" + child.Name, -1);
                if (there < 0)
                {
                    child.ParentLayerId = rootId;
                    changed = true;
                    continue;
                }
                // a sublayer of that name is there already: it takes the objects and sublayers of Skunk's one
                changed |= MoveObjects(doc, child, there);
                foreach (var grandchild in Children(doc, child.Id))
                {
                    if (doc.Layers.FindByFullPath(doc.Layers[there].FullPath + "::" + grandchild.Name, -1) >= 0)
                        continue;
                    grandchild.ParentLayerId = doc.Layers[there].Id;
                    changed = true;
                }
                changed |= DeleteIfEmpty(doc, child.Index);
            }
            Restore(doc, states);
            changed |= DeleteIfEmpty(doc, old);
            return changed;
        }

        static List<Layer> Descendants(RhinoDoc doc, Guid parent)
        {
            var list = new List<Layer>();
            foreach (var child in Children(doc, parent))
            {
                list.Add(child);
                list.AddRange(Descendants(doc, child.Id));
            }
            return list;
        }

        static void Restore(RhinoDoc doc, List<(int Index, bool Visible, bool Locked)> states)
        {
            foreach (var s in states)
            {
                var layer = s.Index >= 0 && s.Index < doc.Layers.Count ? doc.Layers[s.Index] : null;
                if (layer == null || layer.IsDeleted)
                    continue;
                if (layer.IsVisible != s.Visible)
                    layer.IsVisible = s.Visible;
                if (layer.IsLocked != s.Locked)
                    layer.IsLocked = s.Locked;
            }
        }

        static List<Layer> Children(RhinoDoc doc, Guid parent)
        {
            var list = new List<Layer>();
            foreach (var layer in doc.Layers)
                if (layer != null && !layer.IsDeleted && layer.ParentLayerId == parent)
                    list.Add(layer);
            return list;
        }

        static bool MoveObjects(RhinoDoc doc, Layer from, int to)
        {
            bool moved = false;
            var settings = new ObjectEnumeratorSettings { NormalObjects = true, LockedObjects = true, HiddenObjects = true, DeletedObjects = false, ReferenceObjects = false, LayerIndexFilter = from.Index };
            foreach (var o in doc.Objects.GetObjectList(settings).ToList())
            {
                var a = o.Attributes.Duplicate();
                a.LayerIndex = to;
                moved |= doc.Objects.ModifyAttributes(o, a, true);
            }
            return moved;
        }

        /// <summary>Delete a layer that holds no objects and no sublayers (never the current layer).</summary>
        static bool DeleteIfEmpty(RhinoDoc doc, int index)
        {
            var layer = index >= 0 && index < doc.Layers.Count ? doc.Layers[index] : null;
            if (layer == null || layer.IsDeleted || index == doc.Layers.CurrentLayerIndex || Children(doc, layer.Id).Count > 0)
                return false;
            var objects = doc.Objects.FindByLayer(layer);
            if (objects != null && objects.Length > 0)
                return false;
            return doc.Layers.Delete(index, true);
        }
    }
}
