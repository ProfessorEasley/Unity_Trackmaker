# Track Layout Tool Demo

Unity editor: **6000.3.10f1**

This project demonstrates modular driving-track authoring in two modes:

1. **Edit mode:** a Unity `EditorWindow` places prefabs and saves project JSON assets.
2. **Play mode/runtime:** an in-game panel places and deletes prefabs, then saves timestamped JSON beside the player's application data.

The BEDRILL road and environment packs are already mapped through a stable-ID catalog. Saved JSON uses IDs such as `track_straight_short`, `track_curve_90`, `prop_finisharch`, and `prop_cone`, rather than depending on the original prefab filenames.

## Beginner use case 1: start with only `Ground`

Goal: begin with a scene whose Hierarchy contains only a ground plane, build during Unity Play mode through the familiar **Tools > Track Builder** popup, and save the result.

1. Choose **File > New Scene > Empty Scene**.
2. If a Ground object does not exist, choose **GameObject > 3D Object > Plane** and rename it `Ground`.
3. Delete any other scene objects if you truly want only `Ground` in the Hierarchy. The editor-popup workflow uses Scene view, so a Main Camera is not required.
4. Save the scene as something recognizable, such as `Assets/Scenes/ScratchTrack.unity`.
5. Select the **Scene** tab so you can see and navigate the ground.
6. Press Unity's **Play** button.
7. While Play mode is running, choose **Tools > Track Builder**. The same popup shown outside Play mode opens.
8. In **1. PREFAB CATALOG**, confirm that mappings such as `track_straight_short`, `track_curve_90`, `prop_finisharch`, and `prop_cone` are visible. You normally do not need to change this section.
9. In **2. PLACE PIECES**, use:
   - `Grid snap = 15` for clean BEDRILL track-module alignment;
   - a smaller value such as `1` or `0.5` for finer movement;
   - `Ground Y = 0` for a ground plane at world height zero;
   - `Rotation step = 90` for quarter turns.
10. Select **TRACK**, choose `track_straight_short`, and click **PLACE WITH MOUSE**.
11. Move the cursor in Scene view and left-click to place the first road section. Unity creates a `TRACK_LAYOUT` root automatically.
12. Continue placing straights. Press **Q/E** before clicking to rotate upcoming pieces. Press **Esc** when finished placing that piece type.
13. Select another track mapping, such as `track_curve_90`, and repeat.
14. Switch to **PROPS**, select a prop, and click **PLACE WITH MOUSE**. Unity creates a separate `TRACK_PROPS` root.
15. Add cones, an arch, barriers, flags, or other decorations.
16. In **3. SAVE / REBUILD**, change **Layout name** to a new name such as `scratch_track_01`.
17. Click **SAVE TRACK JSON**.
18. Click **SAVE PROP ADDENDUM**.
19. Confirm these files appear beneath `Assets/TrackLayoutTool/Layouts`:
   - `scratch_track_01.track.json`
   - `scratch_track_01.props.json`
20. Stop Play mode. The placed scene objects disappear because Unity discards ordinary Play-mode hierarchy changes. This is expected; the JSON files remain.
21. To prove reconstruction, press Play again, open **Tools > Track Builder**, click **LOAD TRACK JSON**, choose the track file, then click **LOAD PROP ADDENDUM** and choose its matching prop file.
22. To make the reconstructed layout permanent in a `.unity` scene, stop Play mode, load the same JSON pair from the popup while in Edit mode, then save the scene with **Ctrl+S**.

## Beginner use case 2: modify an existing example during Play mode

Goal: open the supplied example, add more track and props while the game is running, save the modified layout under a new name, and preserve the original.

