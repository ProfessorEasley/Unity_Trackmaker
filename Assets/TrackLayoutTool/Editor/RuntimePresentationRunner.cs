using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SimpleTrackBuilder.Editor
{
    public static class RuntimePresentationRunner
    {
        private const string RuntimeScenePath = "Assets/TrackLayoutTool/Demo/ImportedAssetRuntimeDemo.unity";
        private const string ResultScenePath = "Assets/TrackLayoutTool/Demo/RuntimeAuthoringPresentationResult.unity";

        [MenuItem("Tools/Track Builder Setup/Run Runtime Presentation Sequence")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(RuntimeScenePath);
            RuntimeTrackAuthoringController controller = UnityEngine.Object.FindFirstObjectByType<RuntimeTrackAuthoringController>();
            Require(controller != null, "RuntimeTrackAuthoringController is missing from the runtime demo scene.");

            controller.InitializeForUse();
            controller.ClearAll();

            // Step 1: use the runtime API to author a short bend.
            Place(controller, "track_start_finish", PieceCategory.Track, 0f, 0f, 0f);
            Place(controller, "track_straight_short", PieceCategory.Track, 0f, 15f, 0f);
            Place(controller, "track_straight_short", PieceCategory.Track, 0f, 30f, 0f);
            Place(controller, "track_curve_90", PieceCategory.Track, 0f, 45f, 0f);
            Place(controller, "track_straight_short", PieceCategory.Track, 15f, 45f, 90f);

            // Step 2: add a separate prop layer.
            Place(controller, "prop_finisharch", PieceCategory.Prop, 0f, 2f, 0f);
            Place(controller, "prop_cone", PieceCategory.Prop, -5f, 12f, 0f);
            Place(controller, "prop_cone", PieceCategory.Prop, 5f, 12f, 0f);
            Place(controller, "prop_barrier", PieceCategory.Prop, -7f, 27f, 90f);
            Require(controller.TrackPieceCount == 5 && controller.PropPieceCount == 4, "Initial placement counts are wrong.");

            // Step 3: save a timestamped track/prop pair.
            string stem = "presentation_runtime_demo_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string[] paths = controller.SaveTimestampedLayout(stem);
            Require(File.Exists(paths[0]) && File.Exists(paths[1]), "Presentation JSON pair was not written.");

            // Step 4: remove all runtime-created objects.
            controller.ClearAll();
            Require(controller.TrackPieceCount == 0 && controller.PropPieceCount == 0, "Clear did not empty both layers.");

            // Step 5: reconstruct the same result from disk.
            Require(controller.LoadFromFiles(paths[0], paths[1]), "Presentation JSON pair did not load.");
            Require(controller.TrackPieceCount == 5 && controller.PropPieceCount == 4, "Reloaded counts are wrong.");

            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            EditorSceneManager.SaveScene(controller.gameObject.scene, ResultScenePath);
            Selection.activeGameObject = controller.gameObject;

            string report =
                "RUNTIME PRESENTATION SEQUENCE: PASSED\n" +
                "Placed: 5 track pieces + 4 props\n" +
                "Saved: timestamped JSON pair\n" +
                "Cleared: 0 track + 0 props\n" +
                "Reloaded: 5 track pieces + 4 props\n" +
                $"Result scene: {ResultScenePath}\n" +
                $"Track JSON: {paths[0]}\n" +
                $"Prop JSON: {paths[1]}\n";
            File.WriteAllText(ProjectPath("RuntimePresentation-RESULT.txt"), report);
            Debug.Log(report);
        }

        private static void Place(RuntimeTrackAuthoringController controller, string id, PieceCategory category, float x, float z, float yaw)
        {
            GameObject result = controller.PlacePiece(
                id,
                category,
                new Vector3(x, 0f, z),
                new Vector3(0f, yaw, 0f),
                Vector3.one);
            Require(result != null, "Could not place " + id + ".");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Runtime presentation failed: " + message);
        }

        private static string ProjectPath(string relativePath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, relativePath);
        }
    }
}
