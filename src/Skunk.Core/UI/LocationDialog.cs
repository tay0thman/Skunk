using System;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Skunk.Commands;
using Skunk.Core;
using Skunk.Site;
using Skunk.Units;

namespace Skunk.UI
{
    /// <summary>
    /// Location and coordinates (project location + project base point / survey point
    /// properties): shared coordinates of the project base point, angle to true north, survey point
    /// values and clipping, and which named CPlanes hold the two systems.
    /// </summary>
    public sealed class LocationDialog : Dialog<bool>
    {
        readonly RhinoDoc _doc;
        readonly TextBox _pbpN = new TextBox(), _pbpE = new TextBox(), _pbpZ = new TextBox();
        readonly TextBox _angle = new TextBox();
        readonly DropDown _angleSide = new DropDown { Items = { "East", "West" } };
        readonly DropDown _pivot = new DropDown { Items = { "Project Base Point", "Survey Point" } };
        readonly TextBox _spN = new TextBox(), _spE = new TextBox(), _spZ = new TextBox();
        readonly CheckBox _clipped = new CheckBox { Text = "Clipped (moving the survey point moves the shared coordinate system)" };
        readonly ComboBox _pbpPlane = new ComboBox();
        readonly ComboBox _spPlane = new ComboBox();
        readonly TextArea _report = new TextArea { ReadOnly = true, Wrap = false, Height = 150 };

        public static void Show(RhinoDoc doc)
        {
            var dlg = new LocationDialog(doc);
            dlg.ShowModal(EtoKit.Owner);
        }

        LocationDialog(RhinoDoc doc)
        {
            _doc = doc;
            Title = "Skunk - Location and Coordinates";
            Resizable = true;
            ClientSize = new Size(560, 520);
            Padding = new Padding(10);
            EtoKit.Style(this);

            var names = NamedPlanes.Names(doc);
            foreach (var n in names)
            {
                _pbpPlane.Items.Add(n);
                _spPlane.Items.Add(n);
            }

            var pbpPage = new TabPage
            {
                Text = "Project Base Point",
                Padding = new Padding(10),
                Content = new StackLayout
                {
                    Spacing = 6,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        EtoKit.Note("Shared coordinates of the project base point. Changing them moves the project base point, not the model."),
                        EtoKit.Row("N/S", _pbpN), EtoKit.Row("E/W", _pbpE), EtoKit.Row("Elev", _pbpZ),
                        EtoKit.Row("Angle to True North", new TableLayout { Spacing = new Size(6, 0), Rows = { new TableRow(new TableCell(_angle, true), new TableCell(_angleSide)) } }),
                        EtoKit.Row("Rotate about", _pivot),
                        EtoKit.Row("Project CPlane", _pbpPlane),
                        EtoKit.Note("Project north is the Y axis of the project CPlane (the world Y axis unless you rotate that CPlane)."),
                    },
                },
            };
            var spPage = new TabPage
            {
                Text = "Survey Point",
                Padding = new Padding(10),
                Content = new StackLayout
                {
                    Spacing = 6,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        EtoKit.Note("Shared coordinates of the survey point (a known point on site)."),
                        EtoKit.Row("N/S", _spN), EtoKit.Row("E/W", _spE), EtoKit.Row("Elev", _spZ),
                        _clipped,
                        EtoKit.Row("True North CPlane", _spPlane),
                        EtoKit.Note("True north is the Y axis of this named CPlane and the survey point is its origin. Edit the CPlane in Rhino's Named CPlanes panel and Skunk follows."),
                    },
                },
            };
            var reportPage = new TabPage { Text = "Report", Padding = new Padding(10), Content = _report };

            var ok = new Button { Text = "OK" };
            ok.Click += (s, e) =>
            {
                if (Apply())
                    Close(true);
            };
            var cancel = new Button { Text = "Cancel" };
            cancel.Click += (s, e) => Close(false);
            var apply = new Button { Text = "Apply" };
            apply.Click += (s, e) =>
            {
                if (Apply())
                    LoadValues();
            };
            DefaultButton = ok;
            AbortButton = cancel;

