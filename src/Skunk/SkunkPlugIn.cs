using System;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.PlugIns;
using Skunk.Core;
using Skunk.Service;

// Same id as ScriptEditor/Skunk.rhproj: Rhino loads only one Skunk (install one or the other).
[assembly: Guid("f13025f7-108d-504b-80fb-7ff84ca963ea")]
[assembly: PlugInDescription(DescriptionType.Organization, "Tay Othman")]
[assembly: PlugInDescription(DescriptionType.Email, "tay@tayothman.com")]
[assembly: PlugInDescription(DescriptionType.Address, "")]
[assembly: PlugInDescription(DescriptionType.Country, "")]
[assembly: PlugInDescription(DescriptionType.Phone, "")]
[assembly: PlugInDescription(DescriptionType.WebSite, "")]
[assembly: PlugInDescription(DescriptionType.UpdateUrl, "")]
[assembly: PlugInDescription(DescriptionType.Icon, "Skunk.Plugin.Resources.Skunk.ico")]

namespace Skunk.Plugin
{
    /// <summary>
    /// The compiled Skunk plug-in. It loads when Rhino starts and starts the event service as soon as
    /// Rhino is idle, so levels, spots and north arrows stay live without running a Skunk command first.
    /// </summary>
    public sealed class SkunkPlugIn : PlugIn
    {
        public SkunkPlugIn()
        {
            Instance = this;
        }

        public static SkunkPlugIn Instance { get; private set; }

        public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            // documents, views and the display pipeline are ready on the first idle, not during start-up
            RhinoApp.Idle += StartService;
            return LoadReturnCode.Success;
        }

        static void StartService(object sender, EventArgs e)
        {
            RhinoApp.Idle -= StartService;
            try
            {
                SkunkService.Ensure();
            }
            catch (Exception ex)
            {
                Log.Error("the Skunk event service did not start", ex);
            }
        }
    }
}
