using System;
using System.Collections.Generic;
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
        /// <summary>Name of that sublayer of "Skunk".</summary>
        public string DimensionsLayer = Keys.DimensionsLayer;
        /// <summary>
        /// Model units (metres per unit) Skunk's stored lengths are in. A different value later means the
        /// units changed while Skunk was not watching; the annotations then tell whether Rhino scaled them.
        /// </summary>
        public double MetersPerUnit;

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
                    s.AnnotationScale = DocUnits.IsImperial(doc) ? 96 : 100;
                if (!DocUnits.IsImperial(doc))
                {
                    s.CutHeightMeters = 1.2;
                    s.ViewDepthMeters = 0.3;
                }
            }
            if (s.AnnotationScale <= 0)
                s.AnnotationScale = DocUnits.IsImperial(doc) ? 96 : 100;
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
        /// The annotation scale Skunk sizes geometry with: Rhino's model space text scale when
        /// model-space annotation scaling is on, otherwise Skunk's own setting (the view scale).
        /// </summary>
        public static double AnnotationScale(RhinoDoc doc)
        {
            if (doc == null)
                return 1;
            if (doc.ModelSpaceAnnotationScalingEnabled && doc.ModelSpaceTextScale > 0)
                return doc.ModelSpaceTextScale;
            return DocStore.Settings(doc).AnnotationScale;
        }

        /// <summary>True when Rhino scales annotation (dimstyle sizes are paper sizes).</summary>
        public static bool RhinoScalesAnnotation(RhinoDoc doc) =>
            doc != null && doc.ModelSpaceAnnotationScalingEnabled && doc.ModelSpaceTextScale > 0;

        /// <summary>A paper length (mm) as a model length for geometry Skunk draws itself (leader offsets, arrows).</summary>
        public static double PaperToModel(RhinoDoc doc, double paperMillimeters) =>
            FromMeters(doc, paperMillimeters / 1000.0) * AnnotationScale(doc);

        /// <summary>A paper length (mm) as a dimension-style length (text height, arrow size).</summary>
        public static double PaperToStyle(RhinoDoc doc, double paperMillimeters)
        {
            double modelUnits = FromMeters(doc, paperMillimeters / 1000.0);
            return RhinoScalesAnnotation(doc) ? modelUnits : modelUnits * AnnotationScale(doc);
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

    /// <summary>Layers, created on demand under the "Skunk" parent layer.</summary>
    public static class Layers
    {
        public static int Ensure(RhinoDoc doc, string childName, System.Drawing.Color color, double plotWeight = 0.0, string linetype = null)
        {
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
            if (!string.IsNullOrEmpty(linetype))
            {
                var lt = doc.Linetypes.FindName(linetype);
                if (lt != null)
                    layer.LinetypeIndex = lt.Index;
            }
            return doc.Layers.Add(layer);
        }
    }
}
