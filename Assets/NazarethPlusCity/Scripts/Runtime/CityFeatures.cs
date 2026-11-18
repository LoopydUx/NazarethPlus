using System;
using System.Collections.Generic;
using UnityEngine;

namespace NazarethPlusCity
{
    public class GeneratorOwned : MonoBehaviour
    {
        public string key;
    }

    public class FeatureRecord : MonoBehaviour
    {
        public string featureId;
        public string featureKind;
        public string geometryType;
        public string displayName;
        [TextArea(3, 10)]
        public string propertiesJson;
    }

    [Serializable]
    public class LocalRing
    {
        public List<Vector2> points = new List<Vector2>();
    }

    [Serializable]
    public class FootprintPart
    {
        public List<LocalRing> rings = new List<LocalRing>();
    }

    [Serializable]
    public class RoadPart
    {
        public List<Vector2> centerline = new List<Vector2>();
    }

    public class BuildingFeature : MonoBehaviour
    {
        public string sourceHeightRaw;
        public string sourceFloorRaw;
        public float resolvedHeightMeters = 9f;
        public string heightResolution = "Fallback";
        [TextArea(2, 4)]
        public string heightDetail;
        public bool useHeightOverride;
        public float heightOverrideMeters = 9f;
        public float appliedHeightMeters = 9f;
        public bool useTintOverride;
        public Color tintOverride = Color.white;
        public Color appliedTint = Color.white;
        public int materialVariant;
        public List<FootprintPart> parts = new List<FootprintPart>();
        public Material wallMaterial;
        public Material roofMaterial;
        public float uvMeters = 2.5f;

        [Header("Terrain placement")]
        public float levelBaseY;
        public float meshBaseLocalY;
        public float foundationDepthMeters;
        public bool tallFoundation;
        [TextArea(2, 4)]
        public string terrainNote;

        public float ActiveHeight => useHeightOverride ? Mathf.Max(0.25f, heightOverrideMeters) : resolvedHeightMeters;

        void OnEnable()
        {
            if (useTintOverride)
                ApplyTintOverride();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying || CityEditGuard.Suppress)
                return;
            if (!useHeightOverride && !useTintOverride)
                return;
            bool heightChanged = useHeightOverride && Mathf.Abs(heightOverrideMeters - appliedHeightMeters) > 0.01f;
            bool tintChanged = useTintOverride && tintOverride != appliedTint;
            if (!heightChanged && !tintChanged)
                return;
            UnityEditor.EditorApplication.delayCall += DelayedApply;
        }

        void DelayedApply()
        {
            if (this == null || CityEditGuard.Suppress)
                return;
            if (useHeightOverride && Mathf.Abs(heightOverrideMeters - appliedHeightMeters) > 0.01f)
                CityGeometry.RebuildBuilding(this);
            if (useTintOverride)
                ApplyTintOverride();
        }
#endif

        public void ApplyTintOverride()
        {
            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].GetComponent<BuildingFoundation>() != null || BuildingModelVisual.BelongsToModel(renderers[i].transform))
                    continue;
                renderers[i].GetPropertyBlock(block, 0);
                block.SetColor("_BaseColor", tintOverride);
                renderers[i].SetPropertyBlock(block, 0);
            }

            appliedTint = tintOverride;
        }
    }

    public class StreetFeature : MonoBehaviour
    {
        public string highway;
        public string streetType;
        public string widthDetail;
        public float resolvedWidthMeters = 6f;
        public bool useWidthOverride;
        public float widthOverrideMeters = 6f;
        public float appliedWidthMeters = 6f;
        public float surfaceY = 0.04f;
        public List<RoadPart> sourceParts = new List<RoadPart>();
        public Material roadMaterial;
        public float uvMeters = 4f;

        public float ActiveWidth => useWidthOverride ? Mathf.Max(0.4f, widthOverrideMeters) : resolvedWidthMeters;

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying || CityEditGuard.Suppress)
                return;
            if (!useWidthOverride || Mathf.Abs(widthOverrideMeters - appliedWidthMeters) <= 0.01f)
                return;
            UnityEditor.EditorApplication.delayCall += DelayedApply;
        }

        void DelayedApply()
        {
            if (this == null || CityEditGuard.Suppress || !useWidthOverride)
                return;
            CityGeometry.RebuildStreet(this);
        }
#endif
    }

    public class CitySpawnPoint : MonoBehaviour
    {
        public float yaw;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.35f, 0.9f);
            Vector3 feet = transform.position;
            Gizmos.DrawWireSphere(feet + Vector3.up * 0.9f, 0.32f);
            Vector3 look = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Gizmos.DrawRay(feet + Vector3.up * 1.6f, look * 2.5f);
        }
    }

    public static class CityEditGuard
    {
        public static bool Suppress;
    }

    public static class CityLabels
    {
        public static int StableHash(string value)
        {
            unchecked
            {
                int hash = 23;
                if (value == null)
                    return hash;
                for (int i = 0; i < value.Length; i++)
                    hash = hash * 31 + value[i];
                return hash;
            }
        }

        public static string Sanitize(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "missing";
            var chars = id.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                    chars[i] = '_';
            }

            return new string(chars);
        }
    }
}
