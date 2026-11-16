using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;

namespace Skunk.UI
{
    /// <summary>Kinds of parameter editors in the parameter grid.</summary>
    public enum ParamEditor
    {
        ReadOnly,
        Text,
        Check,
        Choice,
        Color,
    }

    /// <summary>One row of a Properties / Type Properties grid ("Parameter | Value").</summary>
    public sealed class Param
    {
        public string Group = "Other";
        public string Name = "";
        public string Tooltip = "";
        public ParamEditor Editor = ParamEditor.ReadOnly;
        public string Text = "";
        public bool Checked;
        public string[] Choices = Array.Empty<string>();
        public int Index;
        public int Argb;
        public bool Enabled = true;
        /// <summary>Return false to reject the text (the box turns back to the previous value).</summary>
        public Func<string, bool> OnText;
        public Action<bool> OnCheck;
        public Action<int> OnChoice;
        public Action<int> OnColor;

        public static Param ReadOnly(string group, string name, string text, string tip = "") =>
            new Param { Group = group, Name = name, Text = text ?? "", Tooltip = tip };

        public static Param Edit(string group, string name, string text, Func<string, bool> apply, string tip = "") =>
            new Param { Group = group, Name = name, Editor = ParamEditor.Text, Text = text ?? "", OnText = apply, Tooltip = tip };

        public static Param Check(string group, string name, bool value, Action<bool> apply, string tip = "") =>
            new Param { Group = group, Name = name, Editor = ParamEditor.Check, Checked = value, OnCheck = apply, Tooltip = tip };

        public static Param Choice(string group, string name, string[] choices, int index, Action<int> apply, string tip = "") =>
            new Param { Group = group, Name = name, Editor = ParamEditor.Choice, Choices = choices, Index = index, OnChoice = apply, Tooltip = tip };

        public static Param Color(string group, string name, int argb, Action<int> apply, string tip = "") =>
            new Param { Group = group, Name = name, Editor = ParamEditor.Color, Argb = argb, OnColor = apply, Tooltip = tip };
    }

    /// <summary>
    /// A two-column parameter grid with group headers, for the Properties palette and the Type
    /// Properties dialog. Edits are committed on Enter / leaving the box / toggling.
    /// </summary>
    public sealed class ParamGrid : Scrollable
    {
        public int LabelWidth { get; set; } = 170;

        /// <summary>Raised after any committed edit.</summary>
        public event Action Edited;

        public ParamGrid()
        {
            Border = BorderType.Line;
            ExpandContentWidth = true;
            ExpandContentHeight = false;
        }

        public void ShowParameters(IEnumerable<Param> parameters)
        {
            var layout = new DynamicLayout { DefaultSpacing = new Size(6, 3), Padding = new Padding(0, 0, 0, 6) };
            string group = null;
            foreach (var p in parameters)
            {
                if (p.Group != group)
                {
                    group = p.Group;
                    layout.Add(Header(group));
                }
                var label = new Label { Text = p.Name, Width = LabelWidth, VerticalAlignment = VerticalAlignment.Center, ToolTip = p.Tooltip };
                layout.BeginHorizontal();
                layout.Add(new Panel { Padding = new Padding(8, 0, 0, 0), Content = label });
                layout.Add(EditorFor(p), true);
                layout.Add(new Panel { Width = 4 });
                layout.EndHorizontal();
            }
            layout.Add(null, true);
            Content = layout;
        }

        static Control Header(string text)
        {
            return new Panel
            {
                Padding = new Padding(6, 3),
                BackgroundColor = Eto.Drawing.Color.FromArgb(128, 128, 128, 48),
                Content = new Label { Text = text, Font = SystemFonts.Bold() },
            };
        }

