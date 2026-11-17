# Skunk design notes

How Skunk models project coordinate systems and spot / level annotations in Rhino 9, and why it
is built the way it is.

## 1. Shape of the project

The command list lives in the Script Editor project (`ScriptEditor/Skunk.rhproj`). Each command is a
one-line C# script calling `Skunk.SkunkEntry.<Command>(doc, mode, command)` in
`shared/Skunk.Core.dll`. The DLL is a project resource, so Rhino adds it as a compile reference for
every script and ships it in the package's `shared/` folder.

This is the same shape as the FileIO project, and it is the only shape that works for Skunk.
Elements must be updated by **one** event service that outlives every command, and every command
must see the same types. Separate scripts each holding their own copy of the classes would each
subscribe to document events.

If two copies of `Skunk.Core.dll` load (for example one from the Script Editor and one from the
installed package), `SkunkService.Ensure()` hands over: it finds the running service through
`AppDomain` data and shuts it down, so only one service ever reacts to document events. The
Skunk Attributes panel listens to Rhino's own events and does not depend on which copy is active.

**The Skunk Attributes panel.** `SkunkAttributesPanel` is a Rhino panel (an Eto `Panel` with a fixed
`GuidAttribute`, `766a1bbd-ba43-5642-9313-4be87be9acaf`, implementing `Rhino.UI.IPanel`, `PanelType.System`: one
panel reused for every model), so it docks, tabs and floats like Rhino's own and Rhino keeps where it was.
`SkunkAttributesPanel.Register` calls `Panels.RegisterPanel(plugIn, type, "Skunk Attributes", assembly,
iconResourceId, PanelType.System)` once a session: the compiled plug-in from its `OnLoad` (passing itself), the
Script Editor channel with the first Skunk command (`PlugIn.Find` with Skunk's id gives the generated plug-in; the
`_Skunk` startup command makes that happen at start-up). Its tab icon is the SkProperties button icon,
rendered by `tools/gen_project.py` (`panel_icons`) into multi-size `.ico` files in `src/Skunk.Core/Resources`
and embedded in Skunk.Core; the dark one when Rhino runs dark. Rhino's events are hooked once a session and
routed to the panel Rhino made last; the panel reloads on idle while shown (`PanelShown` / `PanelHidden`),
never under the user's cursor while they type. `SkProperties` opens it (`Panels.OpenPanel`) or closes it when
its tab is in front.

**Two channels.** A plug-in built by the Script Editor uses the default load time ("when needed"), so
its event service starts with the first Skunk command of a session (or with the `Skunk` startup
command). `src/Skunk` compiles the same sources into `Skunk.rhp` with `PlugInLoadTime.AtStartup`; its
`OnLoad` starts the service on Rhino's first idle, so elements are live from start-up. Its command
classes are generated from `tools/commands.json` with the rhproj's command ids, and it shares the
rhproj's plug-in id: Rhino refuses to load a second plug-in with the same id, so the two channels
cannot both be active (and cannot both define the same command names). Both compile against
RhinoCommon 9.0.26300 from NuGet, the oldest Rhino 9 they load in; the version comes from the
rhproj through the generated `src/Version.props`.

