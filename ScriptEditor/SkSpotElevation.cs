// SkSpotElevation: Spot Elevation: elevation of a point on a surface, polysurface, mesh, SubD, edge or curve - follows its host
//
// Skunk's engine is shared/Skunk.Core.dll (a project resource: referenced automatically here and shipped in the
// package). Change behaviour in src/Skunk.Core, then run src/build-core.cmd before Build / Publish.
using Skunk;

__rhino_command_result__ = SkunkEntry.SkSpotElevation(__rhino_doc__, __rhino_runmode__, __rhino_command__);
