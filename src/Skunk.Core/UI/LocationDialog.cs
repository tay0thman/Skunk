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
    /// Location and coordinates: the project location - the Rhino model's geographic location, read from the model
    /// (set in Rhino) - and Rhino's north against true north; shared coordinates of the project base point, angle to
    /// true north, survey point values and clipping, and which named CPlanes hold the two systems.
    /// </summary>
    public sealed class LocationDialog : Dialog<bool>
    {
        readonly RhinoDoc _doc;
        readonly Label _place = new Label(), _address = new Label(), _latitude = new Label(), _longitude = new Label();
        readonly Label _elevation = new Label(), _anchor = new Label(), _rhinoNorth = new Label { Wrap = WrapMode.Word };
        readonly Button _setInRhino = new Button { Text = "Set in Rhino..." };
        readonly Button _useRhinoNorth = new Button { Text = "True North from Rhino" };
        readonly Button _setRhinoNorth = new Button { Text = "Rhino's North from True North" };
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
            ClientSize = new Size(580, 560);
            Padding = new Padding(10);
            EtoKit.Style(this);

            var names = NamedPlanes.Names(doc);
            foreach (var n in names)
            {
                _pbpPlane.Items.Add(n);
                _spPlane.Items.Add(n);
            }

            var geoPage = new TabPage
            {
                Text = "Location",
                Padding = new Padding(10),
                Content = new StackLayout
                {
                    Spacing = 6,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        EtoKit.Note("The project location is the Rhino model's geographic location: its Earth anchor point, which Rhino's Sun uses too. Skunk reads it from the model; set it in Rhino."),
                        EtoKit.Row("Place", _place), EtoKit.Row("Address", _address),
                        EtoKit.Row("Latitude", _latitude), EtoKit.Row("Longitude", _longitude), EtoKit.Row("Elevation", _elevation),
                        EtoKit.Row("Anchor point (internal)", _anchor),
                        EtoKit.Row("Rhino's north", _rhinoNorth),
                        EtoKit.Buttons(_setInRhino),
                        EtoKit.Buttons(_useRhinoNorth, _setRhinoNorth),
                        EtoKit.Note("Set in Rhino keeps your changes and opens Rhino's document location (Document Properties > Location, with its world map); this dialog comes back when you close it. Rhino's Sun panel sets the same location."),
                        EtoKit.Note("Rhino's north follows true north while the two agree. When they differ, match them either way: true north turns about the point chosen under Project Base Point > Rotate about."),
                    },
                },
            };
            _setInRhino.ToolTip = "Opens Rhino's Document Properties > Location (the world map), then this dialog again";
            _setInRhino.Click += (s, e) =>
            {
                if (!Apply())
                    return;
                Close(true);
                RunAfter(_doc, RhinoLocationPage, "_SkLocation");
            };
            _useRhinoNorth.ToolTip = "Turn true north onto Rhino's north (shared coordinates turn with it)";
            _useRhinoNorth.Click += (s, e) =>
            {
                if (!Apply())
                    return;
                var pivot = _pivot.SelectedIndex == 1 ? RotationPivot.SurveyPoint : RotationPivot.ProjectBasePoint;
                Kit.Edit(_doc, "Skunk Location", ctx =>
                {
                    var site = SiteModel.Get(_doc);
                    GeoLocation.UseRhinoNorth(_doc, site, pivot);
                    site.Save(_doc);
                }, contextChanged: true);
                LoadValues();
            };
            _setRhinoNorth.ToolTip = "Point Rhino's north (its Sun and Earth anchor point) along true north";
            _setRhinoNorth.Click += (s, e) =>
            {
                if (!Apply())
                    return;
                Kit.Edit(_doc, "Skunk: Rhino's North", ctx => GeoLocation.SetRhinoNorth(_doc, SiteModel.Get(_doc).TrueNorth));
                LoadValues();
            };

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
                    new TableRow(new TabControl { Pages = { geoPage, pbpPage, spPage, reportPage } }) { ScaleHeight = true },
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

            var geo = GeoLocation.Read(_doc);
            _place.Text = geo.IsSet ? (geo.Name.Length > 0 ? geo.Name : "(no name)") : "Not set in this Rhino model";
            _address.Text = geo.Description;
            _latitude.Text = geo.IsSet ? GeoLocation.FormatLatitude(geo.Latitude) : "";
            _longitude.Text = geo.IsSet ? GeoLocation.FormatLongitude(geo.Longitude) : "";
            _elevation.Text = geo.IsSet ? geo.Elevation(_doc) : "";
            _anchor.Text = geo.IsSet ? L(geo.ModelPoint.X) + ",  " + L(geo.ModelPoint.Y) + ",  " + L(geo.ModelPoint.Z) : "";
            _rhinoNorth.Text = geo.NorthText(site);
            _useRhinoNorth.Enabled = _setRhinoNorth.Enabled = !GeoLocation.SameNorth(geo.North, site.TrueNorth);

            var o = site.Project.Origin;
            var s = site.Survey.Origin;
            var shared0 = site.SharedOrigin;
            string pbpAt = GeoLocation.LatLongText(_doc, o), spAt = GeoLocation.LatLongText(_doc, s);
            _report.Text = string.Join(System.Environment.NewLine, new[]
            {
                "Project location: " + geo.Summary(_doc, site),
                pbpAt.Length > 0 ? "   project base point at " + pbpAt + ";  survey point at " + spAt : "   (set it in Rhino: Document Properties > Location, or the Location tab's Set in Rhino)",
                "",
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

        /// <summary>
        /// Rhino's document location: Document Properties > Location, with its world map - the page Rhino's own "Manage
        /// Document Location" opens (the Query Model Location component in Grasshopper). It sets the Earth anchor point.
        /// </summary>
        public const string RhinoLocationPage = "_-DocumentPropertiesPage _Location";

        /// <summary>Run Rhino commands, one after the other, once this dialog and the command that opened it have ended.</summary>
        static void RunAfter(RhinoDoc doc, params string[] scripts)
        {
            EventHandler run = null;
            run = (s, e) =>
            {
                if (Rhino.Commands.Command.InCommand())
                    return;
                RhinoApp.Idle -= run;
                foreach (var script in scripts)
                    RhinoApp.RunScript(doc.RuntimeSerialNumber, script, false);
            };
            RhinoApp.Idle += run;
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
