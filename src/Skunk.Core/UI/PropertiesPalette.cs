using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using Skunk.Commands;
using Skunk.Core;
using Skunk.Elements;
using Skunk.Service;
using Skunk.Types;

namespace Skunk.UI
{
    /// <summary>
    /// The Properties palette: Type Selector at the top, Edit Type, then the instance parameters
    /// of the selection, grouped. With nothing selected it shows the project location. It follows the
    /// Rhino selection live and applies edits immediately (each edit is one undo step).
    /// </summary>
    public sealed class PropertiesPalette : Form
    {
        static PropertiesPalette s_instance;

        readonly Label _title = new Label { Font = SystemFonts.Bold(12) };
        readonly DropDown _types = new DropDown();
        readonly Button _editType = new Button { Text = "Edit Type" };
        readonly ParamGrid _grid = new ParamGrid();
        readonly Label _status = new Label { TextColor = SystemColors.DisabledText };
        bool _loading;
        ElementKind _kind;
        List<RhinoObject> _selection = new List<RhinoObject>();
        RhinoDoc _doc;

        public static void Toggle(RhinoDoc doc)
        {
            if (s_instance != null && s_instance.Visible)
            {
                s_instance.Close();
                return;
            }
            Open(doc);
        }

        public static void Open(RhinoDoc doc)
        {
            if (s_instance == null)
            {
                s_instance = new PropertiesPalette();
                s_instance.Closed += (s, e) => s_instance = null;
            }
            s_instance.Show();
            s_instance.BringToFront();
            s_instance.Reload(doc);
        }

        PropertiesPalette()
        {
            Title = "Skunk Properties";
            Resizable = true;
            Minimizable = false;
            Maximizable = false;
            ShowInTaskbar = false;
            MinimumSize = new Size(300, 300);
            ClientSize = new Size(360, 560);
            Owner = EtoKit.Owner;
            EtoKit.Style(this);
            Padding = new Padding(8);

            _types.SelectedIndexChanged += (s, e) => OnTypeChanged();
            _editType.Click += (s, e) =>
            {
                if (_doc == null)
                    return;
                var kind = _kind == ElementKind.None || !_types.Enabled ? ElementKind.SpotElevation : _kind;
                string name = null;
                if (_types.Enabled && _types.SelectedIndex >= 0)
                {
                    var names = TypeTable.Get(_doc).Of(kind).Select(t => t.Name).ToList();
                    if (_types.SelectedIndex < names.Count)
                        name = names[_types.SelectedIndex];
                }
                TypePropertiesDialog.Show(_doc, kind, name);
                Reload(_doc);
            };
            var location = new Button { Text = "Location..." };
            location.Click += (s, e) =>
            {
                if (_doc != null)
                    LocationDialog.Show(_doc);
                Reload(_doc);
            };

            Content = new TableLayout
            {
                Spacing = new Size(4, 6),
                Rows =
                {
                    new TableRow(_title),
                    new TableRow(new TableLayout { Spacing = new Size(4, 0), Rows = { new TableRow(new TableCell(_types, true), new TableCell(_editType)) } }),
                    new TableRow(new TableCell(_grid, true)) { ScaleHeight = true },
                    new TableRow(new TableLayout { Spacing = new Size(4, 0), Rows = { new TableRow(new TableCell(_status, true), new TableCell(location)) } }),
                },
            };

            RestorePlacement();
            // Rhino's own events (not only the Skunk service's), so the palette stays current even when
            // another copy of the engine handles the document events
            _grid.Edited += () => _forceReload = true;
            SkunkService.UiRefresh += OnUiRefresh;
            RhinoDoc.SelectObjects += OnSelection;
            RhinoDoc.DeselectObjects += OnSelection;
            RhinoDoc.DeselectAllObjects += OnDeselectAll;
            RhinoDoc.ReplaceRhinoObject += OnReplace;
            RhinoDoc.ModifyObjectAttributes += OnAttributes;
            RhinoDoc.ActiveDocumentChanged += OnActiveDoc;
            Rhino.Commands.Command.EndCommand += OnEndCommand;
            RhinoApp.Idle += OnIdle;
            Closed += (s, e) =>
            {
                SkunkService.UiRefresh -= OnUiRefresh;
                RhinoDoc.SelectObjects -= OnSelection;
                RhinoDoc.DeselectObjects -= OnSelection;
                RhinoDoc.DeselectAllObjects -= OnDeselectAll;
                RhinoDoc.ReplaceRhinoObject -= OnReplace;
                RhinoDoc.ModifyObjectAttributes -= OnAttributes;
                RhinoDoc.ActiveDocumentChanged -= OnActiveDoc;
                Rhino.Commands.Command.EndCommand -= OnEndCommand;
                RhinoApp.Idle -= OnIdle;
                SavePlacement();
            };
        }