**The toolbar file.** Rhino 9 opens the toolbar files named in its window layout
(`settings\Scheme__Default\containers.xml`, `<files>`) at start-up, before it builds the toolbar containers, and
drops a path that is gone. A plug-in's own toolbar file - the `.rui` beside the plug-in, listed under
`<plug_in_files>` - opens only when the plug-in loads, after the containers are built. History: up to 1.6.2 the
toolbar opened from the version folder vanished with every update (Yak deletes the old version folder); 1.6.3
kept a copy in the package folder (`packages\9.0\Skunk\Skunk.rui`, beside Yak's `manifest.txt`); but every
package still carried its own `Skunk.rui`, and the Script Editor's packaging gave both copies one file id
(`d4a72693`), so when Skunk loaded, Rhino took the package's file in place of the copy, `CheckOnce` closed it -
taking the toolbar out of the user's own container (they dock it as a tab in "VRay All") - and opened the copy,
which Rhino showed floating, at every start. Since 1.6.6 no package carries a toolbar file (gen_project drops
the rhproj's `ruiPath`; the compiled channel packs none): `ScriptEditor/Skunk.rui` is embedded in the engine
(`Skunk.Core.Resources.Skunk.rui`, also in Skunk.csproj) and `SkunkToolbar.Ensure` writes it to the package
folder (beside the plug-in in a development build) when it is missing or its buttons changed - judged by the
sorted macros and texts (`Buttons`, hashed into `Skunk.rui.version`), so Rhino's own re-save of the file does not
count - keeping the file id Rhino's layout knows (`KeepFileId`). It closes any other toolbar file holding the
Skunk group (an older package's own), opens its file when no file holds the group, and clears the plug-in's
registry `RuiFile`, so Skunk has no plug-in toolbar for Rhino to open late. The file then sits in `<files>` like
the user's own toolbar files, and a toolbar docked anywhere stays there.

**Never a second toolbar.** RhinoCommon tells whether the Skunk group's own container shows (`ToolbarGroup.Visible`),
not whether its toolbar is a tab in another container - the user docked it into a toolbar of their own, and the
`_Skunk` startup command, which set `Visible`, put up a second, floating "Skunk 01" at every start. So a toolbar
file that is loaded is left alone: `SkunkToolbar.Load` (the `Skunk` command) opens and shows the file only when no
loaded file holds the group, and `Ensure` reopens a toolbar for new buttons showing its own container only if it
was showing (`ShowAfterOpen`); a toolbar that was lost is opened and shown.

**Layers.** Skunk's layers sit under one top-level layer, `__Annotations__` (`Keys.RootLayer`; `Skunk` before).
`Layers.Migrate` renames an old `Skunk` layer - or, when the model has an `__Annotations__` of its own, moves the
sublayers in (one of the same name there takes their objects) and deletes the emptied old layers (never the
current layer) - from `Layers.Ensure` and from the service's full update when a model opens. Changing a layer makes
Rhino apply its sublayers' remembered (persistent) visibility and locking again, so `Migrate` notes each
sublayer's visibility and locking first and puts them back. `Layer.CommitChanges` is obsolete in Rhino 9: a
model's layers change as their properties are set. `Layers.Find` reads a sublayer under either name.

**Place buttons.** The Skunk Attributes panel's Project Base Point and Survey Point headers hold a *Place* button
(`Param.GroupButton`, drawn by `ParamGrid` in the header row's value column) that runs `SkProjectBasePoint` /
`SkSurveyPoint` the way a toolbar button would (`EtoKit.RunCommand`: "!" ends a running command, else the command
starts on the next idle).

## 2. Coordinate systems

| Skunk | Meaning | Stored as |
|---|---|---|
| Internal origin | Internal origin (fixed) | Rhino world origin |
| Project base point + project north | PBP, Project North = its Y axis | named CPlane `Skunk: Project Base Point` |
| Survey point + **true north** | Survey point, True North = its Y axis | named CPlane `Skunk: Survey Point (True North)` + the survey point's shared N/E/Elev |
| Shared coordinates | Shared (survey) coordinates | derived: `N = SpN + (P-SP)·TN`, `E = SpE + (P-SP)·TE`, `Elev = SpZ + (P-SP)·Z` |
| Any named CPlane | a work plane of your own | Rhino's named CPlane table |

The two named CPlanes are the source of truth. Editing them in Rhino's Named CPlanes panel moves
Skunk's markers and re-labels every spot: there is no change event for named CPlanes, so the service
compares a signature twice a second while idle. Moving a marker point updates its CPlane. Which named
CPlanes play the two roles is set in the Location dialog, so an existing site CPlane can define true
north.

**Project location.** The geographic location is the Rhino model's own: `doc.EarthAnchorPoint` (latitude,
longitude, elevation in meters, name, description, the model base point and model north), read by
`Site/GeoLocation`; Skunk stores no copy. In Rhino 9 the Sun and the anchor point share it: a Sun change (its
latitude, longitude or north, written with `BeginChange` / `EndChange`) is written to the anchor point, while an
anchor point change does not reach a Sun already loaded. So Skunk reads the anchor point, and when it turns
Rhino's north (`GeoLocation.SetRhinoNorth`) it writes the Sun (with the anchor point's latitude and longitude, so
its write never moves the location, and a location that is not set stays not set) and then the anchor point.
*Set in Rhino* (Location tab) closes Skunk's dialog and, once no command runs, opens Rhino's document location
- `_-DocumentPropertiesPage _Location`, Document Properties > Location with its world map, the macro behind
"Manage Document Location" on Grasshopper's Query Model Location component - then `_SkLocation` again (the
`EarthAnchorPoint` command is a command-line one; the user expected the map). `EarthAnchorPoint` hands out a copy (set it back); `EarthLocationIsSet()` is false until latitude and longitude
are set. Rhino's north follows true north while the two agree: `SiteModel.Save` compares Rhino's north with the
true north as last saved (`SiteData.SpPlane`) and moves it along when they matched (`GeoLocation.FollowTrueNorth`);
`SiteSync` saves a site plane edited on its named CPlane so that path follows too, and `PostUndoSync` takes Rhino's
north back with an undo (the service remembers the true north of its last processing). A north set apart in
Rhino stays apart; the panel shows both and the Location dialog matches them either way. A model without a
Skunk site starts with true north along Rhino's north (`SiteModel.StartSurvey`). Latitude and longitude of
points come from `GetModelToEarthTransform`, whose result is x = longitude, y = latitude in Rhino 9.2 (its
documentation says the reverse): `GeoLocation.TryLatLong` reads the order off the anchor point. The Sun panel
raises no event Skunk sees, so the service's idle check (twice a second) compares a signature of the location
and refreshes the panel.

**Angle convention** (East / West of project north): positive
means true north is clockwise (east) of project north. The Skunk Attributes panel also shows the same
angle counter-clockwise, `(-θ) mod 360`, as Angle to True North (CCW).

Behaviour:
- Moving the PBP moves only the PBP; the model and the shared system stay.
- A clipped survey point carries the shared system; an unclipped one changes only its own values.
  "Survey Point" measurements always use the shared origin.
- Rotate True North turns the shared system about the PBP or the survey point; the pivot keeps its
  shared coordinates. Typing the PBP angle does the same with the PBP as pivot.
- Rotate Project North rotates the model about the internal origin (or the PBP). True north turns
  with the model, so shared coordinates stay the same.
- Spot coordinates from the PBP use project-north axes; from the survey point they use true-north
  axes.

## 3. Elements

| Element | Rhino object | Notes |
|---|---|---|
| Level | closed planar polyline (plan extents) on `__Annotations__::Levels` | Shows in model views but does not print (print width No Print): seen edge-on, its front and back edges coincide with their dash patterns running opposite ways and print almost solid. The object name is the level name. Linetype from the level's own Line Pattern, else the type's, else the layer's (Skunk Datum); the type's pattern scale is the object's linetype scale. |
| Level line | line on `__Annotations__::Levels`, owned by the level (`LevelData.Lines`, `HeadData` with End 0) | What prints as the level line: one per elevation direction that has heads (edges parallel within about a degree are one direction), spanning those head edges along the first of them and in its direction, so the pattern always starts at the same end. Same linetype as the level. Dragged up or down it moves the level; otherwise it snaps back; deleted it comes back; a copy is removed. |
| Datum box | closed planar polyline (plan outline) on `__Annotations__::Datum Boxes`, just under its lowest level | Holds the extents of the levels in it (`LevelData.Box`): each takes the outline at its own elevation. No Print. The rest of the box (vertical edges and top, just over its highest level, `DatumBoxData.Top`) is drawn dotted by `DatumBoxConduit` in model views and details, never on a layout page or in print. Moved, scaled, rotated or grip-edited, its levels follow; moved up or down, it goes back under its levels; anything but a closed polyline goes back to its outline (`DatumBoxData.Points`). A level leaves the box when its own plan outline changes against the one it recorded (a head dragged sideways, `LevelElement.DragEnd`; its footprint edited or moved in plan) and joins again with `DatumBoxElement.Attach`, its heads following their sides (`LevelElement.MapHeads`). Processed before the levels; every box re-fits its height after them. |
| Level head | leader in the vertical plane of a footprint edge, facing out | Target / triangle symbol block as the arrowhead at the line end; the tail is the leader's landing (Head Length, at least the symbol's radius) and the leader's own segment a stub of a few tolerances, so symbol, tail, gap and text are all style lengths and print at the type size at any scale. Text is the name plus the elevation. One per edge end (End 1 = left, End 2 = right); Heads At sets the end on every side at once. Dragged mostly up or down it moves the level; dragged mostly sideways it moves that end of the level (the next side out or in, the head's own side across) and takes the level out of its datum box. |
| Spot elevation / coordinate | leader | Symbol block as the arrowhead at the measured point, text at the leader end, optional shoulder (landing). Drawn in the view plane it was placed in. |
| Spot slope | aligned linear dimension | Text is the slope between the arrow's ends; one arrowhead downhill; zero-length extension lines, so the two definition points are the arrow's ends (its grips). The arrow runs through its location along the fall line there, reaching *Top* uphill and *Bottom* downhill (instance values, default half the type's arrow length); each end sits on the host straight below / above the line's end, and the value is rise over plan run between them. Placement On Host (default): the arrow is drawn between those ends, a little above the face (15% of the text height) so it doesn't z-fight; on a curve host (line, polyline, polycurve), along the kink-free piece under the spot (`HostSample.Segment`): its ends stay on that piece (`Anchors.SlopeEnd`), so it reads that segment's slope and never bends round a corner; moving the arrow re-anchors it anywhere on the curve. Placement View Plane: flat in the CPlane it was placed in. Placed in a side view - a CPlane that is not level, more than about 8° off (`SpotElement.PlacedInSideView`) - it is flat in that CPlane whatever the type's placement (`FollowsHost`): on the host it would lie in a plane the side view sees edge-on (a profile drawn in the elevation contains the line of sight). There it sits off its host line by its offset, measured up (`SlopeArrow.Side`, `SpotData.SlopeOffsetMM` in paper mm, else the type's Offset From Model): moved as a whole it re-anchors to the closest host point and keeps its distance from the line as the offset, so it can be moved up or down off the line, and its plane's Y points away from the line (text above the dimension line, drawn forward) so the text is on the far side. A plane turned with its points in place counts as a change (`SpotElement.Turned`), or a move across the line would leave the text on the old side. |
| North arrow | block instance `Skunk North Arrow` | Rotated to true or project north, sized by the annotation scale. |
| Grid | line on `__Annotations__::Grids` from End 1 to End 2, horizontal at End 1's height | Linetype from the type's Line Pattern, else the layer's (Skunk Datum). Named in series (`GridElement.Increment`: a trailing number, else one or two trailing letters, count up), unique; the object name is the grid name. A rename (typed into a bubble, the Skunk Attributes panel's Name, the object name) also renames the parallel grids after it (`RenameFollowing`; `Following`: every pair of parallel grids in one series votes on the direction the names run, else left to right / top to bottom in the project plan), unless the new name is in between (`InBetween`: the grid before's name plus a separator, or a letter after its number, a number after its letter). `Assign` keeps names unique: a grid that has a wanted name takes one given up (a swap); all names are written before any grid updates. `Renumber` (SkRenumberGrids) names grids in a given order; `AlongDirection` orders them by where a drawn direction crosses them. Copies update last, nearest their original first (`UpdateOrder`), so an array is named in turn. In a datum box (`GridData.Box`) its ends sit on the box's outline (`GridElement.Clip`: first and last crossing of the grid's line); moved or turned it stays in and is clipped again; one end moved along the grid (`EndDraggedAlong`, against `GridData.Points`) or a bubble dragged along it takes it out. |
| Grid head | per bubble one text on `__Annotations__::Grid Heads` (key "1t" / "2t"), owned by the grid (`GridHeadData`, Label true), Middle Center at the end point; the grid style draws its mask as a white circle with an outline (`MaskFrame.CircleFrame`: radius = text height / 2 + `MaskOffset`, an absolute style length, so the offset is (bubble - text) / 2) | The circle is the text's own mask, drawn just under the name: it hides the grid line (display order 1 over the line's 0) and can never cover the name, selected, edited in place or in any display mode. Rhino draws that circle with the text object's linetype, and an elevation bubble sits on the Grid Elevations layer (dashed Skunk Datum), so every bubble carries Continuous itself (`GridElement.Solid`): the circle is solid, the datum line dashed. Bubble size and text are style lengths, so they scale together in every view and detail. A name typed into it renames the grid; a bubble whose text, rich text or height differs from the grid's is made again. Dragged along the grid it moves that end; otherwise it snaps back; deleted, that end's bubble is switched off. 1.6.0 drew the circle as a leader with a white-filled arrowhead block (keys "1", "2", "e1u", "e1d"), which could cover the name: the next update deletes those leaders, and the Skunk Grid Bubble block once nothing places it. |
| Grid elevation part | per grid end, beyond it by one bubble diameter at the model's annotation scale, on `__Annotations__::Grid Elevations`, owned by the grid (`GridHeadData.Elevation`): a vertical line, and bubbles (the same one text) at its top and / or bottom in the vertical plane across the grid, facing out | From just under the lowest level to just over the highest (`GridElement.Heights`), a datum box's bottom and top for a grid in a box; every grid is refreshed when a level or box changes. The type's Elevation Bubbles: Top, Bottom, Both, None. Moved they snap back; deleted they come back; copies are removed. Plan cuts clip them (the layer is not in the cuts' exclusion list). |
| Layout details | `GridViews`: Rhino's Hide in Detail overrides (`ObjectAttributes.AddHideInDetailOverride`, keyed by the detail's viewport id) on every grid part; a detail's own bubble sides in its user text `Skunk:GridBubbles` ("Top, Left"; `GridBubbleSides`) | A detail looking within 30 degrees of down shows plan parts only; a parallel detail within 10 degrees of level shows only elevation parts it looks at (its direction within 15 degrees of the part's outward direction, reversed); any other detail shows plan parts. A detail with sides shows a plan bubble only when its end points to one of them on the detail's screen (`SideOf`: the end's outward direction against the camera's right and up; up / down for a grid that runs more up the screen than across), and an elevation bubble only at a kept top or bottom (`ShowsPart`). Re-applied for grids processed in a batch, for a grid whose update made a part, and for every grid when `SkunkService.CheckGridViews` (idle, twice a second, models with grids) sees a detail added, removed, turned or given sides (the signature holds direction, up and sides). |
| Grid bubbles by view | `SkGridBubbles` (`DatumCommands.GridBubbles`) | Interactive: `GridBubblesDialog` - Top / Bottom / Left / Right boxes around a `GridBubbleDiagram` of the scope grids on the view's right and up (`GridViews.SideOf`, the setters' own rule, so it shows what they will do; looking level the grids stand up with top and bottom bubbles; no grids, a sample). *Select grids...* closes it for a pick and opens it again; *Show every bubble* clears a detail's sides. `-SkGridBubbles` (scripted) keeps the command-line options. Model view: `GridElement.SetBubbleSides` sets End 1 / End 2 from the view's right and up, or, looking level (view direction within 60 degrees of level), `SetElevationSides` sets the grids' own `GridData.ElevationBubbles` (null = the type's). Layout detail (the active one, else every detail on the layout): `GridViews.SetSides` on the detail, the same setters adding only (no bubble is removed: other views may show it), then `GridViews.Apply`. |
| Plan cut | clipping plane at level + cut height, depth below the level | One per viewport (Go To Level's View range: `GoToLevelDialog`, or `-SkGoToLevel ViewRange`). Leaves the grid layers unclipped (`ClippingPlaneSurface.SetClipParticipation`, an exclusion list), so grids show on every floor; `PlanCutElement.UpdateAll` runs when a grid is made. |
| PBP / survey point | point objects on `__Annotations__::Site` (no-print) | Screen-size site point glyphs drawn by a display conduit, never printed. |

Each type (Type Properties) has its own dimension style `Skunk <Category> - <Type>`. Sizes
are stored as paper millimetres and shown in Rhino's page units (fractional or decimal inches, or
millimetres, following the page display).

