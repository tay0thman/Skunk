// SkShowSite: Show / hide the internal origin, project base point and survey point
//
// Skunk's engine is shared/Skunk.Core.dll (a project resource: referenced automatically here and shipped in the
// package). Change behaviour in src/Skunk.Core, then run src/build-core.cmd before Build / Publish.
using Skunk;

__rhino_command_result__ = SkunkEntry.SkShowSite(__rhino_doc__, __rhino_runmode__, __rhino_command__);