            Content = new TableLayout
            {
                Spacing = new Size(6, 8),
                Rows =
                {
                    new TableRow(new TabControl { Pages = { pbpPage, spPage, reportPage } }) { ScaleHeight = true },
                    new TableRow(EtoKit.Note("Lengths accept feet-inches (10'-6 1/2\"), inches, mm / m, or plain numbers in model units.")),
                    new TableRow(EtoKit.Buttons(ok, cancel, apply)),
                },
            };
            LoadValues();
        }

        string L(double v) => PropertyModel.Len(_doc, v);

        void LoadValues()
        {
            var site = SiteModel.Get(_doc);
            var pbp = site.ProjectBasePointShared;
            _pbpN.Text = L(pbp.Y);
            _pbpE.Text = L(pbp.X);
            _pbpZ.Text = L(pbp.Z);
            double a = site.AngleToTrueNorthDegrees;
            _angle.Text = UnitFormat.Decimal(Math.Abs(a), 4, false, true, false);
            _angleSide.SelectedIndex = a < 0 ? 1 : 0;
            _pivot.SelectedIndex = 0;
            _spN.Text = L(site.Data.SpNorth);
            _spE.Text = L(site.Data.SpEast);
            _spZ.Text = L(site.Data.SpElevation);
            _clipped.Checked = site.Data.SpClipped;
            _pbpPlane.Text = site.Data.PbpCPlane;
            _spPlane.Text = site.Data.SpCPlane;

            var o = site.Project.Origin;
            var s = site.Survey.Origin;
            var shared0 = site.SharedOrigin;
            _report.Text = string.Join(System.Environment.NewLine, new[]
            {
                "Angle from project north to true north: " + UnitFormat.AngleEastWest(a) + "   (" + UnitFormat.Decimal(((-a % 360) + 360) % 360, 2, false, false, false) + "° counter-clockwise)",
                "",
                "Project base point",
                "   internal  X " + L(o.X) + "   Y " + L(o.Y) + "   Z " + L(o.Z),
                "   shared    N " + L(site.ProjectBasePointShared.Y) + "   E " + L(site.ProjectBasePointShared.X) + "   Elev " + L(site.ProjectBasePointShared.Z),
                "Survey point (" + (site.Data.SpClipped ? "clipped" : "unclipped") + ")",
                "   internal  X " + L(s.X) + "   Y " + L(s.Y) + "   Z " + L(s.Z),
                "   shared    N " + L(site.Data.SpNorth) + "   E " + L(site.Data.SpEast) + "   Elev " + L(site.Data.SpElevation),
                "Shared origin (0, 0, 0) in internal coordinates",
                "   X " + L(shared0.X) + "   Y " + L(shared0.Y) + "   Z " + L(shared0.Z),
                "Internal origin in shared coordinates",
                "   N " + L(site.Shared(Rhino.Geometry.Point3d.Origin).Y) + "   E " + L(site.Shared(Rhino.Geometry.Point3d.Origin).X) + "   Elev " + L(site.Shared(Rhino.Geometry.Point3d.Origin).Z),
            });
        }

        bool Apply()
        {
            double Parse(TextBox box, string what)
            {
                if (!PropertyModel.ParseLen(_doc, box.Text, out double v))
                    throw new FormatException(what);
                return v;
            }
            try
            {
                double pn = Parse(_pbpN, "Project base point N/S"), pe = Parse(_pbpE, "Project base point E/W"), pz = Parse(_pbpZ, "Project base point Elev");
                double sn = Parse(_spN, "Survey point N/S"), se = Parse(_spE, "Survey point E/W"), sz = Parse(_spZ, "Survey point Elev");
                if (!double.TryParse(_angle.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double angle))
                    throw new FormatException("Angle to True North");
                if (_angleSide.SelectedIndex == 1)
                    angle = -angle;
                var pivot = _pivot.SelectedIndex == 1 ? RotationPivot.SurveyPoint : RotationPivot.ProjectBasePoint;
                string pbpPlane = string.IsNullOrWhiteSpace(_pbpPlane.Text) ? Skunk.Core.Keys.DefaultPbpCPlane : _pbpPlane.Text.Trim();
                string spPlane = string.IsNullOrWhiteSpace(_spPlane.Text) ? Skunk.Core.Keys.DefaultSpCPlane : _spPlane.Text.Trim();

                Kit.Edit(_doc, "Skunk Location", ctx =>
                {
                    var site = SiteModel.Get(_doc);
                    // switching to another named CPlane: adopt it when it exists
                    if (spPlane != site.Data.SpCPlane)
                    {
                        site.Data.SpCPlane = spPlane;
                        if (NamedPlanes.TryGet(_doc, spPlane, out var p))
                            site.Survey = SiteModel.Horizontal(p);
                    }
                    if (pbpPlane != site.Data.PbpCPlane)
                    {
                        site.Data.PbpCPlane = pbpPlane;
                        if (NamedPlanes.TryGet(_doc, pbpPlane, out var p))
                            site.Project = SiteModel.Horizontal(p);
                    }
                    site.Data.SpNorth = sn;
                    site.Data.SpEast = se;
                    site.Data.SpElevation = sz;
                    site.Data.SpClipped = _clipped.Checked == true;
                    if (Math.Abs(site.AngleToTrueNorthDegrees - angle) > 1e-9)
                        site.SetAngleToTrueNorth(angle, pivot);
                    site.SetProjectBasePointShared(pe, pn, pz);
                    site.Save(_doc);
                }, contextChanged: true);
                return true;
            }
            catch (FormatException ex)
            {
                MessageBox.Show(this, "Could not read " + ex.Message + ".", "Skunk", MessageBoxType.Warning);
                return false;
            }
        }
    }
}
