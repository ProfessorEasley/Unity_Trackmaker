# Track Layout Tool script guide

This guide explains which script owns each behavior and where to make common changes.

## Mental model

The system has four main concepts:

1. **Catalog:** maps a stable string ID to a prefab.
2. **Identity:** marks an instantiated object with its ID and Track/Prop category.
3. **Data:** records IDs and transforms in JSON.
4. **Authoring/loading:** editor and runtime scripts create, save, clear, and rebuild instances.

## Runtime scripts

### `Runtime/TrackPieceCatalog.cs`

Owns:

- `PieceCategory` (`Track` or `Prop`);
- `TrackPieceCatalogEntry` (`id`, category, prefab reference);
- `TrackPieceCatalog`, the ScriptableObject asset used for ID-to-prefab lookup.

Modify this when:

- adding a new top-level category such as `Obstacle`, `Checkpoint`, `Spawn`, or `Decoration`;
- changing how IDs are resolved;
- adding catalog metadata such as thumbnails, placement offsets, connector type, or default snap spacing.

The configured asset is `Assets/TrackLayoutTool/Data/TrackPieceCatalog.asset`.

### `Runtime/TrackPieceIdentity.cs`

Attached to every tool-managed instance. It stores:

- `pieceId`;
- `category`.

Save routines discover objects through this component. If an object lacks it, the object is not included in track/prop JSON.

Modify this when adding per-instance information such as:

- gameplay variant;
- lane number;
- difficulty;
- checkpoint index;
- custom user label.

If new values must persist, also add matching fields to `PlacedPieceRecord` and both save/load paths.

### `Runtime/TrackLayoutData.cs`

Defines the JSON schema:

- `PlacedPieceRecord` stores ID, world position, Euler rotation, and scale;
- `TrackLayoutFile` stores the track layer;
- `PropAddendumFile` stores the prop layer and matching track filename.

Modify this when the JSON needs additional information. After adding a field, update capture and reconstruction code in:

- `Editor/TrackBuilderWindow.cs`;
- `Runtime/RuntimeTrackAuthoringController.cs`;
- `Runtime/TrackLayoutRuntimeLoader.cs`.

For incompatible schema changes, increment `schemaVersion` and add migration logic rather than silently reinterpreting old files.

### `Runtime/TrackLayoutRuntimeLoader.cs`

A small reconstruction-only component. Given a catalog and JSON `TextAsset`s, it builds the layout under:

