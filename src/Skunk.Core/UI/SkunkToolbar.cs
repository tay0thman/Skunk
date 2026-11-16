using System;
using System.IO;
using Rhino;
using Rhino.PlugIns;
using Rhino.UI;

namespace Skunk.UI
{
    /// <summary>
    /// Shows the Skunk toolbar: the one toolbar group of Skunk.rui, which ships next to the plug-in in
    /// both channels (Script Editor package and compiled plug-in). Used by the Skunk command.
    /// </summary>
    public static class SkunkToolbar
    {
        /// <summary>Skunk's plug-in id (the Script Editor project id; the compiled plug-in uses the same).</summary>
        public static readonly Guid PlugInId = new Guid("f13025f7-108d-504b-80fb-7ff84ca963ea");

        /// <summary>The toolbar group id tools/gen_project.py writes into Skunk.rui (checked by SkunkCheck rui).</summary>
        public static readonly Guid GroupId = new Guid("c879d7c5-a20d-50fc-9afe-ae95427fd409");

        /// <summary>
        /// Make the Skunk toolbar visible. Uses the toolbar file Rhino opened with the plug-in, or opens
        /// Skunk.rui from the plug-in's folder when it is not open. False when neither is found.
        /// </summary>
        public static bool Show(out string path)
        {
            path = "";
            var file = Find() ?? OpenNextToPlugIn();
            var group = Group(file);
            if (group == null)
                return false;
            group.Visible = true;
            path = file.Path ?? "";
            return true;
        }

        /// <summary>The open toolbar file that holds the Skunk toolbar group, or null.</summary>
        public static ToolbarFile Find()
        {
            var files = RhinoApp.ToolbarFiles;
            for (int i = 0; files != null && i < files.Count; i++)
                if (Group(files[i]) != null)
                    return files[i];
            return null;
        }

        static ToolbarGroup Group(ToolbarFile file)
        {
            for (int i = 0; file != null && i < file.GroupCount; i++)
            {
                var g = file.GetGroup(i);
                if (g != null && g.Id == GroupId)
                    return g;
            }
            return null;
        }

        static ToolbarFile OpenNextToPlugIn()
        {
            string rhp;
            try
            {
                rhp = PlugIn.PathFromId(PlugInId);
            }
            catch (Exception)
            {
                return null;
            }
            if (string.IsNullOrEmpty(rhp))
                return null;
            var rui = Path.Combine(Path.GetDirectoryName(rhp) ?? "", "Skunk.rui");
            return File.Exists(rui) ? RhinoApp.ToolbarFiles.Open(rui) : null;
        }
    }
}
