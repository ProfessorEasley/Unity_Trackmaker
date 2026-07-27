using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrefabBuilder
{
    /// <summary>
    /// One placed prefab. Transforms are stored in the LAYOUT ROOT's local space, not world space,
    /// so moving/rotating/scaling the root relocates the whole layout as a rigid unit instead of
    /// corrupting the round-trip. See LayoutFile.schemaVersion.
    /// </summary>
    [Serializable]
    public sealed class PlacedPrefabRecord
    {
        public string pieceId;
        public Vector3 localPosition;
        public Vector3 localRotationEuler;
        public Vector3 localScale = Vector3.one;
    }

    [Serializable]
    public sealed class LayoutFile
    {
        // Deliberately NOT initialised to CurrentSchemaVersion. JsonUtility runs field initialisers
        // and then overwrites only the keys it finds, so a default of 2 would make an UNVERSIONED
        // legacy file claim to be current and take the modern code path - silently, because the
        // legacy field names simply do not bind and every transform comes back as zero.
        // 0 therefore means "unversioned, treat as legacy", which is the safe reading.
        //
        // 1 = world-space transforms under the field names position/rotationEuler/scale.
        // 2 = root-local transforms under localPosition/localRotationEuler/localScale.
        public int schemaVersion;
        public string layoutName;
        public List<PlacedPrefabRecord> pieces = new();

        public const int CurrentSchemaVersion = 2;
    }

    /// <summary>
    /// The pre-package on-disk shape (schemaVersion 0 and 1), kept only so those files can still be
    /// read. JsonUtility binds strictly by field NAME and silently drops keys it does not recognise,
    /// so the old names have to exist somewhere concrete or the data is lost without any error.
    /// </summary>
    [Serializable]
    public sealed class LegacyPlacedPieceRecord
    {
        public string pieceId;
        public Vector3 position;
        public Vector3 rotationEuler;
        public Vector3 scale = Vector3.one;
    }

    [Serializable]
    public sealed class LegacyLayoutFile
    {
        public int schemaVersion;
        public string layoutName;
        public List<LegacyPlacedPieceRecord> pieces = new();
    }
}