        bool _dirty;
        bool _forceReload;

        void OnSelection(object sender, RhinoObjectSelectionEventArgs e) => _dirty = true;

        void OnDeselectAll(object sender, RhinoDeselectAllObjectsEventArgs e) => _dirty = true;

        void OnReplace(object sender, RhinoReplaceObjectEventArgs e) => _dirty = true;

        void OnAttributes(object sender, RhinoModifyObjectAttributesEventArgs e) => _dirty = true;

        void OnActiveDoc(object sender, DocumentEventArgs e) => _dirty = true;

        void OnEndCommand(object sender, Rhino.Commands.CommandEventArgs e) => _dirty = true;

        void OnUiRefresh(RhinoDoc doc) => _dirty = true;

        void OnIdle(object sender, EventArgs e)
        {
            if (!Visible || _loading)
                return;
            // don't rebuild under the user's cursor while they type, except right after a committed edit
            if (_forceReload || (_dirty && !HasFocus))
            {
                _dirty = false;
                _forceReload = false;
                Reload(RhinoDoc.ActiveDoc ?? _doc);
            }
        }

        void Reload(RhinoDoc doc)
        {
            _doc = doc ?? RhinoDoc.ActiveDoc;
            if (_doc == null)
                return;
            _loading = true;
            try
            {
                var selected = _doc.Objects.GetSelectedObjects(false, false).ToList();
                // a level head stands for its level (clicking a head selects the level)
                var resolved = new List<RhinoObject>();
                foreach (var o in selected)
                {
                    if (ElementIO.KindOf(o) == ElementKind.LevelHead)
                    {
                        var hd = ElementIO.Read<HeadData>(o);
                        var level = hd == null ? null : ElementIO.Find(_doc, hd.Level);
                        if (level != null && resolved.All(r => r.Id != level.Id))
                            resolved.Add(level);
                    }
                    else if (resolved.All(r => r.Id != o.Id))
                        resolved.Add(o);
                }
                _selection = resolved;
                var kinds = _selection.Select(ElementIO.KindOf).Distinct().ToList();
                _kind = kinds.Count == 1 ? kinds[0] : ElementKind.None;
                List<Param> parameters;
                if (_selection.Count == 0)
                {
                    _title.Text = "Project Location";
                    parameters = PropertyModel.Location(_doc, true, true);
                    SetTypes(ElementKind.None, null);
                }
                else if (kinds.Count > 1)
                {
                    _title.Text = "Multiple Categories (" + _selection.Count + ")";
                    parameters = new List<Param> { Param.ReadOnly("Selection", "Objects", _selection.Count.ToString()) };
                    SetTypes(ElementKind.None, null);
                }
                else
                {
                    _title.Text = TitleFor(_kind, _selection.Count);
                    parameters = Parameters(_kind);
                    SetTypes(_kind, TypeName(_selection[0], _kind));
                }
                _grid.ShowParameters(parameters);
                _status.Text = SkunkInfo.Title + (SkunkService.Instance?.Running == true ? " - live updates on" : "");
            }
            catch (Exception ex)
            {
                Log.Error("properties palette", ex);
            }
            finally
            {
                _loading = false;
            }
        }

        static string TitleFor(ElementKind kind, int count)
        {
            string name = kind == ElementKind.None ? "Rhino Objects" : ElementKinds.DisplayName(kind) + (count > 1 ? "s" : "");
            return count > 1 ? name + " (" + count + ")" : name;
        }

        List<Param> Parameters(ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.SpotElevation:
                case ElementKind.SpotCoordinate:
                case ElementKind.SpotSlope:
                    return PropertyModel.Spots(_doc, kind, _selection);
                case ElementKind.Level:
                    return PropertyModel.Level(_doc, _selection[0]);
                case ElementKind.ProjectBasePoint:
                    return PropertyModel.Location(_doc, true, false);
                case ElementKind.SurveyPoint:
                    return PropertyModel.Location(_doc, false, true);
                case ElementKind.NorthArrow:
                    return PropertyModel.NorthArrow(_doc, _selection);
                case ElementKind.PlanCut:
                    return PropertyModel.PlanCut(_doc, _selection[0]);
                case ElementKind.None:
                    return PropertyModel.Attached(_doc, _selection);
                default:
                    return new List<Param> { Param.ReadOnly("Other", "Element", ElementKinds.DisplayName(kind)) };
            }
        }

