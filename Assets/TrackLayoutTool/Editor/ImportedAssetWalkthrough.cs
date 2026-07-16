using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SimpleTrackBuilder.Editor
{
    [InitializeOnLoad]
    public static class ImportedAssetWalkthrough
    {
        private const string RunFlagName = "RUN_IMPORTED_ASSET_WALKTHROUGH.flag";
        private const string CatalogPath = "Assets/TrackLayoutTool/Data/TrackPieceCatalog.asset";
        private const string LayoutFolder = "Assets/TrackLayoutTool/Layouts";
        private const string DemoFolder = "Assets/TrackLayoutTool/Demo";
        private const string WalkthroughScenePath = DemoFolder + "/ImportedAssetWalkthrough.unity";
        private const string RuntimeScenePath = DemoFolder + "/ImportedAssetRuntimeDemo.unity";
        private const string TrackJsonPath = LayoutFolder + "/imported_pack_walkthrough.track.json";
        private const string PropJsonPath = LayoutFolder + "/imported_pack_walkthrough.props.json";

        private static readonly Dictionary<string, string> TrackPrefabs = new()
        {
            ["track_straight_short"] = "Assets/BEDRILL/Modular_Track_Free/Prefabs_Obs/Track_line_type_01_15m_free_obs.prefab",
            ["track_straight_long"] = "Assets/BEDRILL/Modular_Track_Free/Prefabs_Obs/Track_line_type_01_30m_free_obs.prefab",
            ["track_start_finish"] = "Assets/BEDRILL/Modular_Track_Free/Prefabs_Obs/Track_line_type_01_start_finish_15m_free_obs.prefab",
            ["track_curve_90"] = "Assets/BEDRILL/Modular_Track_Free/Prefabs_Obs/Track_Corner_90d_type_01_15x15m_quad_free_obs.prefab"
        };

        private static readonly Dictionary<string, string> PropPrefabs = new()
        {
            ["prop_finisharch"] = "Assets/BEDRILL/Track_Environment_Free/Prefabs/Arch_banner_finish_free_obs.prefab",
            ["prop_arch"] = "Assets/BEDRILL/Track_Environment_Free/Prefabs/Arch_01_free_obs.prefab",
            ["prop_cone"] = "Assets/BEDRILL/Track_Environment_Free/Prefabs/Cone_free_obs.prefab",
            ["prop_barrier"] = "Assets/BEDRILL/Track_Environment_Free/Prefabs/Barrier_free_obs.prefab",
            ["prop_flag"] = "Assets/BEDRILL/Track_Environment_Free/Prefabs/Flag_free_obs.prefab",
            ["prop_traffic_light"] = "Assets/BEDRILL/Track_Environment_Free/Prefabs/Traffic_light_free_obs.prefab",
            ["prop_tribune"] = "Assets/BEDRILL/Track_Environment_Free/Prefabs/Tribune_free_obs.prefab",
            ["prop_tire"] = "Assets/BEDRILL/Track_Environment_Free/Prefabs/Tire_free_obs.prefab"
        };

        static ImportedAssetWalkthrough()
        {
            EditorApplication.delayCall += TryRunFlaggedWalkthrough;
        }

        [MenuItem("Tools/Track Builder Setup/Run Imported Asset Walkthrough")]
        public static void Run()
        {
            try
            {
                TrackBuilderWindow.EnsureAssetFolder(LayoutFolder);
                TrackBuilderWindow.EnsureAssetFolder(DemoFolder);
                TrackPieceCatalog catalog = LoadCatalog();
                MapImportedPrefabs(catalog);

                TrackLayoutFile trackData = CreateTrackData();
                PropAddendumFile propData = CreatePropData();

                // Step 1: place the pieces in an editor scene.
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                CreateCameraLightAndGround(new Vector3(22.5f, 0f, 25f));
                BuildRecords(trackData.pieces, PieceCategory.Track, catalog, "TRACK_LAYOUT");
                BuildRecords(propData.props, PieceCategory.Prop, catalog, "TRACK_PROPS");

                // Step 2: save each layer to its own JSON file.
                WriteProjectFile(TrackJsonPath, JsonUtility.ToJson(trackData, true));
                WriteProjectFile(PropJsonPath, JsonUtility.ToJson(propData, true));
                AssetDatabase.ImportAsset(TrackJsonPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(PropJsonPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                // Step 3: clear both roots, proving the scene objects are disposable.
                DestroyNamedRoot("TRACK_LAYOUT");
                DestroyNamedRoot("TRACK_PROPS");

                // Step 4: recreate both roots from the just-written JSON.
                TrackLayoutFile reloadedTrack = JsonUtility.FromJson<TrackLayoutFile>(ReadProjectFile(TrackJsonPath));
                PropAddendumFile reloadedProps = JsonUtility.FromJson<PropAddendumFile>(ReadProjectFile(PropJsonPath));
                BuildRecords(reloadedTrack.pieces, PieceCategory.Track, catalog, "TRACK_LAYOUT");
                BuildRecords(reloadedProps.props, PieceCategory.Prop, catalog, "TRACK_PROPS");
                EditorSceneManager.SaveScene(scene, WalkthroughScenePath);

                // Step 5: create a second scene that starts empty and builds only when Play calls Start().
                CreateRuntimeScene(catalog);

                // Return to the visible editor-authored scene and open the utilitarian window.
                EditorSceneManager.OpenScene(WalkthroughScenePath);
                Selection.activeGameObject = GameObject.Find("TRACK_LAYOUT");
                TrackBuilderWindow window = EditorWindow.GetWindow<TrackBuilderWindow>("Track Builder");
                window.Show();
                window.ShowNotification(new GUIContent("Imported-pack walkthrough created and JSON reload verified."), 8);

                WriteExecutionReport(trackData.pieces.Count, propData.props.Count, catalog);
                Debug.Log($"IMPORTED ASSET WALKTHROUGH PASSED: placed, saved, cleared, and reloaded {trackData.pieces.Count} track pieces plus {propData.props.Count} props. Runtime rebuild scene also verified.");
            }
            catch (Exception exception)
            {
                File.WriteAllText(ProjectPath("ImportedAssetWalkthrough-FAILED.txt"), exception.ToString());
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Track walkthrough failed", exception.Message, "OK");
            }
        }

        private static void TryRunFlaggedWalkthrough()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            string flagPath = ProjectPath(RunFlagName);
            if (!File.Exists(flagPath))
            {
                return;
            }

            File.Delete(flagPath);
            Run();
        }

        private static TrackPieceCatalog LoadCatalog()
        {
            TrackPieceCatalog catalog = AssetDatabase.LoadAssetAtPath<TrackPieceCatalog>(CatalogPath);
            if (catalog == null)
            {
                throw new InvalidOperationException("TrackPieceCatalog.asset is missing.");
            }
            return catalog;
        }

        private static void MapImportedPrefabs(TrackPieceCatalog catalog)
        {
            Undo.RecordObject(catalog, "Map Imported Track Assets");
            foreach (KeyValuePair<string, string> pair in TrackPrefabs)
            {
                SetMapping(catalog, pair.Key, PieceCategory.Track, LoadRequiredPrefab(pair.Value));
            }
            foreach (KeyValuePair<string, string> pair in PropPrefabs)
            {
                SetMapping(catalog, pair.Key, PieceCategory.Prop, LoadRequiredPrefab(pair.Value));
            }

            // Preserve the first placeholder demo's stable IDs while replacing its art.
            SetMapping(catalog, "track_curve_left_mesh", PieceCategory.Track, LoadRequiredPrefab(TrackPrefabs["track_curve_90"]));
            SetMapping(catalog, "track_curve_right_mesh", PieceCategory.Track, LoadRequiredPrefab(TrackPrefabs["track_curve_90"]));
            SetMapping(catalog, "prop_fireplug", PieceCategory.Prop, LoadRequiredPrefab(PropPrefabs["prop_cone"]));
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        private static void SetMapping(TrackPieceCatalog catalog, string id, PieceCategory category, GameObject prefab)
        {
            TrackPieceCatalogEntry entry = catalog.FindEntry(id, category);
            if (entry == null)
            {
                catalog.Entries.Add(new TrackPieceCatalogEntry { id = id, category = category, prefab = prefab });
            }
            else
            {
                entry.prefab = prefab;
            }
        }

        private static GameObject LoadRequiredPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new FileNotFoundException("Required imported prefab was not found.", path);
            }
            return prefab;
        }

        private static TrackLayoutFile CreateTrackData()
        {
            TrackLayoutFile data = new() { layoutName = "imported_pack_walkthrough" };
            data.pieces.Add(Record("track_start_finish", 0f, 0f, 0f));
            data.pieces.Add(Record("track_straight_short", 0f, 15f, 0f));
            data.pieces.Add(Record("track_straight_short", 0f, 30f, 0f));
            data.pieces.Add(Record("track_curve_90", 0f, 45f, 0f));
            data.pieces.Add(Record("track_straight_short", 15f, 45f, 90f));
            data.pieces.Add(Record("track_straight_short", 30f, 45f, 90f));
            data.pieces.Add(Record("track_curve_90", 45f, 45f, 90f));
            data.pieces.Add(Record("track_straight_short", 45f, 30f, 180f));
            data.pieces.Add(Record("track_straight_short", 45f, 15f, 180f));
            return data;
        }

        private static PropAddendumFile CreatePropData()
        {
            PropAddendumFile data = new()
            {
                layoutName = "imported_pack_walkthrough",
                trackFile = "imported_pack_walkthrough.track.json"
            };
            data.props.Add(Record("prop_finisharch", 0f, 2f, 0f));
            data.props.Add(Record("prop_cone", -5f, 12f, 0f));
            data.props.Add(Record("prop_cone", 5f, 12f, 0f));
            data.props.Add(Record("prop_barrier", -7f, 27f, 90f));
            data.props.Add(Record("prop_flag", 7f, 32f, 0f));
            data.props.Add(Record("prop_tribune", 20f, 55f, 180f));
            data.props.Add(Record("prop_traffic_light", 38f, 51f, 180f));
            data.props.Add(Record("prop_tire", 52f, 28f, 0f));
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
                if (prefab == null)
                {
                    throw new InvalidOperationException($"No catalog mapping exists for {record.pieceId}.");
                }

                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, root) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException($"Could not instantiate {record.pieceId}.");
                }
                instance.name = record.pieceId;
                instance.transform.SetPositionAndRotation(record.position, Quaternion.Euler(record.rotationEuler));
                instance.transform.localScale = record.scale;
                TrackPieceIdentity identity = instance.GetComponent<TrackPieceIdentity>() ?? instance.AddComponent<TrackPieceIdentity>();
                identity.pieceId = record.pieceId;
                identity.category = category;
            }
        }

        private static void CreateRuntimeScene(TrackPieceCatalog catalog)
        {
            Scene runtimeScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCameraLightAndGround(new Vector3(22.5f, 0f, 25f));
            GameObject loaderObject = new("RUNTIME JSON REBUILD - press Play");
            TrackLayoutRuntimeLoader loader = loaderObject.AddComponent<TrackLayoutRuntimeLoader>();
            loader.catalog = catalog;
            loader.trackJson = AssetDatabase.LoadAssetAtPath<TextAsset>(TrackJsonPath);
            loader.propAddenda = new[] { AssetDatabase.LoadAssetAtPath<TextAsset>(PropJsonPath) };
            loader.buildOnStart = true;
            if (loader.trackJson == null || loader.propAddenda[0] == null)
            {
                throw new InvalidOperationException("Unity did not import the generated JSON files as TextAssets.");
            }

            // Exercise the same reconstruction code immediately, validate it, then clear before saving.
            loader.Rebuild();
            Transform generatedRoot = loaderObject.transform.Find("Generated Track Layout");
            int rebuiltTrackCount = generatedRoot != null && generatedRoot.Find("Track") != null
                ? generatedRoot.Find("Track").childCount
                : 0;
            int rebuiltPropCount = generatedRoot != null && generatedRoot.Find("Props") != null
                ? generatedRoot.Find("Props").childCount
                : 0;
            if (rebuiltTrackCount != 9 || rebuiltPropCount != 8)
            {
                TrackLayoutFile parsedTrack = JsonUtility.FromJson<TrackLayoutFile>(loader.trackJson.text);
                PropAddendumFile parsedProps = JsonUtility.FromJson<PropAddendumFile>(loader.propAddenda[0].text);
                throw new InvalidOperationException(
                    $"Runtime reconstruction created {rebuiltTrackCount} track objects and {rebuiltPropCount} props " +
                    $"(JSON parsed as {parsedTrack?.pieces?.Count ?? -1} + {parsedProps?.props?.Count ?? -1}).");
            }
            loader.Clear();
            EditorSceneManager.SaveScene(runtimeScene, RuntimeScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(WalkthroughScenePath, true),
                new EditorBuildSettingsScene(RuntimeScenePath, true)
            };
            AssetDatabase.SaveAssets();
        }

        private static void CreateCameraLightAndGround(Vector3 focus)
        {
            GameObject cameraObject = new("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(78f, 68f, -48f);
            cameraObject.transform.LookAt(focus);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500f;

            GameObject lightObject = new("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(22.5f, -0.12f, 25f);
            ground.transform.localScale = new Vector3(9f, 1f, 9f);
            Renderer renderer = ground.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/TrackLayoutTool/Demo/Materials/GroundPlaceholder.mat");
            }
        }

        private static void DestroyNamedRoot(string rootName)
        {
            GameObject root = GameObject.Find(rootName);
            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void WriteExecutionReport(int trackCount, int propCount, TrackPieceCatalog catalog)
        {
            string report =
                "IMPORTED ASSET WALKTHROUGH: PASSED\n" +
                $"Track records saved/reloaded: {trackCount}\n" +
                $"Prop records saved/reloaded: {propCount}\n" +
                "Runtime records reconstructed: 17\n" +
                $"Catalog mappings available: {catalog.Entries.Count}\n" +
                $"Editor scene: {WalkthroughScenePath}\n" +
                $"Runtime scene: {RuntimeScenePath}\n" +
                $"Track JSON: {TrackJsonPath}\n" +
                $"Prop JSON: {PropJsonPath}\n";
            File.WriteAllText(ProjectPath("ImportedAssetWalkthrough-RESULT.txt"), report);
            string failurePath = ProjectPath("ImportedAssetWalkthrough-FAILED.txt");
            if (File.Exists(failurePath))
            {
                File.Delete(failurePath);
            }
        }

        private static void WriteProjectFile(string assetPath, string contents)
        {
            File.WriteAllText(ProjectPath(assetPath), contents);
        }

        private static string ReadProjectFile(string assetPath)
        {
            return File.ReadAllText(ProjectPath(assetPath));
        }

        private static string ProjectPath(string relativePath)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
