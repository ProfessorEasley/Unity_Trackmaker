using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleTrackBuilder
{
    [Serializable]
    public sealed class PlacedPieceRecord
    {
        public string pieceId;
        public Vector3 position;
        public Vector3 rotationEuler;
        public Vector3 scale = Vector3.one;
    }

    [Serializable]
    public sealed class TrackLayoutFile
    {
        public int schemaVersion = 1;
        public string layoutName;
        public List<PlacedPieceRecord> pieces = new();
    }

    [Serializable]
    public sealed class PropAddendumFile
    {
        public int schemaVersion = 1;
        public string layoutName;
        public string trackFile;
        public List<PlacedPieceRecord> props = new();
    }
}
