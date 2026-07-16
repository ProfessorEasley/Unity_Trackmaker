using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SimpleTrackBuilder.Editor
{
    public static class DemoValidator
    {
        public static void ValidateDemo()
        {
            const string scenePath = "Assets/TrackLayoutTool/Demo/TrackBuilderDemo.unity";
            const string catalogPath = "Assets/TrackLayoutTool/Data/TrackPieceCatalog.asset";
            const string trackPath = "Assets/TrackLayoutTool/Layouts/student_test_track.track.json";
            const string propsPath = "Assets/TrackLayoutTool/Layouts/student_test_track.props.json";

            TrackPieceCatalog catalog = AssetDatabase.LoadAssetAtPath<TrackPieceCatalog>(catalogPath);
            Require(catalog != null, "Catalog asset is missing.");
            Require(catalog.Entries.Count == 5, "Catalog should contain five placeholder mappings.");
            Require(catalog.Entries.All(entry => entry.prefab != null), "A catalog prefab mapping is null.");

            TrackLayoutFile track = JsonUtility.FromJson<TrackLayoutFile>(ReadAssetFile(trackPath));
            PropAddendumFile props = JsonUtility.FromJson<PropAddendumFile>(ReadAssetFile(propsPath));
            Require(track != null && track.pieces.Count == 13, "Track JSON did not round-trip 13 pieces.");
            Require(props != null && props.props.Count == 3, "Prop JSON did not round-trip three props.");
            Require(props.trackFile == "student_test_track.track.json", "Prop addendum lost its track-file reference.");

            EditorSceneManager.OpenScene(scenePath);
            GameObject trackRoot = GameObject.Find("TRACK_LAYOUT");
            GameObject propRoot = GameObject.Find("TRACK_PROPS");
            Require(trackRoot != null, "Scene track root is missing.");
            Require(propRoot != null, "Scene prop root is missing.");
            TrackPieceIdentity[] trackIdentities = trackRoot.GetComponentsInChildren<TrackPieceIdentity>(true);
            TrackPieceIdentity[] propIdentities = propRoot.GetComponentsInChildren<TrackPieceIdentity>(true);
            int sceneTrackCount = trackIdentities.Count(item => item.category == PieceCategory.Track);
            int scenePropCount = propIdentities.Count(item => item.category == PieceCategory.Prop);
            Require(sceneTrackCount == 13, $"Scene has {sceneTrackCount} track pieces instead of 13 (root children={trackRoot.transform.childCount}, identities={trackIdentities.Length}, categories={string.Join(",", trackIdentities.Select(item => item.category))}).");
            Require(scenePropCount == 3, $"Scene has {scenePropCount} props instead of three.");

            TrackLayoutRuntimeLoader loader = UnityEngine.Object.FindFirstObjectByType<TrackLayoutRuntimeLoader>();
            Require(loader != null && loader.catalog == catalog, "Runtime loader is missing or has the wrong catalog.");
            loader.Rebuild();
            Transform generated = loader.transform.Find("Generated Track Layout");
            Require(generated != null, "Runtime loader did not create its generated root.");
            Require(generated.GetComponentsInChildren<TrackPieceIdentity>().Length == 16, "Runtime loader did not rebuild all 16 records.");
            loader.Clear();

            Debug.Log("TRACK BUILDER VALIDATION PASSED: catalog, JSON, scene roots, and runtime rebuild are healthy.");
        }

        private static string ReadAssetFile(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return File.ReadAllText(Path.Combine(projectRoot, assetPath));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Track Builder validation failed: " + message);
            }
        }
    }
}
