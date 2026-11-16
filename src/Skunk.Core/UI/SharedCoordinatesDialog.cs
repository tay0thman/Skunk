using System;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Skunk.Commands;
using Skunk.Site;

namespace Skunk.UI
{
    /// <summary>
    /// Import Shared Coordinates: shows the Python snippet to run in the BIM model's console, reads the line
    /// it prints back, previews it and sets Skunk's project base point, survey point and true north to match.
    /// </summary>
    public sealed class SharedCoordinatesDialog : Dialog<bool>
    {
        static readonly string[] Bases =
        {
            "Internal origin (the model was exported in its internal coordinates)",
            "Project base point (the model was exported relative to its project base point)",
            "Shared coordinates (the model was exported in its shared coordinates)",
        };

        readonly RhinoDoc _doc;
        readonly TextArea _snippet = new TextArea { ReadOnly = true, Wrap = false, Font = Fonts.Monospace(9) };
        readonly TextArea _pasted = new TextArea { Wrap = true, Height = 64 };
        readonly DropDown _basis = new DropDown();
        readonly TextArea _preview = new TextArea { ReadOnly = true, Wrap = true, Height = 116 };
        readonly Button _import = new Button { Text = "Import", Enabled = false };
        ImportedSite _site;

        public static void Show(RhinoDoc doc)
        {
            var dlg = new SharedCoordinatesDialog(doc);
            dlg.ShowModal(EtoKit.Owner);
        }

        SharedCoordinatesDialog(RhinoDoc doc)
        {
            _doc = doc;
            Title = "Skunk - Import Shared Coordinates";
            Resizable = true;
            ClientSize = new Size(640, 680);
            Padding = new Padding(10);
            EtoKit.Style(this);

            _snippet.Text = ImportedSite.Snippet;
            foreach (var b in Bases)
                _basis.Items.Add(b);
            _basis.SelectedIndex = 0;

            var copy = new Button { Text = "Copy snippet" };
            copy.Click += (s, e) => new Clipboard { Text = ImportedSite.Snippet };
            var paste = new Button { Text = "Paste" };
            paste.Click += (s, e) =>
            {
                var text = new Clipboard().Text;
                if (!string.IsNullOrEmpty(text))
                    _pasted.Text = text;
            };
            _pasted.TextChanged += (s, e) => Refresh();
            _basis.SelectedIndexChanged += (s, e) => Refresh();
            _import.Click += (s, e) =>
            {
                if (Import())
                    Close(true);
            };
            var cancel = new Button { Text = "Cancel" };
            cancel.Click += (s, e) => Close(false);
            DefaultButton = _import;
            AbortButton = cancel;

            Content = new StackLayout
            {
                Spacing = 6,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new Label { Text = "1.  Copy this snippet and run it in the BIM model's Python console (pyRevit or RevitPythonShell):", Wrap = WrapMode.Word },
                    new StackLayoutItem(_snippet, true),
                    EtoKit.Buttons(copy),
                    new Label { Text = "2.  Paste the line it printed (it starts with " + ImportedSite.Marker + "; the whole output is fine too):", Wrap = WrapMode.Word },
                    _pasted,
                    EtoKit.Buttons(paste),
                    new Label { Text = "3.  Where the Rhino model sits:", Wrap = WrapMode.Word },
                    _basis,
                    _preview,
                    EtoKit.Note("Import sets the project base point, the survey point with its shared values, and true north on their named CPlanes. " +
                                "Every spot coordinate re-labels; one Undo reverts the import."),
                    EtoKit.Buttons(_import, cancel),
                },
            };
            Refresh();
        }

        ImportBasis Basis => (ImportBasis)Math.Max(0, _basis.SelectedIndex);

        void Refresh()
        {
            if (!ImportedSite.TryParse(_pasted.Text, out _site, out string error))
            {
                _preview.Text = string.IsNullOrWhiteSpace(_pasted.Text) ? "Paste the snippet's output above." : error;
                _import.Enabled = false;
                return;
            }
            _preview.Text = _site.Describe(_doc, Basis);
            _import.Enabled = true;
        }

        bool Import()
        {
            if (_site == null)
                return false;
            var site = _site;
            var basis = Basis;
            Kit.Edit(_doc, "Skunk Import Shared Coordinates", ctx => site.Apply(_doc, basis), contextChanged: true);
            Skunk.Core.Log.Info("shared coordinates imported" + (site.Model.Length > 0 ? " from " + site.Model : "") + ":\n" + site.Describe(_doc, basis));
            _doc.Views.Redraw();
            return true;
        }
    }
}
