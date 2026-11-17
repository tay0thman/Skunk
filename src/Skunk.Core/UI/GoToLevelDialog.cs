using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Skunk.Core;
using Skunk.Elements;

namespace Skunk.UI
{
    /// <summary>
    /// SkGoToLevel's dialog: the model's levels with their elevations (as their heads show them), the level the active
    /// view's CPlane is at selected, and the Plan View and View Range options. Go - or a double-click on a level - goes
    /// there. -SkGoToLevel keeps the command-line options for macros.
    /// </summary>
    public sealed class GoToLevelDialog : Dialog<bool>
    {
        sealed class LevelRow
        {
            public string Name = "";
            public string Elevation = "";
        }

        readonly GridView _levels = new GridView { ShowHeader = true, AllowMultipleSelection = false };
        readonly CheckBox _plan = new CheckBox { Text = "Plan view: look straight down at the level, zoomed to its extents" };
        readonly CheckBox _range = new CheckBox { Text = "View range: cut the view at the level's plan cut" };
        int _chosen = -1;

        /// <summary>Show it. False when cancelled; else <paramref name="chosen"/> is the index of the level to go to.</summary>
        public static bool Show(RhinoDoc doc, IList<LevelInfo> levels, int current, string viewName, ref bool planView, ref bool viewRange, out int chosen)
        {
            var dlg = new GoToLevelDialog(doc, levels, current, viewName, planView, viewRange);
            bool ok = dlg.ShowModal(EtoKit.Owner);
            chosen = dlg._chosen;
            if (!ok || chosen < 0 || chosen >= levels.Count)
                return false;
            planView = dlg._plan.Checked == true;
            viewRange = dlg._range.Checked == true;
            return true;
        }

        GoToLevelDialog(RhinoDoc doc, IList<LevelInfo> levels, int current, string viewName, bool planView, bool viewRange)
        {
            Title = "Skunk - Go To Level";
            Resizable = true;
            ClientSize = new Size(440, 420);
            Padding = new Padding(10);
            EtoKit.Style(this);

            var ctx = new UpdateContext(doc);
            var rows = levels.Select(l => new LevelRow { Name = l.Name ?? "", Elevation = LevelElement.ElevationText(ctx, l.Type, l.Z) }).ToList();
            _levels.Columns.Add(new GridColumn
            {
                HeaderText = "Level",
                DataCell = new TextBoxCell { Binding = Binding.Delegate<LevelRow, string>(r => r.Name) },
                Expand = true,
                Editable = false,
            });
            _levels.Columns.Add(new GridColumn
            {
                HeaderText = "Elevation",
                DataCell = new TextBoxCell { Binding = Binding.Delegate<LevelRow, string>(r => r.Elevation) },
                Width = 130,
                Editable = false,
            });
            _levels.DataStore = rows;
            if (current >= 0 && current < rows.Count)
                _levels.SelectedRow = current;
            _levels.CellDoubleClick += (s, e) => Go();
            _plan.Checked = planView;
            _range.Checked = viewRange;

            var settings = DocStore.Settings(doc);
            string cut = PropertyModel.Len(doc, DocUnits.FromMeters(doc, settings.CutHeightMeters));
            string depth = PropertyModel.Len(doc, DocUnits.FromMeters(doc, settings.ViewDepthMeters));

            var go = new Button { Text = "Go" };
            go.Click += (s, e) => Go();
            var cancel = new Button { Text = "Cancel" };
            cancel.Click += (s, e) => Close(false);
            DefaultButton = go;
            AbortButton = cancel;

            Content = new TableLayout
            {
                Spacing = new Size(6, 8),
                Rows =
                {
                    new TableRow(EtoKit.Note("The CPlane of the active view (" + viewName + ") moves to the level you choose. Double-click a level to go straight there.")),
                    new TableRow(_levels) { ScaleHeight = true },
                    new TableRow(_plan),
                    new TableRow(_range),
                    new TableRow(EtoKit.Note("The plan cut is " + cut + " above the level and the view sees " + depth + " below it (SkSettings > Levels and plan views).")),
                    new TableRow(EtoKit.Buttons(go, cancel)),
                },
            };
        }

        void Go()
        {
            _chosen = _levels.SelectedRow;
            if (_chosen >= 0)
                Close(true);
        }
    }
}
