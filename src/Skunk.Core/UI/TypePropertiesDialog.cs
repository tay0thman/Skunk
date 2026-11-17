using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Skunk.Commands;
using Skunk.Core;
using Skunk.Elements;
using Skunk.Types;

namespace Skunk.UI
{
    /// <summary>
    /// The Type Properties dialog: Family (category) and Type selectors, Duplicate / Rename / Delete,
    /// the type parameter grid and a sample of the text the type produces. OK / Apply update every
    /// element of the edited types (one undo step).
    /// </summary>
    public sealed class TypePropertiesDialog : Dialog<bool>
    {
        static readonly ElementKind[] Families = { ElementKind.SpotElevation, ElementKind.SpotCoordinate, ElementKind.SpotSlope, ElementKind.Level, ElementKind.Grid, ElementKind.NorthArrow };
        static readonly string[] FamilyNames = { "Spot Elevations", "Spot Coordinates", "Spot Slopes", "Levels", "Grids", "North Arrows" };

        readonly RhinoDoc _doc;
        readonly TypeLibrary _lib;
        readonly Dictionary<string, string> _renames = new Dictionary<string, string>();
        readonly DropDown _family = new DropDown();
        readonly DropDown _type = new DropDown();
        readonly ParamGrid _grid = new ParamGrid { LabelWidth = 190 };
        readonly Label _sample = new Label { Font = SystemFonts.Bold() };
        ElementKind _kind;
        bool _dirty;
        bool _loading;

        public static void Show(RhinoDoc doc, ElementKind kind, string typeName)
        {
            var dlg = new TypePropertiesDialog(doc, kind, typeName);
            dlg.ShowModal(EtoKit.Owner);
        }

        TypePropertiesDialog(RhinoDoc doc, ElementKind kind, string typeName)
        {
            _doc = doc;
            _lib = Json.Clone(TypeTable.Get(doc));
            _kind = Array.IndexOf(Families, kind) >= 0 ? kind : ElementKind.SpotElevation;
            Title = "Skunk - Type Properties";
            Resizable = true;
            MinimumSize = new Size(460, 520);
            ClientSize = new Size(780, 680);
            Padding = new Padding(10);
            EtoKit.Style(this);

            foreach (var f in FamilyNames)
                _family.Items.Add(f);
            _family.SelectedIndex = Array.IndexOf(Families, _kind);
            _family.SelectedIndexChanged += (s, e) =>
            {
                if (_loading || _family.SelectedIndex < 0)
                    return;
                _kind = Families[_family.SelectedIndex];
                LoadTypes(null);
            };
            _type.SelectedIndexChanged += (s, e) =>
            {
                if (!_loading)
                    LoadGrid();
            };

            var duplicate = new Button { Text = "Duplicate..." };
            duplicate.Click += (s, e) => Duplicate();
            var rename = new Button { Text = "Rename..." };
            rename.Click += (s, e) => Rename();
            var delete = new Button { Text = "Delete" };
            delete.Click += (s, e) => Delete();

            var ok = new Button { Text = "OK" };
            ok.Click += (s, e) =>
            {
                Apply();
                Close(true);
            };
            var cancel = new Button { Text = "Cancel" };
            cancel.Click += (s, e) => Close(false);
            var apply = new Button { Text = "Apply" };
            apply.Click += (s, e) => Apply();
            DefaultButton = ok;
            AbortButton = cancel;

            Content = new TableLayout
            {
                Spacing = new Size(6, 8),
                Rows =
                {
                    new TableRow(EtoKit.Row("Family:", _family, 60)),
                    new TableRow(new TableLayout
                    {
                        Spacing = new Size(6, 0),
                        Rows = { new TableRow(new TableCell(EtoKit.Row("Type:", _type, 60), true), new TableCell(duplicate), new TableCell(rename), new TableCell(delete)) },
                    }),
                    new TableRow(new Label { Text = "Type Parameters", Font = SystemFonts.Bold() }),
                    new TableRow(new TableCell(_grid, true)) { ScaleHeight = true },
                    new TableRow(EtoKit.Row("Sample:", _sample, 60)),
                    new TableRow(EtoKit.Note("Sizes are on the printed sheet; Skunk scales them with the annotation scale (the view scale). " +
                                             "Every element of the type updates when you press OK or Apply.")),
                    new TableRow(EtoKit.Buttons(ok, cancel, apply)),
                },
            };
            LoadTypes(typeName);
        }

        IList<SkunkType> Types() => _lib.Of(_kind).ToList();

        SkunkType Current
        {
            get
            {
                var list = Types();
                int i = _type.SelectedIndex;
                return i >= 0 && i < list.Count ? list[i] : list.FirstOrDefault();
            }
        }

