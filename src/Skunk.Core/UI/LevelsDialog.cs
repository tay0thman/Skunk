using System;
using System.Collections.ObjectModel;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.Geometry;
using Skunk.Commands;
using Skunk.Core;
using Skunk.Elements;
using Skunk.Service;
using Skunk.Types;

namespace Skunk.UI
{
    /// <summary>
    /// Levels manager: every level with its name and elevation (editable in place), new levels a
    /// typical floor height above the top one, delete, and Go To (sets the active view's CPlane).
    /// </summary>
    public sealed class LevelsDialog : Dialog<bool>
    {
        sealed class Row
        {
            public Guid Id;
            public string Name { get; set; }
            public string Elevation { get; set; }
            public string Type { get; set; }
            public string Attached { get; set; }
            public string Story { get; set; }
            public double Z;
            public string OriginalName;
            public string OriginalElevation;
        }

        readonly RhinoDoc _doc;
        readonly GridView _grid = new GridView { AllowMultipleSelection = false, GridLines = GridLines.Horizontal };
        readonly ObservableCollection<Row> _rows = new ObservableCollection<Row>();
        readonly TextBox _height = new TextBox();

        public static void Show(RhinoDoc doc)
        {
            var dlg = new LevelsDialog(doc);
            dlg.ShowModal(EtoKit.Owner);
        }

        LevelsDialog(RhinoDoc doc)
        {
            _doc = doc;
            Title = "Skunk - Levels";
            Resizable = true;
            ClientSize = new Size(620, 440);
            Padding = new Padding(10);
            EtoKit.Style(this);

            _grid.Columns.Add(new GridColumn { HeaderText = "Name", Editable = true, Width = 180, DataCell = new TextBoxCell { Binding = Binding.Property<Row, string>(r => r.Name) } });
            _grid.Columns.Add(new GridColumn { HeaderText = "Elevation", Editable = true, Width = 120, DataCell = new TextBoxCell { Binding = Binding.Property<Row, string>(r => r.Elevation) } });
            _grid.Columns.Add(new GridColumn { HeaderText = "Type", Editable = false, Width = 160, DataCell = new TextBoxCell { Binding = Binding.Property<Row, string>(r => r.Type) } });
            _grid.Columns.Add(new GridColumn { HeaderText = "Story Height", Editable = false, Width = 90, DataCell = new TextBoxCell { Binding = Binding.Property<Row, string>(r => r.Story) } });
            _grid.Columns.Add(new GridColumn { HeaderText = "Attached", Editable = false, Width = 60, DataCell = new TextBoxCell { Binding = Binding.Property<Row, string>(r => r.Attached) } });
            _grid.DataStore = _rows;

            var add = new Button { Text = "New Level Above" };
            add.Click += (s, e) => AddAbove();
            var delete = new Button { Text = "Delete" };
            delete.Click += (s, e) => Delete();
            var goTo = new Button { Text = "Go To" };
            goTo.Click += (s, e) => GoTo();
            var ok = new Button { Text = "OK" };
            ok.Click += (s, e) =>
            {
                Apply();
                Close(true);
            };
            var cancel = new Button { Text = "Close" };
            cancel.Click += (s, e) => Close(false);
            var apply = new Button { Text = "Apply" };
            apply.Click += (s, e) =>
            {
                Apply();
                LoadValues();
            };
            DefaultButton = ok;
            AbortButton = cancel;

            Content = new TableLayout
            {
                Spacing = new Size(6, 8),
                Rows =
                {
                    new TableRow(new TableCell(_grid, true)) { ScaleHeight = true },
                    new TableRow(new TableLayout
                    {
                        Spacing = new Size(6, 0),
                        Rows = { new TableRow(new TableCell(EtoKit.Row("Typical floor height", _height, 130), true), new TableCell(add), new TableCell(delete), new TableCell(goTo)) },
                    }),
                    new TableRow(EtoKit.Note("Edit names and elevations in the table (elevations are relative to each level type's Elevation Base). " +
                                             "Objects attached to a level move with it. Deleting a level keeps the objects that were attached to it.")),
                    new TableRow(EtoKit.Buttons(ok, cancel, apply)),
                },
            };
            LoadValues();
        }

