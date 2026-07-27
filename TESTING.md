# Prefab Builder Test Plan

Use this to test the package the same way a student or instructor would: add only
`PrefabBuilder` to an existing project, not the old Unity project.

## Clean Import Smoke Test

1. Create or open a Unity 2022.3+ project.
2. Copy the `PrefabBuilder` folder into the project's `Assets` folder so it becomes
   `Assets/PrefabBuilder/`.
3. Let Unity compile.
4. Confirm **Tools > Prefab Builder** appears.
5. Confirm there are no compiler errors.
6. Confirm the import did not add or replace `ProjectSettings/`, `Packages/manifest.json`,
   BEDRILL assets, demo scenes, or runtime-authoring systems.

## Basic Workflow Test

1. Create two or three simple prefab assets in the test project, such as Cube-based road,
   corner, and prop prefabs.
2. Open **Tools > Prefab Builder**.
3. Drag those prefab assets into the palette.
4. Select a palette tile, enable **PLACE WITH MOUSE**, and place a few prefabs in the Scene view.
5. Set a layout name and click **SAVE LAYOUT**.
6. Confirm the catalog was created at
   `Assets/PrefabBuilder/Data/PrefabBuilderCatalog.asset`.
7. Confirm the layout JSON was created at
   `Assets/PrefabBuilder/Layouts/<name>.layout.json`.

## Checklist Tests

### Saved ID preflight before clearing

1. Save a layout with at least two placed prefabs.
2. Duplicate the saved `.layout.json`.
3. Edit one `pieceId` in the duplicate to an id that is not in the palette.
4. Load the duplicate.
5. Expected: the load dialog reports the missing id and how many records are affected before
   removing the current layout.
6. Cancel the dialog.
7. Expected: the scene remains unchanged.

### Missing IDs and restored count

1. Repeat the missing-id load, but continue instead of canceling.
2. Expected: the status line reports `Restored X of Y` and lists skipped missing ids.
3. Expected: only resolvable prefabs are instantiated.

### All IDs missing

1. Make a copy of the layout where every `pieceId` is invalid.
2. Load it.
3. Expected: the load aborts and reports `0 of Y ids resolved`.
4. Expected: the existing scene layout is not cleared.

### Overwrite warning

1. Save a layout.
2. Save again using the same layout name.
3. Expected: Unity prompts before overwriting the existing `.layout.json`.
4. Cancel once and confirm the file remains unchanged.
5. Repeat and choose overwrite to confirm the save still works.

### Layout root transform protection

1. Place several prefabs and save a layout.
2. Move, rotate, and scale the `PREFAB_LAYOUT` scene object.
3. Load the saved layout.
4. Expected: the root object is not destroyed or recreated.
5. Expected: the root transform is preserved.
6. Expected: pieces reload in the root's local space, maintaining their arrangement relative to
   the moved/rotated/scaled root.

### Clear and removal behavior

1. Parent a non-tool object under `PREFAB_LAYOUT`.
2. Click **CLEAR LAYOUT**.
3. Expected: the dialog reports how many placed prefabs will be removed.
4. Expected: only placed prefabs that are direct children of `PREFAB_LAYOUT` are removed.
5. Expected: non-tool child objects remain.
6. To uninstall a drop-in install, delete `Assets/PrefabBuilder/` and its `.meta` file.

## Short Demo Video Shot List

Record a 60-90 second screen capture beside the package. Keep the video separate from the
installable package unless the instructor explicitly wants the video imported as an asset.

Recommended shots:

1. Show a normal Unity project, then copy in only `Assets/PrefabBuilder/`.
2. Show **Tools > Prefab Builder** opening with an empty palette.
3. Add two or three local prefabs to the palette.
4. Place several pieces in the Scene view.
5. Save a layout and show the generated JSON path.
6. Save again with the same name and show the overwrite warning.
7. Load a layout with one missing id and show the missing-id/restored-count report.
8. Move or rotate `PREFAB_LAYOUT`, reload, and show the root transform survives.
9. End by showing the removal path: **CLEAR LAYOUT**, then delete `Assets/PrefabBuilder/`.
