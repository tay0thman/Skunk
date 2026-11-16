using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Skunk.Commands;
using Skunk.Core;
using Skunk.Service;

namespace Skunk.UI
{
    /// <summary>Skunk settings: annotation scale (view scale), plan cut, behaviour, aliases, start-up.</summary>
    public sealed class SettingsDialog : Dialog<bool>
    {
        static readonly (string Label, double Scale)[] ImperialScales =
        {
            ("1/32\" = 1'-0\"", 384), ("1/16\" = 1'-0\"", 192), ("3/32\" = 1'-0\"", 128), ("1/8\" = 1'-0\"", 96), ("3/16\" = 1'-0\"", 64),
            ("1/4\" = 1'-0\"", 48), ("3/8\" = 1'-0\"", 32), ("1/2\" = 1'-0\"", 24), ("3/4\" = 1'-0\"", 16), ("1\" = 1'-0\"", 12),
            ("1 1/2\" = 1'-0\"", 8), ("3\" = 1'-0\"", 4), ("1\" = 20'", 240), ("1\" = 30'", 360), ("1\" = 40'", 480), ("1\" = 50'", 600), ("1\" = 100'", 1200),
        };
        static readonly double[] MetricScales = { 1, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000 };

        readonly RhinoDoc _doc;
        readonly CheckBox _rhinoScaling = new CheckBox { Text = "Use Rhino's model space annotation scaling (recommended: text prints at the type size in every detail)" };
        readonly ComboBox _scale = new ComboBox();
        readonly CheckBox _cplanes = new CheckBox { Text = "Create a named CPlane for every level" };
        readonly TextBox _cut = new TextBox(), _depth = new TextBox();
        readonly CheckBox _deleteSpots = new CheckBox { Text = "Delete spots when their host is deleted" };
        readonly CheckBox _markers = new CheckBox { Text = "Show the project base point and survey point" };
        readonly CheckBox _origin = new CheckBox { Text = "Show the internal origin" };
        readonly NumericStepper _markerSize = new NumericStepper { MinValue = 10, MaxValue = 60, DecimalPlaces = 0 };
        readonly CheckBox _verbose = new CheckBox { Text = "Verbose command-line messages (troubleshooting)" };
        readonly Label _aliasNote = EtoKit.Note("");
        readonly CheckBox _dimsLayer = new CheckBox { Text = "Put new Rhino dimensions and leaders on a Skunk sublayer" };
        readonly TextBox _dimsName = new TextBox();

        public static void Show(RhinoDoc doc)
        {
            var dlg = new SettingsDialog(doc);
            dlg.ShowModal(EtoKit.Owner);
        }

        SettingsDialog(RhinoDoc doc)
        {
            _doc = doc;
            Title = "Skunk - Settings";
            Resizable = true;
            ClientSize = new Size(560, 600);
            Padding = new Padding(10);
            EtoKit.Style(this);

            var aliases = new Button { Text = "Add Skunk's aliases (" + string.Join(", ", KeyboardAliases.Defaults.Select(a => a.Alias)) + ")" };
            aliases.Click += (s, e) => AddDefaultAliases();
            var editAliases = new Button { Text = "Edit aliases..." };
            editAliases.Click += (s, e) =>
            {
                AliasesDialog.Show();
                ShowAliases();
            };
            var copy = new Button { Text = "Copy \"Skunk\"" };
            copy.Click += (s, e) => new Clipboard { Text = "Skunk" };
            var moveDims = new Button { Text = "Move existing Rhino dimensions there now" };
            moveDims.Click += (s, e) => MoveExistingDimensions();

            var ok = new Button { Text = "OK" };
            ok.Click += (s, e) =>
            {
                if (Apply())
                    Close(true);
            };
            var cancel = new Button { Text = "Cancel" };
            cancel.Click += (s, e) => Close(false);
            DefaultButton = ok;
            AbortButton = cancel;

            string engine = typeof(SettingsDialog).Assembly.Location;
            Content = new Scrollable
            {
                Border = BorderType.None,
                Content = new StackLayout
                {
                    Spacing = 8,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        Group("Annotation scale (view scale)", _rhinoScaling, EtoKit.Row("Scale", _scale, 80),
                            EtoKit.Note("Skunk types are sized in paper units. With Rhino's scaling on, Rhino scales text and symbols per detail; " +
                                        "leader offsets and slope arrows Skunk draws use this scale.")),
                        Group("Levels and plan views", _cplanes, EtoKit.Row("Plan cut height", _cut, 120), EtoKit.Row("View depth below level", _depth, 120)),
                        Group("Behaviour", _deleteSpots, _verbose),
                        Group("Rhino dimensions", _dimsLayer, EtoKit.Row("Sublayer of Skunk", _dimsName, 140),
                            EtoKit.Note("Dimensions and leaders drawn with Rhino's own commands go to Skunk::<sublayer> as they are made, in the same undo step. " +
                                        "Skunk's spot and level annotations keep their layers; text stays where it is."),
                            EtoKit.Buttons(moveDims)),
                        Group("Site points", _markers, _origin, EtoKit.Row("Marker size (pixels)", _markerSize, 140)),
                        Group("Keyboard aliases", _aliasNote, EtoKit.Buttons(aliases, editAliases)),
                        Group("Start with Rhino",
                            EtoKit.Note("Skunk keeps elements in sync from the first Skunk command of a session. To start it with Rhino, add the command " +
                                        "Skunk to Tools > Options > General > Command Lists (Run these commands every time Rhino starts)."),
                            EtoKit.Buttons(copy)),
                        Group("About", new Label { Text = SkunkInfo.Title + " - project coordinates, levels and spot annotations for Rhino 9" },
                            EtoKit.Note("Engine: " + engine)),
                        EtoKit.Buttons(ok, cancel),
                    },
                },
            };
            LoadValues();
        }

