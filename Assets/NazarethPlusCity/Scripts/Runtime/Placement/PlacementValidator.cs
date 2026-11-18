using UnityEngine;

namespace NazarethPlusCity
{
    public struct PlacementEvaluation
    {
        public bool Valid;
        public bool HasPose;
        public string Reason;
        public Vector3 Position;
        public float GroundY;
        public float Yaw;
        public float SlopeDegrees;
    }

    public struct PlacementCheck
    {
        public PlacementAimSample Aim;
        public bool LockPosition;
        public Vector3 LockedPosition;
        public float YawDegrees;
        public float Scale;
        public BoxCollider Footprint;
        public CityBoundaryMap Boundary;
        public float MaxSlopeDegrees;
        public Vector3 PlayerPosition;
        public float PlayerRadius;
        public bool CheckPlayer;
        public Transform Preview;
        public Transform IgnoreInstance;
    }

    public static class PlacementValidator
    {
        static readonly Collider[] Overlaps = new Collider[32];

        public static PlacementEvaluation Evaluate(PlacementCheck check)
        {
            float yaw = check.YawDegrees;
            float scale = check.Scale > 0f ? check.Scale : 1f;
            Vector3 aim = check.LockPosition ? check.LockedPosition : check.Aim.Point;
            var result = new PlacementEvaluation
            {
                Yaw = yaw,
                Position = aim,
                Reason = "Not on supported ground."
            };
            if (!check.LockPosition && !check.Aim.HasPoint)
            {
                result.Reason = string.IsNullOrEmpty(check.Aim.Reason) ? "Point at a street or the ground." : check.Aim.Reason;
                return result;
            }

            if (!check.LockPosition && check.Aim.Rejected)
            {
                result.HasPose = true;
                result.Position = check.Aim.Point;
                result.Reason = string.IsNullOrEmpty(check.Aim.Reason) ? "Not on supported ground." : check.Aim.Reason;
                return result;
            }

            if (check.Footprint == null)
            {
                result.HasPose = true;
                result.Reason = "The bin prefab has no collider, so its base cannot be checked.";
                return result;
            }

            Vector2[] flat = FootprintPoints(aim, check.Footprint, yaw, scale);
            if (!InsideCity(check.Boundary, flat))
            {
                result.HasPose = true;
                result.Position = new Vector3(aim.x, aim.y, aim.z);
                result.Reason = "Outside the city.";
                return result;
            }

            if (!SampleBase(flat, check.Preview, check.IgnoreInstance, out float minY, out float maxY, out float slope, out bool complete))
            {
                result.HasPose = true;
                result.Reason = complete ? "The base leaves supported ground." : "Not on supported ground.";
                return result;
            }

            result.HasPose = true;
            result.GroundY = maxY;
            result.SlopeDegrees = slope;
            result.Position = new Vector3(aim.x, maxY, aim.z);
            if (slope > Mathf.Max(1f, check.MaxSlopeDegrees))
            {
                result.Reason = "Slope is too steep (" + slope.ToString("0") + "°).";
                return result;
            }

            if (check.CheckPlayer && TooCloseToPlayer(result.Position, check.Footprint, yaw, scale, check.PlayerPosition, check.PlayerRadius))
            {
                result.Reason = "Too close to you.";
                return result;
            }

            string blocked = OverlapReason(result.Position, check.Footprint, yaw, scale, check.Preview, check.IgnoreInstance);
            if (blocked != null)
            {
                result.Reason = blocked;
                return result;
            }

            result.Valid = true;
            result.Reason = "Ready to place.";
            return result;
        }

        static bool InsideCity(CityBoundaryMap boundary, Vector2[] points)
        {
            if (boundary == null)
                return false;
            for (int i = 0; i < points.Length; i++)
            {
                if (!boundary.Contains(points[i]))
                    return false;
            }

            return true;
        }

