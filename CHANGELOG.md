# Changelog

## 1.0.0

First release as a standalone, installable package.

### Packaging

- Extracted from the original demo project. The package contains only the tool: no
  `ProjectSettings/`, no `Packages/manifest.json`, no third-party art, no demo scenes, and none of
  the separate runtime-authoring system.
- No dependencies beyond built-in Unity modules, so the package pulls nothing into your
  `manifest.json` (a UPM install adds one line: the entry for this package itself).
- Assembly definitions added. `PrefabBuilder.Editor` is marked Editor-only, so the window and all
  its editor code are excluded from player builds. `PrefabBuilder.Runtime` is a normal runtime
  assembly and does ship — it has to, because `PlacedPrefabIdentity` sits on scene objects and
  `PrefabCatalog` is a `ScriptableObject`. It is seven small data types whose only logic is id
  lookup on the catalog.
- Ships with an empty palette. The previous build shipped a catalog wired to a specific art pack,
  which would have appeared as broken entries in any other project.

### Fixes

- **Load validates before it destroys.** Every saved prefab id is now resolved against the catalog
  before the existing layout is touched. If nothing resolves, the load aborts and the scene is left
  alone. If some ids are missing, you get a dialog naming them and a count, and can cancel.
  Previously the scene was cleared first and unresolvable ids were discovered afterwards, so a bad
  load destroyed the current layout and replaced it with nothing.
- **Load reports what actually happened.** The status line now reads `Restored X of Y`, using the
  number of prefabs actually instantiated, and lists each missing id with an occurrence count.
  Previously it reported the number of records in the file regardless of how many were skipped, so a
  load that restored 8 of 20 pieces still reported 20.
- **Save warns before overwriting.** Saving onto an existing `.layout.json` now prompts, and the
  prompt names the resolved filename and how many prefabs the existing file holds. This also
  surfaces name collisions: `My Track` and `my-track` both resolve to `my_track.layout.json`.
  Saving an empty layout prompts as well.
- **The layout root can be moved, rotated and scaled.** Layouts are now stored in the root's local
  space (`schemaVersion: 2`), and the placement grid, rotation snapping, the ghost preview and the
  placement gizmo all work in that same space (the preview's scale is exact for a uniformly scaled
  root and approximate under a non-uniform one). Moving the root now relocates the whole layout
  rigidly and it still round-trips. Previously piece transforms were captured as world position +
  world rotation + *local* scale, so an offset root double-applied its offset on reload and a scaled
  root corrupted the layout.
- **Loading no longer destroys the layout root.** It removes the placed prefabs and rebuilds them
  under the existing root, so the root's position, rotation, scale and parent all survive a load.
  Destroying and re-creating the root would have silently reset the layout to the origin — the same
  corruption the root-local format exists to prevent.
- **`schemaVersion` is actually read, and older files really do load.** Version 1 files used the
  field names `position`/`rotationEuler`/`scale`; the current format uses
  `localPosition`/`localRotationEuler`/`localScale`. Because `JsonUtility` binds strictly by field
  name and silently drops what it does not recognise, reading an old file through the new shape
  would have produced a full set of records with every transform at zero — the entire layout stacked
  on the origin and reported as fully restored. Old files are now detected and read through their
  original field names, and their world coordinates converted into the root's local space. A file
  claiming a newer schema version than this build understands prompts before loading.
- **Only direct children of the root are saved**, which is the only shape the loader can rebuild.
  Nested placed prefabs are now reported rather than saved and then silently flattened on reload.
- **Clearing prompts, and keeps what is not ours.** `CLEAR LAYOUT` names how many prefabs it will
  remove, removes only placed prefabs that are direct children, and deletes `PREFAB_LAYOUT` itself
  only when nothing else remains under it. Removing an already-empty root is confirmed too, since
  its transform is what future loads build against.
- **Failed writes are reported.** Saving onto a read-only or locked file now reports the error
  instead of looking like it succeeded.
- **A failed load is a single undo.** A load that throws part way through collapses into one undo
  step, so a half-built layout is one Ctrl+Z rather than a click-by-click unpick.
- **Prop addendum files are identified.** The old companion `.props.json` files store `props`, not
  `pieces`. Loading one now says so instead of reporting that it "contains no prefabs".
- **Root lookup is scene-aware.** The root is found across all loaded scenes, including inactive
  objects and nested ones. `GameObject.Find` missed inactive roots, which silently started a second
  layout on top of the first. Duplicate roots are now reported as an error in the window.
- **Opening the window no longer writes to your project.** The catalog asset and its folders are
  created on first use. Opening and cancelling the load dialog creates nothing.

### Compatibility

Layout files written before this package (`schemaVersion` 1, or absent) still load. They are read
through their original field names, and their world-space position and rotation are converted into
the layout root's local space, so pieces land at the position and orientation they were saved at no
matter where the root sits now. Scale is carried across unchanged and so is interpreted as
root-relative. The original file is never modified: the window tells you when a file was converted,
and a later save writes a new `schemaVersion: 2` file into `Assets/PrefabBuilder/Layouts/`.