        static Control Group(string title, params Control[] items)
        {
            var stack = new StackLayout { Spacing = 4, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            foreach (var i in items)
                stack.Items.Add(i);
            return new GroupBox { Text = title, Padding = new Padding(8), Content = stack };
        }

        bool Imperial => DocUnits.IsImperial(_doc);

        void LoadValues()
        {
            var s = DocStore.Settings(_doc);
            _rhinoScaling.Checked = _doc.ModelSpaceAnnotationScalingEnabled;
            _scale.Items.Clear();
            if (Imperial)
                foreach (var sc in ImperialScales)
                    _scale.Items.Add(sc.Label);
            else
                foreach (var sc in MetricScales)
                    _scale.Items.Add("1:" + sc);
            _scale.Text = ScaleText(DocUnits.AnnotationScale(_doc));
            _cplanes.Checked = s.LevelCPlanes;
            _cut.Text = PropertyModel.Len(_doc, DocUnits.FromMeters(_doc, s.CutHeightMeters));
            _depth.Text = PropertyModel.Len(_doc, DocUnits.FromMeters(_doc, s.ViewDepthMeters));
            _deleteSpots.Checked = s.DeleteSpotsWithHost;
            _markers.Checked = s.ShowSiteMarkers;
            _origin.Checked = s.ShowInternalOrigin;
            _markerSize.Value = s.MarkerPixelSize;
            _verbose.Checked = Log.Verbose;
            _dimsLayer.Checked = s.DimensionsOnSkunkLayer;
            _dimsName.Text = DimensionLayer.LayerName(s.DimensionsLayer);
            ShowAliases();
        }

        void MoveExistingDimensions()
        {
            string name = DimensionLayer.LayerName(_dimsName.Text);
            int moved = 0;
            Kit.Edit(_doc, "Skunk Dimension Layer", ctx => moved = DimensionLayer.MoveAll(_doc, name));
            _doc.Views.Redraw();
            MessageBox.Show(this, moved == 0 ? "No Rhino dimensions needed moving." :
                moved + " Rhino dimension(s) and leader(s) moved to " + Skunk.Core.Keys.RootLayer + "::" + name + ".", "Skunk");
        }

        string ScaleText(double scale)
        {
            if (Imperial)
            {
                var hit = ImperialScales.FirstOrDefault(s => Math.Abs(s.Scale - scale) < 1e-6);
                if (hit.Label != null)
                    return hit.Label;
            }
            return "1:" + scale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        bool TryScale(string text, out double scale)
        {
            scale = 0;
            text = (text ?? "").Trim();
            var hit = ImperialScales.FirstOrDefault(s => s.Label == text);
            if (hit.Label != null)
            {
                scale = hit.Scale;
                return true;
            }
            if (text.StartsWith("1:", StringComparison.Ordinal))
                text = text.Substring(2);
            return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale) && scale > 0;
        }

        bool Apply()
        {
            if (!TryScale(_scale.Text, out double scale))
            {
                MessageBox.Show(this, "Choose a scale or type one as 1:96.", "Skunk", MessageBoxType.Warning);
                return false;
            }
            if (!PropertyModel.ParseLen(_doc, _cut.Text, out double cut) || !PropertyModel.ParseLen(_doc, _depth.Text, out double depth))
            {
                MessageBox.Show(this, "Could not read the plan cut height or view depth.", "Skunk", MessageBoxType.Warning);
                return false;
            }
            var s = DocStore.Settings(_doc);
            s.LevelCPlanes = _cplanes.Checked == true;
            s.CutHeightMeters = DocUnits.ToMeters(_doc, cut);
            s.ViewDepthMeters = DocUnits.ToMeters(_doc, depth);
            s.DeleteSpotsWithHost = _deleteSpots.Checked == true;
            s.ShowSiteMarkers = _markers.Checked == true;
            s.ShowInternalOrigin = _origin.Checked == true;
            s.MarkerPixelSize = (int)_markerSize.Value;
            s.AnnotationScale = scale;
            s.DimensionsOnSkunkLayer = _dimsLayer.Checked == true;
            s.DimensionsLayer = DimensionLayer.LayerName(_dimsName.Text);
            Log.Verbose = _verbose.Checked == true;
            Kit.Edit(_doc, "Skunk Settings", ctx =>
            {
                DocStore.SaveSettings(_doc, s);
                bool rhino = _rhinoScaling.Checked == true;
                if (_doc.ModelSpaceAnnotationScalingEnabled != rhino)
                    _doc.ModelSpaceAnnotationScalingEnabled = rhino;
                if (rhino && Math.Abs(_doc.ModelSpaceTextScale - scale) > 1e-9)
                    _doc.ModelSpaceTextScale = scale;
                Types.Styles.EnsureDatumLinetype(_doc);
            }, contextChanged: true);
            return true;
        }

        /// <summary>Skunk's suggested aliases; an alias that already runs something else is left alone (Edit aliases can take it over).</summary>
        void AddDefaultAliases()
        {
            var outcome = KeyboardAliases.Apply(new RhinoAliasStore(), KeyboardAliases.DefaultTable(), replaceOthers: false);
            ShowAliases();
            MessageBox.Show(this, outcome.Summary(), "Skunk", MessageBoxType.Information);
        }

        void ShowAliases() => _aliasNote.Text = "In Rhino now: " + KeyboardAliases.Describe(new RhinoAliasStore()) +
                                                 ". Aliases are Rhino settings, so they work in every model.";
    }
}
