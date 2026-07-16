# Prefab Builder

This Unity project includes an editor tool at **Tools > Prefab Builder**.

Prefab Builder lets you create a reusable prefab palette, place those prefabs in the Scene view on a configurable 3D grid, and save or reload the placed layout as JSON. It is useful for building modular scenes from road pieces, props, environment objects, blockers, decorations, or any other project prefab.

## What It Does

- Builds a palette from dragged prefabs or every prefab inside a selected folder.
- Gives each prefab a stable layout ID stored in `Assets/TrackLayoutTool/Data/PrefabBuilderCatalog.asset`.
- Places selected prefabs under a scene root named `PREFAB_LAYOUT`.
- Adds `TrackPieceIdentity` to placed objects so the layout can be saved and rebuilt later.
- Supports grid snapping on X/Z, optional Y quantization, surface snapping, yaw rotation, pitch/roll tilt, and per-prefab placement step overrides.
- Saves layouts to `Assets/TrackLayoutTool/Layouts/<layout_name>.layout.json`.
- Loads a saved layout by looking up each saved prefab ID in the catalog and restoring position, rotation, and scale.

## Open the Tool

1. Open the Unity project.
2. Open any scene you want to build in.
3. Choose **Tools > Prefab Builder**.
4. Keep the **Scene** view visible; placement happens from the Scene view, not from the Game view.

## Build the Prefab Palette

1. In **1. PREFAB PALETTE**, leave the default catalog unless you intentionally want another catalog asset.
2. Optional: enter an **ID prefix** before adding prefabs.
3. Add prefabs in either of these ways:
   - Drag prefab assets into the drop area.
   - Assign a prefab folder, then click **Add all prefabs in folder**.
4. Click a prefab tile in the palette to select it.
5. Use the small `x` on a tile to remove that prefab from the palette.

Prefab IDs are generated from prefab names by lowercasing them and replacing non-letter or non-number characters with underscores. For example, `Track Corner 90` becomes `track_corner_90`.

## Place Prefabs

1. In **2. PLACE PIECES**, choose the selected prefab from the palette.
2. Set the global placement controls:
   - **Grid snap (X/Z)** controls horizontal snap spacing.
   - **Height step (Y)** controls vertical step size.
   - **Yaw step** controls normal left/right rotation.
   - **Tilt step** controls pitch and roll rotation.
   - **Surface snap** places onto visible scene geometry when possible.
   - **Quantize Y** snaps surface height to the height step when enabled.
   - **Plane Y** is the fallback placement plane when surface snap is off or no surface is hit.
3. Optional: edit the selected prefab's **Grid**, **Height**, and **Tilt** overrides. A value of `0` uses the global setting.
4. Optional: click **Derive** to estimate grid and height steps from the prefab bounds.
5. Click **PLACE WITH MOUSE**.
6. Move the cursor in the Scene view. A ghost preview and guide marks show where the prefab will go.
7. Left-click in the Scene view to place the prefab.
8. Press **Esc** or click **STOP PLACING** to stop placement mode.

You can also click **Place at scene pivot** to place the selected prefab at the current Scene view pivot.

## Scene View Controls

| Input | Action |
|---|---|
| Left click | Place the selected prefab |
| `Q` / `E` | Rotate yaw left or right |
| `Shift + Q` / `Shift + E` | Tilt pitch |
| `Ctrl + Q` / `Ctrl + E` | Tilt roll |
| `PageUp` / `PageDown` | Move the fallback placement plane up or down |
| `Esc` | Stop placing |

## Edit Placed Prefabs

Select one or more placed prefabs in the scene, then use:

- **Snap selected** to snap selected positions back to each prefab's lattice.
- **Flatten selected** to zero pitch and roll while snapping yaw.
- **Rotate left** and **Rotate right** to rotate selected objects by the yaw step.

Placed prefabs are parented under `PREFAB_LAYOUT`. Clearing or saving works from that root.

## Save and Load Layouts

1. In **3. SAVE / LOAD**, enter a **Layout name**.
2. Click **SAVE LAYOUT** to write `Assets/TrackLayoutTool/Layouts/<layout_name>.layout.json`.
3. Click **LOAD LAYOUT** to choose a saved `.layout.json` file and rebuild it.
4. Click **CLEAR LAYOUT** to remove the current `PREFAB_LAYOUT` root from the scene.

Saved layout JSON records only prefab IDs, positions, rotations, and scales. The catalog must still contain matching IDs when loading the layout.

## Play Mode Note

Prefab Builder can be opened during Unity Editor Play mode. In Play mode, placed scene objects are temporary, but **SAVE LAYOUT** still writes the layout JSON asset to the project.
