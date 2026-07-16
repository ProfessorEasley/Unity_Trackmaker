using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleTrackBuilder
{
    public enum PieceCategory
    {
        Track,
        Prop
    }

    [Serializable]
    public sealed class TrackPieceCatalogEntry
    {
        public string id;
        public PieceCategory category;
        public GameObject prefab;

        // Optional per-prefab lattice steps for the Prefab Builder window. A global grid cannot
        // serve a 15m road module and a 0.2m stair riser at the same time, so each prefab may
        // carry its own. 0 means "fall back to the window's global step".
        public float gridOverride;
        public float heightOverride;
        public float tiltOverride;
    }

    [CreateAssetMenu(menuName = "Track Builder/Piece Catalog", fileName = "TrackPieceCatalog")]
    public sealed class TrackPieceCatalog : ScriptableObject
    {
        [SerializeField] private List<TrackPieceCatalogEntry> entries = new();

        public List<TrackPieceCatalogEntry> Entries => entries;

        public GameObject FindPrefab(string id, PieceCategory category)
        {
            foreach (TrackPieceCatalogEntry entry in entries)
            {
                if (entry.category == category &&
                    string.Equals(entry.id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.prefab;
                }
            }

            return null;
        }

        public bool ContainsId(string id, PieceCategory category)
        {
            return FindEntry(id, category) != null;
        }

        public TrackPieceCatalogEntry FindEntry(string id, PieceCategory category)
        {
            foreach (TrackPieceCatalogEntry entry in entries)
            {
                if (entry.category == category &&
                    string.Equals(entry.id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        // Category-agnostic lookups used by the generic Prefab Builder window.
        public GameObject FindPrefab(string id)
        {
            TrackPieceCatalogEntry entry = FindEntryById(id);
            return entry != null ? entry.prefab : null;
        }

        public bool ContainsId(string id)
        {
            return FindEntryById(id) != null;
        }

        public TrackPieceCatalogEntry FindEntryById(string id)
        {
            foreach (TrackPieceCatalogEntry entry in entries)
            {
                if (string.Equals(entry.id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
