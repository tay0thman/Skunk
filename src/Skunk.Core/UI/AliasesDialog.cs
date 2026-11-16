using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Skunk.Core;

namespace Skunk.UI
{
    /// <summary>
    /// Keyboard aliases for every Skunk command, grouped like the toolbar. Each row starts with the alias
    /// Rhino has now and can be overridden (or cleared); Skunk's suggestions are one click away. Apply writes
    /// them to Rhino's alias list, which is application-wide.
    /// </summary>
    public sealed class AliasesDialog : Dialog<bool>
    {
        readonly IAliasStore _store;
        readonly ParamGrid _grid = new ParamGrid();
        readonly CheckBox _replace = new CheckBox { Text = "Replace an alias that already runs another command" };
        readonly Label _status = EtoKit.Note("");
        readonly Dictionary<string, string> _wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void Show()
        {
            var dlg = new AliasesDialog(new RhinoAliasStore());
            dlg.ShowModal(EtoKit.Owner);
        }

        AliasesDialog(IAliasStore store)
        {
            _store = store;
            Title = "Skunk - Keyboard Aliases";
            Resizable = true;
            ClientSize = new Size(500, 660);
            Padding = new Padding(10);
            EtoKit.Style(this);

            var defaults = new Button { Text = "Skunk defaults" };
            defaults.Click += (s, e) =>
            {
                foreach (var d in KeyboardAliases.Defaults)
                    _wanted[d.Command] = d.Alias;
                Fill();
            };
            var clear = new Button { Text = "Clear all" };
            clear.Click += (s, e) =>
            {
                foreach (var c in CommandList.All)
                    _wanted[c.Name] = "";
                Fill();
            };
            var apply = new Button { Text = "Apply" };
            apply.Click += (s, e) => Apply();
            var close = new Button { Text = "Close" };
            close.Click += (s, e) => Close(true);
            AbortButton = close;

            string suggestions = string.Join(", ", KeyboardAliases.Defaults.Select(d => d.Alias + " " + LabelOf(d.Command)));
            Content = new TableLayout
            {
                Spacing = new Size(6, 6),
                Rows =
                {
                    new TableRow(EtoKit.Note("Type an alias next to a command (one word), or clear it for none, then Apply. Skunk suggests " + suggestions +
                                             ". Aliases are Rhino settings: they work in every model.")),
                    new TableRow(new TableCell(_grid, true)) { ScaleHeight = true },
                    new TableRow(_replace),
                    new TableRow(_status),
                    new TableRow(EtoKit.Buttons(defaults, clear, apply, close)),
                },
            };
            Reload();
        }

        static string LabelOf(string command) => CommandList.All.FirstOrDefault(c => c.Name == command).Label ?? command;

        /// <summary>Start every row from the alias Rhino has now.</summary>
        void Reload()
        {
            var current = KeyboardAliases.Current(_store, CommandList.All.Select(c => c.Name));
            _wanted.Clear();
            foreach (var c in CommandList.All)
                _wanted[c.Name] = current.TryGetValue(c.Name, out var list) && list.Count > 0 ? list[0] : "";
            Fill();
        }

        void Fill()
        {
            var current = KeyboardAliases.Current(_store, CommandList.All.Select(c => c.Name));
            var rows = new List<Param>();
            foreach (var c in CommandList.All)
            {
                string name = c.Name;
                string now = current.TryGetValue(name, out var list) && list.Count > 0 ? string.Join(" / ", list) : "none";
                string suggested = KeyboardAliases.DefaultFor(name);
                rows.Add(Param.Edit(c.Section, c.Label, _wanted.TryGetValue(name, out var w) ? w : "", t =>
                {
                    _wanted[name] = (t ?? "").Trim();
                    return true;
                }, name + " - in Rhino now: " + now + (suggested.Length > 0 ? "; Skunk suggests " + suggested : "")));
            }
            _grid.ShowParameters(rows);
        }

        void Apply()
        {
            var outcome = KeyboardAliases.Apply(_store, _wanted, _replace.Checked == true);
            Reload();
            _status.Text = outcome.Summary();
        }
    }
}
