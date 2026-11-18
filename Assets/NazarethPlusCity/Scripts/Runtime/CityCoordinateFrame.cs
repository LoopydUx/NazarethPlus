using UnityEngine;

namespace NazarethPlusCity
{
    public class CityCoordinateFrame : ScriptableObject
    {
        public bool originSaved;
        public double originLongitude;
        public double originLatitude;

        [Tooltip("Stable id written into placement files and used as the save file name. Empty is treated as nazareth.")]
        public string cityId = "nazareth";

        [TextArea(3, 8)]
        public string orientation =
            "Unity meters. +X east, +Y up, +Z north. " +
            "GeoJSON is WGS84 longitude, latitude. " +
            "Horizontal placement uses the saved boundary centroid. " +
            "localGroundY = (sourceElevation - elevationOrigin) * worldUnitsPerMetre. " +
            "The elevation origin lives on CityGenerationSettings and is reused on every rebuild.";

        public const string AxisSummary =
            "1 Unity unit = 1 meter. +X east, +Y up, +Z north. " +
            "localGroundY = (sourceElevation - elevationOrigin) * worldUnitsPerMetre.";
    }
}
