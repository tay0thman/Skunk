// SkLocation: Location: the project location (the Rhino model's geographic location and Rhino's north), project base point, survey point, angle to true north and their named CPlanes
//
// Skunk's engine is shared/Skunk.Core.dll (a project resource: referenced automatically here and shipped in the
// package). Change behaviour in src/Skunk.Core, then run src/build-core.cmd before Build / Publish.
using Skunk;

__rhino_command_result__ = SkunkEntry.SkLocation(__rhino_doc__, __rhino_runmode__, __rhino_command__);