        static bool SampleBase(Vector2[] points, Transform preview, Transform ignore, out float minY, out float maxY, out float slope, out bool any)
        {
            minY = float.PositiveInfinity;
            maxY = float.NegativeInfinity;
            slope = 0f;
            any = false;
            int hits = 0;
            Vector3 low = default;
            Vector3 high = default;
            for (int i = 0; i < points.Length; i++)
            {
                if (!PlacementGround.TryDownward(points[i].x, points[i].y, preview, ignore, out RaycastHit hit))
                    continue;
                any = true;
                hits++;
                float angle = Vector3.Angle(hit.normal, Vector3.up);
                if (angle > slope)
                    slope = angle;
                if (hit.point.y < minY)
                {
                    minY = hit.point.y;
                    low = hit.point;
                }

                if (hit.point.y > maxY)
                {
                    maxY = hit.point.y;
                    high = hit.point;
                }
            }

            if (hits == 0)
            {
                minY = maxY = 0f;
                return false;
            }

            if (hits < points.Length)
                return false;
            float run = Vector2.Distance(new Vector2(low.x, low.z), new Vector2(high.x, high.z));
            if (run > 0.05f)
            {
                float rise = Mathf.Atan2(maxY - minY, run) * Mathf.Rad2Deg;
                if (rise > slope)
                    slope = rise;
            }

            return true;
        }

        static Vector2[] FootprintPoints(Vector3 aim, BoxCollider footprint, float yaw, float scale)
        {
            Vector3 size = footprint.size * scale;
            Vector3 center = footprint.center * scale;
            float halfX = Mathf.Max(0.05f, size.x * 0.5f - 0.02f);
            float halfZ = Mathf.Max(0.05f, size.z * 0.5f - 0.02f);
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            var locals = new[]
            {
                new Vector3(center.x, 0f, center.z),
                new Vector3(center.x - halfX, 0f, center.z - halfZ),
                new Vector3(center.x + halfX, 0f, center.z - halfZ),
                new Vector3(center.x + halfX, 0f, center.z + halfZ),
                new Vector3(center.x - halfX, 0f, center.z + halfZ)
            };
            var points = new Vector2[locals.Length];
            for (int i = 0; i < locals.Length; i++)
            {
                Vector3 world = aim + rotation * locals[i];
                points[i] = new Vector2(world.x, world.z);
            }

            return points;
        }

        static bool TooCloseToPlayer(Vector3 position, BoxCollider footprint, float yaw, float scale, Vector3 player, float radius)
        {
            Vector3 size = footprint.size * scale;
            Vector3 center = footprint.center * scale;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 worldCenter = position + rotation * new Vector3(center.x, 0f, center.z);
            Vector3 local = Quaternion.Inverse(rotation) * (player - worldCenter);
            float outsideX = Mathf.Max(0f, Mathf.Abs(local.x) - size.x * 0.5f);
            float outsideZ = Mathf.Max(0f, Mathf.Abs(local.z) - size.z * 0.5f);
            float distance = Mathf.Sqrt(outsideX * outsideX + outsideZ * outsideZ);
            return distance < Mathf.Max(0.05f, radius) + 0.08f;
        }

        static string OverlapReason(Vector3 position, BoxCollider footprint, float yaw, float scale, Transform preview, Transform ignore)
        {
            Vector3 size = footprint.size * scale;
            size.x = Mathf.Max(0.08f, size.x - 0.04f);
            size.z = Mathf.Max(0.08f, size.z - 0.04f);
            size.y = Mathf.Max(0.1f, size.y - 0.08f);
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 center = position + rotation * (footprint.center * scale);
            center.y = position.y + footprint.center.y * scale + 0.04f;
            int count = Physics.OverlapBoxNonAlloc(center, size * 0.5f, Overlaps, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = Overlaps[i];
                if (collider == null || collider.isTrigger)
                    continue;
                Transform transform = collider.transform;
                if (preview != null && (transform == preview || transform.IsChildOf(preview)))
                    continue;
                if (ignore != null && (transform == ignore || transform.IsChildOf(ignore)))
                    continue;
                if (transform.GetComponentInParent<PlacementPreviewMarker>() != null)
                    continue;
                if (PlacementGround.IsWalkSurface(collider))
                    continue;
                if (transform.GetComponentInParent<FirstPersonWalker>() != null)
                    return "Too close to you.";
                if (transform.GetComponentInParent<PlacedAssetMarker>() != null)
                    return "Overlaps another placed object.";
                if (transform.GetComponentInParent<BuildingFeature>() != null || transform.GetComponentInParent<BuildingFoundation>() != null)
                    return "Overlaps a building.";
                string name = collider.name;
                Transform parent = collider.transform.parent;
                if ((name != null && name.IndexOf("Boundary", System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (parent != null && parent.name.IndexOf("Boundary", System.StringComparison.OrdinalIgnoreCase) >= 0))
                    return "Overlaps the boundary.";
                return "Blocked by " + name + ".";
            }

            return null;
        }
    }
}