With Rhino's model space annotation scaling on, dimension style lengths are paper lengths and Rhino
scales them per viewport and detail. Otherwise Skunk multiplies them by its own annotation scale.
The annotation scale is Skunk's own (`SkunkSettings.AnnotationScale`, SkSettings; 1:96 in imperial units
and 1:100 in metric to begin with, `DocUnits.DefaultViewScale`). How Rhino draws a style, measured with
`AnnotationBase.GetDimensionScale(doc, style, viewport)` (`SkunkCheck docinfo` lists a model's styles):
- a model view multiplies the style's lengths by **the style's own `DimensionScale`** (its model space
  scale; an annotation object can override it, Skunk's do not) and reads the result as model units;
- a layout detail (with layout space scaling on) reads the style's lengths as **page units** and draws them
  in the model at the detail's model length per page unit, whatever the style's `DimensionScale` (8 in a
  1/8" = 1'-0" detail of a feet model with inch pages, 4 at 1/4", 16 at 1/16"), so they print at that size.
Rhino's own styles follow that: their sizes are paper sizes in page units (0.125" text in both the feet
and the inch template, 3 mm in the millimetre and the metre one) and their model space scale is the view
scale times one page unit in model units (Meters Small 0.1 for 1:100 with millimetre pages).
`RhinoDoc.ModelSpaceTextScale` is a separate document value, 1 in every template, that Rhino does not draw
them with. So while Rhino scales annotation (`DocUnits.RhinoScalesAnnotation`: its model space *and*
layout space scaling on) `DocUnits.PaperToStyle` gives page units and `Styles.Configure` sets
`DimensionScale` to `DocUnits.StyleModelSpaceScale` (the annotation scale times `PageUnitInModel`);
otherwise Skunk puts the scale in the lengths and leaves `DimensionScale` at 1. Skunk neither reads nor
sets the document value. History: 1.6.3 set `ModelSpaceTextScale` and left the styles at 1 (bubbles 1/2"
across in a feet model's views); the first 1.6.4 build put the annotation scale on `DimensionScale` but kept
the lengths in model units (model views right, layout details printed them a twelfth of their size with
inch pages and feet: 3/16" text as 1/64"). Headless Rhino resolves a detail's scale only in the first model
with layouts, so `SkunkCheck print` measures prints in a run of its own.
Anything that has to print at a fixed size is a style length: a level head is entirely one (symbol,
landing as the tail, gap, text). Geometry Skunk draws itself is model geometry: spot leaders and the
slope arrow (it measures the slope), and the north arrow block, sized with the annotation scale.

Skunk Datum is a paper pattern (19.05 / 3.175 / 3.175 / 3.175 mm). Rhino keeps linetype patterns in
paper millimetres whatever the model units and prints them at that size in every detail; model views
use the document's linetype display scale, which `LinetypeDisplay` keeps at the annotation scale while
it is Rhino's default or the value Skunk set. `LinetypeTable.FindName` never returns null (a missing
name answers the Continuous linetype, index -1), so Skunk looks linetypes up with `Layers.FindLinetype`;
up to 1.4.1 Skunk Datum was therefore never added and the Levels layer was left Continuous, and an
older Skunk Datum held model units times the scale. `Styles.RepairDatum` fixes both once per model.
Neither Rhino's scaling switch (Document Properties) nor Skunk's own scale changing without an edit (an
Undo of SkSettings) raises an event: the idle loop compares both twice a second and refreshes every
element when one moved.

### Hosting

`AnchorData` stores the host id and one of:
- brep face index plus (u, v) normalised to the face domain (extrusions and SubDs are evaluated as
  breps)
- mesh face index plus barycentric weights (face normal computed from the vertices)
- normalised curve parameter
- point

Transforms keep parameters, so a moved, rotated or scaled host is followed exactly, as are
control-point edits. When the stored parameters no longer fit (the topology changed), the spot
re-attaches at the closest point to its last position.

User edits are read back before rebuilding:
- **The whole spot moved** (rigid move or copy): it slides along the host. The tip snaps to the
  closest host point and the text keeps its offset.
- **A grip moved**: the text stays where the user put it, and the tip re-attaches.
- **Spot slope arrows** record their grip points (both definition points, the arrow's ends on the
  dimension line, the dimension line point). One definition point moved: that end's reach is read
  along the arrow line in plan, after taking off the step the arrow is drawn above / beside the
  host, so dropping a grip on its own end changes nothing even on a warped surface; the sideways part
  of a drag is ignored. Both moved, or only the dimension line (dragged sideways), or a copy: the
  arrow's location becomes the matching point of the arrow as it is now, dropped vertically onto the
  host, and direction and value are measured again there. Arrows from Skunk 1.1 and older recorded
  three plane points; that different count is read as "not an edit", and they are simply rebuilt.
- **A copy** (a different object id than the stored `Id`) becomes a new element. It stays on the
  same host, or moves to a host copied in the same operation that it touches (copying a floor
  together with its spots gives the copy its own spots).
- **Deleting the host** deletes its spots (a setting), inside the same undo record.
  Spots without a host measure their own point.

Levels:
- **Moving a whole level** changes its elevation. It moves attached objects, updates relative spots,
  its CPlane, its heads and its plan cuts.
- **Moving a single corner vertically** is rejected (the majority Z wins); plan edits of the extents
  are kept.
- **Moving a head vertically** moves the level. A horizontal head move snaps back.
- **Deleting a head** turns that head off (heads are hidden, not deleted).
- **Copying a level** gives it the next free name and new heads. Objects attached to it and spots
  relative to it, copied in the same operation, follow the copy.

## 4. Event service

```
document events  ->  record ids (changed / added / deleted with their kind + data)
                     ignored while Skunk edits (Suppress) and during Undo / Redo
Command.UndoRedo  "before end recording"  ->  process now, inside the command's undo record
RhinoApp.Idle     (no command running)     ->  process in a "Skunk Update" undo record
```

Processing order: site markers and CPlanes, then level deletions, head edits and level updates
(attached objects move), then spots (self-edited, host changed, host moved by a level, relative to a
changed level, or everything on a context change), then north arrows and plan cuts.

A headless test confirmed that changes made while a command's undo record is ending (replacing a
dependent, adding an object) land in that record: one Undo restored the host and its dependents and
removed the added object.