        void LoadTypes(string select)
        {
            _loading = true;
            _type.Items.Clear();
            var list = Types();
            foreach (var t in list)
                _type.Items.Add(t.Name);
            int idx = list.ToList().FindIndex(t => string.Equals(t.Name, select, StringComparison.OrdinalIgnoreCase));
            _type.SelectedIndex = idx < 0 ? 0 : idx;
            _loading = false;
            LoadGrid();
        }

        void LoadGrid()
        {
            var t = Current;
            if (t == null)
                return;
            _grid.ShowParameters(PropertyModel.Type(_doc, t, () =>
            {
                _dirty = true;
                _sample.Text = PropertyModel.Sample(_doc, t);
            }));
            _sample.Text = PropertyModel.Sample(_doc, t);
        }

        void Duplicate()
        {
            var t = Current;
            if (t == null)
                return;
            var name = Ask("Duplicate", "Name:", _lib.UniqueName(t.Kind, t.Name + " 2"));
            if (string.IsNullOrWhiteSpace(name))
                return;
            var copy = _lib.Duplicate(t, name.Trim());
            _dirty = true;
            LoadTypes(copy?.Name);
        }

        void Rename()
        {
            var t = Current;
            if (t == null)
                return;
            var name = Ask("Rename", "New name:", t.Name);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == t.Name)
                return;
            name = _lib.UniqueName(t.Kind, name.Trim());
            string original = _renames.FirstOrDefault(kv => kv.Value == t.Name).Key ?? t.Name;
            _renames[original] = name;
            t.Name = name;
            _dirty = true;
            LoadTypes(name);
        }

        void Delete()
        {
            var t = Current;
            if (t == null)
                return;
            if (Types().Count < 2)
            {
                MessageBox.Show(this, "A family needs at least one type.", "Skunk", MessageBoxType.Warning);
                return;
            }
            if (InUse(t))
            {
                MessageBox.Show(this, "\"" + t.Name + "\" is used by elements in this model. Change their type first.", "Skunk", MessageBoxType.Warning);
                return;
            }
            _lib.Remove(t);
            _dirty = true;
            LoadTypes(null);
        }

        bool InUse(SkunkType t)
        {
            foreach (var o in ElementIO.All(_doc, t.Kind))
            {
                string name = t.Kind == ElementKind.Level ? ElementIO.Read<LevelData>(o)?.Type :
                              t.Kind == ElementKind.Grid ? ElementIO.Read<GridData>(o)?.Type :
                              t.Kind == ElementKind.NorthArrow ? ElementIO.Read<NorthArrowData>(o)?.Type : ElementIO.Read<SpotData>(o)?.Type;
                if (string.Equals(name, t.Name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        string Ask(string title, string prompt, string value)
        {
            string result = value;
            return Rhino.UI.Dialogs.ShowEditBox("Skunk - " + title, prompt, value, false, out result) ? result : null;
        }

        void Apply()
        {
            if (!_dirty)
                return;
            var lib = _lib;
            var renames = new Dictionary<string, string>(_renames);
            Kit.Edit(_doc, "Skunk Type Properties", ctx =>
            {
                TypeTable.Save(_doc, lib);
                // renamed types: point their elements at the new name
                if (renames.Count > 0)
                {
                    foreach (var o in ElementIO.All(_doc))
                    {
                        var kind = ElementIO.KindOf(o);
                        switch (kind)
                        {
                            case ElementKind.Level:
                                {
                                    var d = ElementIO.Read<LevelData>(o);
                                    if (d != null && renames.TryGetValue(d.Type ?? "", out var n)) { d.Type = n; ElementIO.Write(_doc, o, kind, d); }
                                    break;
                                }
                            case ElementKind.NorthArrow:
                                {
                                    var d = ElementIO.Read<NorthArrowData>(o);
                                    if (d != null && renames.TryGetValue(d.Type ?? "", out var n)) { d.Type = n; ElementIO.Write(_doc, o, kind, d); }
                                    break;
                                }
                            case ElementKind.Grid:
                                {
                                    var d = ElementIO.Read<GridData>(o);
                                    if (d != null && renames.TryGetValue(d.Type ?? "", out var n)) { d.Type = n; ElementIO.Write(_doc, o, kind, d); }
                                    break;
                                }
                            case ElementKind.SpotElevation:
                            case ElementKind.SpotCoordinate:
                            case ElementKind.SpotSlope:
                                {
                                    var d = ElementIO.Read<SpotData>(o);
                                    if (d != null && renames.TryGetValue(d.Type ?? "", out var n)) { d.Type = n; ElementIO.Write(_doc, o, kind, d); }
                                    break;
                                }
                        }
                    }
                }
                // refresh the annotation styles of every type
                foreach (var kind in Families)
                    foreach (var t in lib.Of(kind))
                        Skunk.Types.Styles.DimStyle(_doc, t);
            }, contextChanged: true);
            _renames.Clear();
            _dirty = false;
        }
    }
}
