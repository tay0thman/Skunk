// SkGoToLevel: Go To Level: set the CPlane to a level, with a plan view and a view range (plan cut)
//
// Skunk's engine is shared/Skunk.Core.dll (a project resource: referenced automatically here and shipped in the
// package). Change behaviour in src/Skunk.Core, then run src/build-core.cmd before Build / Publish.
using Skunk;

__rhino_command_result__ = SkunkEntry.SkGoToLevel(__rhino_doc__, __rhino_runmode__, __rhino_command__);