        static string TypeName(RhinoObject o, ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Level: return ElementIO.Read<LevelData>(o)?.Type;
                case ElementKind.NorthArrow: return ElementIO.Read<NorthArrowData>(o)?.Type;
                case ElementKind.SpotElevation:
                case ElementKind.SpotCoordinate:
                case ElementKind.SpotSlope: return ElementIO.Read<SpotData>(o)?.Type;
                default: return null;
            }
        }

        void SetTypes(ElementKind kind, string current)
        {
            _types.Items.Clear();
            bool typed = kind == ElementKind.SpotElevation || kind == ElementKind.SpotCoordinate || kind == ElementKind.SpotSlope ||
                         kind == ElementKind.Level || kind == ElementKind.NorthArrow;
            _types.Enabled = typed;
            _editType.Enabled = true;
            if (!typed)
                return;
            var lib = TypeTable.Get(_doc);
            var names = lib.Of(kind).Select(t => t.Name).ToList();
            foreach (var n in names)
                _types.Items.Add(Skunk.Types.Styles.CategoryName(kind) + ": " + n);
            int idx = names.FindIndex(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
            _types.SelectedIndex = idx < 0 ? 0 : idx;
        }

        void OnTypeChanged()
        {
            if (_loading || _doc == null || _types.SelectedIndex < 0 || _selection.Count == 0)
                return;
            var lib = TypeTable.Get(_doc);
            var names = lib.Of(_kind).Select(t => t.Name).ToList();
            if (_types.SelectedIndex >= names.Count)
                return;
            string name = names[_types.SelectedIndex];
            var ids = _selection.Select(o => o.Id).ToList();
            var kind = _kind;
            Kit.Edit(_doc, "Skunk Change Type", ctx =>
            {
                foreach (var id in ids)
                {
                    var o = ElementIO.Find(_doc, id);
                    if (o == null)
                        continue;
                    switch (kind)
                    {
                        case ElementKind.Level:
                            {
                                var d = ElementIO.Read<LevelData>(o);
                                d.Type = name;
                                ElementIO.Write(_doc, o, kind, d);
                                LevelElement.Update(ctx, _doc.Objects.FindId(id), UpdateReason.Context);
                                break;
                            }
                        case ElementKind.NorthArrow:
                            {
                                var d = ElementIO.Read<NorthArrowData>(o);
                                d.Type = name;
                                ElementIO.Write(_doc, o, kind, d);
                                NorthArrowElement.Update(ctx, _doc.Objects.FindId(id));
                                break;
                            }
                        default:
                            {
                                var d = ElementIO.Read<SpotData>(o);
                                d.Type = name;
                                ElementIO.Write(_doc, o, kind, d);
                                SpotElement.Update(ctx, _doc.Objects.FindId(id), UpdateReason.Context);
                                break;
                            }
                    }
                }
            });
            Kit.SetLastType(kind, name);
            Reload(_doc);
        }

        void RestorePlacement()
        {
            try
            {
                var s = Rhino.PersistentSettings.RhinoAppSettings;
                if (s.TryGetInteger("Skunk.Palette.X", out int x) && s.TryGetInteger("Skunk.Palette.Y", out int y) &&
                    s.TryGetInteger("Skunk.Palette.W", out int w) && s.TryGetInteger("Skunk.Palette.H", out int h) && w > 100 && h > 100)
                {
                    Location = new Point(x, y);
                    ClientSize = new Size(w, h);
                }
            }
            catch
            {
                // first use
            }
        }

        void SavePlacement()
        {
            try
            {
                var s = Rhino.PersistentSettings.RhinoAppSettings;
                s.SetInteger("Skunk.Palette.X", Location.X);
                s.SetInteger("Skunk.Palette.Y", Location.Y);
                s.SetInteger("Skunk.Palette.W", ClientSize.Width);
                s.SetInteger("Skunk.Palette.H", ClientSize.Height);
            }
            catch
            {
                // not critical
            }
        }
    }
}
