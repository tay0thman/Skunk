// SkProperties: Skunk Attributes: the dockable panel with the type and parameters of the selected Skunk elements - opens it, or closes it when its tab is in front
//
// Skunk's engine is shared/Skunk.Core.dll (a project resource: referenced automatically here and shipped in the
// package). Change behaviour in src/Skunk.Core, then run src/build-core.cmd before Build / Publish.
using Skunk;

__rhino_command_result__ = SkunkEntry.SkProperties(__rhino_doc__, __rhino_runmode__, __rhino_command__);
