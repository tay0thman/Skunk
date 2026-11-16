// SkRotateTrueNorth: Rotate True North: turn the shared coordinate system about the project base point or survey point - the model stays
//
// Skunk's engine is shared/Skunk.Core.dll (a project resource: referenced automatically here and shipped in the
// package). Change behaviour in src/Skunk.Core, then run src/build-core.cmd before Build / Publish.
using Skunk;

__rhino_command_result__ = SkunkEntry.SkRotateTrueNorth(__rhino_doc__, __rhino_runmode__, __rhino_command__);