1. Open `Assets/TrackLayoutTool/Demo/ImportedAssetWalkthrough.unity`.
2. Before changing anything, choose **File > Save As** and make a copy such as `ImportedAssetWalkthrough_MyVersion.unity`. This protects the supplied example.
3. Inspect the Hierarchy. The example already contains `TRACK_LAYOUT` and `TRACK_PROPS`, plus ordinary scene objects such as the camera, light, and ground.
4. Press Unity's **Play** button.
5. While Play mode is running, choose **Tools > Track Builder**.
6. Immediately change **Layout name** to something new, such as `example_modified_01`. This prevents accidentally overwriting an earlier JSON layout.
7. Select **TRACK** and choose the track piece you want to add.
8. Click **PLACE WITH MOUSE**, move over Scene view, rotate with **Q/E**, and left-click to place additional road sections.
9. To adjust an existing tool-managed object, select it in Scene view or the Hierarchy, then use **Snap selected transforms**, **Rotate selected left**, or **Rotate selected right**.
10. Switch to **PROPS** and place extra cones, barriers, arches, flags, grandstands, lights, or tires.
11. Check that new road objects are children of `TRACK_LAYOUT` and new decorations are children of `TRACK_PROPS`. The popup does this automatically; this check is useful when troubleshooting.
12. Click **SAVE TRACK JSON** and then **SAVE PROP ADDENDUM**.
13. Optional proof: click **CLEAR TRACK** and **CLEAR PROPS**, then load the newly saved JSON pair. The modified layout should return.
14. Stop Play mode. The scene returns to its pre-Play state, while `example_modified_01.track.json` and `example_modified_01.props.json` remain saved.
15. To bake the modified layout into your copied scene, stay out of Play mode, load those two files through **Tools > Track Builder**, and press **Ctrl+S**.

Important: the JSON pair saves only objects created or recognized by this tool through `TrackPieceIdentity` beneath `TRACK_LAYOUT` or `TRACK_PROPS`. Your camera, player car, lights, Ground, UI, scripts, and other ordinary scene objects remain part of the `.unity` scene itself; they are not duplicated into the track/prop JSON files.

## Fastest live presentation: runtime authoring

1. Open `Assets/TrackLayoutTool/Demo/ImportedAssetRuntimeDemo.unity`.
2. Press **Play**. The scene intentionally begins as a blank ground plane.
3. While Play mode is running, choose **Tools > Track Builder**. The exact same editor popup used outside Play mode opens; no scene-mode preparation is required.
4. Keep the **Scene** view visible. The popup places and edits objects in the running scene through Scene view, not through the Game view.
5. Leave **TRACK** selected, choose a road piece, and click **PLACE WITH MOUSE**.
6. Move over Scene view. The placement marker snaps to a 15-unit grid. Left-click to place and use **Q/E** to rotate.
7. Switch the popup to **PROPS** and place decorations the same way.
8. Click **SAVE TRACK JSON** and **SAVE PROP ADDENDUM** before stopping Play mode.
9. Play-mode scene objects disappear when Play stops, but the saved JSON assets remain and can be loaded again.

The separate in-game runtime panel in `ImportedAssetRuntimeDemo.unity` is still available as an optional prototype, but it is not required for this editor-popup workflow. Its **Snap to grid** toggle is off by default, so the preview follows the cursor continuously. Enable snapping when aligning track modules; choose a spacing from `0.5`, `1`, `5`, or `15`, or hold **Shift** to temporarily bypass snapping.

### Runtime controls

| Input | Action |
|---|---|
| Left click | Place the previewed piece |
| Right click | Delete the nearest placed piece |
| Q / E | Rotate placement by 90 degrees |
| Tab | Switch between track and prop catalogs |
| P | Toggle placement mode |
| Shift | Temporarily bypass grid snapping |
| WASD / arrow keys | Pan the camera |
| Mouse wheel | Zoom the camera |

### Runtime save location

Runtime saves are written to:

`Application.persistentDataPath/TrackBuilderSaves`

For this Windows project, that resolves to:

`%USERPROFILE%/AppData/LocalLow/DefaultCompany/TrackLayoutToolDemo/TrackBuilderSaves`

Each save produces a matched pair:

- `runtime_track_YYYYMMDD_HHMMSS_mmm.track.json`
- `runtime_track_YYYYMMDD_HHMMSS_mmm.props.json`

The track file stores track piece IDs and transforms. The prop addendum stores prop IDs and transforms plus the matching track filename.

