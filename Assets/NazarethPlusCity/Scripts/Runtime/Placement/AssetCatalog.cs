using System;
using System.Collections.Generic;
using UnityEngine;

namespace NazarethPlusCity
{
    public static class PlaceableIds
    {
        public const string Bin = "nazareth.wheelie-bin.closed";
        public const string BinType = "bin";
        public const string DefaultCityId = "nazareth";
        public const string PrefabPath = "Assets/NazarethPlusCity/Art/Props/WheelieBinClosed.prefab";
        public const string CatalogPath = "Assets/NazarethPlusCity/Art/AssetCatalog.asset";
        public const string CoordinatePath = "Assets/NazarethPlusCity/Settings/CityCoordinateFrame.asset";
        public const string ThumbnailPath = "Assets/NazarethPlusCity/Art/Props/WheelieBinClosed_Thumbnail.png";

        public const string MissingBin =
            "The Bin prefab is not assigned. Assign " + PrefabPath +
            " on the Asset Catalog entry '" + Bin + "' and on Bin Prefab on CityWalker. No substitute object was spawned.";
    }

    public enum PlaceableCategory
    {
        Bin = 0,
        Bench = 1,
        Tree = 2,
        Streetlight = 3,
        Other = 4
    }

    [Serializable]
    public class PlaceableAssetEntry
    {
        public string catalogId;
        public string displayName;
        public PlaceableCategory category;
        public string assetType;
        public GameObject prefab;
        public Sprite thumbnail;
        public float uniformScale = 1f;
    }

    /// <summary>
    /// Placeable objects. Benches, trees, and streetlights are additional entries of the same list.
    /// </summary>
    [CreateAssetMenu(fileName = "AssetCatalog", menuName = "NazarethPlusCity/Asset Catalog")]
    public class AssetCatalog : ScriptableObject
    {
        public List<PlaceableAssetEntry> entries = new List<PlaceableAssetEntry>();

        public PlaceableAssetEntry Find(string catalogId)
        {
            if (entries == null || string.IsNullOrEmpty(catalogId))
                return null;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].catalogId == catalogId)
                    return entries[i];
            }

            return null;
        }
    }
}
