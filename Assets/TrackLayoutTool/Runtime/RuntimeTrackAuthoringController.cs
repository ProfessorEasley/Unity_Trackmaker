using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SimpleTrackBuilder
{
    [AddComponentMenu("Track Builder/Runtime Track Authoring Controller")]
    public sealed class RuntimeTrackAuthoringController : MonoBehaviour
    {
        private const string SaveFolderName = "TrackBuilderSaves";
        private const string LastTrackKey = "TrackBuilder.LastTrackPath";
        private const string LastPropsKey = "TrackBuilder.LastPropsPath";

        [Header("References")]
        public TrackPieceCatalog catalog;
        public Camera authoringCamera;
        public TextAsset templateTrackJson;
        public TextAsset[] templatePropJson;

        [Header("Placement")]
        public bool snapToGrid;
        public float gridSize = 15f;
        public float groundY;
        public float rotationStep = 90f;
        public bool placementEnabled = true;
        public bool loadTemplateOnStart;
        public float cameraPanSpeed = 25f;
        public float cameraZoomSpeed = 8f;

        [Header("Save")]
        public string saveNamePrefix = "runtime_track";

        private Transform authoredRoot;
        private Transform trackRoot;
        private Transform propRoot;
        private GameObject preview;
        private PieceCategory selectedCategory;
        private int selectedIndex;
        private float placementYaw;
        private string status = "Ready.";
        private Vector2 panelScroll;

        public string SaveFolderPath => Path.Combine(Application.persistentDataPath, SaveFolderName);
        public int TrackPieceCount => CountPieces(trackRoot, PieceCategory.Track);
        public int PropPieceCount => CountPieces(propRoot, PieceCategory.Prop);
        public PieceCategory SelectedCategory => selectedCategory;
        public string SelectedPieceId => CurrentChoice()?.id ?? "(none)";
        public float PlacementYaw => placementYaw;
        public string Status => status;

        private void Start()
        {
            InitializeForUse();
            if (loadTemplateOnStart)
            {
                LoadTemplate();
            }
        }

        private void OnDisable()
        {
            DestroyPreview();
        }

        public void InitializeForUse()
        {
            if (authoringCamera == null)
            {
                authoringCamera = Camera.main;
            }

            EnsureRoots();
            ClampSelectedIndex();
        }

        private void Update()
        {
            InitializeForUse();
            HandleCameraMovement();
            HandleKeyboardShortcuts();

            if (!placementEnabled || catalog == null || authoringCamera == null || PointerIsOverPanel())
            {
                if (preview != null)
                {
                    preview.SetActive(false);
                }
                return;
            }

            if (!TryGetSnappedMousePoint(out Vector3 point))
            {
                return;
            }

            UpdatePreview(point);
            if (Input.GetMouseButtonDown(0))
            {
                TrackPieceCatalogEntry choice = CurrentChoice();
                if (choice != null)
                {
                    PlacePiece(choice.id, choice.category, point, new Vector3(0f, placementYaw, 0f), Vector3.one);
                    status = $"Placed {choice.id} at {point}.";
                }
            }

            if (Input.GetMouseButtonDown(1))
            {
                DeletePieceUnderMouse(point);
            }
        }

        private void OnGUI()
        {
            Rect panel = PanelRect();
            GUI.Box(panel, "RUNTIME TRACK AUTHORING");
            GUILayout.BeginArea(new Rect(panel.x + 10f, panel.y + 24f, panel.width - 20f, panel.height - 34f));
            panelScroll = GUILayout.BeginScrollView(panelScroll);

            placementEnabled = GUILayout.Toggle(placementEnabled, "Placement enabled (P)");

            GUILayout.BeginHorizontal();
            if (CategoryButton("TRACK", PieceCategory.Track)) SelectCategory(PieceCategory.Track);
            if (CategoryButton("PROPS", PieceCategory.Prop)) SelectCategory(PieceCategory.Prop);
            GUILayout.EndHorizontal();

            List<TrackPieceCatalogEntry> choices = CurrentChoices();
            if (choices.Count == 0)
            {
                GUILayout.Label("No catalog entries in this category.");
            }
            else
            {
                ClampSelectedIndex();
                GUILayout.Label($"Piece {selectedIndex + 1}/{choices.Count}");
                GUILayout.Label(choices[selectedIndex].id);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("< PREVIOUS")) ChangeSelection(-1);
                if (GUILayout.Button("NEXT >")) ChangeSelection(1);
                GUILayout.EndHorizontal();
            }

            snapToGrid = GUILayout.Toggle(snapToGrid, "Snap to grid");
            if (snapToGrid)
            {
                GUILayout.Label($"Snap spacing: {gridSize:0.##}");
                gridSize = GUILayout.HorizontalSlider(gridSize, 0.25f, 15f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("0.5")) gridSize = 0.5f;
                if (GUILayout.Button("1")) gridSize = 1f;
                if (GUILayout.Button("5")) gridSize = 5f;
                if (GUILayout.Button("15")) gridSize = 15f;
                GUILayout.EndHorizontal();
                GUILayout.Label("Hold Shift for temporary free placement.");
            }
            else
            {
                GUILayout.Label("Placement: FREE / continuous");
            }
            GUILayout.Label($"Yaw: {placementYaw:0}°");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ROTATE Q")) RotatePlacement(-rotationStep);
            if (GUILayout.Button("ROTATE E")) RotatePlacement(rotationStep);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label($"Track pieces: {TrackPieceCount}");
            GUILayout.Label($"Props: {PropPieceCount}");

            saveNamePrefix = GUILayout.TextField(saveNamePrefix ?? "runtime_track");
            if (GUILayout.Button("SAVE TIMESTAMPED JSON"))
            {
                SaveTimestampedLayout();
            }
            if (GUILayout.Button("LOAD LATEST SAVE"))
            {
                LoadLatestSave();
            }
            if (GUILayout.Button("LOAD ORIGINAL TEMPLATE"))
            {
                LoadTemplate();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("CLEAR TRACK")) ClearCategory(PieceCategory.Track);
            if (GUILayout.Button("CLEAR PROPS")) ClearCategory(PieceCategory.Prop);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("OPEN SAVE FOLDER"))
            {
                Directory.CreateDirectory(SaveFolderPath);
                Application.OpenURL("file:///" + SaveFolderPath.Replace('\\', '/'));
            }

            GUILayout.Space(8f);
            GUILayout.Label("CONTROLS");
            GUILayout.Label("Left click: place");
            GUILayout.Label("Right click: delete nearest piece");
            GUILayout.Label("Q/E: rotate   Tab: track/props");
            GUILayout.Label("P: placement on/off");
            GUILayout.Label("Shift: bypass snapping temporarily");
            GUILayout.Label("WASD/arrows: pan camera");
            GUILayout.Label("Mouse wheel: zoom");
            GUILayout.Space(6f);
            GUILayout.Label(status);
            GUILayout.Label("Save folder:");
            GUILayout.TextArea(SaveFolderPath);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        public GameObject PlacePiece(string pieceId, PieceCategory category, Vector3 position, Vector3 rotationEuler, Vector3 scale)
        {
            InitializeForUse();
            GameObject prefab = catalog != null ? catalog.FindPrefab(pieceId, category) : null;
            if (prefab == null)
            {
                status = $"No catalog prefab for {pieceId}.";
                Debug.LogWarning(status, this);
                return null;
            }

            Transform parent = category == PieceCategory.Track ? trackRoot : propRoot;
            GameObject instance = Instantiate(prefab, position, Quaternion.Euler(rotationEuler), parent);
            instance.name = pieceId;
            instance.transform.localScale = scale;
            TrackPieceIdentity identity = instance.GetComponent<TrackPieceIdentity>();
            if (identity == null)
            {
                identity = instance.AddComponent<TrackPieceIdentity>();
            }
            identity.pieceId = pieceId;
            identity.category = category;
            return instance;
        }

        public string[] SaveTimestampedLayout(string forcedStem = null)
        {
            InitializeForUse();
            Directory.CreateDirectory(SaveFolderPath);
            string prefix = SanitizeFilePart(string.IsNullOrWhiteSpace(saveNamePrefix) ? "runtime_track" : saveNamePrefix);
            string stem = string.IsNullOrWhiteSpace(forcedStem)
                ? $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}"
                : SanitizeFilePart(forcedStem);

            string trackPath = Path.Combine(SaveFolderPath, stem + ".track.json");
            string propPath = Path.Combine(SaveFolderPath, stem + ".props.json");
            TrackLayoutFile trackData = new()
            {
                layoutName = stem,
                pieces = CaptureRecords(trackRoot, PieceCategory.Track)
            };
            PropAddendumFile propData = new()
            {
                layoutName = stem,
                trackFile = Path.GetFileName(trackPath),
                props = CaptureRecords(propRoot, PieceCategory.Prop)
            };

            File.WriteAllText(trackPath, JsonUtility.ToJson(trackData, true));
            File.WriteAllText(propPath, JsonUtility.ToJson(propData, true));
            PlayerPrefs.SetString(LastTrackKey, trackPath);
            PlayerPrefs.SetString(LastPropsKey, propPath);
            PlayerPrefs.Save();
            status = $"Saved {trackData.pieces.Count} track pieces and {propData.props.Count} props as {stem}.";
            Debug.Log($"Runtime Track Builder saved:\n{trackPath}\n{propPath}", this);
            return new[] { trackPath, propPath };
        }

        public bool LoadLatestSave()
        {
            Directory.CreateDirectory(SaveFolderPath);
            string trackPath = PlayerPrefs.GetString(LastTrackKey, string.Empty);
            string propPath = PlayerPrefs.GetString(LastPropsKey, string.Empty);
            if (!File.Exists(trackPath))
            {
                trackPath = new DirectoryInfo(SaveFolderPath)
                    .GetFiles("*.track.json")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Select(file => file.FullName)
                    .FirstOrDefault();
            }
            if (string.IsNullOrEmpty(trackPath))
            {
                status = "No runtime save exists yet.";
                return false;
            }
            if (!File.Exists(propPath))
            {
                propPath = Path.ChangeExtension(Path.ChangeExtension(trackPath, null), "props.json");
            }
            return LoadFromFiles(trackPath, File.Exists(propPath) ? propPath : null);
        }

        public bool LoadFromFiles(string trackPath, string propPath)
        {
            if (!File.Exists(trackPath))
            {
                status = "Track JSON not found.";
                return false;
            }

            ClearAll();
            TrackLayoutFile trackData = JsonUtility.FromJson<TrackLayoutFile>(File.ReadAllText(trackPath));
            BuildRecords(trackData?.pieces, PieceCategory.Track);
            if (!string.IsNullOrWhiteSpace(propPath) && File.Exists(propPath))
            {
                PropAddendumFile propData = JsonUtility.FromJson<PropAddendumFile>(File.ReadAllText(propPath));
                BuildRecords(propData?.props, PieceCategory.Prop);
            }
            status = $"Loaded {TrackPieceCount} track pieces and {PropPieceCount} props.";
            return true;
        }

        public void LoadTemplate()
        {
            ClearAll();
            if (templateTrackJson != null)
            {
                TrackLayoutFile trackData = JsonUtility.FromJson<TrackLayoutFile>(templateTrackJson.text);
                BuildRecords(trackData?.pieces, PieceCategory.Track);
            }
            if (templatePropJson != null)
            {
                foreach (TextAsset addendum in templatePropJson)
                {
                    if (addendum == null) continue;
                    PropAddendumFile propData = JsonUtility.FromJson<PropAddendumFile>(addendum.text);
                    BuildRecords(propData?.props, PieceCategory.Prop);
                }
            }
            status = $"Loaded template: {TrackPieceCount} track pieces and {PropPieceCount} props.";
        }

        public void ClearAll()
        {
            ClearCategory(PieceCategory.Track);
            ClearCategory(PieceCategory.Prop);
        }

        public void SetSelectedCategory(PieceCategory category)
        {
            SelectCategory(category);
        }

        public void SelectPreviousPiece()
        {
            ChangeSelection(-1);
        }

        public void SelectNextPiece()
        {
            ChangeSelection(1);
        }

        public void RotatePlacementLeft()
        {
            RotatePlacement(-rotationStep);
        }

        public void RotatePlacementRight()
        {
            RotatePlacement(rotationStep);
        }

        public void ClearCategory(PieceCategory category)
        {
            InitializeForUse();
            Transform oldRoot = category == PieceCategory.Track ? trackRoot : propRoot;
            if (oldRoot != null)
            {
                oldRoot.gameObject.SetActive(false);
                oldRoot.SetParent(null);
                DestroyRuntimeObject(oldRoot.gameObject);
            }
            Transform replacement = FindOrCreateChild(authoredRoot, category == PieceCategory.Track ? "Track" : "Props");
            if (category == PieceCategory.Track)
            {
                trackRoot = replacement;
            }
            else
            {
                propRoot = replacement;
            }
            status = $"Cleared {category}.";
        }

        private void BuildRecords(IEnumerable<PlacedPieceRecord> records, PieceCategory category)
        {
            if (records == null) return;
            foreach (PlacedPieceRecord record in records)
            {
                PlacePiece(record.pieceId, category, record.position, record.rotationEuler, record.scale);
            }
        }

        private List<PlacedPieceRecord> CaptureRecords(Transform root, PieceCategory category)
        {
            List<PlacedPieceRecord> records = new();
            if (root == null) return records;
            foreach (TrackPieceIdentity identity in root.GetComponentsInChildren<TrackPieceIdentity>(true))
            {
                if (identity.category != category) continue;
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

        private void EnsureRoots()
        {
            if (authoredRoot == null)
            {
                Transform existing = transform.Find("Runtime Authored Layout");
                authoredRoot = existing != null ? existing : new GameObject("Runtime Authored Layout").transform;
                authoredRoot.SetParent(transform, false);
            }
            if (trackRoot == null)
            {
                trackRoot = FindOrCreateChild(authoredRoot, "Track");
            }
            if (propRoot == null)
            {
                propRoot = FindOrCreateChild(authoredRoot, "Props");
            }
        }

        private static Transform FindOrCreateChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child != null) return child;
            child = new GameObject(childName).transform;
            child.SetParent(parent, false);
            return child;
        }

        private void HandleKeyboardShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.P)) placementEnabled = !placementEnabled;
            if (Input.GetKeyDown(KeyCode.Tab)) SelectCategory(selectedCategory == PieceCategory.Track ? PieceCategory.Prop : PieceCategory.Track);
            if (Input.GetKeyDown(KeyCode.Q)) RotatePlacement(-rotationStep);
            if (Input.GetKeyDown(KeyCode.E)) RotatePlacement(rotationStep);
        }

        private void HandleCameraMovement()
        {
            if (authoringCamera == null || PointerIsOverPanel()) return;
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            Vector3 forward = authoringCamera.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = authoringCamera.transform.right;
            right.y = 0f;
            right.Normalize();
            authoringCamera.transform.position += (right * horizontal + forward * vertical) * cameraPanSpeed * Time.deltaTime;

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                authoringCamera.transform.position += authoringCamera.transform.forward * wheel * cameraZoomSpeed;
            }
        }

        private void UpdatePreview(Vector3 point)
        {
            TrackPieceCatalogEntry choice = CurrentChoice();
            if (choice == null) return;
            if (preview == null || preview.name != "PREVIEW - " + choice.id)
            {
                DestroyPreview();
                preview = Instantiate(choice.prefab);
                preview.name = "PREVIEW - " + choice.id;
                foreach (Collider collider in preview.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (Rigidbody body in preview.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            }
            preview.SetActive(true);
            preview.transform.SetPositionAndRotation(point, Quaternion.Euler(0f, placementYaw, 0f));
        }

        private void DestroyPreview()
        {
            if (preview == null) return;
            DestroyRuntimeObject(preview);
            preview = null;
        }

        private void DeletePieceUnderMouse(Vector3 fallbackPoint)
        {
            TrackPieceIdentity target = null;
            Ray ray = authoringCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                target = hit.collider.GetComponentInParent<TrackPieceIdentity>();
            }
            if (target == null)
            {
                target = authoredRoot.GetComponentsInChildren<TrackPieceIdentity>(true)
                    .OrderBy(identity => HorizontalDistanceSquared(identity.transform.position, fallbackPoint))
                    .FirstOrDefault(identity => HorizontalDistanceSquared(identity.transform.position, fallbackPoint) <= gridSize * gridSize);
            }
            if (target == null)
            {
                status = "No placed piece close enough to delete.";
                return;
            }
            string deletedName = target.pieceId;
            target.gameObject.SetActive(false);
            DestroyRuntimeObject(target.gameObject);
            status = $"Deleted {deletedName}.";
        }

        private bool TryGetSnappedMousePoint(out Vector3 point)
        {
            Plane plane = new(Vector3.up, new Vector3(0f, groundY, 0f));
            Ray ray = authoringCamera.ScreenPointToRay(Input.mousePosition);
            if (!plane.Raycast(ray, out float distance))
            {
                point = default;
                return false;
            }
            Vector3 hit = ray.GetPoint(distance);
            bool temporaryFreePlacement = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool shouldSnap = snapToGrid && !temporaryFreePlacement && gridSize > 0.0001f;
            point = shouldSnap
                ? new Vector3(
                    Mathf.Round(hit.x / gridSize) * gridSize,
                    groundY,
                    Mathf.Round(hit.z / gridSize) * gridSize)
                : new Vector3(hit.x, groundY, hit.z);
            return true;
        }

        private void SelectCategory(PieceCategory category)
        {
            selectedCategory = category;
            selectedIndex = 0;
            DestroyPreview();
        }

        private bool CategoryButton(string label, PieceCategory category)
        {
            Color old = GUI.backgroundColor;
            if (selectedCategory == category) GUI.backgroundColor = new Color(0.5f, 0.9f, 1f);
            bool clicked = GUILayout.Button(label);
            GUI.backgroundColor = old;
            return clicked;
        }

        private void ChangeSelection(int delta)
        {
            List<TrackPieceCatalogEntry> choices = CurrentChoices();
            if (choices.Count == 0) return;
            selectedIndex = (selectedIndex + delta + choices.Count) % choices.Count;
            DestroyPreview();
        }

        private void RotatePlacement(float amount)
        {
            placementYaw = Mathf.Repeat(placementYaw + amount, 360f);
        }

        private List<TrackPieceCatalogEntry> CurrentChoices()
        {
            if (catalog == null) return new List<TrackPieceCatalogEntry>();
            return catalog.Entries
                .Where(entry => entry.category == selectedCategory && entry.prefab != null)
                .OrderBy(entry => entry.id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private TrackPieceCatalogEntry CurrentChoice()
        {
            List<TrackPieceCatalogEntry> choices = CurrentChoices();
            if (choices.Count == 0) return null;
            selectedIndex = Mathf.Clamp(selectedIndex, 0, choices.Count - 1);
            return choices[selectedIndex];
        }

        private void ClampSelectedIndex()
        {
            int count = CurrentChoices().Count;
            selectedIndex = count == 0 ? 0 : Mathf.Clamp(selectedIndex, 0, count - 1);
        }

        private Rect PanelRect()
        {
            return new Rect(10f, 10f, 350f, Mathf.Max(300f, Screen.height - 20f));
        }

        private bool PointerIsOverPanel()
        {
            Vector2 guiPoint = new(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return PanelRect().Contains(guiPoint);
        }

        private static int CountPieces(Transform root, PieceCategory category)
        {
            return root == null ? 0 : root.GetComponentsInChildren<TrackPieceIdentity>(true).Count(item => item.category == category);
        }

        private static float HorizontalDistanceSquared(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static string SanitizeFilePart(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value.Trim().Replace(' ', '_');
        }

        private static void DestroyRuntimeObject(GameObject target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