        void LoadValues()
        {
            _rows.Clear();
            var ctx = new UpdateContext(_doc);
            var levels = LevelElement.All(_doc);
            for (int i = levels.Count - 1; i >= 0; i--)
            {
                var l = levels[i];
                string elev = LevelElement.ElevationText(ctx, l.Type, l.Z);
                string story = i < levels.Count - 1 ? PropertyModel.Len(_doc, levels[i + 1].Z - l.Z) : "";
                _rows.Add(new Row
                {
                    Id = l.Id,
                    Name = l.Name,
                    Elevation = elev,
                    Type = l.Type?.Name ?? "",
                    Story = story,
                    Attached = LevelElement.Attached(_doc, l.Id).Length.ToString(),
                    Z = l.Z,
                    OriginalName = l.Name,
                    OriginalElevation = elev,
                });
            }
            double typical = levels.Count > 1 ? levels[levels.Count - 1].Z - levels[levels.Count - 2].Z : DocUnits.FromMeters(_doc, DocUnits.IsImperial(_doc) ? 3.6576 : 3.5);
            if (string.IsNullOrEmpty(_height.Text))
                _height.Text = PropertyModel.Len(_doc, typical);
        }

        void Apply()
        {
            var rows = _rows.ToList();
            Kit.Edit(_doc, "Skunk Levels", ctx =>
            {
                foreach (var r in rows)
                {
                    var info = LevelElement.Info(_doc, ElementIO.Find(_doc, r.Id), ctx.Types);
                    if (info == null)
                        continue;
                    if (r.Name != r.OriginalName && !string.IsNullOrWhiteSpace(r.Name))
                        LevelElement.Rename(ctx, r.Id, r.Name);
                    if (r.Elevation != r.OriginalElevation && PropertyModel.ParseLen(_doc, r.Elevation, out double v))
                        LevelElement.SetElevationZ(ctx, r.Id, LevelElement.WorldZ(ctx, info.Type, v));
                }
            });
        }

        void AddAbove()
        {
            Apply();
            if (!PropertyModel.ParseLen(_doc, _height.Text, out double h) || h <= 0)
            {
                MessageBox.Show(this, "Type a floor height, for example 12' or 3600 mm.", "Skunk", MessageBoxType.Warning);
                return;
            }
            var levels = LevelElement.All(_doc);
            double z = levels.Count > 0 ? levels[levels.Count - 1].Z + h : SiteModelZ();
            string type = levels.Count > 0 ? levels[levels.Count - 1].Type?.Name : null;
            Kit.Edit(_doc, "Skunk New Level", ctx => LevelElement.Create(_doc, type, null, z, LevelElement.DefaultFootprint(_doc, z), DatumBoxElement.DefaultBox(_doc)));
            LoadValues();
        }

        double SiteModelZ() => Skunk.Site.SiteModel.Get(_doc).Project.Origin.Z;

        void Delete()
        {
            if (!(_grid.SelectedItem is Row r))
                return;
            var obj = ElementIO.Find(_doc, r.Id);
            if (obj == null)
                return;
            Kit.Edit(_doc, "Skunk Delete Level", ctx =>
            {
                var data = ElementIO.Read<LevelData>(obj);
                LevelElement.Deleted(ctx, r.Id, data);
                _doc.Objects.Delete(obj, true, true);
                SkunkService.Register(_doc, r.Id);
            });
            LoadValues();
        }

        void GoTo()
        {
            if (!(_grid.SelectedItem is Row r))
                return;
            var view = _doc.Views.ActiveView;
            if (view == null)
                return;
            var plane = Skunk.Site.SiteModel.Get(_doc).Project;
            plane.Origin = new Point3d(plane.Origin.X, plane.Origin.Y, r.Z);
            view.ActiveViewport.SetConstructionPlane(plane);
            view.Redraw();
        }
    }
}