After Undo / Redo the service only refreshes side state that is not in undo records (level CPlanes,
the index, the panel). The undo record already holds a consistent state of the objects.

### Units

Lengths follow Rhino's *Document Properties > Units*. The length format *Rhino document units*
(`LengthFormat.Project`) resolves per document (`TypeTable.Resolve` -> `DocUnits.ModelLengthStyle`)
to the unit system, the distance display (decimal / fractional / feet and inches) and the precision
(decimal places, or a 1/2^n fraction); the type keeps its own symbol, zero and sign settings. New
types use it; libraries saved before (no `UnitsVersion`) switch their stock fixed formats to it on
read and keep any format the user chose. Rhino's rarer units fall back to the nearest Skunk format.

A change in *Document Properties* raises `DocumentPropertiesChanged`, a context change: every element
is rebuilt and re-labelled. A unit change with scaling raises `UnitsChangedWithScaling` (before Rhino
scales the objects and the named CPlanes). Skunk records the factor and, at the next processing,
before it compares anything, scales every length it stores in model units (`UnitScaling.Apply`: spot
anchors' last points, leader offsets, slope reach, the recorded edit-detection points, level
elevations, north arrow sizes, the site planes and the survey point's shared values). Without that,
the scaled annotations would read as user edits and a level would move its attached objects a second
time. The model units are recorded in the settings (`MetersPerUnit`); a later difference means the
change happened while Skunk was not running, and the annotations' positions (recorded x factor, or
unchanged) tell whether Rhino scaled them. Headless documents raise neither event: the tests call
`SkunkService.UnitsScaled` and `ContextChanged`.

