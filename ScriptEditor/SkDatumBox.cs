// SkDatumBox: Datum Box: one box holds the extents of the levels and the ends of the grids in it - move, scale or rotate the box and they follow; drag a level's head sideways, or a grid's bubble along it, to take that one out
//
// Skunk's engine is shared/Skunk.Core.dll (a project resource: referenced automatically here and shipped in the
// package). Change behaviour in src/Skunk.Core, then run src/build-core.cmd before Build / Publish.
using Skunk;

__rhino_command_result__ = SkunkEntry.SkDatumBox(__rhino_doc__, __rhino_runmode__, __rhino_command__);
