using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// Marks the imported GLB under a building. The building object stays the feature;
    /// this child is the visible model and its collision.
    /// </summary>
    public class BuildingModelVisual : MonoBehaviour
    {
        public string featureId;
        public string catalogBuildingId;
        public string sourceFile;
        public string contentHash;
        public int importVersion;
        public int materialCount;
        public int vertexCount;
        public float yawDegrees;
        public float pitchDegrees;
        public float rollDegrees;

        public static void HideExtrusion(BuildingFeature feature)
        {
            if (feature == null || feature.GetComponentInChildren<BuildingModelVisual>(true) == null)
                return;
            var renderers = feature.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].GetComponent<BuildingFoundation>() != null || BelongsToModel(renderers[i].transform))
                    continue;
                renderers[i].enabled = false;
            }

            var colliders = feature.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].GetComponent<BuildingFoundation>() != null || BelongsToModel(colliders[i].transform))
                    continue;
                colliders[i].enabled = false;
            }
        }

        public static bool BelongsToModel(Transform transform)
        {
            return transform != null && transform.GetComponentInParent<BuildingModelVisual>(true) != null;
        }
    }
}