### Rhino dimensions on an __Annotations__ sublayer

With the setting on (the default; settings saved by older versions switch to it once), the
processing step moves objects added in the operation that are Rhino
dimensions (`Dimension`: linear, aligned, angular, radial, ordinate, centermark) or leaders, and not
Skunk elements, onto `__Annotations__::<name>` - inside the command's undo record, like every Skunk update.

### Importing shared coordinates

`Site/SharedCoordinatesImport.cs` holds the snippet, the reader and the solver. The snippet (single-quoted
Python that runs in IronPython 2.7 and CPython 3 consoles, blank lines after each block so it pastes into
a prompt) prints one `SKUNK-SITE` JSON line: both base points' internal positions and their own shared
values (east, north, elevation), and the model's total transform, all in decimal feet. Which way that
transform runs is not assumed: the direction that reproduces both points' shared values wins (the
documented shared-to-internal one on a tie), and what is left over is shown as a disagreement.

Where the Rhino model sits gives the map from the BIM model's internal coordinates to Rhino's world: a
scale to model units for the internal origin, plus a shift by the project base point, or plus the
internal-to-shared transform for a model exported in shared coordinates. Through that map the project
base point CPlane gets its origin and project north (the internal Y axis), the survey point CPlane its
origin and true north (the shared north axis), and the survey point keeps the model's shared values;
Skunk's own shared coordinates then match the model's everywhere.

