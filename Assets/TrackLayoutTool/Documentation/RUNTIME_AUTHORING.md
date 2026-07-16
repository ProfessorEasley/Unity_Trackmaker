# Runtime track authoring

Open `Assets/TrackLayoutTool/Demo/ImportedAssetRuntimeDemo.unity` and press Play.

The scene starts as a blank authoring canvas. The utilitarian runtime panel supports:

- selecting track or prop catalog entries;
- a visible placement preview;
- free continuous left-click placement by default;
- optional grid snapping with adjustable `0.25` to `15` unit spacing;
- Shift to temporarily bypass snapping;
- Q/E rotation in 90-degree steps;
- right-click deletion of a placed piece;
- WASD/arrow camera panning and mouse-wheel zoom;
- clearing track and props independently;
- loading the original imported-pack template;
- timestamped JSON saves and loading the latest save.

Runtime saves go to:

`Application.persistentDataPath/TrackBuilderSaves`

On this Windows project that normally resolves to:

`%USERPROFILE%/AppData/LocalLow/DefaultCompany/TrackLayoutToolDemo/TrackBuilderSaves`

Each save creates a pair such as:

- `runtime_track_20260630_140501_123.track.json`
- `runtime_track_20260630_140501_123.props.json`

The prop file references its matching track filename. Both files contain stable catalog IDs, positions, Euler rotations, and scales.

## Why JSON instead of a new Unity scene

Creating and saving `.unity` scene assets requires the `UnityEditor` API, which is unavailable in a built game. JSON in `Application.persistentDataPath` works in both Play mode and standalone builds. A later editor import command can convert one of these saved pairs into a `.unity` scene if desired.