```text
Generated Track Layout
|- Track
`- Props
```

Modify this when:

- changing startup reconstruction behavior;
- loading additional JSON layers;
- applying a parent offset;
- using addressables or asset bundles instead of direct prefab references.

It does not provide interactive authoring UI.

### `Runtime/RuntimeTrackAuthoringController.cs`

Owns the optional in-game/Game-view prototype:

- runtime `OnGUI` panel;
- continuous mouse preview and placement;
- optional grid snapping and Shift bypass;
- Q/E rotation;
- right-click deletion;
- camera pan/zoom;
- timestamped JSON saving to `Application.persistentDataPath`;
- load latest and load template.

Common change points:

- `OnGUI()` changes the runtime panel layout;
- `TryGetSnappedMousePoint()` changes free/grid placement behavior;
- `UpdatePreview()` changes the ghost preview;
- `PlacePiece()` changes instance creation;
- `SaveTimestampedLayout()` changes runtime filenames and save location;
- `LoadFromFiles()` changes runtime reconstruction;
- `HandleCameraMovement()` changes runtime camera controls.

## Editor scripts

### `Editor/TrackBuilderWindow.cs`

This is the **Tools > Track Builder** popup shown in the screenshots. It is available both before and during Unity Editor Play mode.

Owns:

- catalog editing and prefab-folder scanning;
- Scene-view placement;
- editor grid snapping and Q/E rotation;
- `TRACK_LAYOUT` and `TRACK_PROPS` roots;
- project JSON saving under `Assets/TrackLayoutTool/Layouts`;
- clearing and loading track/prop files.

Common change points:

- `DrawCatalogSection()` changes catalog UI;
- `DrawPlacementSection()` changes placement controls;
- `DrawSaveLoadSection()` changes save/load controls;
- `DuringSceneGui()` handles Scene-view mouse and keyboard input;
- `SnapPoint()` controls editor snapping;
- `PlacePiece()` creates editor or Play-mode scene instances;
- `SaveTrack()` / `SaveProps()` build project JSON;
- `LoadTrackWithDialog()` / `LoadPropsWithDialog()` restore project JSON.

### `Editor/ImportedAssetWalkthrough.cs`

Maps the imported BEDRILL prefab paths to stable IDs and generates:

- `ImportedAssetWalkthrough.unity`;
- `ImportedAssetRuntimeDemo.unity`;
- imported-pack sample JSON.

Modify its prefab-path dictionaries when replacing the demonstration asset pack or changing the supplied example.

### `Editor/RuntimeAuthoringSetup.cs`

Configures and validates the optional runtime/Game-view authoring demo. It also repairs the known BEDRILL catalog mappings and ensures the runtime demo has one controller.

Modify this when changing default runtime settings such as:

- `snapToGrid`;
- `gridSize`;
- rotation step;
- template files;
- runtime scene setup.

### `Editor/DemoSetup.cs`

Creates the crude placeholder prefabs, materials, catalog, JSON, and original placeholder scene. This exists so the tool can function without third-party assets.

### `Editor/DemoValidator.cs`

Checks the placeholder catalog, JSON, saved scene, and reconstruction behavior. It is test/support code, not normal authoring code.

### `Editor/RuntimePresentationRunner.cs`

Creates the automated presentation result: place track/props, save, clear, reload, and save a visible result scene. It is demonstration/test code.

## Add another prefab as Track or Prop

This is the easiest extension and requires no C# changes:

1. Import or create a prefab.
2. Select the prefab in the Project window.
3. Open **Tools > Track Builder**.
4. Click **Selected prefabs = TRACK** or **Selected prefabs = PROP**.
5. Edit the generated stable ID in the mapping list. Use lowercase descriptive IDs such as `track_ramp_short` or `prop_tree_pine`.
6. Save the project. The catalog asset stores the mapping.

Changing a catalog prefab later changes what future JSON loads instantiate while keeping the same saved ID and coordinates.

## Add something beyond Track and Prop

### Simple option: treat it as a Prop

Use this for decorations, obstacles, signs, checkpoints, pickups, trees, buildings, or anything that can live in the prop addendum. No code change is required.

### Full option: create a new category/layer

For a genuinely separate file or hierarchy layer:

1. Add a value to `PieceCategory` in `TrackPieceCatalog.cs`.
2. Add the new collection/file type in `TrackLayoutData.cs`, or refactor the schema to a generic list of named layers.
3. Add a new root and category button in `TrackBuilderWindow.cs`.
4. Update `CaptureRecords()`, `BuildRecords()`, clear, save, and load logic in the editor window.
5. Update `RuntimeTrackAuthoringController.cs` with the new root, UI category, save data, and load data.
6. Update `TrackLayoutRuntimeLoader.cs` so builds can reconstruct it.
7. Increment the JSON schema version and update validators.

If many categories are expected, a generic `LayoutLayer { string name; List<PlacedPieceRecord> pieces; }` design will scale better than adding one new class and button per category.

## Change snapping

Editor popup snapping:

- file: `Editor/TrackBuilderWindow.cs`;
- method: `SnapPoint()`;
- default field: `gridSize`.

In-game runtime snapping:

- file: `Runtime/RuntimeTrackAuthoringController.cs`;
- method: `TryGetSnappedMousePoint()`;
- fields: `snapToGrid`, `gridSize`, `groundY`;
- panel controls: `OnGUI()`.

True connector/socket snapping would add named child transforms to each prefab, store connector metadata in the catalog, and choose the nearest compatible connector instead of rounding coordinates.

## Change save locations or filenames

Editor popup files:

- `TrackBuilderWindow.LayoutFolder`;
- `SaveTrack()`;
- `SaveProps()`;
- `SaveJson()`.

Runtime/player files:

- `RuntimeTrackAuthoringController.SaveFolderPath`;
- `SaveTimestampedLayout()`;
- `LoadLatestSave()`.

Do not use `UnityEditor` APIs inside Runtime scripts; doing so prevents standalone player builds.

## What is and is not saved

Saved by track/prop JSON:

- objects with `TrackPieceIdentity`;
- stable ID;
- position;
- rotation;
- scale.

Not saved by track/prop JSON:

- camera;
- player car;
- Ground;
- lights;
- UI;
- unrelated scripts or GameObjects;
- arbitrary component state.

Those remain in the `.unity` scene. To create a permanent full scene, load the desired JSON pair in Edit mode and save the scene normally.
