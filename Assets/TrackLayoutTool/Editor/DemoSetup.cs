using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SimpleTrackBuilder.Editor
{
    public static class DemoSetup
    {
        private const string BaseFolder = "Assets/TrackLayoutTool";
        private const string DemoFolder = BaseFolder + "/Demo";
        private const string PrefabFolder = DemoFolder + "/PlaceholderPrefabs";
        private const string MaterialFolder = DemoFolder + "/Materials";
        private const string LayoutFolder = BaseFolder + "/Layouts";
        private const string CatalogPath = BaseFolder + "/Data/TrackPieceCatalog.asset";

        [MenuItem("Tools/Track Builder Setup/Create or Reset Placeholder Demo")]
        public static void CreateDemo()
        {
            TrackBuilderWindow.EnsureAssetFolder(PrefabFolder);
            TrackBuilderWindow.EnsureAssetFolder(MaterialFolder);
            TrackBuilderWindow.EnsureAssetFolder(LayoutFolder);
            TrackBuilderWindow.EnsureAssetFolder(BaseFolder + "/Data");

            Material roadMaterial = CreateMaterial(MaterialFolder + "/RoadPlaceholder.mat", new Color(0.14f, 0.16f, 0.18f));
            Material edgeMaterial = CreateMaterial(MaterialFolder + "/EdgePlaceholder.mat", new Color(0.95f, 0.72f, 0.12f));
            Material propMaterial = CreateMaterial(MaterialFolder + "/PropPlaceholder.mat", new Color(0.9f, 0.2f, 0.12f));

            GameObject straight = CreateStraight(PrefabFolder + "/track_straight_short.prefab", roadMaterial, edgeMaterial);
            GameObject curveLeft = CreateCurve(PrefabFolder + "/track_curve_left_mesh.prefab", roadMaterial, edgeMaterial, false);
            GameObject curveRight = CreateCurve(PrefabFolder + "/track_curve_right_mesh.prefab", roadMaterial, edgeMaterial, true);
            GameObject finishArch = CreateFinishArch(PrefabFolder + "/prop_finisharch.prefab", propMaterial);
            GameObject fireplug = CreateFireplug(PrefabFolder + "/prop_fireplug.prefab", propMaterial);

            TrackPieceCatalog catalog = AssetDatabase.LoadAssetAtPath<TrackPieceCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<TrackPieceCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.Entries.Clear();
            catalog.Entries.Add(new TrackPieceCatalogEntry { id = "track_straight_short", category = PieceCategory.Track, prefab = straight });
            catalog.Entries.Add(new TrackPieceCatalogEntry { id = "track_curve_left_mesh", category = PieceCategory.Track, prefab = curveLeft });
            catalog.Entries.Add(new TrackPieceCatalogEntry { id = "track_curve_right_mesh", category = PieceCategory.Track, prefab = curveRight });
            catalog.Entries.Add(new TrackPieceCatalogEntry { id = "prop_finisharch", category = PieceCategory.Prop, prefab = finishArch });
            catalog.Entries.Add(new TrackPieceCatalogEntry { id = "prop_fireplug", category = PieceCategory.Prop, prefab = fireplug });
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            TrackLayoutFile trackData = MakeTrackData();
            PropAddendumFile propData = MakePropData();
            string trackAssetPath = LayoutFolder + "/student_test_track.track.json";
            string propAssetPath = LayoutFolder + "/student_test_track.props.json";
            WriteProjectFile(trackAssetPath, JsonUtility.ToJson(trackData, true));
            WriteProjectFile(propAssetPath, JsonUtility.ToJson(propData, true));
            AssetDatabase.Refresh();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCameraAndLight();
            CreateGround();
            BuildRecords(trackData.pieces, PieceCategory.Track, catalog, "TRACK_LAYOUT");
            BuildRecords(propData.props, PieceCategory.Prop, catalog, "TRACK_PROPS");

            GameObject loaderObject = new("Runtime JSON Loader (optional)");
            TrackLayoutRuntimeLoader loader = loaderObject.AddComponent<TrackLayoutRuntimeLoader>();
            loader.catalog = catalog;
            loader.trackJson = AssetDatabase.LoadAssetAtPath<TextAsset>(trackAssetPath);
            loader.propAddenda = new[] { AssetDatabase.LoadAssetAtPath<TextAsset>(propAssetPath) };
            loader.buildOnStart = false;

            string scenePath = DemoFolder + "/TrackBuilderDemo.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
            AssetDatabase.SaveAssets();
            Selection.activeObject = loaderObject;
            Debug.Log("Track Builder placeholder demo created. Open Tools > Track Builder.");
        }

        private static TrackLayoutFile MakeTrackData()
        {
            TrackLayoutFile data = new() { layoutName = "student_test_track" };
            data.pieces.Add(Record("track_straight_short", 0, 0, 0));
            data.pieces.Add(Record("track_straight_short", 4, 0, 0));
            data.pieces.Add(Record("track_straight_short", 8, 0, 0));
            data.pieces.Add(Record("track_curve_left_mesh", 12, 0, 0));
            data.pieces.Add(Record("track_straight_short", 12, 4, 90));
            data.pieces.Add(Record("track_straight_short", 12, 8, 90));
            data.pieces.Add(Record("track_curve_left_mesh", 12, 12, 90));
            data.pieces.Add(Record("track_straight_short", 8, 12, 180));
            data.pieces.Add(Record("track_straight_short", 4, 12, 180));
            data.pieces.Add(Record("track_curve_left_mesh", 0, 12, 180));
            data.pieces.Add(Record("track_straight_short", 0, 8, 270));
            data.pieces.Add(Record("track_straight_short", 0, 4, 270));
            data.pieces.Add(Record("track_curve_left_mesh", 0, 0, 270));
            return data;
        }

        private static PropAddendumFile MakePropData()
        {
            PropAddendumFile data = new()
            {
                layoutName = "student_test_track",
                trackFile = "student_test_track.track.json"
            };
            data.props.Add(Record("prop_finisharch", 2, 0, 0));
            data.props.Add(Record("prop_fireplug", 6, -2, 0));
            data.props.Add(Record("prop_fireplug", 14, 6, 0));
            return data;
        }

        private static PlacedPieceRecord Record(string id, float x, float z, float yaw)
        {
            return new PlacedPieceRecord
            {
                pieceId = id,
                position = new Vector3(x, 0f, z),
                rotationEuler = new Vector3(0f, yaw, 0f),
                scale = Vector3.one
            };
        }

        private static void BuildRecords(IEnumerable<PlacedPieceRecord> records, PieceCategory category, TrackPieceCatalog catalog, string rootName)
        {
            Transform root = new GameObject(rootName).transform;
            foreach (PlacedPieceRecord record in records)
            {
                GameObject prefab = catalog.FindPrefab(record.pieceId, category);
                if (prefab == null) continue;
                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, root) as GameObject;
                instance.name = record.pieceId;
                instance.transform.SetPositionAndRotation(record.position, Quaternion.Euler(record.rotationEuler));
                instance.transform.localScale = record.scale;
                TrackPieceIdentity identity = instance.AddComponent<TrackPieceIdentity>();
                identity.pieceId = record.pieceId;
                identity.category = category;
            }
        }

        private static GameObject CreateStraight(string path, Material road, Material edge)
        {
            GameObject root = new("track_straight_short");
            AddCube(root.transform, "road", new Vector3(4f, 0.2f, 2f), Vector3.zero, road);
            AddCube(root.transform, "edge_left", new Vector3(4f, 0.35f, 0.12f), new Vector3(0f, 0.2f, -1.05f), edge);
            AddCube(root.transform, "edge_right", new Vector3(4f, 0.35f, 0.12f), new Vector3(0f, 0.2f, 1.05f), edge);
            return SavePrefab(root, path);
        }

        private static GameObject CreateCurve(string path, Material road, Material edge, bool mirror)
        {
            GameObject root = new(Path.GetFileNameWithoutExtension(path));
            float direction = mirror ? -1f : 1f;
            AddCube(root.transform, "curve_a", new Vector3(4f, 0.2f, 2f), Vector3.zero, road);
            AddCube(root.transform, "curve_b", new Vector3(2f, 0.2f, 4f), new Vector3(direction, 0f, 2f), road);
            AddCube(root.transform, "warning_edge", new Vector3(4f, 0.4f, 0.15f), new Vector3(0f, 0.25f, -direction), edge);
            return SavePrefab(root, path);
        }

        private static GameObject CreateFinishArch(string path, Material material)
        {
            GameObject root = new("prop_finisharch");
            AddCube(root.transform, "left_post", new Vector3(0.25f, 3f, 0.25f), new Vector3(0f, 1.5f, -1.35f), material);
            AddCube(root.transform, "right_post", new Vector3(0.25f, 3f, 0.25f), new Vector3(0f, 1.5f, 1.35f), material);
            AddCube(root.transform, "top", new Vector3(0.3f, 0.35f, 3f), new Vector3(0f, 3f, 0f), material);
            return SavePrefab(root, path);
        }

        private static GameObject CreateFireplug(string path, Material material)
        {
            GameObject root = new("prop_fireplug");
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.35f, 0.5f, 0.35f);
            body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            body.GetComponent<Renderer>().sharedMaterial = material;
            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cap.name = "cap";
            cap.transform.SetParent(root.transform, false);
            cap.transform.localScale = Vector3.one * 0.55f;
            cap.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            cap.GetComponent<Renderer>().sharedMaterial = material;
            return SavePrefab(root, path);
        }

        private static void AddCube(Transform parent, string name, Vector3 scale, Vector3 position, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localScale = scale;
            cube.transform.localPosition = position;
            cube.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static Material CreateMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Color");
                material = new Material(shader) { color = color };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.color = color;
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        private static void CreateCameraAndLight()
        {
            GameObject cameraObject = new("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(19f, 24f, -18f);
            cameraObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            camera.clearFlags = CameraClearFlags.Skybox;

            GameObject lightObject = new("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
        }

        private static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground (placeholder)";
            ground.transform.position = new Vector3(6f, -0.15f, 6f);
            ground.transform.localScale = new Vector3(2.5f, 1f, 2.5f);
            Material groundMaterial = CreateMaterial(MaterialFolder + "/GroundPlaceholder.mat", new Color(0.18f, 0.34f, 0.15f));
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        }

        private static void WriteProjectFile(string assetPath, string contents)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            File.WriteAllText(Path.Combine(projectRoot, assetPath), contents);
        }
    }
}
