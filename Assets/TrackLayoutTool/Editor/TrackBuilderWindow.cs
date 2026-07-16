using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SimpleTrackBuilder.Editor
{
    // Prefab-agnostic layout builder. Drag in any prefabs, place them on a 3D lattice, and save/load
    // the whole placed hierarchy as a single JSON file. The class name is kept as TrackBuilderWindow
    // for backwards compatibility with the demo/setup scripts, but everything the user sees is
    // generic ("Prefab Builder").
    public sealed class TrackBuilderWindow : EditorWindow
    {
        private const string LayoutRootName = "PREFAB_LAYOUT";
        private const string DefaultCatalogPath = "Assets/TrackLayoutTool/Data/PrefabBuilderCatalog.asset";
        private const string LayoutFolder = "Assets/TrackLayoutTool/Layouts";
        private const string GhostName = "__PrefabBuilderGhost";

        [SerializeField] private TrackPieceCatalog catalog;
        [SerializeField] private DefaultAsset prefabFolder;
        [SerializeField] private string idPrefix = "";
        [SerializeField] private string layoutName = "my_layout";
        [SerializeField] private float gridSize = 15f;
        [SerializeField] private float heightStep = 1f;
        // Off by default: quantizing a surface hit by a height step that does not match the geometry
        // (e.g. step 1 on a 0.2-thick road) would drag the piece back down to y = 0.
        [SerializeField] private bool snapY;
        // Off by default: surface snap picks against every renderer, so a ground plane (which sits under
        // the cursor almost everywhere) would win every time and silently override Plane Y.
        [SerializeField] private bool surfaceSnap;
        [SerializeField] private float placementHeight;
        [SerializeField] private float rotationStep = 90f;
        [SerializeField] private float tiltStep = 15f;
        [SerializeField] private string selectedPieceId;
        [SerializeField] private float placementYaw;
        [SerializeField] private float placementPitch;
        [SerializeField] private float placementRoll;

        private Vector2 scroll;
        private bool placementMode;
        private string status = "Ready.";
        private bool repaintForPreview;
        private GameObject ghost;
        private string ghostEntryId;

        [MenuItem("Tools/Prefab Builder")]
        public static void ShowWindow()
        {
            TrackBuilderWindow window = GetWindow<TrackBuilderWindow>("Prefab Builder");
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += DuringSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyGhost;
            FindOrCreateCatalog();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DuringSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyGhost;
            DestroyGhost();
        }

        private void OnGUI()
        {
            repaintForPreview = false;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("PREFAB LAYOUT BUILDER", EditorStyles.boldLabel);
            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "PLAY MODE: use this same window with the Scene view. Scene objects are temporary, but SAVE LAYOUT persists their layout as JSON.",
                    MessageType.Warning);
            }

            DrawPaletteSection();
            EditorGUILayout.Space(8);
            DrawPlacementSection();
            EditorGUILayout.Space(8);
            DrawSaveLoadSection();
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(status, MessageType.None);
            EditorGUILayout.EndScrollView();
            if (repaintForPreview)
            {
                Repaint();
            }
        }

        // ---------------------------------------------------------------- palette

        private void DrawPaletteSection()
        {
            EditorGUILayout.LabelField("1. PREFAB PALETTE", EditorStyles.boldLabel);
            catalog = (TrackPieceCatalog)EditorGUILayout.ObjectField("Catalog", catalog, typeof(TrackPieceCatalog), false);
            idPrefix = EditorGUILayout.TextField("ID prefix (optional)", idPrefix);

            DrawDropArea();

            prefabFolder = (DefaultAsset)EditorGUILayout.ObjectField("Prefab folder", prefabFolder, typeof(DefaultAsset), false);
            using (new EditorGUI.DisabledScope(prefabFolder == null))
            {
                if (GUILayout.Button("Add all prefabs in folder"))
                {
                    AddFolderToCatalog(prefabFolder, idPrefix);
                }
            }
        }

        private void DrawDropArea()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 42f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, "Drag & drop prefabs here to add them");
            Event evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
            {
                return;
            }
            if (!rect.Contains(evt.mousePosition))
            {
                return;
            }

            bool anyPrefab = DragAndDrop.objectReferences.OfType<GameObject>().Any(EditorUtility.IsPersistent);
            DragAndDrop.visualMode = anyPrefab ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (evt.type == EventType.DragPerform && anyPrefab)
            {
                DragAndDrop.AcceptDrag();
                AddPrefabsToCatalog(DragAndDrop.objectReferences.OfType<GameObject>().Where(EditorUtility.IsPersistent), idPrefix);
            }
            evt.Use();
        }

        // ---------------------------------------------------------------- placement UI

        private void DrawPlacementSection()
        {
            EditorGUILayout.LabelField("2. PLACE PIECES", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            gridSize = Mathf.Max(0.01f, EditorGUILayout.FloatField("Grid snap (X/Z)", gridSize));
            heightStep = Mathf.Max(0.01f, EditorGUILayout.FloatField("Height step (Y)", heightStep));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            rotationStep = Mathf.Max(1f, EditorGUILayout.FloatField("Yaw step", rotationStep));
            tiltStep = Mathf.Max(1f, EditorGUILayout.FloatField("Tilt step", tiltStep));
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            surfaceSnap = GUILayout.Toggle(surfaceSnap, "Surface snap", EditorStyles.miniButton);
            snapY = GUILayout.Toggle(snapY, "Quantize Y", EditorStyles.miniButton);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            placementHeight = EditorGUILayout.FloatField("Plane Y", placementHeight);
            if (GUILayout.Button("-", GUILayout.Width(24))) NudgePlane(-1);
            if (GUILayout.Button("+", GUILayout.Width(24))) NudgePlane(1);
            GUILayout.Label($"Level {CurrentLevel()}", EditorStyles.miniLabel, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                // Otherwise the ghost keeps sitting at the old height until the mouse happens to move.
                SceneView.RepaintAll();
            }

            // Which control actually decides Y is the single most confusing thing in this window, so say it.
            EditorGUILayout.HelpBox(
                surfaceSnap
                    ? "Height source: SURFACE under the cursor. Pieces land on existing geometry (including the ground), so Plane Y is used ONLY where nothing is under the cursor. Turn this off to build at Plane Y."
                    : "Height source: PLANE Y. Pieces land at this height; use +/- or PgUp/PgDn in the Scene view to change levels. Turn on Surface snap to drop pieces onto existing geometry instead.",
                MessageType.None);

            List<TrackPieceCatalogEntry> choices = GetChoices();
            if (choices.Count == 0)
            {
                EditorGUILayout.HelpBox("No prefabs in the palette yet. Drag prefabs into the box above.", MessageType.Warning);
            }
            else
            {
                DrawPrefabGrid(choices);

                TrackPieceCatalogEntry entry = CurrentEntry();
                EditorGUILayout.LabelField($"Selected: {entry.id}", EditorStyles.miniBoldLabel);
                DrawPerPrefabSteps(entry);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"Yaw {placementYaw:0}  Pitch {placementPitch:0}  Roll {placementRoll:0}", EditorStyles.miniLabel);
                if (GUILayout.Button("Reset rotation", EditorStyles.miniButton, GUILayout.Width(100)))
                {
                    placementYaw = placementPitch = placementRoll = 0f;
                    SceneView.RepaintAll();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = placementMode ? new Color(1f, 0.65f, 0.35f) : Color.white;
                if (GUILayout.Button(placementMode ? "STOP PLACING (Esc)" : "PLACE WITH MOUSE"))
                {
                    placementMode = !placementMode;
                    if (!placementMode) DestroyGhost();
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Place at scene pivot"))
                {
                    Vector3 pivot = SceneView.lastActiveSceneView != null
                        ? SceneView.lastActiveSceneView.pivot
                        : Vector3.zero;
                    PlacePiece(entry, SnapPoint(pivot, entry), PlacementRotation());
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Snap selected")) SnapSelection();
            if (GUILayout.Button("Flatten selected")) FlattenSelection();
            if (GUILayout.Button("Rotate left")) RotateSelection(-rotationStep);
            if (GUILayout.Button("Rotate right")) RotateSelection(rotationStep);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "Scene keys: click = place | Q/E = yaw | Shift+Q/E = pitch | Ctrl+Q/E = roll | PgUp/PgDn = plane up/down | Esc = stop",
                EditorStyles.miniLabel);
        }

        private void DrawPerPrefabSteps(TrackPieceCatalogEntry entry)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            float grid = EditorGUILayout.FloatField("Grid", Mathf.Max(0f, entry.gridOverride));
            float height = EditorGUILayout.FloatField("Height", Mathf.Max(0f, entry.heightOverride));
            float tilt = EditorGUILayout.FloatField("Tilt", Mathf.Max(0f, entry.tiltOverride));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(catalog, "Edit Prefab Steps");
                entry.gridOverride = Mathf.Max(0f, grid);
                entry.heightOverride = Mathf.Max(0f, height);
                entry.tiltOverride = Mathf.Max(0f, tilt);
                EditorUtility.SetDirty(catalog);
            }
            if (GUILayout.Button("Derive", EditorStyles.miniButton, GUILayout.Width(52)))
            {
                DeriveStepsFromPrefab(entry);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                $"Per-prefab steps (0 = use global). Active: grid {GridFor(entry):0.##}, height {HeightFor(entry):0.##}, tilt {TiltFor(entry):0.##}",
                EditorStyles.miniLabel);
        }

        private void DrawSaveLoadSection()
        {
            EditorGUILayout.LabelField("3. SAVE / LOAD", EditorStyles.boldLabel);
            layoutName = EditorGUILayout.TextField("Layout name", layoutName);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("SAVE LAYOUT")) SaveLayout();
            if (GUILayout.Button("LOAD LAYOUT")) LoadLayoutWithDialog();
            if (GUILayout.Button("CLEAR LAYOUT")) ClearLayout();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Files: Assets/TrackLayoutTool/Layouts/*.layout.json", EditorStyles.miniLabel);
        }

        // ---------------------------------------------------------------- scene view

        private void DuringSceneGui(SceneView sceneView)
        {
            // Allocate unconditionally and BEFORE any early-out. GetControlID must run the same number
            // of times on every event pass, or IMGUI's control-ID counter desyncs between Layout and
            // MouseDown/Repaint and every later handle in the scene hit-tests against the wrong control.
            int placementControl = GUIUtility.GetControlID(FocusType.Passive);

            if (!placementMode)
            {
                DestroyGhost();
                return;
            }

            TrackPieceCatalogEntry entry = CurrentEntry();
            if (entry == null)
            {
                placementMode = false;
                DestroyGhost();
                return;
            }

            Event evt = Event.current;
            if (evt.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(placementControl);
            }

            if (evt.type == EventType.KeyDown && HandleKey(evt, sceneView, entry))
            {
                return;
            }

            if (!TryResolvePlacementPoint(evt, entry, out Vector3 point))
            {
                // Cursor is over nothing (e.g. the ray is parallel to the plane). Do not leave an
                // opaque copy of the prefab frozen in the scene.
                if (ghost != null) ghost.SetActive(false);
                return;
            }

            Quaternion rotation = PlacementRotation();
            UpdateGhost(entry, point, rotation);
            DrawPlacementGizmos(entry, point, rotation);

            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
            {
                PlacePiece(entry, point, rotation);
                evt.Use();
            }
        }

        // Returns true when the caller should bail out for this event.
        private bool HandleKey(Event evt, SceneView sceneView, TrackPieceCatalogEntry entry)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                placementMode = false;
                DestroyGhost();
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.keyCode == KeyCode.Q || evt.keyCode == KeyCode.E)
            {
                float direction = evt.keyCode == KeyCode.Q ? -1f : 1f;
                if (evt.shift)
                {
                    placementPitch = Mathf.Repeat(placementPitch + direction * TiltFor(entry), 360f);
                }
                else if (evt.control || evt.command)
                {
                    placementRoll = Mathf.Repeat(placementRoll + direction * TiltFor(entry), 360f);
                }
                else
                {
                    placementYaw = Mathf.Repeat(placementYaw + direction * rotationStep, 360f);
                }
                evt.Use();
                sceneView.Repaint();
                Repaint();
                return false;
            }

            if (evt.keyCode == KeyCode.PageUp || evt.keyCode == KeyCode.PageDown)
            {
                NudgePlane(evt.keyCode == KeyCode.PageUp ? 1 : -1);
                evt.Use();
                sceneView.Repaint();
                Repaint();
                return false;
            }

            return false;
        }

        private bool TryResolvePlacementPoint(Event evt, TrackPieceCatalogEntry entry, out Vector3 point)
        {
            float grid = GridFor(entry);

            // Surface snap is opt-in, and it is a HEIGHT SOURCE, not an add-on: when it is on, the
            // geometry under the cursor decides Y and Plane Y only covers empty space. When it is off,
            // Plane Y is authoritative. Defaulting it on made Plane Y look broken, because the ground
            // plane is under the cursor almost everywhere and always answered y = 0.
            if (surfaceSnap && TrySurfacePoint(evt, out Vector3 surfacePoint))
            {
                // The Y came from real geometry, so it is already meaningful. Only quantize it if the
                // user explicitly asked for it, and then with THIS prefab's height step.
                float surfaceY = snapY ? Snap(surfacePoint.y, HeightFor(entry)) : surfacePoint.y;
                point = new Vector3(Snap(surfacePoint.x, grid), surfaceY, Snap(surfacePoint.z, grid));
                return true;
            }

            // Otherwise fall back to the build plane. Its Y is already deliberate, so do NOT re-quantize
            // it: with Plane Y = 2.5 and Height step = 1, re-snapping would silently drag it to 2.
            Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            Plane plane = new(Vector3.up, new Vector3(0f, placementHeight, 0f));
            if (!plane.Raycast(ray, out float distance))
            {
                point = default;
                return false;
            }
            Vector3 hit = ray.GetPoint(distance);
            point = new Vector3(Snap(hit.x, grid), placementHeight, Snap(hit.z, grid));
            return true;
        }

        private bool TrySurfacePoint(Event evt, out Vector3 point)
        {
            // HandleUtility.ignoreRaySnapObjects is internal to UnityEditor, so we cannot ask the snap
            // to skip the ghost. Hide the ghost for the duration of the cast instead - these casts hit
            // renderer geometry (not just colliders), so the ghost would otherwise be the first thing
            // hit and the piece would climb on top of itself every frame.
            bool wasActive = ghost != null && ghost.activeSelf;
            if (wasActive)
            {
                ghost.SetActive(false);
            }

            try
            {
                // PlaceObject picks against Renderers, so target geometry needs no colliders.
                if (HandleUtility.PlaceObject(evt.mousePosition, out Vector3 placed, out Vector3 _))
                {
                    point = placed;
                    return true;
                }

                if (HandleUtility.RaySnap(HandleUtility.GUIPointToWorldRay(evt.mousePosition)) is RaycastHit raycastHit)
                {
                    point = raycastHit.point;
                    return true;
                }
            }
            finally
            {
                if (wasActive)
                {
                    ghost.SetActive(true);
                }
            }

            point = default;
            return false;
        }

        private void DrawPlacementGizmos(TrackPieceCatalogEntry entry, Vector3 point, Quaternion rotation)
        {
            Bounds bounds = PrefabBounds(entry.prefab);

            Handles.color = Color.cyan;
            using (new Handles.DrawingScope(Matrix4x4.TRS(point, rotation, Vector3.one)))
            {
                Handles.DrawWireCube(bounds.center, bounds.size);
            }

            // Depth perception in a perspective view is genuinely bad; the drop line is the cheap fix.
            Vector3 ground = new(point.x, 0f, point.z);
            Handles.color = new Color(0f, 1f, 1f, 0.45f);
            Handles.DrawDottedLine(point, ground, 3f);
            Handles.DrawWireDisc(ground, Vector3.up, Mathf.Max(0.2f, GridFor(entry) * 0.08f));

            Handles.color = Color.white;
            Handles.Label(
                point + Vector3.up * (bounds.size.y * 0.5f + 0.5f),
                $"{entry.id}\ny = {point.y:0.##}\nYaw {placementYaw:0}  Pitch {placementPitch:0}  Roll {placementRoll:0}");
        }

        // ---------------------------------------------------------------- ghost preview

        private void UpdateGhost(TrackPieceCatalogEntry entry, Vector3 position, Quaternion rotation)
        {
            if (entry == null || entry.prefab == null)
            {
                DestroyGhost();
                return;
            }

            if (ghost == null || ghostEntryId != entry.id)
            {
                DestroyGhost();
                ghost = Instantiate(entry.prefab);
                ghost.name = GhostName;
                ghostEntryId = entry.id;

                // Make the clone inert. In Play mode its scripts and physics would otherwise really run,
                // and a Rigidbody would let the preview fall through the world.
                foreach (Behaviour behaviour in ghost.GetComponentsInChildren<Behaviour>(true))
                {
                    behaviour.enabled = false;
                }
                foreach (Rigidbody body in ghost.GetComponentsInChildren<Rigidbody>(true))
                {
                    DestroyImmediate(body);
                }
                foreach (Collider collider in ghost.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                }
            }

            // Re-apply every frame, not just on creation: anything the prefab spawned after the fact
            // would otherwise escape HideAndDontSave and get written into the user's .unity scene.
            // The ghost is also never parented under PREFAB_LAYOUT, so CaptureRecords() cannot see it.
            foreach (Transform child in ghost.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }

            if (!ghost.activeSelf)
            {
                ghost.SetActive(true);
            }
            ghost.transform.SetPositionAndRotation(position, rotation);
        }

        private void DestroyGhost()
        {
            if (ghost == null)
            {
                return;
            }
            DestroyImmediate(ghost);
            ghost = null;
            ghostEntryId = null;
        }

        // ---------------------------------------------------------------- lattice

        private Quaternion PlacementRotation()
        {
            return Quaternion.Euler(placementPitch, placementYaw, placementRoll);
        }

        private void NudgePlane(int direction)
        {
            // Pure translation. Re-quantizing here would destroy a deliberately off-lattice Plane Y,
            // and Mathf.Round's round-half-to-even would make one "step" not equal one height step.
            placementHeight += direction * HeightFor(CurrentEntry());
        }

        private int CurrentLevel()
        {
            float step = HeightFor(CurrentEntry());
            return step <= 0.0001f ? 0 : Mathf.RoundToInt(placementHeight / step);
        }

        private static float Snap(float value, float step)
        {
            return step <= 0.0001f ? value : Mathf.Round(value / step) * step;
        }

        private static float StepOrDefault(float overrideValue, float fallback)
        {
            return overrideValue > 0.0001f ? overrideValue : fallback;
        }

        private float GridFor(TrackPieceCatalogEntry entry)
        {
            return StepOrDefault(entry?.gridOverride ?? 0f, gridSize);
        }

        private float HeightFor(TrackPieceCatalogEntry entry)
        {
            return StepOrDefault(entry?.heightOverride ?? 0f, heightStep);
        }

        private float TiltFor(TrackPieceCatalogEntry entry)
        {
            return StepOrDefault(entry?.tiltOverride ?? 0f, tiltStep);
        }

        private Vector3 SnapPoint(Vector3 point, TrackPieceCatalogEntry entry)
        {
            float grid = GridFor(entry);
            return new Vector3(
                Snap(point.x, grid),
                snapY ? Snap(point.y, HeightFor(entry)) : point.y,
                Snap(point.z, grid));
        }

        // The catalog entry a PLACED object came from, so "Snap selected" uses that object's own steps
        // rather than whichever tile happens to be highlighted in the palette.
        private TrackPieceCatalogEntry EntryForPlaced(Transform placed)
        {
            if (catalog == null) return null;
            TrackPieceIdentity identity = placed.GetComponent<TrackPieceIdentity>();
            if (identity == null || string.IsNullOrEmpty(identity.pieceId)) return null;
            return catalog.FindEntryById(identity.pieceId);
        }

        // ---------------------------------------------------------------- selection

        private List<TrackPieceCatalogEntry> GetChoices()
        {
            if (catalog == null)
            {
                return new List<TrackPieceCatalogEntry>();
            }

            return catalog.Entries
                .Where(entry => entry.prefab != null)
                .OrderBy(entry => entry.id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // The selected prefab is tracked by ID, not by index: the choice list is sorted by id, so an
        // index would silently re-point at a different prefab as soon as one sorting earlier is added.
        private TrackPieceCatalogEntry CurrentEntry()
        {
            List<TrackPieceCatalogEntry> choices = GetChoices();
            if (choices.Count == 0)
            {
                return null;
            }

            TrackPieceCatalogEntry entry = string.IsNullOrEmpty(selectedPieceId)
                ? null
                : choices.FirstOrDefault(item => item.id == selectedPieceId);

            if (entry == null)
            {
                entry = choices[0];
                selectedPieceId = entry.id;
            }
            return entry;
        }

        // ---------------------------------------------------------------- palette grid

        private void DrawPrefabGrid(List<TrackPieceCatalogEntry> choices)
        {
            const float tile = 74f;
            float available = EditorGUIUtility.currentViewWidth - 26f;
            int columns = Mathf.Max(1, Mathf.FloorToInt(available / (tile + 4f)));
            GUIStyle nameStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };
            GUIStyle closeStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0)
            };

            string activeId = CurrentEntry()?.id;
            TrackPieceCatalogEntry removeEntry = null;

            for (int i = 0; i < choices.Count; i++)
            {
                if (i % columns == 0)
                {
                    if (i > 0) EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }

                TrackPieceCatalogEntry entry = choices[i];
                Rect r = GUILayoutUtility.GetRect(tile, tile, GUILayout.Width(tile), GUILayout.Height(tile));

                // A Box, not a Button: it stays passive so it cannot swallow the remove click below.
                Color oldBg = GUI.backgroundColor;
                if (entry.id == activeId)
                {
                    GUI.backgroundColor = new Color(0.35f, 0.7f, 1f);
                }
                GUI.Box(r, new GUIContent(string.Empty, entry.id), GUI.skin.button);
                GUI.backgroundColor = oldBg;

                Rect imageRect = new Rect(r.x + 3f, r.y + 2f, r.width - 6f, r.height - 17f);
                Texture preview = GetPreviewTexture(entry.prefab);
                if (preview != null)
                {
                    GUI.DrawTexture(imageRect, preview, ScaleMode.ScaleToFit);
                }
                GUI.Label(new Rect(r.x + 1f, r.yMax - 15f, r.width - 2f, 14f), entry.id, nameStyle);

                // Declared before the tile hit-test so it wins the click; drawn last so it sits on top.
                Rect closeRect = new Rect(r.xMax - 15f, r.y + 1f, 14f, 14f);
                if (GUI.Button(closeRect, new GUIContent("x", $"Remove {entry.id} from the palette"), closeStyle))
                {
                    removeEntry = entry;
                }

                Event evt = Event.current;
                if (evt.type == EventType.MouseDown && evt.button == 0 && r.Contains(evt.mousePosition))
                {
                    selectedPieceId = entry.id;
                    evt.Use();
                    Repaint();
                }
            }

            if (choices.Count > 0)
            {
                EditorGUILayout.EndHorizontal();
            }

            if (removeEntry != null)
            {
                Undo.RecordObject(catalog, "Remove Prefab From Palette");
                catalog.Entries.Remove(removeEntry);
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                status = $"Removed {removeEntry.id} from the palette.";

                // CurrentEntry() self-heals to the first remaining prefab if this was the selected one.
                if (selectedPieceId == removeEntry.id)
                {
                    selectedPieceId = null;
                }
                DestroyGhost();
                Repaint();
            }
        }

        private Texture GetPreviewTexture(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }
            Texture2D preview = AssetPreview.GetAssetPreview(prefab);
            if (preview != null)
            {
                return preview;
            }
            if (AssetPreview.IsLoadingAssetPreview(prefab.GetInstanceID()))
            {
                repaintForPreview = true;
            }
            return AssetPreview.GetMiniThumbnail(prefab);
        }

        // ---------------------------------------------------------------- catalog

        private void FindOrCreateCatalog()
        {
            if (catalog != null)
            {
                return;
            }

            catalog = AssetDatabase.LoadAssetAtPath<TrackPieceCatalog>(DefaultCatalogPath);
            if (catalog != null)
            {
                return;
            }

            EnsureAssetFolder("Assets/TrackLayoutTool/Data");
            catalog = CreateInstance<TrackPieceCatalog>();
            AssetDatabase.CreateAsset(catalog, DefaultCatalogPath);
            AssetDatabase.SaveAssets();
        }

        private void AddFolderToCatalog(DefaultAsset folder, string prefix)
        {
            string path = AssetDatabase.GetAssetPath(folder);
            if (!AssetDatabase.IsValidFolder(path))
            {
                status = "That object is not a project folder.";
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { path });
            AddPrefabsToCatalog(guids.Select(guid => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid))), prefix);
        }

        private void AddPrefabsToCatalog(IEnumerable<GameObject> prefabs, string prefix)
        {
            FindOrCreateCatalog();
            Undo.RecordObject(catalog, "Add Prefabs To Palette");
            int added = 0;
            foreach (GameObject prefab in prefabs.Where(item => item != null))
            {
                if (catalog.Entries.Any(entry => entry.prefab == prefab))
                {
                    continue;
                }

                string id = NormalizeId(prefix + prefab.name);
                if (string.IsNullOrEmpty(id))
                {
                    id = "prefab";
                }
                id = MakeUniqueId(id);
                catalog.Entries.Add(new TrackPieceCatalogEntry { id = id, category = PieceCategory.Track, prefab = prefab });
                added++;
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            status = $"Added {added} prefab(s) to the palette.";
        }

        private void DeriveStepsFromPrefab(TrackPieceCatalogEntry entry)
        {
            if (entry == null || entry.prefab == null)
            {
                status = "Nothing to derive from.";
                return;
            }

            Bounds bounds = PrefabBounds(entry.prefab);
            Undo.RecordObject(catalog, "Derive Prefab Steps");
            entry.gridOverride = Round2(Mathf.Max(bounds.size.x, bounds.size.z));
            entry.heightOverride = Round2(bounds.size.y);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            status = $"Derived from bounds of {entry.id}: grid {entry.gridOverride:0.##}, height {entry.heightOverride:0.##}. Tweak if wrong.";
        }

        private static float Round2(float value)
        {
            return Mathf.Round(value * 100f) / 100f;
        }

        private static Bounds PrefabBounds(GameObject prefab)
        {
            if (prefab == null)
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            bounds.center -= prefab.transform.position;
            return bounds;
        }

        // ---------------------------------------------------------------- place / edit

        private GameObject PlacePiece(TrackPieceCatalogEntry entry, Vector3 position, Quaternion rotation)
        {
            Transform root = GetOrCreateRoot();
            GameObject instance = PrefabUtility.InstantiatePrefab(entry.prefab, root) as GameObject;
            if (instance == null)
            {
                instance = Instantiate(entry.prefab, root);
            }

            Undo.RegisterCreatedObjectUndo(instance, "Place Prefab");
            instance.name = entry.id;
            instance.transform.SetPositionAndRotation(position, rotation);
            TrackPieceIdentity identity = instance.GetComponent<TrackPieceIdentity>();
            if (identity == null)
            {
                identity = Undo.AddComponent<TrackPieceIdentity>(instance);
            }
            identity.pieceId = entry.id;
            identity.category = entry.category;
            Selection.activeGameObject = instance;
            status = $"Placed {entry.id} at {position}.";
            return instance;
        }

        private void SnapSelection()
        {
            // Position only. Independently rounding the three components of transform.eulerAngles would
            // visibly re-orient a tilted piece (canonical decomposition), so rotation is left alone.
            // Steps come from each object's OWN catalog entry, not from the highlighted palette tile.
            foreach (Transform selected in Selection.transforms)
            {
                Undo.RecordObject(selected, "Snap Prefab");
                selected.position = SnapPoint(selected.position, EntryForPlaced(selected));
            }
            status = "Snapped selected positions to each prefab's lattice. Rotation untouched.";
        }

        private void FlattenSelection()
        {
            foreach (Transform selected in Selection.transforms)
            {
                Undo.RecordObject(selected, "Flatten Prefab");
                float yaw = Snap(selected.eulerAngles.y, rotationStep);
                selected.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            status = "Flattened selected: pitch/roll zeroed, yaw snapped.";
        }

        private void RotateSelection(float degrees)
        {
            foreach (Transform selected in Selection.transforms)
            {
                Undo.RecordObject(selected, "Rotate Prefab");
                selected.Rotate(Vector3.up, degrees, Space.World);
            }
        }

        private Transform GetOrCreateRoot()
        {
            GameObject root = GameObject.Find(LayoutRootName);
            if (root == null)
            {
                root = new GameObject(LayoutRootName);
                Undo.RegisterCreatedObjectUndo(root, "Create Prefab Layout Root");
            }
            return root.transform;
        }

        // ---------------------------------------------------------------- save / load

        private void SaveLayout()
        {
            TrackLayoutFile data = new()
            {
                layoutName = SafeLayoutName(),
                pieces = CaptureRecords()
            };
            string path = $"{LayoutFolder}/{data.layoutName}.layout.json";
            SaveJson(path, JsonUtility.ToJson(data, true));
            status = $"Saved {data.pieces.Count} prefab(s) to {path}.";
        }

        private List<PlacedPieceRecord> CaptureRecords()
        {
            Transform root = GameObject.Find(LayoutRootName)?.transform;
            List<PlacedPieceRecord> records = new();
            if (root == null)
            {
                return records;
            }

            foreach (TrackPieceIdentity identity in root.GetComponentsInChildren<TrackPieceIdentity>(true))
            {
                records.Add(new PlacedPieceRecord
                {
                    pieceId = identity.pieceId,
                    position = identity.transform.position,
                    rotationEuler = identity.transform.eulerAngles,
                    scale = identity.transform.localScale
                });
            }
            return records;
        }

        private void LoadLayoutWithDialog()
        {
            string absolute = EditorUtility.OpenFilePanel("Load layout JSON", AbsoluteLayoutFolder(), "json");
            if (string.IsNullOrEmpty(absolute)) return;
            TrackLayoutFile data = JsonUtility.FromJson<TrackLayoutFile>(File.ReadAllText(absolute));
            if (data == null) { status = "Could not parse layout JSON."; return; }
            ClearLayout();
            BuildRecords(data.pieces);
            if (!string.IsNullOrWhiteSpace(data.layoutName)) layoutName = data.layoutName;
            status = $"Loaded {data.pieces?.Count ?? 0} prefab(s).";
        }

        private void BuildRecords(IEnumerable<PlacedPieceRecord> records)
        {
            if (catalog == null) FindOrCreateCatalog();
            foreach (PlacedPieceRecord record in records ?? Enumerable.Empty<PlacedPieceRecord>())
            {
                TrackPieceCatalogEntry entry = catalog.FindEntryById(record.pieceId);
                if (entry == null || entry.prefab == null)
                {
                    Debug.LogWarning($"Prefab Builder: no prefab mapping for '{record.pieceId}'.");
                    continue;
                }

                GameObject instance = PlacePiece(entry, record.position, Quaternion.Euler(record.rotationEuler));
                if (instance != null)
                {
                    instance.transform.localScale = record.scale;
                }
            }
        }

        private void ClearLayout()
        {
            GameObject root = GameObject.Find(LayoutRootName);
            if (root == null) return;
            Undo.DestroyObjectImmediate(root);
            status = $"Cleared {LayoutRootName}. Undo is available.";
        }

        private static void SaveJson(string assetPath, string json)
        {
            EnsureAssetFolder(LayoutFolder);
            string absolute = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
            File.WriteAllText(absolute, json);
            AssetDatabase.Refresh();
        }

        private static string AbsoluteLayoutFolder()
        {
            EnsureAssetFolder(LayoutFolder);
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, LayoutFolder);
        }

        private string SafeLayoutName()
        {
            string result = NormalizeId(layoutName);
            if (string.IsNullOrWhiteSpace(result)) result = "unnamed_layout";
            layoutName = result;
            return result;
        }

        private string MakeUniqueId(string baseId)
        {
            string candidate = baseId;
            int suffix = 2;
            while (catalog.Entries.Any(entry => string.Equals(entry.id, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{baseId}_{suffix++}";
            }
            return candidate;
        }

        private static string NormalizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            char[] chars = value.Trim().ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_')
                .ToArray();
            string result = new(chars);
            while (result.Contains("__")) result = result.Replace("__", "_");
            return result.Trim('_');
        }

        internal static void EnsureAssetFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
