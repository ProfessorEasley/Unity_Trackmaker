# Track Builder classroom walkthrough

## What this demonstrates

The tool separates a driving level into two replaceable layers:

- **Track JSON**: road-piece IDs plus world position, Euler rotation, and scale.
- **Prop addendum JSON**: decoration IDs and transforms, with a reference to its track file.

The catalog maps those stable IDs to actual prefab assets. The source pack may use any filename convention; saved layouts remain readable as long as the catalog mapping remains valid.

## Five-minute presenter script

1. Open `Assets/TrackLayoutTool/Demo/ImportedAssetWalkthrough.unity`.
2. Open **Tools > Track Builder**.
3. Point out the catalog: `track_straight_short` maps to the imported 15 m BEDRILL road prefab, while `prop_finisharch`, `prop_cone`, and the others map to the environment pack.
4. Set **Grid snap** to `15`, choose **TRACK**, select a piece, and click **PLACE WITH MOUSE**.
5. In Scene view, use **Q/E** to rotate in 90-degree steps, click to place, and **Esc** to stop.
6. Enter `live_demo` as the layout name and click **SAVE TRACK JSON**.
7. Switch to **PROPS**, place an arch or cone, then click **SAVE PROP ADDENDUM**.
8. Click **CLEAR TRACK** and **CLEAR PROPS**. The scene geometry disappears, but the JSON remains.
9. Click **LOAD TRACK JSON** and choose `live_demo.track.json`; then load `live_demo.props.json`. The layout returns from IDs and transforms.
10. Open `Assets/TrackLayoutTool/Demo/ImportedAssetRuntimeDemo.unity` and press **Play**. The scene starts without authored track objects; `TrackLayoutRuntimeLoader.Start()` reads both JSON assets and instantiates all pieces.

## Capabilities and limits

- Works in Edit mode for designer authoring.
- Runtime reconstruction is supported through `TrackLayoutRuntimeLoader`.
- Runtime editing and writing files is not yet exposed through an in-game UI. The data classes support it, but a player-facing placement interface and a writable save location such as `Application.persistentDataPath` would be the next layer.
- Snapping is grid-based, not connector/socket-based. A 15-unit grid fits this imported pack. True endpoint snapping would require connector transforms on each prefab.
- Track and props can be versioned, exchanged, or recombined separately.
- Changing a catalog prefab changes the art used next time the same JSON is loaded without rewriting the coordinates.
