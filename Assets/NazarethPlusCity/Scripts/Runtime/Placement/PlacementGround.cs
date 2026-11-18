using System.Collections.Generic;
using UnityEngine;

namespace NazarethPlusCity
{
    public struct PlacementAimSample
    {
        public bool HasPoint;
        public bool Rejected;
        public string Reason;
        public Vector3 Point;
    }

    /// <summary>
    /// Ground queries that skip the preview, the player, and rooftops.
    /// </summary>
    public static class PlacementGround
    {
        static readonly RaycastHit[] Hits = new RaycastHit[256];

        public static bool IsWalkSurface(Collider collider)
        {
            if (collider == null)
                return false;
            if (collider.GetComponentInParent<StreetFeature>() != null)
                return true;
            return collider.GetComponentInParent<TerrainChunk>() != null;
        }

        public static PlacementAimSample CursorSample(Camera camera, Transform preview, Transform ignore)
        {
            var sample = new PlacementAimSample { Reason = "Point at a street or the ground." };
            if (camera == null || MousePosition(out Vector2 screen) == false)
                return sample;
            Ray ray = camera.ScreenPointToRay(screen);
            int count = Physics.RaycastNonAlloc(ray, Hits, Mathf.Max(camera.farClipPlane, 200f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            int nearest = -1;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (Skip(Hits[i].collider, preview, ignore))
                    continue;
                if (Hits[i].distance < nearestDistance)
                {
                    nearestDistance = Hits[i].distance;
                    nearest = i;
                }
            }

            if (nearest < 0)
                return sample;
            Collider collider = Hits[nearest].collider;
            sample.HasPoint = true;
            sample.Point = Hits[nearest].point;
            if (IsWalkSurface(collider) && Hits[nearest].normal.y >= 0.4f)
                return sample;
            sample.Rejected = true;
            if (collider.GetComponentInParent<PlacedAssetMarker>() != null)
                sample.Reason = "Overlaps another placed object.";
            else
                sample.Reason = "Not on supported ground.";
            return sample;
        }

        public static bool TryDownward(float east, float north, Transform preview, Transform ignore, out RaycastHit hit)
        {
            hit = default;
            var terrain = CityTerrain.Active != null ? CityTerrain.Active : Object.FindAnyObjectByType<CityTerrain>();
            float top = terrain != null ? terrain.maxWorldY + 80f : 900f;
            float bottom = terrain != null ? terrain.minWorldY - 80f : -120f;
            var origin = new Vector3(east, top, north);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, Mathf.Max(1f, top - bottom), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool found = false;
            float bestY = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (Skip(Hits[i].collider, preview, ignore))
                    continue;
                if (!IsWalkSurface(Hits[i].collider) || Hits[i].normal.y < 0.4f)
                    continue;
                if (Hits[i].point.y > bestY)
                {
                    bestY = Hits[i].point.y;
                    hit = Hits[i];
                    found = true;
                }
            }

            return found;
        }

        public static bool TrySupport(float east, float north, Transform ignore, out float y)
        {
            y = 0f;
            var terrain = CityTerrain.Active != null ? CityTerrain.Active : Object.FindAnyObjectByType<CityTerrain>();
            float top = terrain != null ? terrain.maxWorldY + 80f : 900f;
            float bottom = terrain != null ? terrain.minWorldY - 40f : -80f;
            Vector3 origin = new Vector3(east, top, north);
            for (int step = 0; step < 16; step++)
            {
                float distance = origin.y - bottom;
                if (distance < 0.2f)
                    return false;
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return false;
                if (!Skip(hit.collider, null, ignore) && IsWalkSurface(hit.collider) && hit.normal.y >= 0.4f)
                {
                    y = hit.point.y;
                    return true;
                }

                origin = hit.point + Vector3.down * 0.08f;
            }

            return false;
        }

        public static bool TryRestoreHeight(float east, float north, BoxCollider footprint, float yawDegrees, float scale, out float groundY)
        {
            groundY = 0f;
            if (!TryDownward(east, north, null, null, out RaycastHit center))
                return false;
            groundY = center.point.y;
            if (footprint == null)
                return true;
            Vector3 size = footprint.size * Mathf.Max(0.01f, scale);
            Vector3 localCenter = footprint.center * Mathf.Max(0.01f, scale);
            Quaternion rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            float halfX = Mathf.Max(0.05f, size.x * 0.5f - 0.02f);
            float halfZ = Mathf.Max(0.05f, size.z * 0.5f - 0.02f);
            var corners = new[]
            {
                new Vector3(-halfX, 0f, -halfZ),
                new Vector3(halfX, 0f, -halfZ),
                new Vector3(halfX, 0f, halfZ),
                new Vector3(-halfX, 0f, halfZ)
            };
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 world = new Vector3(east, 0f, north) + rotation * (new Vector3(localCenter.x, 0f, localCenter.z) + corners[i]);
                if (TryDownward(world.x, world.z, null, null, out RaycastHit corner) && corner.point.y > groundY)
                    groundY = corner.point.y;
            }

            return true;
        }

        static bool Skip(Collider collider, Transform preview, Transform ignore)
        {
            if (collider == null || collider.isTrigger)
                return true;
            Transform transform = collider.transform;
            if (preview != null && (transform == preview || transform.IsChildOf(preview)))
                return true;
            if (ignore != null && (transform == ignore || transform.IsChildOf(ignore)))
                return true;
            if (transform.GetComponentInParent<PlacementPreviewMarker>() != null)
                return true;
            if (transform.GetComponentInParent<FirstPersonWalker>() != null)
                return true;
            return false;
        }

        static bool MousePosition(out Vector2 screen)
        {
            screen = default;
            if (UnityEngine.InputSystem.Mouse.current == null)
                return false;
            screen = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            return true;
        }
    }
}
