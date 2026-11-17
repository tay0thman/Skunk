using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Skunk.Elements;

namespace Skunk.UI
{
    /// <summary>
    /// SkGridBubbles: which sides of the view grid bubbles go on. Top, Bottom, Left and Right sit around a diagram of the
    /// grids they apply to, drawn as the view shows them; the diagram shows the bubbles as they will be (solid) and the
    /// grid ends that will have none (a faint ring) as the boxes change, and marks the ticked edges.
    /// </summary>
    public sealed class GridBubblesDialog : Dialog<GridBubblesDialog.Choice>
    {
        public enum Choice
        {
            Cancel,
            Apply,
            ShowAll,
            Pick,
        }

        readonly GridBubbleDiagram _diagram;
        readonly CheckBox _top = new CheckBox { Text = "Top" };
        readonly CheckBox _bottom = new CheckBox { Text = "Bottom" };
        readonly CheckBox _left = new CheckBox { Text = "Left" };
        readonly CheckBox _right = new CheckBox { Text = "Right" };
        readonly Drawable _canvas = new Drawable { Size = new Size(380, 260) };

        /// <summary>The ticked sides (looking level only top and bottom count).</summary>
        public GridBubbleSides Sides =>
            (_top.Checked == true ? GridBubbleSides.Top : 0) | (_bottom.Checked == true ? GridBubbleSides.Bottom : 0) |
            (!_diagram.LookingLevel && _left.Checked == true ? GridBubbleSides.Left : 0) |
            (!_diagram.LookingLevel && _right.Checked == true ? GridBubbleSides.Right : 0);

        /// <summary>
        /// Show it. <paramref name="scope"/> says which grids, <paramref name="note"/> what changes; a layout detail offers
        /// Show every bubble, a model view Select grids.
        /// </summary>
        public static Choice Show(GridBubbleDiagram diagram, GridBubbleSides sides, string scope, string note, bool details, bool canPick,
                                  out GridBubbleSides chosen)
        {
            var dlg = new GridBubblesDialog(diagram, sides, scope, note, details, canPick);
            var result = dlg.ShowModal(EtoKit.Owner);
            chosen = dlg.Sides;
            return result;
        }

        GridBubblesDialog(GridBubbleDiagram diagram, GridBubbleSides sides, string scope, string note, bool details, bool canPick)
        {
            _diagram = diagram;
            Title = "Skunk - Grid Bubbles";
            Resizable = true;
            Padding = new Padding(10);
            MinimumSize = new Size(460, 440);
            EtoKit.Style(this);

            _top.Checked = (sides & GridBubbleSides.Top) != 0;
            _bottom.Checked = (sides & GridBubbleSides.Bottom) != 0;
            _left.Checked = (sides & GridBubbleSides.Left) != 0;
            _right.Checked = (sides & GridBubbleSides.Right) != 0;
            if (diagram.LookingLevel)
            {
                _left.Enabled = _right.Enabled = false;
                _left.ToolTip = _right.ToolTip = "Looking level, grids have bubbles at the top and bottom only.";
            }
            foreach (var box in new[] { _top, _bottom, _left, _right })
                box.CheckedChanged += (s, e) =>
                {
                    _diagram.Apply(Sides);
                    _canvas.Invalidate();
                };
            _diagram.Apply(Sides);
            _canvas.Paint += (s, e) => DrawDiagram(e.Graphics, _canvas.Width, _canvas.Height, _diagram, Sides);

            var ok = new Button { Text = "OK" };
            ok.Click += (s, e) => Close(Choice.Apply);
            var cancel = new Button { Text = "Cancel" };
            cancel.Click += (s, e) => Close(Choice.Cancel);
            DefaultButton = ok;
            AbortButton = cancel;
            var buttons = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 6, VerticalContentAlignment = VerticalAlignment.Center };
            if (canPick)
            {
                var pick = new Button { Text = "Select grids...", ToolTip = "Pick the grids in the view, then come back here" };
                pick.Click += (s, e) => Close(Choice.Pick);
                buttons.Items.Add(pick);
            }
            if (details)
            {
                var all = new Button { Text = "Show every bubble", ToolTip = "This detail drops its own sides and shows every grid bubble again" };
                all.Click += (s, e) => Close(Choice.ShowAll);
                buttons.Items.Add(all);
            }
            buttons.Items.Add(new StackLayoutItem(null, true));
            buttons.Items.Add(ok);
            buttons.Items.Add(cancel);

