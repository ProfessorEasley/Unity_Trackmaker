using UnityEngine;

namespace SimpleTrackBuilder
{
    [DisallowMultipleComponent]
    public sealed class TrackPieceIdentity : MonoBehaviour
    {
        public string pieceId;
        public PieceCategory category;
    }
}
