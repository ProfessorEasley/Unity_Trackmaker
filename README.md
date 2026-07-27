# Prefab Builder

A Unity editor tool for building modular scenes. Create a reusable prefab palette, place those
prefabs in the Scene view on a configurable 3D grid, and save or reload the placed layout as JSON.
Works with any prefabs — road pieces, props, environment objects, blockers, decorations.

Adds the menu item **Tools > Prefab Builder**. It also registers **Assets > Create > Prefab
Builder > Prefab Catalog**, though you will not normally need it — the window creates and uses its
own catalog at a fixed path, and re-selects that one whenever scripts recompile.

---

## Installation

**Requirements:** Unity 2022.3 or newer. The tool uses only built-in Unity modules, so it pulls in
no package dependencies of its own. (Installing via Option B below adds one line to
`Packages/manifest.json` — the entry for this package. Option A adds nothing.)

### Option A — drop-in folder (recommended)

1. Get the folder, either way:
   - **Clone:** `git clone https://github.com/<your-username>/PrefabBuilder.git` — this produces a
     folder already named `PrefabBuilder`.
   - **Download ZIP:** extract it, then rename the extracted `PrefabBuilder-main` folder to
     `PrefabBuilder`.
2. Copy that folder into your project's `Assets` folder, so you end up with `Assets/PrefabBuilder/`.
   Copy the whole folder including the `.meta` files.
3. Return to Unity and let it compile.
4. Open **Tools > Prefab Builder**.

Use that exact folder name if you can. The tool always writes its catalog and layouts to
`Assets/PrefabBuilder/`, so keeping the code there too makes uninstalling a single delete. Any other
location works identically — it just means removal is two deletes instead of one. Removing the
cloned `.git` folder afterwards is optional and breaks nothing.

### Option B — Unity package (UPM)

In **Window > Package Manager > + > Install package from git URL**, paste the repository URL.
Or add it to `Packages/manifest.json` manually:

```json
"com.prefabbuilder.tool": "https://github.com/<your-username>/<repo>.git"
```

Note that even when installed this way, the tool writes its catalog and layout files into
`Assets/PrefabBuilder/`, because a UPM package folder is read-only. Removal is then a two-step
process — see below.

### What this installs

The package contains nothing but the tool — no art, no scenes, no demo content, and no palette
asset. It never writes to `ProjectSettings/`, and never modifies your existing scenes or assets. You
start with an empty palette and add your own prefabs; the catalog asset is created on first use.

Everything it creates goes under `Assets/PrefabBuilder/` — the catalog asset and your saved layout
JSON. Two caveats worth stating plainly:

- Palette edits (adding prefabs, removing a tile, clicking **Derive**) call Unity's "save assets",
  which also flushes any *other* unsaved asset edits you happen to have pending. It does not modify
  them; it writes changes you had already made.
- Placing prefabs modifies the open **scene**, which is the point of the tool. Those changes are
  not written to disk until you save the scene yourself.

---

## Removal

**If you installed via Option A:** delete `Assets/PrefabBuilder/` and its `.meta` file. That is
everything — the code, the catalog, and your saved layouts all live in that one folder.

**If you installed via Option B:** remove the package in Package Manager, then delete
`Assets/PrefabBuilder/` (the data folder the tool created).

**In both cases**, if you want to strip the tool's traces from your *scenes* as well:

- **To discard a layout entirely:** delete the `PREFAB_LAYOUT` object. This removes everything under
  it, including anything you parented there yourself.
- **To remove only what the tool placed:** use **CLEAR LAYOUT** in the window. It deletes the placed
  prefabs that are direct children of the root and leaves everything else — nested pieces and any
  objects you parented under the root by hand — in place. If nothing is left under the root
  afterwards, the root is deleted too.
- **To keep the placed prefabs but drop the tool's marker:** select them and remove the
  `PlacedPrefabIdentity` component. They become ordinary prefab instances. Drag them out of
  `PREFAB_LAYOUT` first if you also want the root gone.

Then save the scenes and uninstall.

If you uninstall first, any placed prefabs remain in your scenes and keep working — they are
ordinary prefab instances. Unity will report a missing `PlacedPrefabIdentity` script on them, which
is harmless and disappears once you remove the component.

---

## Quick start

1. **Tools > Prefab Builder**.
2. Drag a few prefabs into the drop area under **1. PREFAB PALETTE**.
3. Click a prefab tile to select it.
4. Click **PLACE WITH MOUSE**, then click in the Scene view to place. `Esc` to stop.
5. Enter a **Layout name** and click **SAVE LAYOUT**.

Keep the **Scene** view visible — placement happens there, not in the Game view.

---

## Build the prefab palette

Add prefabs in either of these ways:

- Drag prefab assets into the drop area.
- Assign a prefab folder, then click **Add all prefabs in folder**.

Click a tile to select it; the small `x` on a tile removes that prefab from the palette. An optional
**ID prefix** is applied to ids generated while it is set.

Prefab ids are generated from prefab names by lowercasing and replacing anything that is not a
letter or digit with an underscore, so `Track Corner 90` becomes `track_corner_90`. Runs of
underscores collapse to one and leading/trailing underscores are trimmed, so `My  Prefab!!` becomes
`my_prefab`. A name that reduces to nothing becomes `prefab`, and a duplicate id gets a numeric
suffix (`cone`, `cone_2`, `cone_3`). Ids are what
layout files store, so renaming a prefab after placing it does not break existing layouts. Removing
it from the palette does, and so does deleting the prefab asset itself — an entry whose prefab
reference no longer resolves counts as missing. Either way the tool reports it on load rather than
failing silently.

