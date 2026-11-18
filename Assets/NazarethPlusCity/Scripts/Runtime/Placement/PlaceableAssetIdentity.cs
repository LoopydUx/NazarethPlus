using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// Stable identity stored on the prefab itself.
    /// </summary>
    public class PlaceableAssetIdentity : MonoBehaviour
    {
        public string catalogId = PlaceableIds.Bin;
        public string assetType = PlaceableIds.BinType;
        public string sourceFile = "nazareth-green-wheelie-bin-closed.glb";
        public float measuredHeightMeters;
        public float measuredWidthMeters;
        public float measuredDepthMeters;
    }
}
