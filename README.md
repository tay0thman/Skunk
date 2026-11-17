# Skunk

Project coordinate systems, datum levels and spot annotations for Rhino 9, as parametric objects
you can model with and print.

## Download and install

**[Download Skunk 1.6.6 for Rhino 9 on Windows (.yak)](https://github.com/tay0thman/Skunk/releases/download/v1.6.6/skunk-1.6.6-rh9_0-win.yak)**

The package is publicly downloadable without a GitHub account. See
[all releases](https://github.com/tay0thman/Skunk/releases) for release notes and SHA-256 checksums.

1. Download the `.yak` package above. It contains the compiled Skunk plug-in and toolbar.
2. Drag the downloaded file onto Rhino 9 and complete the installation, then restart Rhino.
3. Run `Skunk` once to load the toolbar and check the installed version. Skunk writes its toolbar file into its
   package folder (`%APPDATA%\McNeel\Rhinoceros\packages\9.0\Skunk\Skunk.rui`) and Rhino opens it at every start
   like a toolbar file of your own, so the Skunk toolbar stays where you dock it - a tab in a toolbar of your own
   too - after restarts and package updates. A new version rewrites it only when its buttons changed. In
   `SkSettings`, **Start Skunk with Rhino** loads
   Skunk with Rhino, so elements stay in sync from the first moment.

If you prefer the command line, [Yak supports installing a local package](https://developer.rhino3d.com/guides/yak/yak-cli-reference/#install):

```powershell
& "C:\Program Files\Rhino 9\System\Yak.exe" install "C:\path\to\skunk-1.6.6-rh9_0-win.yak"
```

Requires Rhino 9 for Windows. Install only one Skunk build: the compiled plug-in and
Script Editor build share a plug-in ID. The download above is the compiled build, which
starts its update service when Rhino starts. For building from source, see [Install / build](#install--build).

## Features

- **Coordinate systems.** The internal origin is Rhino's world origin. The project base point and the
  survey point each live on their own named CPlane, so **true north is the Y axis of a separate
  CPlane**, independent of the world and of project north. Shared coordinates, clipped and unclipped
  survey points, Rotate True North / Project North and Specify / Report Coordinates work the way
  BIM users expect. The **project location** is the Rhino model's own geographic location (its Earth
  anchor point, which Rhino's Sun shares): Skunk reads it from the model, and Rhino's north follows true
  north while the two agree.
- **Levels.** Datum levels have heads in every elevation direction, their own named CPlane (the work
  plane), and objects attached to them that move when the level moves. A **datum box** holds the extents
  of many levels and the ends of grids: move, stretch or rotate it and they all follow.
- **Grids.** Grid lines named 1, 2, 3 or A, B, C, with a bubble at either or both ends, on every floor's
  plan, and in elevations and sections as vertical lines with their bubbles. Layout details show each grid
  the way it reads there. Type a new name into a bubble and the grids after it follow on; Renumber Grids
  numbers grids in the order you pick them or along a direction; Grid Bubbles puts bubbles on the top,
  bottom, left or right of the view (ticked around a diagram of the grids), and a layout detail can keep sides
  of its own. Drag a bubble along its
  grid to lengthen or shorten it.
- **Spot annotations.** Spot elevations, spot coordinates and spot slopes sit on surfaces,
  polysurfaces, terrain meshes, SubDs, edges and curves. They measure from the project base point, the
  survey point, the internal origin, a level, or **any named CPlane**.
- **North arrows** that keep pointing to true north.
- **A familiar UI.** A dockable **Skunk Attributes** panel (dock it, tab it with Rhino's Properties and Layers,
  or float it) with a Type Selector and Edit Type, a Type Properties
  dialog, and Location, Levels and Settings dialogs, on one Skunk toolbar with separators between its sections
  (Architecture, Annotate, Manage, View) and between groups of similar buttons.

Every annotation is a native Rhino object: a leader, a linear dimension or a block. That means it
snaps, prints at the right size in layout details, goes through Make2D and exports to DWG like any
Rhino annotation. Skunk's event service keeps each one up to date **whenever it or its host moves**.

Icon sheet (both themes): [docs/icons-preview.html](docs/icons-preview.html).

## Install / build

Skunk ships two ways, built from the same sources, with the same commands, toolbar and plug-in id.
**Install one or the other, never both** (Rhino loads only one plug-in with a given id). Publish to
the Yak server from one channel only.

| | Script Editor project (the deliverable) | Compiled, always-on plug-in |
|---|---|---|
| Source | `ScriptEditor/Skunk.rhproj` + `shared/Skunk.Core.dll` | `src/Skunk/Skunk.csproj` (engine compiled in) |
| Built by | Rhino 9 Script Editor: Build / Publish | `src\build-plugin.cmd` (dotnet + Yak.exe) |
| Output | `ScriptEditor\build\rh9\skunk-1.6.6-rh9-any.yak` | `dist\Skunk.rhp`, `dist\skunk-1.6.6-rh9_0-win.yak` (the toolbar is compiled in) |
| Live updates start | at the first Skunk command of a session (or add `Skunk` to the startup commands) | **when Rhino starts** (the plug-in loads at start-up) |
| First-command cost | about 1-2 s once per session (C# script engine) | none |

Both compile against RhinoCommon 9.0.26300.9401 (NuGet). Use Rhino 9 at that build or newer.
Building from source requires the .NET 8 SDK and Rhino 9 for packaging; Python 3 is needed
when regenerating the project and command files. Generated binaries and package outputs
are excluded from Git; download a release above if you only want to install Skunk.

**Script Editor project**

Prebuilt alternative: [download the Skunk 1.6.6 Script Editor package (.yak)](https://github.com/tay0thman/Skunk/releases/download/v1.6.6/skunk-1.6.6-rh9-any.yak).
Install this package or the compiled build above, never both.

1. Build the engine (only after changing C#): `src\build-core.cmd`. This writes
   `ScriptEditor\shared\Skunk.Core.dll`.
2. In Rhino 9, open the Script Editor (`ScriptEditor` command), then *File > Open Project* and choose
   `ScriptEditor\Skunk.rhproj`.
3. Choose *Build & Load* to try it, or *Publish* to push it to the Yak server (your McNeel account).
   A package built from the project is in `ScriptEditor\build\rh9\skunk-1.6.6-rh9-any.yak`. Drag it
   onto Rhino, or install it with `yak install`.
   **One Skunk at a time.** *Build & Load* registers the build folder's `Skunk.rhp` under the same plug-in id
   as an installed Skunk package. Rhino loads only one plug-in per id: it runs whichever its records point to
   (they flip back to the package when Rhino starts) and refuses the other with *"Unable to load Skunk.rhp
   plug-in: ID already in use"* - for example when it brings back a docked Skunk Attributes panel. To try a
   build with a Skunk package installed, install the build's `.yak` instead (it replaces the package), or
   uninstall the package while you use *Build & Load*.
4. **To keep elements live from the moment Rhino starts:** add the command `Skunk` to
   *Tools > Options > General > Command Lists* (Run these commands every time Rhino starts).
   Otherwise live updates start with the first Skunk command of a session. Starting the service later
   is safe: it re-syncs every element of every open model. `Skunk` also opens the Skunk toolbar when Rhino
   has not loaded it; a toolbar Rhino has loaded stays where you put it (docked as a tab in a toolbar of your
   own, say, or hidden), so starting Rhino never shows a second Skunk toolbar.

**Google Drive and the build folder.** Google Drive can mark project folders read-only,
and the Script Editor cannot delete read-only folders. Its package build deletes the previous
`build\rh9` first, so every build after the first one failed with *Error preparing build directory
... Access to the path '...\build\rh9\shared' is denied*. That is why `ScriptEditor\build` is a
junction to `%LOCALAPPDATA%\Skunk-build\ScriptEditor-build` in the author's workspace, outside
Drive: every path stays the same and the build output is not synced. The junction is not
stored in Git. If your checkout is synced by Google Drive, run `tools\link-build-folder.cmd`
once (`gen_project.py` warns when the link is missing).

**Compiled plug-in**
1. `src\build-plugin.cmd` builds `dist\Skunk.rhp` and the `.yak` (the toolbar is compiled in; Skunk writes its
   toolbar file itself).
2. Drag `dist\Skunk.rhp` (or the `.yak`) onto Rhino 9, then restart Rhino. The plug-in loads at
   start-up and starts the event service on Rhino's first idle; no startup command is needed.
3. To switch back to the Script Editor build, uninstall the plug-in first (Plug-in Manager, or the
   Package Manager for the `.yak`).

Either way, show the toolbar with `Toolbar` > *Skunk*: one toolbar, with separators between its
Architecture, Annotate, Manage and View sections and between groups of similar buttons in them (levels |
level tools, spots | north arrow | properties, site points | coordinates | rotation | Skunk itself).
Rhino 9.2 (9.2.26314) reads and keeps these separators but does not draw them yet: its rewritten
toolbars drop separators in custom toolbars (a known Rhino 9 issue). They appear once Rhino draws them.

**Updating the compiled plug-in: close Rhino first.** Rhino locks the `Skunk.rhp` it loaded (the
build then warns and leaves it; the `.yak` in `dist` is still rebuilt). So close Rhino, run
`src\build-plugin.cmd`, then start Rhino: it loads the new plug-in, which brings the toolbar's new buttons.

**No toolbar file in the packages (since 1.6.6).** Rhino opens a plug-in's own toolbar file (one shipped beside
it) only when the plug-in loads, after it has built its toolbars. Up to 1.6.5 every Skunk package carried one, with
the same file id as the copy Skunk kept in the package folder: each start Rhino took the package's file in place of
the copy, Skunk closed it - taking the Skunk toolbar out of the toolbar you had docked it in - and opened its copy,
which Rhino showed floating. Since 1.6.6 the toolbar is compiled into the engine and Skunk writes the one toolbar
file itself.

The version of every build is the rhproj `identity.version` (edit it in the Script Editor). Run
`python tools/gen_project.py` afterwards: it writes `src/Version.props` and the plug-in's Yak manifest.
**Before you publish a new version**, set it, close Rhino, run `python tools/gen_project.py` and
`src\build-core.cmd`, then reopen the project and publish: the engine in the package then reports the
same version as the package (the `Skunk` command and SkSettings > About show the engine's version).

## Commands and toolbar

| Section | Command | What it does |
|---|---|---|
| Architecture | `SkLevel` | Pick an elevation (or type one) to create a level. Extents come from the previous level, the model, or two picked corners. Right-click opens the Levels manager. |
| | `SkLevels` | Levels manager: rename, change elevations, add a level above, delete, go to. |
| | `SkGrid` | Draw grid lines one after another (Enter ends): each is named after the last one (1, 2, 3 or A, B, C) and gets a bubble at its end. Options: Type, Name, Bubbles (end, start, both, none) and a DatumBox for the ends. |
| | `SkRenumberGrids` | Name grids one after another (1, 2, 3 or A, B, C): pick them in order and press Enter, or select several and draw a direction across them. StartName sets the first name (by default the first of their names). |
| | `SkGridBubbles` | A dialog: tick Top, Bottom, Left and / or Right around a diagram of the grids it changes, drawn as the view shows them, with the bubbles where they will be as you tick. In a model view it sets the grids' bubbles (the selected grids, *Select grids...*, or every grid in the view; looking level, the top and bottom of their elevation lines). In a layout detail the sides become that detail's own; on a layout with no detail active, every detail on it. *Show every bubble* gives a detail all of them back. `-SkGridBubbles` asks on the command line instead (for macros). |
| | `SkDatumBox` | A datum box for the extents of levels and the ends of grids: pick levels and grids (Enter = all), then two corners (Enter = fit the levels). Move, stretch, rotate or grip-edit the box and everything in it follows. Drag one level's head sideways, or a grid's bubble along it, to take that one out. |
| | `SkGoToLevel` | A dialog lists the levels with their elevations (the one the CPlane is at selected; double-click to go): sets the CPlane to the level, with an optional plan view and a view range - a clipping plane at the cut height with depth below the level. `-SkGoToLevel` keeps the command-line options for macros. |
| | `SkAttachToLevel` / `SkDetachFromLevel` | Attached objects move with their level. |
| Annotate | `SkSpotElevation` | Actual / Top / Bottom / Top & Bottom elevations. Options: Leader, Shoulder, Relative Base, PickMode (object or snapped point). |
| | `SkSpotCoordinate` | N/S and E/W (plus elevation) from the PBP, the survey point (shared), the internal origin or a named CPlane. |
| | `SkSpotSlope` | Slope arrow on surfaces, meshes, lines and polylines, pointing downhill (Up / Down / flip). It lies **on the surface, down the fall line** (on a curve, along the segment under it, which is the slope it reads); the type's Placement option can keep it flat in the view plane instead. **Placed in a side view** (an elevation or section, whose CPlane is not level) its arrow and text lie in that view's CPlane, so that view shows them, just above the host line; **move it up or down** and it stays there (its *Offset From Host* in the Skunk Attributes panel), with the text on the far side from the line. **Drag its top and bottom grips** to set how far it reaches; **move it** and it measures where it now is. Formats: %, ‰, degrees, rise / 12", 1:x, x:12 and more. |
| | `SkNorthArrow` | Re-orients itself when true north changes. |
| | `SkEditType` | Type Properties of the selected element's type. |
| | `SkProperties` | Opens the **Skunk Attributes** panel (or closes it when its tab is in front): a Rhino panel that docks, tabs and floats like Properties and Layers, live for the selection. With nothing selected it shows the project location, the Rhino model's geographic location first. |
| Manage | `SkLocation` | The project location (the Rhino model's geographic location: place, latitude, longitude, elevation, and Rhino's north against true north; *Set in Rhino* opens Rhino's Document Properties > Location, the world map), PBP shared coordinates, angle to true north, survey point values / clip, which named CPlanes hold them. |
| | `SkProjectBasePoint` | Move it (the model stays) or type N/S, E/W, Elev and the angle. *Place* in the Project Base Point header of the Skunk Attributes panel runs it. |
| | `SkSurveyPoint` | Clipped: moves the shared system. Unclipped: moves only the marker. *Place* in the Survey Point header of the Skunk Attributes panel runs it. |
| | `SkSpecifyCoordinates` | Give a picked point its shared coordinates and true north. |
| | `SkReportCoordinates` | Shared, project and internal coordinates of picked points. |
| | `SkImportSharedCoordinates` | Match a BIM model's shared coordinates: copy the Python snippet it shows into that model's Python console, paste the line it prints back, say where the Rhino model sits (the model's internal origin, its project base point, or its shared coordinates) and import. Sets the project base point, the survey point with its shared values, and true north; one Undo reverts it. |
| | `SkRotateTrueNorth` | About the PBP or the survey point; the model stays. |
| | `SkRotateProjectNorth` | Rotates the model (90°, 180°, an angle, align to a line); shared coordinates stay. |
| | `SkAbout` | **About Skunk**: the version, what it does, who made it, the project page, where it is installed and where its toolbar file is (selectable, to copy). `-SkAbout` prints the same. |
| | `SkUpdate` / `SkSettings` / `Skunk` | Re-sync everything; settings; `Skunk` loads Skunk, starts live updates, **opens the Skunk toolbar if Rhino has not loaded it** (a loaded one stays where you put it) and reports status. |
| View | `SkOrientView` | Left click: true north up. Right click: project north up. |
| | `SkShowSite` | Show / hide the internal origin, PBP and survey point. |

**Keyboard aliases.** `SkSettings` > *Keyboard aliases* adds Skunk's suggestions in one click: LL (Level),
PI (Properties), SPE / SPC / SPS (spot elevation / coordinate / slope). *Edit aliases...* lists every Skunk
command, grouped like the toolbar, with the alias Rhino has for it now: type your own to override it, or
clear it for none, then Apply. An alias that already runs another command is left alone unless you tick
*Replace an alias that already runs another command*; the result says what was set, removed, replaced or
left alone. A command's old alias goes when you give it a new one (an earlier PP for Properties becomes
PI). Aliases are Rhino settings, so they work in every model.

**Units follow Rhino.** Every length Skunk shows (spot and level text, the Skunk Attributes panel, the
dialogs, command-line reports) follows *Document Properties > Units*: the unit system, the distance
display (decimal, fractional, feet and inches) and the display precision. Change them and every
annotation re-labels itself. New types use *Rhino document units*; a type can still choose a fixed
format in Type Properties (for example decimal feet for survey coordinates), and that choice stays.
Type sizes (text height, symbols, arrow length) are paper sizes shown in Rhino's page (layout) units.
Changing the model units with scaling keeps everything in place: Skunk scales the lengths it stores
(leader offsets, slope reach, level elevations, the survey point's shared values) by the same factor,
also when the change happened while Skunk was not running.

**Rhino dimensions on an `__Annotations__` sublayer.** On by default: dimensions and leaders drawn with Rhino's own
commands go to `__Annotations__::Dimensions` as they are made, in the same undo step. Text stays where it is, and
Skunk's own annotations keep their layers. In `SkSettings` > *Rhino dimensions* you can turn it off or
name another sublayer; *Move existing Rhino dimensions there now* moves the ones already in the model.

**Importing shared coordinates.** `SkImportSharedCoordinates` shows a short Python snippet. Run it in the
BIM model's Python console; it prints one line starting with `SKUNK-SITE` (both base points with their
shared values, and the model's coordinate transform). Paste that line (or the whole output) back, choose
where the Rhino model sits, and the preview shows what will be set before you import. Skunk checks the
transform against both points' own shared values and warns when they disagree. Lengths arrive in the
model's feet and are converted to Rhino's units.

**Levels: line pattern and heads.** A level type has a *Line Pattern* (By Layer = Skunk Datum, or
Skunk Datum, Center, DashDot, Dashed, Hidden, Border, Dots, Continuous or any linetype in the model;
Rhino's standard ones load on demand) and a *Line Pattern Scale*. Each level can override the
pattern in the Skunk Attributes panel. *Heads At* (Right end / Left end / Both ends / None) puts the
heads at either end of the level line in every elevation; `SkLevel` has the same choice as its
`Heads` option, and each side can still be switched on its own.

**Levels in print.** A level head prints the same at every scale: its symbol (a filled target by
default), tail (*Head Length*, at least the symbol's radius so the text always clears it), gap and
text are all type sizes that Rhino scales together per view and layout detail. What prints as the
level line is one **level line** per elevation direction that has heads, along those head edges;
the footprint itself shows in model views but does not print. (Seen edge-on in an elevation, a
footprint's front and back edges lie on top of each other with their dash patterns running opposite
ways, so together they print almost solid, more or less so depending on the scale.) A level line
has the level's line pattern, moves the level when dragged up or down, snaps back otherwise, and
comes back if deleted: switch off the heads on that side to drop it from that elevation. Skunk
Datum is a paper pattern (3/4" dash, 1/8" gap, 1/8" dash, 1/8" gap), like Rhino's own linetypes,
so level lines print the same dashes in any model units and at any detail scale. Model views show
linetypes at Rhino's linetype display scale; Skunk keeps that at the annotation scale (Settings)
unless the model has a value of its own. Change the annotation scale in `SkSettings`; Skunk also
notices it changing without an edit of its own (an Undo, say).

**Model views and prints.** Skunk's annotation scale (the view scale, `SkSettings`) starts at 1/8" =
1'-0" in feet and inches and 1:100 in metric. With Rhino's annotation scaling on in model views and
layouts (as in every Rhino 9 template), Skunk's annotation styles hold their sizes as paper sizes in the
page units, the way Rhino's own styles do (3/16" text is 0.1875 with inch pages), and carry a *model space
scale* of the annotation scale in model units per page unit (8 for 1/8" = 1'-0" in a feet model with inch
pages, as 96 / 12). So model views show level heads, grid bubbles and spot text at the annotation scale (a
1/2" bubble is 4' across at 1/8" = 1'-0"), and layout details print them at their type size at any detail
scale: a 1/2" bubble prints 1/2" across in a 1/8", 1/4" or 1/16" detail. The document's own text scale in
Document Properties is a separate value Rhino does not draw these with; Skunk leaves it alone. *Use Rhino's
annotation scaling* in `SkSettings` switches Rhino's model space and layout scaling together; off, Skunk
puts the annotation scale in the sizes itself, which print right only in details at that scale.

**Datum boxes.** `SkDatumBox` draws one box around the levels (all of them, or the ones you pick) and
puts them in it: each level takes the box's outline as its extents, at its own elevation. In plan the box
is that outline; elevations show it as a dotted box from just under the lowest level to just over the
highest, with its name, and it never prints. Move it, stretch it with the gumball, rotate it or drag its
corners, and every level in it follows, heads and level lines included. The box's height follows its levels.
Type a *Rotation* in the Skunk Attributes panel to turn it about its middle. New levels made with `SkLevel`'s *Previous* extents, or the Levels
manager's *Add above*, join the top level's box. Dragging one level's head **sideways** moves that end of
the level and takes the level out of the box; dragged mostly up or down, the head moves the level and it
stays in. Editing or moving a level's own footprint in plan also takes it out. The level's *Datum Box*
property in the Skunk Attributes panel puts it back, or moves it to another box. Copy a box together with its
levels and the copies go into the copied box. Delete the box and its levels keep their extents.

**Grids.** `SkGrid` draws grid lines one after another, each named after the last one: 1, 2, 3, or A,
B, C (Z is followed by AA, A9 by A10, B.1 by B.2). Type a Name option to start a new series. A grid is a
horizontal line on `__Annotations__::Grids` (Skunk Datum) with a bubble at End 2 by default. The bubble is one
text: the grid's name, drawn by Rhino in a white circle with a solid outline (the text's own mask), so the
line never shows through it and the circle can never cover the name - selected, edited or in any display
mode. Its size and the text are type sizes, so it prints at its size at any scale, the name always
centred. The Skunk Attributes panel switches each end's bubble on or off (deleting one switches it off
too). Type Properties sets bubble size, text, line pattern and the default ends. Drag a bubble **along**
its grid to move that end; dragged across, it snaps back.

**Grid names.** Double-click a bubble and type a new name: the grid takes it, and the grids after it
follow on - the parallel grids beyond it, in the direction the names already run (left to right or top to
bottom when they don't say). Rename 6 to A and 7, 8, 9 become B, C, D; rename the 6 in 1, 2, 6, 3, 4 to 3 and
the 3 and 4 after it become 4 and 5. A name in between (2.5 or 2A after grid 2, A.1 after A) renames
that grid alone. The Skunk Attributes panel's Name and Rhino's Properties rename the same way. Names stay unique: a
grid that already has a name you give takes the name the other gave up. `SkRenumberGrids` numbers grids
from a start name in the order you pick them, or in the order a direction you draw crosses them. Copies
get the next names in turn, nearest the original first, so an array of grids is numbered in order. Plan
cuts (`SkGoToLevel` view ranges) never clip grids, so they show on
every floor's plan, whatever their height. In a **datum box** a grid's ends sit on the box: move or
turn the grid, or change the box, and the ends follow. Drag a bubble along the grid, or one end of
the line, and the grid leaves the box and keeps that end. Its *Datum Box* property puts it back.

**Grids in elevations.** Each grid also stands in elevations and sections. Beyond each end, one bubble diameter out, a vertical line
on `__Annotations__::Grid Elevations` runs from just under the lowest level to just over the highest (in a datum
box, the box's height), with the grid's bubble at the top. It faces out, so an elevation looking along
the grid from that side sees it face on and reads the name the right way round. The type's *Elevation
Bubbles* puts the bubbles at the top, the bottom, both or neither; a grid's own *Elevation Bubbles* (the Skunk
Attributes panel, or `SkGridBubbles` in a view looking level) overrides it. The lines follow the grid, its levels
and its box; moved they snap back, deleted they come back. **Layout details show grids as drawings
do:** a plan detail shows the plan grid and bubbles, an elevation or section detail (parallel, looking
level) shows only the elevation parts it looks at - grids it looks along, from the side it stands on -
and a 3D detail shows the plan grid. Skunk sets Rhino's Hide in Detail overrides for this, so it prints
and exports as it shows, and it notices new or turned details by itself. Model views show every part:
switch off `__Annotations__::Grid Elevations` while you work in plan (from above, an elevation bubble is a short
stroke just beyond the plan bubble).

**Grid bubbles by view.** `SkGridBubbles` puts bubbles on the top, bottom, left and / or right of the active
view, as the view shows them (a plan turned to true north turns them too). Its dialog has the four sides as
boxes around a diagram of the grids it changes, drawn the way the view shows them: a bubble with the grid's name
where one will be, a faint ring at an end that will have none, the ticked edges marked. In a model view it sets
the grids' own bubbles: each grid end pointing to a ticked side gets one, the other ends lose theirs. It works on
the grids you selected first, the ones you pick with *Select grids...*, or else every grid in the view; looking
level, it sets the top and bottom bubbles of the grids the view looks along. Model views cannot hide an object in one view only, so there it changes the grids everywhere. In a layout
detail the sides become the **detail's own**: it shows only the bubbles on them (a grid that has none there gets
one) while model views and other details keep theirs - plans on one sheet can show bubbles top and left, an
enlarged plan on another bottom and right, an elevation only the top ones. Skunk keeps it with the detail
(through Hide in Detail, like the plan / elevation split), for grids added or moved later too. On a layout with
no detail active it sets every detail on it; *Show every bubble* gives a detail all of them back.
`-SkGridBubbles` asks on the command line (Top, Bottom, Left, Right toggles; ShowAll in a detail) for macros.

**Spot slope arrows.** The arrow runs through its location along the fall line there, and its two
grips (the dimension's definition points) are its top and bottom ends. Drag one to set how far the
arrow reaches uphill or downhill: only the part of the drag along the slope counts, so the arrow
keeps following the surface. Move the arrow (drag it, nudge its line sideways, Move, Rotate, Copy)
and it lands on the host where you put it, turns to the fall line there and measures there. The
value is the slope between the arrow's two ends, rise over plan run, with the ends on the host
straight below / above the arrow: on a plane it is the plane's slope, on a ruled or warped surface
it is the slope where the arrow is and as long as it is. On a line, polyline or other curve the
arrow follows the segment under it: it reads that segment's slope and stops at its corners, so a
ramp drawn as a polyline gives each run its own slope; move the arrow onto another segment and it
reads that one. The Skunk Attributes panel shows the reach (*Arrow Top* / *Arrow Bottom*, editable;
clear one for the type's arrow length), the rise and run, and the surface's own slope at the
location when it differs.

**Skunk Attributes panel.** Besides the editable parameters, every element shows what it measures
and where it is: spots list the host, the face they sit on, the value from their origin, top /
bottom / thickness, the slope in four formats with the fall direction as a bearing from true and
project north, and the point in shared, project and internal coordinates; levels list their
elevation in every system, story height, extents and area, attached objects, relative spots and plan
cuts; ordinary objects list base level, base / top offset, height and top / bottom elevations. Every
element shows its layer and id, and nothing selected shows the project location and counts. The Project Base
Point and Survey Point headers each have a *Place* button (`SkProjectBasePoint`, `SkSurveyPoint`: pick the new
location, or type its values).

**Layers.** Skunk's layers sit under one top-level layer, `__Annotations__` (`__Annotations__::Levels`,
`::Grids`, `::Spot Elevations` and so on). Earlier builds named it `Skunk`: when such a model opens, Skunk renames
it - or, when the model already has an `__Annotations__` layer of its own, moves its sublayers in (a sublayer
of the same name already there takes their objects) and deletes the emptied `Skunk` layers. Sublayers keep their
colours, linetypes, visibility and locking.

**Project location.** The project location is the Rhino model's geographic location: its Earth anchor point
(latitude, longitude, elevation, a place name and address, and the model point they belong to), which Rhino's Sun
uses too. Skunk keeps no copy of it. Set it in Rhino's own document location, Document Properties > Location
with its world map (the Location dialog's *Set in Rhino* opens it and comes back afterwards), or with the Sun panel's
location; the Skunk Attributes panel and the Location dialog show it,
and the Location report adds the latitude and longitude of the project base point and the survey point. Rhino's
north (the Sun's and the anchor point's) follows true north while the two agree, so turning true north in Skunk
turns the sun with it, and a model's first Skunk site starts with true north along Rhino's north. A north set
apart in Rhino stays apart: the panel shows both, and the Location dialog matches them either way (*True North
from Rhino*, *Rhino's North from True North*).

## How it works (short version)

- **Data.** Each element is a native Rhino object carrying `Skunk:Kind` and `Skunk:Data` (JSON) user
  text. Types, the site and settings are JSON in the document strings (section `Skunk`). The project
  base point and survey point are point objects on a no-print layer, kept in sync both ways with
  their named CPlanes.
- **Hosting.** A spot stores its host as surface parameters normalised to the face domain, a mesh
  face plus barycentric weights, or a normalised curve parameter. Moving, rotating or scaling the
  host, or editing its control points, keeps the spot on the same spot. Dragging a spot slides it
  along the host. Its leader text keeps its offset.
- **Events.** Document events only record what changed. Updates run at the end of the command's undo
  record, so **one Undo reverts the host and its annotations together**. Changes made outside
  commands are processed on idle in a "Skunk Update" undo record. Undo / Redo replays are left alone.
- **Printing.** Type sizes are paper sizes. With Rhino's model space annotation scaling on
  (recommended, see `SkSettings`), text, symbols and whole level heads print at the type size in
  every detail at any scale, and level lines print Skunk Datum's paper pattern. Spot leaders and
  slope arrows are model geometry; the north arrow (a block) is sized with the annotation scale.

Details: [docs/Design.md](docs/Design.md).

## Repository map

```
ScriptEditor/            the Rhino 9 Script Editor project (deliverable)
  Skunk.rhproj           commands, icons, ids, version, Yak settings
  Sk*.cs, Skunk.cs       one-line C# command scripts -> Skunk.SkunkEntry in shared/Skunk.Core.dll
  Skunk.rui              the Skunk toolbar, sections and button groups separated (embedded in the engine; no package carries it)
  shared/Skunk.Core.dll  the engine (built from src/Skunk.Core)
  build/rh9/             Script Editor build output (.rhp stub, shared engine, .yak); build/ is a junction
                         to %LOCALAPPDATA%\Skunk-build\ScriptEditor-build, outside Google Drive
src/Skunk.Core/          C# engine: Core, Units, Site, Types, Elements, Service, Commands, UI (Eto)
src/Skunk/               compiled, always-on plug-in: SkunkPlugIn.cs, Commands.g.cs (generated), Yak manifest
src/Version.props        version of every build (generated from the rhproj)
src/build-core.cmd       engine -> ScriptEditor\shared\Skunk.Core.dll
src/build-plugin.cmd     compiled plug-in -> dist\
src/tests/SkunkCheck/    headless Rhino 9 checks (engine, toolbar parse, Script Editor build, plug-in)
dist/                    Skunk.rhp, skunk-<version>-rh9_0-win.yak (compiled channel); skunk-<version>-rh9-any.yak to publish
tools/                   commands.json (command list / toolbar sections and groups / tooltips), gen_project.py, icons.py,
                         link-build-folder.cmd (keeps the Script Editor build folder out of Google Drive)
docs/                    Design.md, icons-preview.html
```

To add or change a command: edit `tools/commands.json`, add the `SkunkEntry` method and its
implementation, then run `python tools/gen_project.py` (scripts, rhproj, rui, compile check, the
plug-in's command classes), `src\build-core.cmd` and, for the compiled channel, `src\build-plugin.cmd`.

## Verification

- `SkunkCheck print` (a run of its own: headless Rhino finds a layout detail's scale only in the first model with
  layouts) runs **26 print checks**: grid bubbles (name and circle) and level heads (text and symbol) print at
  their type size in layout details at 1/8", 1/4" and 1/16" = 1'-0" in a feet model and at 1:50, 1:100 and 1:200
  in a millimetre one, measured with Rhino's own detail scale, and model views show them at the annotation scale.
- `SkunkCheck` (headless Rhino 9) runs **640 engine checks**: the toolbar file Skunk writes (embedded as generated; a
  first run; later starts leaving it alone, so a toolbar docked in your own toolbar stays; Rhino's own re-save left
  alone; new buttons rewritten with the file id Rhino knows; an older package's own toolbar file closed; a toolbar
  file you closed reopened; development builds), About Skunk, spot slopes placed in side views (a profile in the Front
  and Right views and a roof seen from the side: arrow and text in the CPlane they were placed in, reading the slope,
  staying there when moved; above the host line by default, moved up or down they stay put with the text on the far
  side, an offset typed or cleared in the panel, a flipped arrow keeping its side; placed in plan they follow the
  host as before), Go To Level's preselected level, solid
  bubble circles (in plan and in elevations, where the layer is the dashed datum one; a dashed bubble from an earlier
  build put right), Skunk's layers under `__Annotations__` (a new model;
  an old `Skunk` layer renamed with its sublayers still locked or hidden; merged into an `__Annotations__` of the
  model's own, a sublayer there taking the objects; an old layer that is the current layer left alone), the Place
  buttons in the Project Base Point and Survey Point headers, the Skunk toolbar shown only when it was lost or
  showing (a toolbar docked elsewhere keeps its place through an update), the project location (the Rhino model's location
  read from its Earth anchor point and shown in the panel, not set and set; a first site taking Rhino's north;
  Rhino's north and the Sun's following true north while they agree, without moving the location, and staying when
  set apart; both matching actions; a true north turned on its named CPlane; latitude and longitude of points;
  the refresh after a change made in Rhino; a location set on Rhino's Sun reaching the anchor point; a model
  without a location staying without one), the Skunk Attributes panel as Rhino needs it (its fixed
  id, a constructor Rhino can call, Rhino's panel callbacks, its light and dark tab icons at 16 to 64 px), the annotation scale on Skunk's styles (a feet model
  like Rhino's templates holds a grid bubble's 3/16" text as 0.1875 page inches at model space scale 8, 1'-6" tall in
  model views; the document's own text scale changes nothing; 1/4" = 1'-0" chosen; millimetre and metre models;
  Rhino's scaling off, or on only in model space; an older model's styles put right when it opens; every Skunk
  style of a model), the
  Grid Bubbles diagram (the bubbles it shows are the ones the command sets, for five side choices and three turns of
  the view; grids standing in an elevation; the sample for a view with no grids), grid bubbles by the sides of a view (plan sides, a
  view turned a quarter, skewed grids, elevation bubbles of a grid's own, layout details with sides of their own and
  without, ShowAll, a bubble switched on later hidden in elevations at once), Start Skunk with Rhino (an in-memory startup list:
  added once after the user's own commands, removed alone), grid
  names (a name typed into a bubble, the grids after it following on in their own direction, names in between,
  swaps that keep names unique, renumbering in a picked order or along a direction, copies named in turn, a
  bubble formatted by hand made again, 1.6.0 bubbles tidied), grids (names in series, bubbles that are one
  text with a white circular mask, centred on their ends and drawn over the line, bubble drags, renames, copies, deletes, plan cuts that never clip grids,
  datum boxes setting grid ends, unit changes), grids in elevations (lines from the lowest level to the
  highest, bubbles facing out, Elevation Bubbles, a real layout with plan, elevation and 3D details and the
  Hide in Detail overrides that follow them, a detail turned round), datum boxes (rotate, stretch, move, height
  fit, new levels joining, a head dragged sideways or a footprint edited taking its level out, joining
  again with heads on the same sides, copies, deletion, a box made into a circle, unit changes), level lines (one per elevation
  direction, a no-print footprint, moves, deletes, copies, patterns), spot slopes on lines and
  polyline segments, filled level heads by default, print size (level heads at three
  scales, Skunk Datum in four unit systems, repairs of older models, annotation scale changes made
  without an edit, the level heads' style following them, the linetype display scale), unit formatting, shared coordinates
  and true north, clipped and unclipped survey points, levels (heads, CPlanes, attached objects, head
  drags, renames), spots following moved and rotated breps and terrain meshes, copies, deletions,
  relative spots, top and bottom elevations, slopes (end grips, moving the arrow, a ruled surface, arrows from older versions), units following
  Rhino (display, page units, types, unit changes with and without scaling), the Rhino-dimensions
  sublayer (on by default), importing shared coordinates (three placements, either transform direction,
  disagreements, metric models), keyboard aliases (defaults, overrides, conflicts, swaps - on an alias list
  in memory, never Rhino's), north arrows, copies made together with their host or level, type edits. It also compiles all
  29 command scripts against the engine.
- Rhino parses `Skunk.rui` (one group, one toolbar) and, saving it again, keeps every button and
  separator in place. Rhino's own project API loads `Skunk.rhproj` and
  the Script Editor package build produces the `.yak`.
- The compiled plug-in (11 checks, by reflection, without registering it in Rhino): same id and version
  as the project, loads at start-up, all 29 commands with the project's command ids, icon, panel icons, the
  toolbar and the About logo embedded.
- The coordinate snippet runs, as written, against a stand-in for the BIM model's API
  (`src/tests/snippet_standin.py`); `SkunkCheck site <file>` reads what it printed and applies it.
- **Not automated:** dialogs, command-line prompts and the live event wiring inside an interactive
  Rhino, and loading either build into Rhino itself. Rhino doesn't raise object events for headless
  documents, so the tests drive the same processing directly. Install one build and try each command
  once.