## Edit-mode authoring presentation

1. Open `Assets/TrackLayoutTool/Demo/ImportedAssetWalkthrough.unity`.
2. Open **Tools > Track Builder**.
3. Explain that the catalog maps clean IDs to the imported BEDRILL prefabs.
4. Set **Grid snap** to `15`.
5. Choose **TRACK**, select a piece, and click **PLACE WITH MOUSE**.
6. In Scene view, click to place, use **Q/E** to rotate, and press **Esc** to stop placement.
7. Enter a layout name and click **SAVE TRACK JSON**.
8. Switch to **PROPS**, place decorations, and click **SAVE PROP ADDENDUM**.
9. Click **CLEAR TRACK** and **CLEAR PROPS**.
10. Load the two JSON files to reconstruct the layout.

Edit-mode JSON is stored under `Assets/TrackLayoutTool/Layouts`, so it is importable as a `TextAsset` and suitable for source control.

## What is happening internally

Every placed instance receives a `TrackPieceIdentity` containing its stable catalog ID and category. Saving scans the track and prop roots and records:

- stable piece ID;
- world position;
- Euler rotation;
- local scale.

Loading performs the reverse operation: look up the ID in `TrackPieceCatalog`, instantiate its mapped prefab, and restore the transform.

Because the layout references catalog IDs rather than asset paths, changing a catalog mapping can replace the art without rewriting the saved coordinates.

## What works during Play mode or a built game

- Piece selection and visible placement preview.
- Free continuous placement or adjustable grid-snapped placement.
- Rotation, deletion, and independent layer clearing.
- Timestamped JSON saving to `Application.persistentDataPath`.
- Loading the latest save or supplied template.
- Camera panning and zooming.
- The same runtime code can be included in a standalone build.

Within Unity Editor Play mode, **Tools > Track Builder** opens the same editor window shown in Edit mode. It works against the live Play-mode hierarchy and Scene view. The placed objects are temporary, while JSON written by the Save buttons remains in `Assets/TrackLayoutTool/Layouts`.

## What only works in the Unity Editor

- The **Tools > Track Builder** editor popup. It is available both before and during Unity Editor Play mode, but cannot exist in a standalone player build.
- Direct creation or modification of assets beneath the project's `Assets` directory.
- Saving a `.unity` scene asset.

Runtime saves use JSON rather than `.unity` scene files because `UnityEditor.SceneManagement` is not available in a player build. An optional editor importer could later turn a runtime JSON pair into a permanent scene asset.

## Current limitations

- Optional snapping is grid-based, not connector/socket-based.
- Placement uses a flat authoring plane at `groundY`.
- There is no spline deformation, automatic lap validation, or check that the track forms a closed loop.
- Runtime saves are local files; sharing/cloud synchronization is outside this prototype.
- Stopping Play mode discards unsaved in-memory changes. Saved JSON remains and can be restored with **LOAD LATEST SAVE**.

## Useful files

- `Assets/TrackLayoutTool/Runtime/RuntimeTrackAuthoringController.cs` — Play-mode authoring and persistence.
- `Assets/TrackLayoutTool/Runtime/TrackLayoutRuntimeLoader.cs` — lightweight JSON reconstruction.
- `Assets/TrackLayoutTool/Editor/TrackBuilderWindow.cs` — edit-mode popup.
- `Assets/TrackLayoutTool/Data/TrackPieceCatalog.asset` — stable ID to prefab mappings.
- `Assets/TrackLayoutTool/Demo/ImportedAssetRuntimeDemo.unity` — blank runtime canvas.
- `Assets/TrackLayoutTool/Demo/ImportedAssetWalkthrough.unity` — completed edit-mode example.
- `Assets/TrackLayoutTool/Documentation/RUNTIME_AUTHORING.md` - focused runtime notes.
- `Assets/TrackLayoutTool/Documentation/CLASSROOM_WALKTHROUGH.md` - original classroom sequence.
- `Assets/TrackLayoutTool/Documentation/SCRIPT_GUIDE.md` - file-by-file code guide and extension instructions.