The palette is stored at `Assets/PrefabBuilder/Data/PrefabBuilderCatalog.asset`, created the first
time you add a prefab. Opening the window, or opening and cancelling the load dialog, creates
nothing.

---

## Place prefabs

Set the global placement controls under **2. PLACE PIECES**:

| Control | Meaning |
|---|---|
| **Grid snap (X/Z)** | Horizontal snap spacing |
| **Height step (Y)** | Vertical step size |
| **Yaw step** | Left/right rotation increment |
| **Tilt step** | Pitch and roll increment |
| **Surface snap** | Place onto visible scene geometry when possible |
| **Quantize Y** | Snap Y to the height step — applies to surface hits, **Snap selected**, and **Place at scene pivot** |
| **Plane Y** | The fallback placement plane height |

**Surface snap is a height source, not an add-on.** When it is on, geometry under the cursor decides
Y and Plane Y only applies where nothing is under the cursor. When it is off, Plane Y is
authoritative. The window states which one is active.

Each prefab can override **Grid**, **Height** and **Tilt** individually; `0` means "use the global
value". **Derive** estimates grid and height from the prefab's bounds.

**Place at scene pivot** places one copy at the current Scene view pivot, snapped to the grid,
without entering placement mode.

### Scene view controls

| Input | Action |
|---|---|
| Left click | Place the selected prefab |
| `Q` / `E` | Rotate yaw left or right |
| `Shift + Q` / `Shift + E` | Tilt pitch |
| `Ctrl + Q` / `Ctrl + E` | Tilt roll |
| `PageUp` / `PageDown` | Move the fallback placement plane up or down |
| `Esc` | Stop placing |

### Edit placed prefabs

Select one or more placed prefabs, then:

- **Snap selected** — snap positions back onto each prefab's own lattice. Rotation is untouched.
- **Flatten selected** — zero pitch and roll, snap yaw.
- **Rotate left** / **Rotate right** — rotate by the yaw step.

---

## Save and load layouts

Layouts are written to `Assets/PrefabBuilder/Layouts/<layout_name>.layout.json` and record prefab
ids, positions, rotations and scales — not the prefabs themselves. The catalog must still contain
matching ids when you load.

**Saving** prompts before overwriting an existing file. The prompt reports how many prefabs the
existing file holds, or warns you instead when the file cannot be read or does not look like a
layout at all. Note that the layout name is normalised into a filename, so `My Track` and `my-track`
both resolve to `my_track.layout.json`; the window shows the resolved filename when it differs from
what you typed. A name that reduces to nothing is saved as `unnamed_layout.layout.json`. Saving with
nothing under the layout root prompts as well. If the write fails — a
read-only file, or one locked by another program — you get an error, not a false success.

**Loading** checks every saved id against the catalog *before* it touches your scene. If none of the
ids resolve, the load aborts and the scene is left exactly as it was. If some resolve and some do
not, you get a dialog listing the missing ids with counts, and can cancel. When the load completes,
the status line reports how many prefabs were actually restored out of how many were in the file —
for example:

```
Restored 8 of 20 prefab(s). Removed 5 previously placed prefab(s). Skipped 12 with missing ids:
  - prop_barrier  x5
  - track_chicane  x7
```

The whole load is one undo step.

**Clearing** asks for confirmation and reports how many prefabs it will remove. It removes only the
placed prefabs that are direct children of the root; anything else you parented under
`PREFAB_LAYOUT` survives, and the root itself is removed only when nothing is left under it. If the
root is already empty, you are asked before it is removed, because its transform is meaningful.
Undo works.

### The layout root

Placed prefabs are parented under a scene object named `PREFAB_LAYOUT`, as direct children.

You can move, rotate and scale this root freely. The placement grid, rotation snapping, the preview
and the saved layout all work in the root's local space, so the layout moves as a rigid unit and
still round-trips through save/load. Loading never destroys or re-creates the root, so its position
is preserved across loads. (The ghost preview matches the placed result exactly for a uniformly
scaled root; under a *non-uniformly* scaled and rotated root it is only approximate, because an
unparented preview object cannot reproduce the shear a parent applies to its children.)

Two consequences worth knowing:

- Under a scaled root the grid spacing is in the root's units, so a **Grid snap** of 15 under a 2x
  root produces 30 world units of visible spacing.
- Only **direct children** of the root are saved. If you parent one placed prefab under another, the
  nested one is not included in the layout file; the window tells you when this happens.

The window shows a warning banner whenever the root's **world** transform is not the identity — so a
root nested under a moved or scaled parent warns too, even though its own local transform is
untouched. It is there so an accidental nudge does not go unnoticed, not because anything is broken.
A **Reset root transform to origin** button is provided.

If more than one object named `PREFAB_LAYOUT` exists in the loaded scenes, the window reports an
error. Only the first is used, so pieces under the others would not be saved.

---

## Play mode

The window can be used during Play mode. Placed scene objects are temporary and disappear when you
exit Play mode, but **SAVE LAYOUT** still writes the layout JSON to the project, so you can reload it
in edit mode afterwards.

---

## File format

```json
{
    "schemaVersion": 2,
    "layoutName": "my_layout",
    "pieces": [
        {
            "pieceId": "track_corner_90",
            "localPosition": { "x": 15.0, "y": 0.0, "z": 0.0 },
            "localRotationEuler": { "x": 0.0, "y": 90.0, "z": 0.0 },
            "localScale": { "x": 1.0, "y": 1.0, "z": 1.0 }
        }
    ]
}
```

Transforms are relative to `PREFAB_LAYOUT`.