            var around = new TableLayout
            {
                Spacing = new Size(6, 6),
                Rows =
                {
                    new TableRow(new TableCell(), new TableCell(TableLayout.AutoSized(_top, centered: true)), new TableCell()),
                    new TableRow(new TableCell(TableLayout.AutoSized(_left, centered: true)), new TableCell(_canvas, true),
                                 new TableCell(TableLayout.AutoSized(_right, centered: true))) { ScaleHeight = true },
                    new TableRow(new TableCell(), new TableCell(TableLayout.AutoSized(_bottom, centered: true)), new TableCell()),
                },
            };
            Content = new StackLayout
            {
                Spacing = 8,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new Label { Text = scope, Wrap = WrapMode.Word },
                    new StackLayoutItem(around, true),
                    EtoKit.Note(note),
                    buttons,
                },
            };
        }

        /// <summary>
        /// The diagram: the view's edges (the ticked ones marked), the grids fitted into it as dash-dot lines, a bubble with
        /// the name where one will be and a faint ring where an end will have none. Public for SkunkCheck's picture.
        /// </summary>
        public static void DrawDiagram(Graphics g, float width, float height, GridBubbleDiagram d, GridBubbleSides sides)
        {
            DrawDiagramCore(g, width, height, d, sides);
        }

        /// <summary>SkunkCheck: the diagram drawn into a PNG file (Eto's drawing, no window needed).</summary>
        public static void SavePicture(string path, GridBubbleDiagram d, GridBubbleSides sides, int width = 380, int height = 260)
        {
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgba))
            {
                using (var g = new Graphics(bitmap))
                    DrawDiagramCore(g, width, height, d, sides);
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        static void DrawDiagramCore(Graphics g, float width, float height, GridBubbleDiagram d, GridBubbleSides sides)
        {
            if (width < 4 || height < 4)
                return;
            g.AntiAlias = true;
            var back = SystemColors.ControlBackground;
            var ink = SystemColors.ControlText;
            var faint = SystemColors.DisabledText;
            g.FillRectangle(back, 0, 0, width, height);
            g.DrawRectangle(faint, 0, 0, width - 1, height - 1);

            // the ticked edges of the view
            const float bar = 4;
            var accent = SystemColors.Highlight;
            if ((sides & GridBubbleSides.Top) != 0)
                g.FillRectangle(accent, 0, 0, width, bar);
            if ((sides & GridBubbleSides.Bottom) != 0)
                g.FillRectangle(accent, 0, height - bar, width, bar);
            if ((sides & GridBubbleSides.Left) != 0)
                g.FillRectangle(accent, 0, 0, bar, height);
            if ((sides & GridBubbleSides.Right) != 0)
                g.FillRectangle(accent, width - bar, 0, bar, height);

            if (d == null || d.Grids.Count == 0)
                return;
            float r = BubbleRadius(d, width, height);
            float margin = r + 12;
            double dx = d.MaxX - d.MinX, dy = d.MaxY - d.MinY;
            double sx = dx > 1e-9 ? (width - 2 * margin) / dx : double.PositiveInfinity;
            double sy = dy > 1e-9 ? (height - 2 * margin) / dy : double.PositiveInfinity;
            double scale = Math.Min(sx, sy);
            if (double.IsInfinity(scale) || scale <= 0)
                scale = 1;
            float ox = (float)(width / 2 - (d.MinX + d.MaxX) / 2 * scale);
            float oy = (float)(height / 2 + (d.MinY + d.MaxY) / 2 * scale);
            PointF At(GridBubbleDiagram.End e) => new PointF(ox + (float)(e.X * scale), oy - (float)(e.Y * scale));

            using (var line = new Pen(ink, 1) { DashStyle = DashStyles.DashDot })
            using (var ghost = new Pen(faint, 1) { DashStyle = DashStyles.Dash })
            using (var ring = new Pen(ink, 1.5f))
            {
                foreach (var grid in d.Grids)
                    g.DrawLine(line, At(grid.A), At(grid.B));
                foreach (var grid in d.Grids)
                {
                    foreach (var end in new[] { grid.A, grid.B })
                    {
                        var c = At(end);
                        var circle = new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r);
                        if (!end.Bubble)
                        {
                            g.DrawEllipse(ghost, circle);
                            continue;
                        }
                        g.FillEllipse(back, circle);
                        g.DrawEllipse(ring, circle);
                        DrawName(g, grid.Name, c, r, ink);
                    }
                }
            }
        }

        /// <summary>
        /// Bubbles a little smaller when grid ends on the same side of the view crowd together (they line up there); 11
        /// pixels at most, 6 at least.
        /// </summary>
        static float BubbleRadius(GridBubbleDiagram d, float width, float height)
        {
            double dx = d.MaxX - d.MinX, dy = d.MaxY - d.MinY;
            double span = Math.Max(dx, dy);
            if (span <= 1e-9)
                return 11;
            double pixels = Math.Min(width, height) - 40;
            var ends = d.Grids.SelectMany(gr => new[] { gr.A, gr.B }).ToList();
            double closest = double.MaxValue;
            for (int i = 0; i < ends.Count; i++)
                for (int j = i + 1; j < ends.Count; j++)
                {
                    if (ends[i].Side != ends[j].Side)
                        continue;
                    double gap = Math.Sqrt(Math.Pow(ends[i].X - ends[j].X, 2) + Math.Pow(ends[i].Y - ends[j].Y, 2)) / span * pixels;
                    if (gap > 1)
                        closest = Math.Min(closest, gap);
                }
            return (float)Math.Max(6, Math.Min(11, closest / 2.4));
        }

        static void DrawName(Graphics g, string name, PointF center, float radius, Color ink)
        {
            if (string.IsNullOrEmpty(name))
                return;
            for (float size = Math.Max(6, radius * 0.8f); size >= 5; size -= 1)
            {
                using (var font = new Font(SystemFont.Default, size))
                {
                    var text = g.MeasureString(font, name);
                    if (text.Width <= radius * 1.7f || size <= 5)
                    {
                        g.DrawText(font, ink, center.X - text.Width / 2, center.Y - text.Height / 2, name);
                        return;
                    }
                }
            }
        }
    }
}
