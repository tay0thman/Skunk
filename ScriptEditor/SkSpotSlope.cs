// SkSpotSlope: Spot Slope: slope arrow on a surface, terrain mesh or edge; drag its end grips to set its reach, move it and it measures where it is - percent, rise / 12", 1:x, degrees ...
//
// Skunk's engine is shared/Skunk.Core.dll (a project resource: referenced automatically here and shipped in the
// package). Change behaviour in src/Skunk.Core, then run src/build-core.cmd before Build / Publish.
using Skunk;

__rhino_command_result__ = SkunkEntry.SkSpotSlope(__rhino_doc__, __rhino_runmode__, __rhino_command__);
