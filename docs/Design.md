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
Properties palette listens to Rhino's own events and does not depend on which copy is active.

**Two channels.** A plug-in built by the Script Editor uses the default load time ("when needed"), so
its event service starts with the first Skunk command of a session (or with the `Skunk` startup
command). `src/Skunk` compiles the same sources into `Skunk.rhp` with `PlugInLoadTime.AtStartup`; its
`OnLoad` starts the service on Rhino's first idle, so elements are live from start-up. Its command
classes are generated from `tools/commands.json` with the rhproj's command ids, and it shares the
rhproj's plug-in id: Rhino refuses to load a second plug-in with the same id, so the two channels
cannot both be active (and cannot both define the same command names). Both compile against
RhinoCommon 9.0.26300 from NuGet, the oldest Rhino 9 they load in; the version comes from the
rhproj through the generated `src/Version.props`.

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

**Angle convention** (East / West of project north): positive
means true north is clockwise (east) of project north. The Properties palette also shows the same
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
| Level | closed planar polyline (plan extents) on `Skunk::Levels` | Seen edge-on in elevations it is the level line. The object name is the level name. Linetype from the level's own Line Pattern, else the type's, else the layer's (Skunk Datum); the type's pattern scale is the object's linetype scale. |
| Level head | leader in the vertical plane of a footprint edge, facing out | Target / triangle symbol block as the arrowhead. Text is the name plus the elevation. One per edge end (End 1 = left, End 2 = right); Heads At sets the end on every side at once. |
| Spot elevation / coordinate | leader | Symbol block as the arrowhead at the measured point, text at the leader end, optional shoulder (landing). Drawn in the view plane it was placed in. |
| Spot slope | aligned linear dimension | Text is the slope between the arrow's ends; one arrowhead downhill; zero-length extension lines, so the two definition points are the arrow's ends (its grips). The arrow runs through its location along the fall line there, reaching *Top* uphill and *Bottom* downhill (instance values, default half the type's arrow length); each end sits on the host straight below / above the line's end, and the value is rise over plan run between them. Placement On Host (default): the arrow is drawn between those ends, a little above the face (15% of the text height) so it doesn't z-fight; on an edge, along the edge. Placement View Plane: flat in the CPlane it was placed in. |
| North arrow | block instance `Skunk North Arrow` | Rotated to true or project north, sized by the annotation scale. |
| Plan cut | clipping plane at level + cut height, depth below the level | One per viewport (Go To Level > ViewRange). |
| PBP / survey point | point objects on `Skunk::Site` (no-print) | Screen-size site point glyphs drawn by a display conduit, never printed. |

Each type (Type Properties) has its own dimension style `Skunk <Category> - <Type>`. Sizes
are stored as paper millimetres and shown in Rhino's page units (fractional or decimal inches, or
millimetres, following the page display).

With Rhino's model space annotation scaling on, dimension style lengths are paper lengths and Rhino
scales them per viewport and detail. Otherwise Skunk multiplies them by its own annotation scale.
Geometry Skunk draws itself (head leader length, slope arrow length, north arrow size) always uses the
annotation scale.

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
the index, the palette). The undo record already holds a consistent state of the objects.

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

### Rhino dimensions on a Skunk sublayer

With the setting on (the default; settings saved by older versions switch to it once), the
processing step moves objects added in the operation that are Rhino
dimensions (`Dimension`: linear, aligned, angular, radial, ordinate, centermark) or leaders, and not
Skunk elements, onto `Skunk::<name>` - inside the command's undo record, like every Skunk update.

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

- Levels are horizontal polylines with real extents, not infinite planes. They show in plan views
  too; hide the `Skunk::Levels` layer per detail, or use Go To Level's plan cut.
- The spot symbol sits at the measured point, not at the leader shoulder.
- Spot slope "Triangle" representation (elevation and section views only) is not implemented.
  The arrow lies on the sloped face, which reads in 3D views and in elevations as well as in plan.
- Skunk adds Internal Origin and Named CPlane to the usual PBP / Survey Point / Relative options.
- Spot elevation "Relative" with "Current Level" (the view's level) does not exist in Rhino. Pick the
  relative base level instead.
- Annotation is model-space and shared by all views, not drawn per view. Rhino's annotation
  scaling keeps text right in every detail; Skunk's own geometric offsets follow one annotation scale.

## 6. Tests

`src/tests/SkunkCheck` is a headless Rhino 9 console:
- default: 264 checks (see README)
- `rui <file>`: Rhino parses the toolbar, and the `Skunk` command finds its group by id
- `site <file>`: reads a pasted console output (for example from `src/tests/snippet_standin.py`, which
  runs the coordinate snippet as written against a stand-in for the BIM API) and applies it
- `rhproj <file> package`: Rhino's project API loads the project and runs the Script Editor package
  build; needs the .NET 10 runtime via `DOTNET_ROLL_FORWARD=LatestMajor`.
- `plugin dist\Skunk.rhp ScriptEditor\Skunk.rhproj`: the compiled plug-in by reflection only (never
  loaded as a Rhino plug-in, so nothing is registered): id, version, start-up load time, the 24
  commands and their ids against the rhproj, embedded icon, toolbar file alongside.

Rhino raises no object events for headless documents, so the tests call `SkunkService.Touch` +
`ProcessNow`, the same processing the events drive. The live wiring, dialogs and prompts need an
interactive Rhino.

The RhinoCode runtime probe (running a script that references the shared DLL) ends silently in
headless mode, as the FileIO notes also report, so scripts are proven by compiling their bodies
against the engine (`ScriptsCheck.g.cs`) and, in the end, by Build & Load.
