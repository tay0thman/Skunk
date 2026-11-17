// SkGrid: Grid: draw grid lines named 1, 2, 3 or A, B, C with a bubble at the end; type a new name into a bubble and the grids after it follow; drag a bubble along its grid to lengthen it; in a datum box the ends sit on the box
//
// Skunk's engine is shared/Skunk.Core.dll (a project resource: referenced automatically here and shipped in the
// package). Change behaviour in src/Skunk.Core, then run src/build-core.cmd before Build / Publish.
using Skunk;

__rhino_command_result__ = SkunkEntry.SkGrid(__rhino_doc__, __rhino_runmode__, __rhino_command__);
