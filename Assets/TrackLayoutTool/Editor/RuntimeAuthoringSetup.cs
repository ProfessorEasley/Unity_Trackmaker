using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SimpleTrackBuilder.Editor
{
    [InitializeOnLoad]
    public static class RuntimeAuthoringSetup
    {
        private const string RunFlag = "RUN_RUNTIME_AUTHORING_SETUP.flag";
        private const string ScenePath = "Assets/TrackLayoutTool/Demo/ImportedAssetRuntimeDemo.unity";
        private const string CatalogPath = "Assets/TrackLayoutTool/Data/TrackPieceCatalog.asset";
        private const string TrackTemplatePath = "Assets/TrackLayoutTool/Layouts/imported_pack_walkthrough.track.json";
        private const string PropTemplatePath = "Assets/TrackLayoutTool/Layouts/imported_pack_walkthrough.props.json";

        static RuntimeAuthoringSetup()
        {
            EditorApplication.delayCall += TryRunFlaggedSetup;
        }

        [MenuItem("Tools/Track Builder Setup/Configure Runtime Authoring Demo")]
        public static void ConfigureAndValidate()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                TrackPieceCatalog catalog = AssetDatabase.LoadAssetAtPath<TrackPieceCatalog>(CatalogPath);
                TextAsset trackTemplate = AssetDatabase.LoadAssetAtPath<TextAsset>(TrackTemplatePath);
                TextAsset propTemplate = AssetDatabase.LoadAssetAtPath<TextAsset>(PropTemplatePath);
                if (catalog == null || trackTemplate == null || propTemplate == null)
                {
                    throw new InvalidOperationException("Catalog or template JSON assets are missing.");
                }
                EnsureImportedMappings(catalog);

                RuntimeTrackAuthoringController existingController = UnityEngine.Object.FindFirstObjectByType<RuntimeTrackAuthoringController>();
                TrackLayoutRuntimeLoader oldLoader = UnityEngine.Object.FindFirstObjectByType<TrackLayoutRuntimeLoader>();
                GameObject controllerObject = existingController != null
                    ? existingController.gameObject
                    : oldLoader != null
                        ? oldLoader.gameObject
                        : new GameObject("RUNTIME AUTHORING - press Play");
                if (oldLoader != null)
                {
                    UnityEngine.Object.DestroyImmediate(oldLoader);
                }
                controllerObject.name = "RUNTIME AUTHORING - press Play";

                RuntimeTrackAuthoringController controller = controllerObject.GetComponent<RuntimeTrackAuthoringController>();
                if (controller == null)
                {
                    controller = controllerObject.AddComponent<RuntimeTrackAuthoringController>();
                }
                foreach (RuntimeTrackAuthoringController duplicate in UnityEngine.Object.FindObjectsByType<RuntimeTrackAuthoringController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (duplicate != controller)
                    {
                        UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
                    }
                }
                controller.catalog = catalog;
                controller.authoringCamera = Camera.main;
                controller.templateTrackJson = trackTemplate;
                controller.templatePropJson = new[] { propTemplate };
                controller.snapToGrid = false;
                controller.gridSize = 15f;
                controller.groundY = 0f;
                controller.rotationStep = 90f;
                controller.placementEnabled = true;
                controller.loadTemplateOnStart = false;
                controller.saveNamePrefix = "runtime_track";

                // Exercise the same public API used during Play mode.
                controller.InitializeForUse();
                controller.ClearAll();
                Require(controller.PlacePiece("track_straight_short", PieceCategory.Track, Vector3.zero, Vector3.zero, Vector3.one) != null,
                    "Could not place validation track piece.");
                Require(controller.PlacePiece("prop_cone", PieceCategory.Prop, new Vector3(0f, 0f, 15f), Vector3.zero, Vector3.one) != null,
                    "Could not place validation prop.");
                string validationStem = "runtime_authoring_validation_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string[] paths = controller.SaveTimestampedLayout(validationStem);
                Require(File.Exists(paths[0]) && File.Exists(paths[1]), "Timestamped JSON files were not written.");

                controller.ClearAll();
                Require(controller.LoadFromFiles(paths[0], paths[1]), "Validation JSON could not be loaded.");
                Require(controller.TrackPieceCount == 1 && controller.PropPieceCount == 1,
                    $"Reloaded counts were {controller.TrackPieceCount} track and {controller.PropPieceCount} props.");

                // Leave the scene blank so Play mode starts as a clean authoring canvas.
                controller.ClearAll();
                EditorUtility.SetDirty(controller);
                EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
                EditorSceneManager.SaveScene(controller.gameObject.scene, ScenePath);
                Selection.activeGameObject = controllerObject;

                string report =
                    "RUNTIME AUTHORING VALIDATION: PASSED\n" +
                    "Placed: 1 track + 1 prop\n" +
                    "Saved: timestamped track/prop JSON pair\n" +
                    "Cleared: runtime roots\n" +
                    "Reloaded: 1 track + 1 prop\n" +
                    "Scene left blank and ready for Play mode authoring\n" +
                    $"Validation track: {paths[0]}\n" +
                    $"Validation props: {paths[1]}\n" +
                    $"Runtime save folder: {controller.SaveFolderPath}\n";
                File.WriteAllText(ProjectPath("RuntimeAuthoring-RESULT.txt"), report);
                string failurePath = ProjectPath("RuntimeAuthoring-FAILED.txt");
                if (File.Exists(failurePath)) File.Delete(failurePath);
                Debug.Log(report);
            }
            catch (Exception exception)
            {
                File.WriteAllText(ProjectPath("RuntimeAuthoring-FAILED.txt"), exception.ToString());
                Debug.LogException(exception);
            }
        }

        private static void TryRunFlaggedSetup()
        {
            if (Application.isBatchMode) return;
            string flagPath = ProjectPath(RunFlag);
            if (!File.Exists(flagPath)) return;
            File.Delete(flagPath);
            ConfigureAndValidate();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Runtime authoring validation failed: " + message);
        }

        private static void EnsureImportedMappings(TrackPieceCatalog catalog)
        {
            SetMapping(catalog, "track_straight_short", PieceCategory.Track,
                "Assets/BEDRILL/Modular_Track_Free/Prefabs_Obs/Track_line_type_01_15m_free_obs.prefab");
            SetMapping(catalog, "track_straight_long", PieceCategory.Track,
                "Assets/BEDRILL/Modular_Track_Free/Prefabs_Obs/Track_line_type_01_30m_free_obs.prefab");
            SetMapping(catalog, "track_start_finish", PieceCategory.Track,
                "Assets/BEDRILL/Modular_Track_Free/Prefabs_Obs/Track_line_type_01_start_finish_15m_free_obs.prefab");
            SetMapping(catalog, "track_curve_90", PieceCategory.Track,
                "Assets/BEDRILL/Modular_Track_Free/Prefabs_Obs/Track_Corner_90d_type_01_15x15m_quad_free_obs.prefab");
            SetMapping(catalog, "track_curve_left_mesh", PieceCategory.Track,
                "Assets/BEDRILL/Modular_Track_Free/Prefabs_Obs/Track_Corner_90d_type_01_15x15m_quad_free_obs.prefab");
            SetMapping(catalog, "track_curve_right_mesh", PieceCategory.Track,
                "Assets/BEDRILL/Modular_Track_Free/Prefabs_Obs/Track_Corner_90d_type_01_15x15m_quad_free_obs.prefab");

            SetMapping(catalog, "prop_finisharch", PieceCategory.Prop,
                "Assets/BEDRILL/Track_Environment_Free/Prefabs/Arch_banner_finish_free_obs.prefab");
            SetMapping(catalog, "prop_arch", PieceCategory.Prop,
                "Assets/BEDRILL/Track_Environment_Free/Prefabs/Arch_01_free_obs.prefab");
            SetMapping(catalog, "prop_cone", PieceCategory.Prop,
                "Assets/BEDRILL/Track_Environment_Free/Prefabs/Cone_free_obs.prefab");
            SetMapping(catalog, "prop_barrier", PieceCategory.Prop,
                "Assets/BEDRILL/Track_Environment_Free/Prefabs/Barrier_free_obs.prefab");
            SetMapping(catalog, "prop_flag", PieceCategory.Prop,
                "Assets/BEDRILL/Track_Environment_Free/Prefabs/Flag_free_obs.prefab");
            SetMapping(catalog, "prop_traffic_light", PieceCategory.Prop,
                "Assets/BEDRILL/Track_Environment_Free/Prefabs/Traffic_light_free_obs.prefab");
            SetMapping(catalog, "prop_tribune", PieceCategory.Prop,
                "Assets/BEDRILL/Track_Environment_Free/Prefabs/Tribune_free_obs.prefab");
            SetMapping(catalog, "prop_tire", PieceCategory.Prop,
                "Assets/BEDRILL/Track_Environment_Free/Prefabs/Tire_free_obs.prefab");
            SetMapping(catalog, "prop_fireplug", PieceCategory.Prop,
                "Assets/BEDRILL/Track_Environment_Free/Prefabs/Cone_free_obs.prefab");
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        private static void SetMapping(TrackPieceCatalog catalog, string id, PieceCategory category, string assetPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            Require(prefab != null, "Missing imported prefab: " + assetPath);
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

        private static string ProjectPath(string relativePath)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
