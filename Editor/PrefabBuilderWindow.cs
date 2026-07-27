using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrefabBuilder.Editor
{
    /// <summary>
    /// Prefab-agnostic layout builder. Drag in any prefabs, place them on a 3D lattice, and
    /// save/load the whole placed hierarchy as a single JSON file.
    ///
    /// The build lattice lives in the LAYOUT ROOT's local space, and layouts are saved in that same
    /// space. Moving, rotating or scaling the root therefore relocates the whole layout rigidly
    /// instead of desynchronising placement from what gets saved.
    /// </summary>
    public sealed class PrefabBuilderWindow : EditorWindow
    {
        private const string LayoutRootName = "PREFAB_LAYOUT";
        private const string DataRoot = "Assets/PrefabBuilder";
        private const string DataFolder = DataRoot + "/Data";
        private const string LayoutFolder = DataRoot + "/Layouts";
        private const string DefaultCatalogPath = DataFolder + "/PrefabBuilderCatalog.asset";
        private const string GhostName = "__PrefabBuilderGhost";

        [SerializeField] private PrefabCatalog catalog;
        [SerializeField] private DefaultAsset prefabFolder;
        [SerializeField] private string idPrefix = "";
        [SerializeField] private string layoutName = "my_layout";
        [SerializeField] private float gridSize = 15f;
        [SerializeField] private float heightStep = 1f;
        // Off by default: quantizing a surface hit by a height step that does not match the geometry
        // (e.g. step 1 on a 0.2-thick road) would drag the piece back down to y = 0.
        [SerializeField] private bool snapY;
        // Off by default: surface snap picks against every renderer, so a ground plane (which sits
        // under the cursor almost everywhere) would win every time and silently override Plane Y.
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
            PrefabBuilderWindow window = GetWindow<PrefabBuilderWindow>("Prefab Builder");
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += DuringSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyGhost;

            // Load only. Creating the catalog here would spray Assets/PrefabBuilder/Data into the
            // project the moment somebody merely opens the window to look at it; the asset is
            // created on the first action that actually needs it instead.
            catalog = AssetDatabase.LoadAssetAtPath<PrefabCatalog>(DefaultCatalogPath);
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

            DrawRootWarnings();
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

        // ---------------------------------------------------------------- layout root

        /// <summary>
        /// Every root object of every loaded scene is walked, including inactive ones.
        /// GameObject.Find would skip an inactive root and silently start a second layout on top of
        /// the first, and it cannot see into additively loaded scenes at all.
        /// </summary>
        private static List<GameObject> FindRoots()
        {
            List<GameObject> matches = new();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                {
                    foreach (Transform candidate in sceneRoot.GetComponentsInChildren<Transform>(true))
                    {
                        if (candidate.gameObject.name == LayoutRootName)
                        {
                            matches.Add(candidate.gameObject);
                        }
                    }
                }
            }
            return matches;
        }

        // FindRoots walks every transform of every loaded scene, and the placement path needs the
        // root several times per repaint. Cache it: the Unity null check already reports a destroyed
        // root as null, and the name check catches a rename, so a stale cache cannot survive either.
        private static GameObject cachedRoot;

        private static GameObject FindRootOrNull()
        {
            if (cachedRoot != null && cachedRoot.name == LayoutRootName)
            {
                return cachedRoot;
            }

            List<GameObject> matches = FindRoots();
            cachedRoot = matches.Count > 0 ? matches[0] : null;
            return cachedRoot;
        }

        private static Transform GetOrCreateRoot()
        {
            GameObject root = FindRootOrNull();
            if (root == null)
            {
                root = new GameObject(LayoutRootName);
                Undo.RegisterCreatedObjectUndo(root, "Create Prefab Layout Root");
                cachedRoot = root;
            }
            return root.transform;
        }

        /// <summary>
        /// The frame of reference for both the build lattice and the saved layout. Identity when no
        /// root exists yet, so placement before the first piece behaves exactly like world space.
        /// </summary>
        private static Matrix4x4 RootToWorld()
        {
            GameObject root = FindRootOrNull();
            return root == null ? Matrix4x4.identity : root.transform.localToWorldMatrix;
        }

        private static Quaternion RootRotation()
        {
            GameObject root = FindRootOrNull();
            return root == null ? Quaternion.identity : root.transform.rotation;
        }

        private static Vector3 RootLossyScale()
        {
            GameObject root = FindRootOrNull();
            return root == null ? Vector3.one : root.transform.lossyScale;
        }

        // All three components are compared in WORLD space, because the world transform is what
        // RootToWorld feeds the lattice. Comparing localScale here would call a root at identity
        // while it sat scaled under a scaled parent.
        private static bool IsIdentity(Transform transform)
        {
            return transform.position.sqrMagnitude < 1e-6f
                   && Quaternion.Angle(transform.rotation, Quaternion.identity) < 1e-3f
                   && (transform.lossyScale - Vector3.one).sqrMagnitude < 1e-6f;
        }

        // Snapshotted on Layout and reused on Repaint. Recomputing per event would let the number of
        // IMGUI controls change mid-pass if the user nudged the root between Layout and Repaint,
        // which desyncs the control-ID counter for everything drawn afterwards.
        private int rootCount;
        private bool rootIsOffset;
        private string rootOffsetDescription = string.Empty;

        private void DrawRootWarnings()
        {
            if (Event.current.type == EventType.Layout)
            {
                List<GameObject> roots = FindRoots();
                rootCount = roots.Count;

                // Re-point the cache at the authoritative first match. Without this, a second
                // PREFAB_LAYOUT could leave the cache on one object while this warning describes
                // another, so the "Reset root" button would move something other than the root the
                // placement math is actually using.
                cachedRoot = rootCount > 0 ? roots[0] : null;

                rootIsOffset = rootCount > 0 && !IsIdentity(roots[0].transform);
                if (rootIsOffset)
                {
                    Transform root = roots[0].transform;
                    // lossyScale, not localScale: IsIdentity judges the WORLD transform, so a root
                    // sitting unscaled under a scaled parent must not report "scale (1, 1, 1)"
                    // while the banner insists it is offset.
                    rootOffsetDescription = $"position {root.position}, rotation {root.eulerAngles}, scale {root.lossyScale}";
                }
            }

            if (rootCount > 1)
            {
                EditorGUILayout.HelpBox(
                    $"{rootCount} objects named {LayoutRootName} exist in the loaded scenes. Only the first is used, so pieces under the others will NOT be saved. Rename or merge them.",
                    MessageType.Error);
            }

            if (!rootIsOffset)
            {
                return;
            }

            EditorGUILayout.HelpBox(
                $"{LayoutRootName} is not at the origin ({rootOffsetDescription}).\n\n" +
                "This is supported: the grid and the saved layout both follow the root, so the layout stays intact and moves with it. " +
                "But snapping now happens along the root's axes, not the world axes. Reset the root if that was not intentional.",
                MessageType.Warning);

            if (GUILayout.Button("Reset root transform to origin"))
            {
                GameObject rootObject = FindRootOrNull();
                if (rootObject != null)
                {
                    Transform root = rootObject.transform;
                    Undo.RecordObject(root, "Reset Prefab Layout Root");
                    root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    root.localScale = Vector3.one;
                    status = $"Reset {LayoutRootName} to the origin. The layout moved with it. Undo is available.";
                    SceneView.RepaintAll();
                }
            }
        }

        // ---------------------------------------------------------------- palette

        private void DrawPaletteSection()
        {
            EditorGUILayout.LabelField("1. PREFAB PALETTE", EditorStyles.boldLabel);
            catalog = (PrefabCatalog)EditorGUILayout.ObjectField("Catalog", catalog, typeof(PrefabCatalog), false);
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

            List<PrefabCatalogEntry> choices = GetChoices();
            if (choices.Count == 0)
            {
                EditorGUILayout.HelpBox("No prefabs in the palette yet. Drag prefabs into the box above.", MessageType.Warning);
            }
            else
            {
                DrawPrefabGrid(choices);

                PrefabCatalogEntry entry = CurrentEntry();
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
                    PlacePiece(entry, SnapWorldPoint(pivot, entry), PlacementRotation());
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

        private void DrawPerPrefabSteps(PrefabCatalogEntry entry)
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

            // The typed name is not the filename. Showing the resolved name up front is what stops
            // "My Track" and "my-track" from quietly turning out to be the same file.
            string resolved = NormalizeId(layoutName);
            if (!string.IsNullOrEmpty(resolved) && resolved != layoutName)
            {
                EditorGUILayout.LabelField($"Saves as: {resolved}.layout.json", EditorStyles.miniLabel);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("SAVE LAYOUT")) SaveLayout();
            if (GUILayout.Button("LOAD LAYOUT")) LoadLayoutWithDialog();
            if (GUILayout.Button("CLEAR LAYOUT")) ClearLayoutWithConfirm();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField($"Files: {LayoutFolder}/*.layout.json", EditorStyles.miniLabel);
        }

        // ---------------------------------------------------------------- scene view

        private void DuringSceneGui(SceneView sceneView)
        {
            // Allocate unconditionally and BEFORE any early-out. GetControlID must run the same
            // number of times on every event pass, or IMGUI's control-ID counter desyncs between
            // Layout and MouseDown/Repaint and every later handle in the scene hit-tests against
            // the wrong control.
            int placementControl = GUIUtility.GetControlID(FocusType.Passive);

            if (!placementMode)
            {
                DestroyGhost();
                return;
            }

            PrefabCatalogEntry entry = CurrentEntry();
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
        private bool HandleKey(Event evt, SceneView sceneView, PrefabCatalogEntry entry)
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

        private bool TryResolvePlacementPoint(Event evt, PrefabCatalogEntry entry, out Vector3 point)
        {
            Matrix4x4 rootToWorld = RootToWorld();
            Matrix4x4 worldToRoot = rootToWorld.inverse;
            float grid = GridFor(entry);

            // Surface snap is opt-in, and it is a HEIGHT SOURCE, not an add-on: when it is on, the
            // geometry under the cursor decides Y and Plane Y only covers empty space. When it is
            // off, Plane Y is authoritative. Defaulting it on made Plane Y look broken, because the
            // ground plane is under the cursor almost everywhere and always answered y = 0.
            if (surfaceSnap && TrySurfacePoint(evt, out Vector3 surfacePoint))
            {
                Vector3 local = worldToRoot.MultiplyPoint3x4(surfacePoint);
                // The Y came from real geometry, so it is already meaningful. Only quantize it if
                // the user explicitly asked for it, and then with THIS prefab's height step.
                float localY = snapY ? Snap(local.y, HeightFor(entry)) : local.y;
                point = rootToWorld.MultiplyPoint3x4(new Vector3(Snap(local.x, grid), localY, Snap(local.z, grid)));
                return true;
            }

            // Otherwise fall back to the build plane. It is defined in root space, so the whole
            // lattice tilts and slides with the root instead of drifting away from it.
            Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            Vector3 planeOrigin = rootToWorld.MultiplyPoint3x4(new Vector3(0f, placementHeight, 0f));
            Vector3 planeNormal = rootToWorld.MultiplyVector(Vector3.up).normalized;
            Plane plane = new(planeNormal, planeOrigin);
            if (!plane.Raycast(ray, out float distance))
            {
                point = default;
                return false;
            }

            Vector3 hitLocal = worldToRoot.MultiplyPoint3x4(ray.GetPoint(distance));
            // Plane Y is already deliberate, so do NOT re-quantize it: with Plane Y = 2.5 and
            // Height step = 1, re-snapping would silently drag it to 2.
            point = rootToWorld.MultiplyPoint3x4(new Vector3(Snap(hitLocal.x, grid), placementHeight, Snap(hitLocal.z, grid)));
            return true;
        }

        private bool TrySurfacePoint(Event evt, out Vector3 point)
        {
            // HandleUtility.ignoreRaySnapObjects is internal to UnityEditor, so we cannot ask the
            // snap to skip the ghost. Hide the ghost for the duration of the cast instead - these
            // casts hit renderer geometry (not just colliders), so the ghost would otherwise be the
            // first thing hit and the piece would climb on top of itself every frame.
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

        private void DrawPlacementGizmos(PrefabCatalogEntry entry, Vector3 point, Quaternion rotation)
        {
            Bounds bounds = PrefabBounds(entry.prefab);

            // Scaled to match what will actually be placed, for the same reason the ghost is.
            Handles.color = Color.cyan;
            using (new Handles.DrawingScope(Matrix4x4.TRS(point, rotation, RootLossyScale())))
            {
                Handles.DrawWireCube(bounds.center, bounds.size);
            }

            // Depth perception in a perspective view is genuinely bad; the drop line is the cheap
            // fix. It drops to the root's ground plane, which is the one the lattice uses.
            Matrix4x4 rootToWorld = RootToWorld();
            Vector3 local = rootToWorld.inverse.MultiplyPoint3x4(point);
            Vector3 ground = rootToWorld.MultiplyPoint3x4(new Vector3(local.x, 0f, local.z));
            Handles.color = new Color(0f, 1f, 1f, 0.45f);
            Handles.DrawDottedLine(point, ground, 3f);
            Handles.DrawWireDisc(ground, rootToWorld.MultiplyVector(Vector3.up).normalized, Mathf.Max(0.2f, GridFor(entry) * 0.08f));

            Handles.color = Color.white;
            Handles.Label(
                point + Vector3.up * (bounds.size.y * 0.5f + 0.5f),
                $"{entry.id}\ny = {local.y:0.##}\nYaw {placementYaw:0}  Pitch {placementPitch:0}  Roll {placementRoll:0}");
        }

        // ---------------------------------------------------------------- ghost preview

        private void UpdateGhost(PrefabCatalogEntry entry, Vector3 position, Quaternion rotation)
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

                // Make the clone inert. In Play mode its scripts and physics would otherwise really
                // run, and a Rigidbody would let the preview fall through the world.
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
            // The ghost is also never parented under the layout root, so CaptureRecords cannot see it.
            foreach (Transform child in ghost.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }

            if (!ghost.activeSelf)
            {
                ghost.SetActive(true);
            }
            ghost.transform.SetPositionAndRotation(position, rotation);

            // The ghost is not parented under the root, so it does not inherit the root's scale the
            // way the placed instance will. Apply it by hand or the preview is the wrong size under
            // a scaled root - you would be aiming with something that is not what you get. This is
            // exact for a uniformly scaled root; a non-uniformly scaled and rotated root shears its
            // children in a way an unparented object cannot reproduce, so the preview is only close.
            ghost.transform.localScale = Vector3.Scale(RootLossyScale(), entry.prefab.transform.localScale);
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

        // Placement angles are relative to the root, so a rotated root carries its layout's
        // orientation with it rather than shearing the lattice away from the pieces.
        private Quaternion PlacementRotation()
        {
            return RootRotation() * Quaternion.Euler(placementPitch, placementYaw, placementRoll);
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

        private float GridFor(PrefabCatalogEntry entry)
        {
            return StepOrDefault(entry?.gridOverride ?? 0f, gridSize);
        }

        private float HeightFor(PrefabCatalogEntry entry)
        {
            return StepOrDefault(entry?.heightOverride ?? 0f, heightStep);
        }

        private float TiltFor(PrefabCatalogEntry entry)
        {
            return StepOrDefault(entry?.tiltOverride ?? 0f, tiltStep);
        }

        /// <summary>Snap a world point onto the root-space lattice and return it in world space.</summary>
        private Vector3 SnapWorldPoint(Vector3 worldPoint, PrefabCatalogEntry entry)
        {
            Matrix4x4 rootToWorld = RootToWorld();
            Vector3 local = rootToWorld.inverse.MultiplyPoint3x4(worldPoint);
            float grid = GridFor(entry);
            Vector3 snapped = new(
                Snap(local.x, grid),
                snapY ? Snap(local.y, HeightFor(entry)) : local.y,
                Snap(local.z, grid));
            return rootToWorld.MultiplyPoint3x4(snapped);
        }

        // The catalog entry a PLACED object came from, so "Snap selected" uses that object's own
        // steps rather than whichever tile happens to be highlighted in the palette.
        private PrefabCatalogEntry EntryForPlaced(Transform placed)
        {
            if (catalog == null) return null;
            PlacedPrefabIdentity identity = placed.GetComponent<PlacedPrefabIdentity>();
            if (identity == null || string.IsNullOrEmpty(identity.pieceId)) return null;
            return catalog.FindEntryById(identity.pieceId);
        }

        // ---------------------------------------------------------------- selection

        private List<PrefabCatalogEntry> GetChoices()
        {
            if (catalog == null)
            {
                return new List<PrefabCatalogEntry>();
            }

            return catalog.Entries
                .Where(entry => entry.prefab != null)
                .OrderBy(entry => entry.id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // The selected prefab is tracked by ID, not by index: the choice list is sorted by id, so an
        // index would silently re-point at a different prefab as soon as one sorting earlier is added.
        private PrefabCatalogEntry CurrentEntry()
        {
            List<PrefabCatalogEntry> choices = GetChoices();
            if (choices.Count == 0)
            {
                return null;
            }

            PrefabCatalogEntry entry = string.IsNullOrEmpty(selectedPieceId)
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

        private void DrawPrefabGrid(List<PrefabCatalogEntry> choices)
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
            PrefabCatalogEntry removeEntry = null;

            for (int i = 0; i < choices.Count; i++)
            {
                if (i % columns == 0)
                {
                    if (i > 0) EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }

                PrefabCatalogEntry entry = choices[i];
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
                status = $"Removed {removeEntry.id} from the palette. Layouts that reference it will now report it as missing on load.";

                // CurrentEntry self-heals to the first remaining prefab if this was the selected one.
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

        /// <summary>
        /// Returns the catalog, creating the asset only if one is genuinely needed. Callers that
        /// merely display the palette must not call this - see OnEnable.
        /// </summary>
        private PrefabCatalog EnsureCatalog()
        {
            if (catalog != null)
            {
                return catalog;
            }

            catalog = AssetDatabase.LoadAssetAtPath<PrefabCatalog>(DefaultCatalogPath);
            if (catalog != null)
            {
                return catalog;
            }

            EnsureAssetFolder(DataFolder);
            catalog = CreateInstance<PrefabCatalog>();
            AssetDatabase.CreateAsset(catalog, DefaultCatalogPath);
            AssetDatabase.SaveAssets();
            return catalog;
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
            EnsureCatalog();
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
                catalog.Entries.Add(new PrefabCatalogEntry { id = id, prefab = prefab });
                added++;
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            status = $"Added {added} prefab(s) to the palette.";
        }

        private void DeriveStepsFromPrefab(PrefabCatalogEntry entry)
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

        private GameObject PlacePiece(PrefabCatalogEntry entry, Vector3 position, Quaternion rotation)
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
            PlacedPrefabIdentity identity = instance.GetComponent<PlacedPrefabIdentity>();
            if (identity == null)
            {
                identity = Undo.AddComponent<PlacedPrefabIdentity>(instance);
            }
            identity.pieceId = entry.id;
            Selection.activeGameObject = instance;
            status = $"Placed {entry.id} at {position}.";
            return instance;
        }

        private void SnapSelection()
        {
            // Position only. Independently rounding the three components of transform.eulerAngles
            // would visibly re-orient a tilted piece (canonical decomposition), so rotation is left
            // alone. Steps come from each object's OWN catalog entry, not the highlighted tile.
            foreach (Transform selected in Selection.transforms)
            {
                Undo.RecordObject(selected, "Snap Prefab");
                selected.position = SnapWorldPoint(selected.position, EntryForPlaced(selected));
            }
            status = "Snapped selected positions to each prefab's lattice. Rotation untouched.";
        }

        private void FlattenSelection()
        {
            // Yaw is flattened in ROOT space so that "flat" means level with the layout, which is
            // what the grid and the saved file both use.
            Quaternion rootRotation = RootRotation();
            Quaternion inverseRoot = Quaternion.Inverse(rootRotation);
            foreach (Transform selected in Selection.transforms)
            {
                Undo.RecordObject(selected, "Flatten Prefab");
                float localYaw = (inverseRoot * selected.rotation).eulerAngles.y;
                selected.rotation = rootRotation * Quaternion.Euler(0f, Snap(localYaw, rotationStep), 0f);
            }
            status = "Flattened selected: pitch/roll zeroed, yaw snapped.";
        }

        private void RotateSelection(float degrees)
        {
            Vector3 axis = RootRotation() * Vector3.up;
            foreach (Transform selected in Selection.transforms)
            {
                Undo.RecordObject(selected, "Rotate Prefab");
                selected.Rotate(axis, degrees, Space.World);
            }
        }

        // ---------------------------------------------------------------- save

        private void SaveLayout()
        {
            string resolvedName = SafeLayoutName();
            string path = $"{LayoutFolder}/{resolvedName}.layout.json";
            string absolute = ProjectRelativeToAbsolute(path);

            List<PlacedPrefabRecord> records = CaptureRecords();
            if (records.Count == 0 && !EditorUtility.DisplayDialog(
                    "Save an empty layout?",
                    $"No placed prefabs were found under {LayoutRootName}, so '{resolvedName}.layout.json' would be saved with zero pieces.\n\nSave anyway?",
                    "Save empty layout",
                    "Cancel"))
            {
                status = "Save cancelled. Nothing was written.";
                return;
            }

            // Requirement: never clobber an existing layout silently. The dialog names the resolved
            // FILE, not the typed name, because "My Track" and "my-track" both resolve to
            // my_track.layout.json and that collision is otherwise invisible.
            if (File.Exists(absolute))
            {
                int existingCount = CountPiecesInFile(absolute);
                string existingDescription = existingCount switch
                {
                    // -1 unreadable, -2 parsed but is not a layout file. Reporting "0 prefabs" for a
                    // JSON file that simply is not a layout would make clobbering it sound harmless.
                    -1 => "Its contents could not be read, so it may not be a layout file.",
                    -2 => "It does not look like a Prefab Builder layout file. Overwriting will destroy whatever it is.",
                    _ => $"It currently holds {existingCount} prefab(s)."
                };

                bool overwrite = EditorUtility.DisplayDialog(
                    "Overwrite existing layout?",
                    $"{path} already exists.\n\n{existingDescription}\n\n" +
                    $"Saving replaces it with the {records.Count} prefab(s) currently under {LayoutRootName}. This cannot be undone.",
                    "Overwrite",
                    "Cancel");

                if (!overwrite)
                {
                    status = $"Save cancelled. {path} was left unchanged.";
                    return;
                }
            }

            LayoutFile data = new()
            {
                schemaVersion = LayoutFile.CurrentSchemaVersion,
                layoutName = resolvedName,
                pieces = records
            };

            try
            {
                EnsureAssetFolder(LayoutFolder);
                File.WriteAllText(absolute, JsonUtility.ToJson(data, true));
            }
            catch (Exception exception)
            {
                // A read-only file, a lock held by another program, or a path-length limit all land
                // here. Saying nothing would look exactly like a successful save.
                status = $"SAVE FAILED: {exception.Message} Nothing was written to {path}.";
                Debug.LogException(exception);
                return;
            }

            AssetDatabase.Refresh();
            int nested = CountNestedPieces();
            status = nested > 0
                ? $"Saved {records.Count} prefab(s) to {path}. {nested} nested prefab(s) were NOT saved - only direct children of {LayoutRootName} are included."
                : $"Saved {records.Count} prefab(s) to {path}.";
        }

        /// <summary>
        /// Piece transforms are captured in the layout root's LOCAL space. Capturing world position
        /// while capturing local scale (as earlier builds did) meant a moved, rotated or scaled root
        /// could not round-trip: reload re-applied the root offset on top of coordinates that
        /// already contained it.
        ///
        /// Only DIRECT children are captured, because that is the only shape the loader can rebuild:
        /// it restores every record as a direct child. Recursing would save a nested piece and then
        /// silently re-parent it flat on the next load, changing its pose and inflating the count.
        /// </summary>
        private static List<PlacedPrefabRecord> CaptureRecords()
        {
            List<PlacedPrefabRecord> records = new();
            GameObject rootObject = FindRootOrNull();
            if (rootObject == null)
            {
                return records;
            }

            Transform root = rootObject.transform;
            foreach (Transform piece in root)
            {
                PlacedPrefabIdentity identity = piece.GetComponent<PlacedPrefabIdentity>();
                if (identity == null)
                {
                    continue;
                }

                records.Add(new PlacedPrefabRecord
                {
                    pieceId = identity.pieceId,
                    localPosition = piece.localPosition,
                    localRotationEuler = piece.localEulerAngles,
                    localScale = piece.localScale
                });
            }
            return records;
        }

        /// <summary>
        /// Placed pieces that are NOT direct children of the root, and so cannot be saved. Reported
        /// rather than silently dropped.
        /// </summary>
        private static int CountNestedPieces()
        {
            GameObject rootObject = FindRootOrNull();
            if (rootObject == null)
            {
                return 0;
            }

            Transform root = rootObject.transform;
            int total = root.GetComponentsInChildren<PlacedPrefabIdentity>(true).Length;
            int direct = 0;
            foreach (Transform piece in root)
            {
                if (piece.GetComponent<PlacedPrefabIdentity>() != null)
                {
                    direct++;
                }
            }
            return total - direct;
        }

        /// <summary>
        /// Removes the placed prefabs and nothing else. The ROOT ITSELF SURVIVES - destroying it and
        /// letting GetOrCreateRoot build a fresh one would silently reset the layout's position,
        /// rotation, scale and parent, which is exactly the corruption the root-local format exists
        /// to prevent. Anything the user parented under the root by hand also survives.
        /// </summary>
        private static int RemovePlacedPieces()
        {
            GameObject rootObject = FindRootOrNull();
            if (rootObject == null)
            {
                return 0;
            }

            List<GameObject> doomed = new();
            foreach (Transform piece in rootObject.transform)
            {
                if (piece.GetComponent<PlacedPrefabIdentity>() != null)
                {
                    doomed.Add(piece.gameObject);
                }
            }

            foreach (GameObject piece in doomed)
            {
                Undo.DestroyObjectImmediate(piece);
            }
            return doomed.Count;
        }

        /// <summary>
        /// Piece count, or -1 if the file could not be read/parsed, or -2 if it parsed but carries
        /// none of the fields a layout file has.
        /// </summary>
        private static int CountPiecesInFile(string absolutePath)
        {
            try
            {
                LayoutFile existing = JsonUtility.FromJson<LayoutFile>(File.ReadAllText(absolutePath));
                if (existing == null)
                {
                    return -1;
                }
                // JsonUtility does not fail on unrelated JSON: it runs the field initialisers and
                // then overwrites only the keys it recognises, so 'pieces' comes back as an empty
                // list rather than null. schemaVersion deliberately has NO initialiser, so it comes
                // back as 0. Every file this tool writes carries a version, which makes
                // "no version AND no pieces AND no name" a reliable signal that this JSON is
                // something else entirely rather than an empty layout that we wrote.
                if (existing.pieces == null ||
                    (existing.schemaVersion == 0 && existing.pieces.Count == 0 && string.IsNullOrEmpty(existing.layoutName)))
                {
                    return -2;
                }
                return existing.pieces.Count;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        // ---------------------------------------------------------------- load

        /// <summary>
        /// Cheap shape probe for the old companion "prop addendum" files, which store a "props"
        /// array alongside a "trackFile" reference and carry no "pieces" at all.
        /// </summary>
        private static bool LooksLikePropAddendum(string json)
        {
            return json != null
                   && json.Contains("\"props\"")
                   && json.Contains("\"trackFile\"")
                   && !json.Contains("\"pieces\"");
        }

        /// <summary>
        /// Re-reads a schemaVersion 0/1 file through the legacy field names and rewrites
        /// <paramref name="data"/>.pieces in the current shape.
        ///
        /// Legacy coordinates are WORLD space, so they are converted into the current root's local
        /// space. That reproduces the layout at the exact world position it was saved at, whatever
        /// the root happens to be doing now - which is the whole point of keeping the format.
        /// </summary>
        private static bool TryReadLegacy(string json, LayoutFile data, out bool converted)
        {
            converted = false;
            LegacyLayoutFile legacy;
            try
            {
                legacy = JsonUtility.FromJson<LegacyLayoutFile>(json);
            }
            catch (Exception)
            {
                return false;
            }

            if (legacy?.pieces == null)
            {
                return false;
            }

            GameObject rootObject = FindRootOrNull();
            Transform root = rootObject != null ? rootObject.transform : null;

            List<PlacedPrefabRecord> migrated = new();
            foreach (LegacyPlacedPieceRecord old in legacy.pieces)
            {
                if (old == null)
                {
                    continue;
                }

                Quaternion worldRotation = Quaternion.Euler(old.rotationEuler);
                migrated.Add(new PlacedPrefabRecord
                {
                    pieceId = old.pieceId,
                    localPosition = root != null ? root.InverseTransformPoint(old.position) : old.position,
                    localRotationEuler = root != null
                        ? (Quaternion.Inverse(root.rotation) * worldRotation).eulerAngles
                        : old.rotationEuler,
                    localScale = old.scale == Vector3.zero ? Vector3.one : old.scale
                });
            }

            data.pieces = migrated;
            converted = true;
            return true;
        }

        /// <summary>
        /// Validates the whole file against the catalog BEFORE touching the scene. Earlier builds
        /// cleared the existing layout first and only then discovered that ids were unresolvable,
        /// which destroyed the user's work in exchange for nothing.
        /// </summary>
        private void LoadLayoutWithDialog()
        {
            string absolute = EditorUtility.OpenFilePanel("Load layout JSON", AbsoluteLayoutFolder(), "json");
            if (string.IsNullOrEmpty(absolute))
            {
                return;
            }

            LayoutFile data;
            string json;
            try
            {
                json = File.ReadAllText(absolute);
                data = JsonUtility.FromJson<LayoutFile>(json);
            }
            catch (Exception exception)
            {
                status = $"Could not read that file: {exception.Message} Scene unchanged.";
                return;
            }

            if (data == null)
            {
                status = "Could not parse that layout JSON. Scene unchanged.";
                return;
            }

            // Older files name their transform fields position/rotationEuler/scale. JsonUtility binds
            // by field name and drops what it does not recognise, so reading one of those through the
            // current shape yields a full set of records whose transforms are ALL zero - every piece
            // silently stacked on the origin and cheerfully reported as fully restored. Re-read it
            // through the legacy shape instead and convert.
            bool converted = false;
            if (data.schemaVersion <= 1)
            {
                if (!TryReadLegacy(json, data, out converted))
                {
                    status = "That file uses the older layout format but its contents could not be read. Scene unchanged.";
                    return;
                }
            }

            List<PlacedPrefabRecord> records = data.pieces ?? new List<PlacedPrefabRecord>();
            if (records.Count == 0)
            {
                // A prop addendum is a different file shape entirely - it carries "props", not
                // "pieces". Reporting "contains no prefabs" for a file holding eleven of them is
                // worse than no message at all, so name what it actually is.
                if (LooksLikePropAddendum(json))
                {
                    status = "That is a prop addendum file (it stores 'props'), not a layout file. Load the matching .track.json or .layout.json instead. Scene unchanged.";
                    return;
                }
                status = "That layout file contains no prefabs. Scene unchanged.";
                return;
            }

            // Load the catalog, but do NOT create one. An aborted load must leave the project
            // exactly as it found it, and a catalog that does not exist yet resolves nothing anyway.
            if (catalog == null)
            {
                catalog = AssetDatabase.LoadAssetAtPath<PrefabCatalog>(DefaultCatalogPath);
            }
            if (catalog == null)
            {
                // Name the ids. "Add the matching prefabs" is useless advice without saying which.
                string wanted = string.Join("\n", records
                    .Select(record => string.IsNullOrWhiteSpace(record.pieceId) ? "(blank id)" : record.pieceId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .Select(id => $"  - {id}"));

                EditorUtility.DisplayDialog(
                    "No prefab palette yet",
                    $"There is no catalog at {DefaultCatalogPath}, so none of the {records.Count} saved prefab id(s) can be resolved.\n\n" +
                    $"This layout needs prefabs with these ids:\n{wanted}\n\n" +
                    "Add them to the palette first. The scene has NOT been changed.",
                    "OK");
                status = "Load aborted: no prefab palette exists yet. Scene unchanged.";
                return;
            }

            // Pre-flight: resolve every id before anything is destroyed.
            List<PlacedPrefabRecord> resolvable = records.Where(record => catalog.CanResolve(record.pieceId)).ToList();
            List<string> missingSummary = records
                .Where(record => !catalog.CanResolve(record.pieceId))
                // Ids resolve case-insensitively, so group that way too. Ordinal grouping would list
                // "Road" and "road" as two separate missing ids with split counts.
                .GroupBy(record => string.IsNullOrWhiteSpace(record.pieceId) ? "(blank id)" : record.pieceId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => $"  - {group.Key}  x{group.Count()}")
                .OrderBy(line => line, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int missingCount = records.Count - resolvable.Count;

            if (resolvable.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Nothing in this layout can be restored",
                    $"None of the {records.Count} saved prefab id(s) are in the catalog:\n\n{string.Join("\n", missingSummary)}\n\n" +
                    "Add the matching prefabs to the palette first. The scene has NOT been changed.",
                    "OK");
                status = $"Load aborted: 0 of {records.Count} ids resolved. Scene unchanged.";
                Debug.LogWarning($"Prefab Builder: load aborted, no resolvable ids in {absolute}.");
                return;
            }

            int existingCount = CaptureRecords().Count;
            GameObject existingRoot = FindRootOrNull();
            bool rootIsOffsetNow = existingRoot != null && !IsIdentity(existingRoot.transform);

            List<string> message = new();
            if (existingCount > 0)
            {
                message.Add($"Loading replaces the current layout: {existingCount} placed prefab(s) under {LayoutRootName} will be removed. Anything else parented under {LayoutRootName} is left alone.");
            }
            if (missingCount > 0)
            {
                message.Add($"{missingCount} of {records.Count} saved prefab(s) cannot be restored because their ids are not in the catalog:\n{string.Join("\n", missingSummary)}");
            }

            // schemaVersion is read, not merely written.
            if (data.schemaVersion > LayoutFile.CurrentSchemaVersion)
            {
                message.Add($"This file reports schemaVersion {data.schemaVersion}, but this version of Prefab Builder understands up to {LayoutFile.CurrentSchemaVersion}. It was written by a newer version and may not load correctly.");
            }
            else if (converted)
            {
                string frame = rootIsOffsetNow
                    ? $" Its world-space coordinates have been converted into {LayoutRootName}'s current frame, so the pieces will land where they were originally saved."
                    : string.Empty;
                // Be exact about where a later save goes: SAVE LAYOUT always writes to the layout
                // folder under the current layout name, never back to the file that was opened.
                message.Add(
                    $"This file uses the older layout format (schemaVersion {data.schemaVersion}) and has been converted for loading.{frame}\n\n" +
                    $"The original file is left unchanged. Saving afterwards writes a NEW file to {LayoutFolder}/<layout name>.layout.json in the current format.");
            }

            if (message.Count > 0)
            {
                message.Add($"Proceed and restore {resolvable.Count} of {records.Count} prefab(s)?");
                if (!EditorUtility.DisplayDialog("Load layout?", string.Join("\n\n", message), "Load", "Cancel"))
                {
                    status = "Load cancelled. Scene unchanged.";
                    return;
                }
            }

            int restored;
            int removed;
            // One undo step for the whole load, so a half-finished rebuild is never something the
            // user has to unpick click by click.
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Load Prefab Layout");
            try
            {
                removed = RemovePlacedPieces();
                restored = BuildRecords(resolvable);
            }
            catch (Exception exception)
            {
                Undo.CollapseUndoOperations(undoGroup);
                status = $"Load failed part way through: {exception.Message} Press Ctrl+Z to restore the previous layout.";
                Debug.LogException(exception);
                return;
            }
            Undo.CollapseUndoOperations(undoGroup);

            // Requirement: report what actually happened, not what was asked for.
            string report = $"Restored {restored} of {records.Count} prefab(s). Removed {removed} previously placed prefab(s).";
            if (missingCount > 0)
            {
                report += $" Skipped {missingCount} with missing ids:\n{string.Join("\n", missingSummary)}";
                Debug.LogWarning($"Prefab Builder: {report}");
            }
            if (restored < resolvable.Count)
            {
                report += $" {resolvable.Count - restored} resolvable prefab(s) failed to instantiate - see the Console.";
            }

            if (!string.IsNullOrWhiteSpace(data.layoutName))
            {
                layoutName = data.layoutName;
            }
            status = report;
        }

        /// <summary>Returns how many prefabs were actually instantiated.</summary>
        private int BuildRecords(IEnumerable<PlacedPrefabRecord> records)
        {
            Transform root = GetOrCreateRoot();
            int restored = 0;

            foreach (PlacedPrefabRecord record in records ?? Enumerable.Empty<PlacedPrefabRecord>())
            {
                PrefabCatalogEntry entry = catalog.FindEntryById(record.pieceId);
                if (entry == null || entry.prefab == null)
                {
                    // Pre-flight already filtered these; reaching here means the catalog changed
                    // underneath us, so skip rather than throw.
                    Debug.LogWarning($"Prefab Builder: no prefab mapping for '{record.pieceId}'.");
                    continue;
                }

                // A zero scale is never something anyone meant to save, and it makes the piece
                // invisible - which reads as "the load lost my work". Treat it as unset.
                if (record.localScale == Vector3.zero)
                {
                    record.localScale = Vector3.one;
                }

                // Records are root-local, so build them in root-local space. Feeding these numbers
                // to a world-space setter is exactly the bug that made an offset root corrupt loads.
                Vector3 world = root.TransformPoint(record.localPosition);
                Quaternion rotation = root.rotation * Quaternion.Euler(record.localRotationEuler);

                GameObject instance = PlacePiece(entry, world, rotation);
                if (instance == null)
                {
                    Debug.LogWarning($"Prefab Builder: failed to instantiate '{record.pieceId}'.");
                    continue;
                }

                instance.transform.localScale = record.localScale;
                restored++;
            }

            return restored;
        }

        // ---------------------------------------------------------------- clear

        private void ClearLayoutWithConfirm()
        {
            GameObject root = FindRootOrNull();
            if (root == null)
            {
                status = $"No {LayoutRootName} in the scene. Nothing to clear.";
                return;
            }

            int count = CaptureRecords().Count;
            int nested = CountNestedPieces();
            // Anything under the root that is NOT a placed prefab is the user's own, so it is
            // counted separately and left behind rather than swept up silently.
            int others = root.transform.childCount - count;

            if (count == 0 && others == 0)
            {
                // Still ask. An empty root can carry a deliberately configured transform, which is
                // exactly the state the root-local format exists to preserve.
                if (!EditorUtility.DisplayDialog(
                        "Remove the empty layout root?",
                        $"{LayoutRootName} holds no placed prefabs. Removing it also discards its transform ({root.transform.position}, {root.transform.eulerAngles}, {root.transform.localScale}), which future loads would otherwise build against.\n\nUndo is available.",
                        "Remove it",
                        "Keep it"))
                {
                    status = $"Kept the empty {LayoutRootName}. Scene unchanged.";
                    return;
                }

                Undo.DestroyObjectImmediate(root);
                status = $"Removed the empty {LayoutRootName}. Undo is available.";
                return;
            }

            List<string> message = new()
            {
                $"This removes {count} placed prefab(s) from {LayoutRootName}."
            };
            if (others > 0)
            {
                message.Add($"{others} other object(s) parented under {LayoutRootName} will be LEFT IN PLACE, and so will {LayoutRootName} itself.");
            }
            else
            {
                message.Add($"{LayoutRootName} will be removed too, since nothing else is under it.");
            }
            if (nested > 0)
            {
                message.Add($"Note: {nested} placed prefab(s) are nested deeper than the top level and are not managed by this tool. They will be left in place.");
            }
            message.Add("Saved .layout.json files are not affected, and Undo is available.");

            if (!EditorUtility.DisplayDialog("Clear layout?", string.Join("\n\n", message), "Clear", "Cancel"))
            {
                status = "Clear cancelled. Scene unchanged.";
                return;
            }

            int removed = RemovePlacedPieces();
            GameObject remaining = FindRootOrNull();
            if (remaining != null && remaining.transform.childCount == 0)
            {
                Undo.DestroyObjectImmediate(remaining);
                status = $"Cleared {removed} prefab(s) and removed the now-empty {LayoutRootName}. Undo is available.";
                return;
            }
            status = $"Cleared {removed} prefab(s). {LayoutRootName} was kept because other objects are still under it. Undo is available.";
        }

        // ---------------------------------------------------------------- paths / ids

        private static string ProjectRelativeToAbsolute(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
        }

        /// <summary>
        /// Where the load dialog opens. Deliberately does NOT create the folder: merely opening a
        /// file picker, or cancelling it, must not write anything into the user's project.
        /// </summary>
        private static string AbsoluteLayoutFolder()
        {
            string absolute = ProjectRelativeToAbsolute(LayoutFolder);
            return Directory.Exists(absolute) ? absolute : Application.dataPath;
        }

        private string SafeLayoutName()
        {
            string result = NormalizeId(layoutName);
            if (string.IsNullOrWhiteSpace(result))
            {
                result = "unnamed_layout";
            }
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

        private static void EnsureAssetFolder(string path)
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
