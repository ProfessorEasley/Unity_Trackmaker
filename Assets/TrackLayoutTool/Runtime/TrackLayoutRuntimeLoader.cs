using System.Collections.Generic;
using UnityEngine;

namespace SimpleTrackBuilder
{
    [AddComponentMenu("Track Builder/Runtime Layout Loader")]
    public sealed class TrackLayoutRuntimeLoader : MonoBehaviour
    {
        public TrackPieceCatalog catalog;
        public TextAsset trackJson;
        public TextAsset[] propAddenda;
        public bool buildOnStart = true;

        private Transform generatedRoot;

        private void Start()
        {
            if (buildOnStart)
            {
                Rebuild();
            }
        }

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            Clear();
            generatedRoot = new GameObject("Generated Track Layout").transform;
            generatedRoot.SetParent(transform, false);

            if (trackJson != null)
            {
                BuildTrackFromJson(trackJson.text);
            }

            if (propAddenda == null)
            {
                return;
            }

            foreach (TextAsset addendum in propAddenda)
            {
                if (addendum != null)
                {
                    BuildPropsFromJson(addendum.text);
                }
            }
        }

        public void BuildTrackFromJson(string json)
        {
            TrackLayoutFile data = JsonUtility.FromJson<TrackLayoutFile>(json);
            BuildRecords(data?.pieces, PieceCategory.Track, "Track");
        }

        public void BuildPropsFromJson(string json)
        {
            PropAddendumFile data = JsonUtility.FromJson<PropAddendumFile>(json);
            BuildRecords(data?.props, PieceCategory.Prop, "Props");
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            Transform oldRoot = generatedRoot != null ? generatedRoot : transform.Find("Generated Track Layout");
            if (oldRoot == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(oldRoot.gameObject);
            }
            else
            {
                DestroyImmediate(oldRoot.gameObject);
            }

            generatedRoot = null;
        }

        private void BuildRecords(IEnumerable<PlacedPieceRecord> records, PieceCategory category, string rootName)
        {
            if (records == null || catalog == null)
            {
                return;
            }

            if (generatedRoot == null)
            {
                generatedRoot = new GameObject("Generated Track Layout").transform;
                generatedRoot.SetParent(transform, false);
            }

            Transform categoryRoot = generatedRoot.Find(rootName);
            if (categoryRoot == null)
            {
                categoryRoot = new GameObject(rootName).transform;
                categoryRoot.SetParent(generatedRoot, false);
            }

            foreach (PlacedPieceRecord record in records)
            {
                GameObject prefab = catalog.FindPrefab(record.pieceId, category);
                if (prefab == null)
                {
                    Debug.LogWarning($"Track Builder: no {category} prefab mapped for '{record.pieceId}'.", this);
                    continue;
                }

                GameObject instance = Instantiate(prefab, record.position, Quaternion.Euler(record.rotationEuler), categoryRoot);
                instance.name = record.pieceId;
                instance.transform.localScale = record.scale;
                TrackPieceIdentity identity = instance.GetComponent<TrackPieceIdentity>();
                if (identity == null)
                {
                    identity = instance.AddComponent<TrackPieceIdentity>();
                }

                identity.pieceId = record.pieceId;
                identity.category = category;
            }
        }
    }
}
