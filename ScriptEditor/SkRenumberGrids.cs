// SkRenumberGrids: Renumber Grids: name grids one after another (1, 2, 3 or A, B, C) - pick them in order, or select several and draw a direction across them
//
// Skunk's engine is shared/Skunk.Core.dll (a project resource: referenced automatically here and shipped in the
// package). Change behaviour in src/Skunk.Core, then run src/build-core.cmd before Build / Publish.
using Skunk;

__rhino_command_result__ = SkunkEntry.SkRenumberGrids(__rhino_doc__, __rhino_runmode__, __rhino_command__);