        Control EditorFor(Param p)
        {
            switch (p.Editor)
            {
                case ParamEditor.Text:
                    {
                        var box = new TextBox { Text = p.Text, ToolTip = p.Tooltip, Enabled = p.Enabled };
                        string committed = p.Text;
                        void Commit()
                        {
                            if (box.Text == committed)
                                return;
                            bool ok = p.OnText == null || p.OnText(box.Text);
                            if (ok)
                            {
                                committed = box.Text;
                                Edited?.Invoke();
                            }
                            else
                                box.Text = committed;
                        }
                        box.LostFocus += (s, e) => Commit();
                        box.KeyDown += (s, e) =>
                        {
                            if (e.Key == Keys.Enter)
                            {
                                Commit();
                                e.Handled = true;
                            }
                            else if (e.Key == Keys.Escape)
                                box.Text = committed;
                        };
                        return box;
                    }
                case ParamEditor.Check:
                    {
                        var cb = new CheckBox { Checked = p.Checked, ToolTip = p.Tooltip, Enabled = p.Enabled };
                        cb.CheckedChanged += (s, e) =>
                        {
                            p.OnCheck?.Invoke(cb.Checked == true);
                            Edited?.Invoke();
                        };
                        return cb;
                    }
                case ParamEditor.Choice:
                    {
                        var dd = new DropDown { ToolTip = p.Tooltip, Enabled = p.Enabled };
                        foreach (var c in p.Choices)
                            dd.Items.Add(c);
                        dd.SelectedIndex = Math.Max(-1, Math.Min(p.Index, p.Choices.Length - 1));
                        dd.SelectedIndexChanged += (s, e) =>
                        {
                            if (dd.SelectedIndex < 0)
                                return;
                            p.OnChoice?.Invoke(dd.SelectedIndex);
                            Edited?.Invoke();
                        };
                        return dd;
                    }
                case ParamEditor.Color:
                    {
                        var button = new Button { Text = p.Argb == 0 ? "By Layer" : "", ToolTip = "Click to choose a colour (Cancel = by layer)", Enabled = p.Enabled };
                        if (p.Argb != 0)
                            button.BackgroundColor = Eto.Drawing.Color.FromArgb(p.Argb);
                        button.Click += (s, e) =>
                        {
                            var c = p.Argb == 0 ? System.Drawing.Color.Black : System.Drawing.Color.FromArgb(p.Argb);
                            int argb = Rhino.UI.Dialogs.ShowColorDialog(ref c) ? c.ToArgb() : 0;
                            p.Argb = argb;
                            button.Text = argb == 0 ? "By Layer" : "";
                            button.BackgroundColor = argb == 0 ? SystemColors.Control : Eto.Drawing.Color.FromArgb(argb);
                            p.OnColor?.Invoke(argb);
                            Edited?.Invoke();
                        };
                        return button;
                    }
                default:
                    return new TextBox { Text = p.Text, ReadOnly = true, ToolTip = p.Tooltip };
            }
        }
    }

    /// <summary>Owner window and small layout helpers.</summary>
    public static class EtoKit
    {
        public static Window Owner => Rhino.UI.RhinoEtoApp.MainWindow;

        public static void Style(Window w)
        {
            try
            {
                Rhino.UI.EtoExtensions.UseRhinoStyle(w);
            }
            catch
            {
                // styling is cosmetic
            }
        }

        public static Control Row(string label, Control control, int width = 150) =>
            new TableLayout
            {
                Spacing = new Size(6, 0),
                Rows = { new TableRow(new TableCell(new Label { Text = label, Width = width, VerticalAlignment = VerticalAlignment.Center }), new TableCell(control, true)) },
            };

        public static Label Note(string text) => new Label { Text = text, Wrap = WrapMode.Word, TextColor = SystemColors.DisabledText };

        public static Control Buttons(params Button[] buttons)
        {
            var row = new TableRow { ScaleHeight = false };
            row.Cells.Add(new TableCell(null, true));
            foreach (var b in buttons)
                row.Cells.Add(new TableCell(b));
            return new TableLayout { Spacing = new Size(6, 0), Rows = { row } };
        }

        public static string[] Names<T>() where T : struct, Enum => Enum.GetNames(typeof(T));

        public static int IndexOf<T>(T value) where T : struct, Enum => Array.IndexOf(Enum.GetValues(typeof(T)).Cast<T>().ToArray(), value);

        public static T ValueAt<T>(int index) where T : struct, Enum => Enum.GetValues(typeof(T)).Cast<T>().ElementAt(index);

        /// <summary>"TopAndBottom" -> "Top And Bottom" for display.</summary>
        public static string Words(string identifier)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < identifier.Length; i++)
            {
                char c = identifier[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(identifier[i - 1]))
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string[] Words(string[] identifiers) => identifiers.Select(Words).ToArray();
    }
}
