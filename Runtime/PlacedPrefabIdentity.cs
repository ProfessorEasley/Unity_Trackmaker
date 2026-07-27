using UnityEngine;

namespace PrefabBuilder
{
    /// <summary>
    /// Stamped onto every prefab the builder places, so a layout can be captured from the scene
    /// later. Without this the tool cannot tell a placed piece from anything else under the root.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlacedPrefabIdentity : MonoBehaviour
    {
        public string pieceId;
    }
}
