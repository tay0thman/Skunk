// SkRotateProjectNorth: Rotate Project North: rotate the model (90 degrees, 180, an angle, or align to a line); shared coordinates stay
//
// Skunk's engine is shared/Skunk.Core.dll (a project resource: referenced automatically here and shipped in the
// package). Change behaviour in src/Skunk.Core, then run src/build-core.cmd before Build / Publish.
using Skunk;

__rhino_command_result__ = SkunkEntry.SkRotateProjectNorth(__rhino_doc__, __rhino_runmode__, __rhino_command__);
