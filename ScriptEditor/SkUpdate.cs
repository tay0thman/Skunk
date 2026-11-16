// SkUpdate: Update: re-sync every level, spot and north arrow with its host and the coordinate systems
//
// Skunk's engine is shared/Skunk.Core.dll (a project resource: referenced automatically here and shipped in the
// package). Change behaviour in src/Skunk.Core, then run src/build-core.cmd before Build / Publish.
using Skunk;

__rhino_command_result__ = SkunkEntry.SkUpdate(__rhino_doc__, __rhino_runmode__, __rhino_command__);
