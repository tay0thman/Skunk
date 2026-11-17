using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using Rhino.PlugIns;
using Rhino.UI;
using Skunk.Commands;
using Skunk.Core;
using Skunk.Elements;
using Skunk.Service;
using Skunk.Types;

namespace Skunk.UI
{
    /// <summary>
    /// Skunk Attributes: a Rhino panel - it docks, tabs and floats like Rhino's own Properties and Layers - with the
    /// Type Selector at the top, Edit Type, then the instance parameters of the selection, grouped. With nothing
    /// selected it shows the project location. It follows the Rhino selection live and applies edits immediately
    /// (each edit is one undo step). Registered with Skunk's plug-in once a session (<see cref="Register"/>); the
    /// SkProperties command opens it.
    /// </summary>
    [Guid("766a1bbd-ba43-5642-9313-4be87be9acaf")]
    public sealed class SkunkAttributesPanel : Panel, IPanel
    {
        /// <summary>The panel's tab caption.</summary>
        public const string Caption = "Skunk Attributes";

        /// <summary>Rhino's id for the panel (fixed: Rhino keeps where it is docked under it).</summary>
        public static Guid PanelId => typeof(SkunkAttributesPanel).GUID;

        /// <summary>The tab icons embedded in Skunk.Core (tools/gen_project.py renders them from the SkProperties icon).</summary>
        public const string IconLight = "Skunk.Core.Resources.SkunkAttributes.light.ico";
        public const string IconDark = "Skunk.Core.Resources.SkunkAttributes.dark.ico";

        static bool s_registered;
        static bool s_hooked;
        static SkunkAttributesPanel s_current;

        readonly Label _title = new Label { Font = SystemFonts.Bold(12) };
        readonly DropDown _types = new DropDown();
        readonly Button _editType = new Button { Text = "Edit Type" };
        readonly ParamGrid _grid = new ParamGrid();
        readonly Label _status = new Label { TextColor = SystemColors.DisabledText };
        bool _loading;
        bool _shown = true;
        bool _dirty = true;
        bool _forceReload;
        ElementKind _kind;
        List<RhinoObject> _selection = new List<RhinoObject>();
        RhinoDoc _doc;

        /// <summary>
        /// Register the panel with Rhino, once a session: the compiled plug-in from its OnLoad (passing itself), the
        /// Script Editor channel with the first Skunk command (its plug-in found by id). False when Skunk's plug-in is
        /// not loaded.
        /// </summary>
        public static bool Register(PlugIn plugIn = null)
        {
            if (s_registered)
                return true;
            try
            {
                plugIn = plugIn ?? PlugIn.Find(SkunkToolbar.PlugInId);
                if (plugIn == null)
                    return false;
                string icon = Rhino.Runtime.HostUtils.RunningInDarkMode ? IconDark : IconLight;
                Panels.RegisterPanel(plugIn, typeof(SkunkAttributesPanel), Caption, typeof(SkunkAttributesPanel).Assembly, icon, PanelType.System);
                s_registered = true;
            }
            catch (Exception ex)
            {
                Log.Warn("the " + Caption + " panel was not registered (" + ex.GetType().Name + ": " + ex.Message + ").");
            }
            return s_registered;
        }

        /// <summary>SkProperties: open the panel (or bring its tab to the front); close it when it is the tab in front.</summary>
        public static void Toggle(RhinoDoc doc)
        {
            if (!Register())
            {
                Log.Warn("Skunk's plug-in is not loaded, so the " + Caption + " panel cannot open.");
                return;
            }
            if (Panels.IsPanelVisible(PanelId, true))
            {
                Panels.ClosePanel(PanelId);
                return;
            }
            Panels.OpenPanel(PanelId);
            s_current?.MarkDirty();
        }

        /// <summary>Rhino makes it (one on Windows, reused for every model).</summary>
        public SkunkAttributesPanel()
        {
            s_current = this;
            Padding = new Padding(6);

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
            _grid.Edited += () => _forceReload = true;
            Hook();
        }

        // ---- Rhino's panel callbacks -----------------------------------------------------------------

        public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
        {
            _shown = true;
            _dirty = true;
        }

        public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason) => _shown = false;

        public void PanelClosing(uint documentSerialNumber, bool onCloseDocument) => _shown = false;

        // ---- following the selection -------------------------------------------------------------

        /// <summary>
        /// Rhino's own events (not only the Skunk service's), so the panel stays current even when another copy of the
        /// engine handles the document events. Hooked once a session and routed to the panel Rhino made last, so a panel
        /// Rhino replaces leaves no handlers behind.
        /// </summary>
        static void Hook()
        {
            if (s_hooked)
                return;
            s_hooked = true;
            SkunkService.UiRefresh += doc => s_current?.MarkDirty();
            RhinoDoc.SelectObjects += (s, e) => s_current?.MarkDirty();
            RhinoDoc.DeselectObjects += (s, e) => s_current?.MarkDirty();
            RhinoDoc.DeselectAllObjects += (s, e) => s_current?.MarkDirty();
            RhinoDoc.ReplaceRhinoObject += (s, e) => s_current?.MarkDirty();
            RhinoDoc.ModifyObjectAttributes += (s, e) => s_current?.MarkDirty();
            RhinoDoc.ActiveDocumentChanged += (s, e) => s_current?.MarkDirty();
            Rhino.Commands.Command.EndCommand += (s, e) => s_current?.MarkDirty();
            RhinoApp.Idle += (s, e) => s_current?.OnIdle();
        }

        void MarkDirty() => _dirty = true;

        /// <summary>The user is typing in it (the grid or the type list has the focus): no rebuild under the cursor.</summary>
        bool Typing
        {
            get
            {
                try
                {
                    return HasFocus || Children.Any(c => c.HasFocus);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        void OnIdle()
        {
            if (!_shown || _loading)
                return;
            // don't rebuild under the user's cursor while they type, except right after a committed edit
            if (_forceReload || (_dirty && !Typing))
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
                // a level head or level line stands for its level, a grid bubble for its grid
                var resolved = new List<RhinoObject>();
                foreach (var o in selected)
                {
                    if (ElementKinds.IsLevelPart(ElementIO.KindOf(o)))
                    {
                        var hd = ElementIO.Read<HeadData>(o);
                        var level = hd == null ? null : ElementIO.Find(_doc, hd.Level);
                        if (level != null && resolved.All(r => r.Id != level.Id))
                            resolved.Add(level);
                    }
                    else if (ElementKinds.IsGridPart(ElementIO.KindOf(o)))
                    {
                        var gh = ElementIO.Read<GridHeadData>(o);
                        var grid = gh == null ? null : ElementIO.Find(_doc, gh.Grid);
                        if (grid != null && resolved.All(r => r.Id != grid.Id))
                            resolved.Add(grid);
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
                Log.Error(Caption + " panel", ex);
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
                case ElementKind.DatumBox:
                    return PropertyModel.DatumBox(_doc, _selection[0]);
                case ElementKind.Grid:
                    return PropertyModel.Grid(_doc, _selection[0]);
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
                case ElementKind.Grid: return ElementIO.Read<GridData>(o)?.Type;
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
                         kind == ElementKind.Level || kind == ElementKind.NorthArrow || kind == ElementKind.Grid;
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
                        case ElementKind.Grid:
                            {
                                var d = ElementIO.Read<GridData>(o);
                                d.Type = name;
                                ElementIO.Write(_doc, o, kind, d);
                                GridElement.Update(ctx, _doc.Objects.FindId(id), UpdateReason.Context);
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
    }
}
