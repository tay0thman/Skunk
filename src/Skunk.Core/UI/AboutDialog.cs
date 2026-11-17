using System;
using System.IO;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.PlugIns;
using Skunk.Commands;
using Skunk.Core;

namespace Skunk.UI
{
    /// <summary>
    /// SkAbout: the Skunk logo, its version, what it does, who made it, where it is installed and where its toolbar file is
    /// (both selectable, to copy), and the project page. -SkAbout prints the same on the command line.
    /// </summary>
    public sealed class AboutDialog : Dialog
    {
        /// <summary>The project page (the one the README links its downloads from).</summary>
        public const string ProjectPage = "https://github.com/tay0thman/Skunk";

        /// <summary>What Skunk does, in a line.</summary>
        public const string Tagline = "Project coordinates, datum levels, grids and spot annotations for Rhino 9.";

        public static void Show(RhinoDoc doc)
        {
            using (var dlg = new AboutDialog())
                dlg.ShowModal(EtoKit.Owner);
        }

        /// <summary>The lines -SkAbout prints (and the dialog shows).</summary>
        public static string[] Lines()
        {
            string rhp = InstalledAt();
            return new[]
            {
                SkunkInfo.Title + " - " + Tagline,
                "By Tay Othman. " + ProjectPage,
                "Installed: " + (string.IsNullOrEmpty(rhp) ? "(not found)" : rhp),
                "Toolbar file: " + (string.IsNullOrEmpty(rhp) ? "(not found)" : SkunkToolbar.Target(rhp, File.Exists)),
            };
        }

        static string InstalledAt()
        {
            try
            {
                return PlugIn.PathFromId(SkunkToolbar.PlugInId) ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        AboutDialog()
        {
            Title = "About Skunk";
            Resizable = false;
            Padding = new Padding(16);
            EtoKit.Style(this);

            string rhp = InstalledAt();
            var logo = new ImageView { Size = new Size(96, 96) };
            try
            {
                string name = "Skunk.Core.Resources.Skunk.About." + (Rhino.Runtime.HostUtils.RunningInDarkMode ? "dark" : "light") + ".png";
                using (var s = typeof(AboutDialog).Assembly.GetManifestResourceStream(name))
                    if (s != null)
                        logo.Image = new Bitmap(s);
            }
            catch (Exception)
            {
                // the logo is cosmetic
            }

            TextBox PathBox(string text) => new TextBox { Text = text, ReadOnly = true, Width = 420 };
            var page = new LinkButton { Text = ProjectPage };
            page.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ProjectPage) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Warn("the project page did not open (" + ex.Message + ").");
                }
            };
            var ok = new Button { Text = "OK" };
            ok.Click += (s, e) => Close();
            DefaultButton = ok;
            AbortButton = ok;

            Content = new TableLayout
            {
                Spacing = new Size(12, 8),
                Rows =
                {
                    new TableRow(
                        new TableCell(logo),
                        new TableCell(new StackLayout
                        {
                            Spacing = 4,
                            Items =
                            {
                                new Label { Text = SkunkInfo.Title, Font = SystemFonts.Bold(16) },
                                new Label { Text = Tagline, Wrap = WrapMode.Word, Width = 420 },
                                new Label { Text = "By Tay Othman" },
                                page,
                            },
                        }, true)),
                    new TableRow(new Label { Text = "Installed" }, PathBox(string.IsNullOrEmpty(rhp) ? "(not found)" : rhp)),
                    new TableRow(new Label { Text = "Toolbar file" }, PathBox(string.IsNullOrEmpty(rhp) ? "(not found)" : SkunkToolbar.Target(rhp, File.Exists))),
                    new TableRow(null, EtoKit.Buttons(ok)),
                },
            };
        }
    }
}
