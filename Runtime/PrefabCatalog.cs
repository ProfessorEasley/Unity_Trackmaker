using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrefabBuilder
{
    /// <summary>
    /// One prefab in the palette, plus the optional per-prefab lattice steps.
    /// </summary>
    [Serializable]
    public sealed class PrefabCatalogEntry
    {
        public string id;
        public GameObject prefab;

        // A single global grid cannot serve a 15m road module and a 0.2m stair riser at the same
        // time, so each prefab may carry its own steps. 0 means "fall back to the window's global".
        public float gridOverride;
        public float heightOverride;
        public float tiltOverride;
    }

    /// <summary>
    /// The prefab palette. Created on demand by the Prefab Builder window at
    /// Assets/PrefabBuilder/Data/PrefabBuilderCatalog.asset; nothing else writes to it.
    /// </summary>
    [CreateAssetMenu(menuName = "Prefab Builder/Prefab Catalog", fileName = "PrefabBuilderCatalog")]
    public sealed class PrefabCatalog : ScriptableObject
    {
        [SerializeField] private List<PrefabCatalogEntry> entries = new();

        public List<PrefabCatalogEntry> Entries => entries;

        public PrefabCatalogEntry FindEntryById(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (PrefabCatalogEntry entry in entries)
            {
                if (string.Equals(entry.id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>
        /// True only when the id maps to an entry that still has a live prefab reference. A catalog
        /// entry whose prefab was deleted is exactly as unusable as a missing id, so both callers
        /// (the palette and the layout loader) must treat them the same way.
        /// </summary>
        public bool CanResolve(string id)
        {
            PrefabCatalogEntry entry = FindEntryById(id);
            return entry != null && entry.prefab != null;
        }
    }
}