## 5. Where Skunk differs from the usual BIM behaviour

- Grids show in elevations through their elevation parts, which only layout details hide by view; model
  views show every part. Elevation extents follow the levels (or the datum box), not a drag of the
  bubble. Grids are straight (no arcs or multi-segment grids) and have no elbows or center-segment gaps.
- Levels are horizontal polylines with real extents, not infinite planes. The footprint shows in
  plan views too but does not print; the level lines (one per elevation direction with heads) print,
  in plans as well: hide the `__Annotations__::Levels` layer per detail, or use Go To Level's plan cut.
- The spot symbol sits at the measured point, not at the leader shoulder.
- Spot slope "Triangle" representation (elevation and section views only) is not implemented.
  The arrow lies on the sloped face, which reads in 3D views and in elevations as well as in plan.
- Skunk adds Internal Origin and Named CPlane to the usual PBP / Survey Point / Relative options.
- Spot elevation "Relative" with "Current Level" (the view's level) does not exist in Rhino. Pick the
  relative base level instead.
- Annotation is model-space and shared by all views, not drawn per view. Rhino's annotation
  scaling keeps text, symbols and level heads right in every detail; spot leaders, slope arrows and
  the north arrow are model geometry and follow one annotation scale.

## 6. Tests

`src/tests/SkunkCheck` is a headless Rhino 9 console:
- default: 544 checks (see README)
- `print`: 26 checks of what layout details print (grid bubbles, level heads) at three detail scales in a
  feet and a millimetre model, measured with Rhino's own detail scale; a run of its own because headless
  Rhino finds a detail's scale only in the first model with layouts
- `docinfo <file.3dm> ...`: opens each model headless, read-only, and prints its units and annotation
  scale settings (Rhino's model space scaling and scale, Skunk's scale)
- `rui <file>`: Rhino parses the toolbar, and the `Skunk` command finds its group by id
- `site <file>`: reads a pasted console output (for example from `src/tests/snippet_standin.py`, which
  runs the coordinate snippet as written against a stand-in for the BIM API) and applies it
- `rhproj <file> package`: Rhino's project API loads the project and runs the Script Editor package
  build; needs the .NET 10 runtime via `DOTNET_ROLL_FORWARD=LatestMajor`.
- `plugin dist\Skunk.rhp ScriptEditor\Skunk.rhproj`: the compiled plug-in by reflection only (never
  loaded as a Rhino plug-in, so nothing is registered): id, version, start-up load time, the 24
  commands and their ids against the rhproj, embedded icon and the Skunk Attributes panel's tab icons, toolbar file alongside.

`src/tests/DiagramPreview` draws the SkGridBubbles diagram into PNG files with the dialog's own code
(`GridBubblesDialog.SavePicture`), using Rhino's Eto and its WPF platform outside Rhino: headless Rhino has
no Eto platform.

Rhino raises no object events for headless documents, so the tests call `SkunkService.Touch` +
`ProcessNow`, the same processing the events drive. The live wiring, dialogs and prompts need an
interactive Rhino.

The RhinoCode runtime probe (running a script that references the shared DLL) ends silently in
headless mode, as the FileIO notes also report, so scripts are proven by compiling their bodies
against the engine (`ScriptsCheck.g.cs`) and, in the end, by Build & Load.
