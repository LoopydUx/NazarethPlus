using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// Scene root for player placements. City rebuilds do not own or delete it.
    /// </summary>
    public class PlacedAssetsRoot : MonoBehaviour
    {
        [TextArea(2, 4)]
        public string note = "Player placements. City importer rebuilds leave this object and its children alone.";
    }

    public class PlacedAssetMarker : MonoBehaviour
    {
        public string placementId;
        public string catalogId;
        public string assetType;
        public double longitude;
        public double latitude;
        public float groundOffset;
        public float rotationY;
        public float scale = 1f;
        public string createdUtc;
    }

    public class PlacementPreviewMarker : MonoBehaviour
    {
    }
}
