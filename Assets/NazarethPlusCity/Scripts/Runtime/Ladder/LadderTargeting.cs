using System.Collections.Generic;
using UnityEngine;

namespace NazarethPlusCity
{
    public static class LadderTargeting
    {
        static readonly RaycastHit[] Hits = new RaycastHit[48];
        static readonly Collider[] Overlaps = new Collider[32];
        static readonly List<LadderStructure> Structures = new List<LadderStructure>();

        public static LadderPlacement Evaluate(Transform eye, Vector3 playerFeet, LadderSystemSettings settings, IReadOnlyList<LadderStructure> known)
        {
            if (eye == null || settings == null)
                return LadderPlacement.Reject("Ladder targeting is not ready.");

            Vector3 origin = eye.position;
            Vector3 direction = eye.forward;
            if (direction.sqrMagnitude < 0.0001f)
                return LadderPlacement.Reject("Look at a building wall to place a ladder.");

            direction.Normalize();
            float pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (Mathf.Abs(pitch) > settings.maxAimPitchDegrees)
                return LadderPlacement.Reject("Aim more level at the wall base.");

            int count = Physics.RaycastNonAlloc(origin, direction, Hits, settings.targetRangeMeters, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            RaycastHit? best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var building = Hits[i].collider != null ? Hits[i].collider.GetComponentInParent<BuildingFeature>() : null;
                if (building == null)
                    continue;
                bool modeled = building.GetComponentInChildren<BuildingModelVisual>(true) != null;
                if (modeled && !BuildingModelVisual.BelongsToModel(Hits[i].collider.transform))
                    continue;
                if (Hits[i].normal.y > 0.45f)
                    continue;
                Vector3 flatNormal = Hits[i].normal;
                flatNormal.y = 0f;
                if (flatNormal.sqrMagnitude < 0.01f)
                    continue;
                if (Hits[i].distance < bestDist)
                {
                    bestDist = Hits[i].distance;
                    best = Hits[i];
                }
            }

            if (best == null)
                return LadderPlacement.Reject("Face a nearby building wall.");

            RaycastHit hit = best.Value;
            var feature = hit.collider.GetComponentInParent<BuildingFeature>();
            Vector3 wallNormal = hit.normal;
            wallNormal.y = 0f;
            wallNormal.Normalize();

            Vector3 wallPoint = hit.point;
            Vector3 baseProbe = wallPoint + wallNormal * settings.wallOffsetMeters;
            if (!TerrainGround.TrySurface(baseProbe.x, baseProbe.z, out float groundY))
                groundY = playerFeet.y;

            float roofY = RoofWorldY(feature);
            Vector3 basePosition = new Vector3(baseProbe.x, groundY, baseProbe.z);
            Vector3 landing = wallPoint - wallNormal * settings.landingInsetMeters;
            if (TryRoofSurface(feature, landing, roofY, groundY, out float surfaceY, out Vector3 surface))
            {
                roofY = surfaceY;
                landing = surface;
            }
            else
                landing.y = roofY;

            float climbHeight = roofY - groundY;
            if (climbHeight < 0.75f)
                return LadderPlacement.Reject("That wall is too short to climb.");
            if (!LandingClear(feature, landing, settings, out string landingReason))
                return LadderPlacement.Reject(landingReason);

            if (!ApproachClear(basePosition, wallNormal, settings, playerFeet, out string approachReason))
                return LadderPlacement.Reject(approachReason);

            int required = LadderMath.RequiredSections(climbHeight, settings.sectionHeightMeters);
            string slot = LadderMath.SlotKey(feature, basePosition, wallNormal, settings.duplicateCellMeters);
            LadderStructure existing = FindExisting(slot, known);
            if (existing != null && existing.IsComplete)
                return LadderPlacement.Reject("A ladder already reaches this roof.");

            float yaw = Quaternion.LookRotation(-wallNormal, Vector3.up).eulerAngles.y;
            return new LadderPlacement
            {
                Valid = true,
                Reason = null,
                Building = feature,
                BasePosition = basePosition,
                WallNormal = wallNormal,
                Yaw = yaw,
                ClimbHeight = climbHeight,
                RoofY = roofY,
                LandingPoint = landing,
                RequiredSections = required,
                SlotKey = slot,
                Existing = existing
            };
        }

        public static float RoofWorldY(BuildingFeature building)
        {
            if (building == null)
                return 0f;
            bool hasModel = building.GetComponentInChildren<BuildingModelVisual>(true) != null;
            var renderers = building.GetComponentsInChildren<Renderer>(true);
            float maxY = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                if (renderer.GetComponentInParent<BuildingFoundation>(true) != null)
                    continue;
                bool modelRenderer = BuildingModelVisual.BelongsToModel(renderer.transform);
                if (hasModel && !modelRenderer)
                    continue;
                if (!hasModel && !renderer.enabled)
                    continue;
                float top = renderer.bounds.max.y;
                if (top > maxY)
                {
                    maxY = top;
                    found = true;
                }
            }

            if (found)
                return maxY;
            return building.transform.position.y + building.meshBaseLocalY + building.ActiveHeight;
        }

        static bool TryRoofSurface(BuildingFeature building, Vector3 landing, float roofHint, float groundY, out float surfaceY, out Vector3 surface)
        {
            surfaceY = roofHint;
            surface = landing;
            surface.y = roofHint;
            Vector3 origin = new Vector3(landing.x, roofHint + 3f, landing.z);
            float distance = Mathf.Max(4f, roofHint - groundY + 6f);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider == null || Hits[i].collider.GetComponentInParent<BuildingFeature>() != building)
                    continue;
                if (Hits[i].normal.y < 0.45f)
                    continue;
                if (Hits[i].point.y < groundY + 0.75f)
                    continue;
                if (Hits[i].point.y <= best)
                    continue;
                best = Hits[i].point.y;
                surface = Hits[i].point;
                found = true;
            }

            if (!found)
                return false;
            surfaceY = best;
            surface.y = best;
            return true;
        }

        static bool LandingClear(BuildingFeature building, Vector3 landing, LadderSystemSettings settings, out string reason)
        {
            reason = null;
            if (!PointOverBuilding(building, landing))
            {
                reason = "Roof landing is outside the building footprint.";
                return false;
            }

            Vector3 probe = landing + Vector3.up * (settings.landingProbeRadius + 0.05f);
            int hits = Physics.OverlapSphereNonAlloc(probe, settings.landingProbeRadius, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                Collider collider = Overlaps[i];
                if (collider == null)
                    continue;
                if (collider.GetComponentInParent<BuildingFeature>() == building)
                    continue;
                if (collider.GetComponentInParent<LadderStructure>() != null)
                    continue;
                if (collider.GetComponentInParent<PlayerRoot>() != null || collider.GetComponentInParent<FirstPersonWalker>() != null)
                    continue;
                if (collider.isTrigger)
                    continue;
                reason = "Roof landing is obstructed.";
                return false;
            }

            return true;
        }

        static bool PointOverBuilding(BuildingFeature building, Vector3 point)
        {
            if (building == null || building.parts == null || building.parts.Count == 0)
                return true;
            Vector2 local = new Vector2(point.x - building.transform.position.x, point.z - building.transform.position.z);
            for (int p = 0; p < building.parts.Count; p++)
            {
                FootprintPart part = building.parts[p];
                if (part == null || part.rings == null || part.rings.Count == 0)
                    continue;
                LocalRing outer = part.rings[0];
                if (outer == null || outer.points == null || outer.points.Count < 3)
                    continue;
                if (!PointInRing(local, outer.points))
                    continue;
                bool inHole = false;
                for (int r = 1; r < part.rings.Count; r++)
                {
                    LocalRing hole = part.rings[r];
                    if (hole != null && hole.points != null && PointInRing(local, hole.points))
                    {
                        inHole = true;
                        break;
                    }
                }

                if (!inHole)
                    return true;
            }

            return false;
        }

        static bool PointInRing(Vector2 point, List<Vector2> ring)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                Vector2 a = ring[i];
                Vector2 b = ring[j];
                bool intersect = ((a.y > point.y) != (b.y > point.y)) &&
                                 (point.x < (b.x - a.x) * (point.y - a.y) / ((b.y - a.y) + Mathf.Epsilon) + a.x);
                if (intersect)
                    inside = !inside;
            }

            return inside;
        }

        static bool ApproachClear(Vector3 basePosition, Vector3 wallNormal, LadderSystemSettings settings, Vector3 playerFeet, out string reason)
        {
            reason = null;
            Vector3 approach = basePosition - wallNormal * settings.approachClearanceMeters;
            if (TerrainGround.TrySurface(approach.x, approach.z, out float y))
                approach.y = y;
            else
                approach.y = basePosition.y;

            Vector3 from = approach + Vector3.up * 0.9f;
            Vector3 to = basePosition - wallNormal * 0.35f + Vector3.up * 0.9f;
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist > 0.05f && Physics.SphereCast(from, 0.22f, delta.normalized, out RaycastHit block, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (block.collider.GetComponentInParent<BuildingFeature>() == null &&
                    block.collider.GetComponentInParent<FirstPersonWalker>() == null)
                {
                    reason = "No clear approach to the ladder base.";
                    return false;
                }
            }

            Vector3 climbProbe = basePosition - wallNormal * settings.climbStandOffMeters + Vector3.up * 1.0f;
            if (Physics.CheckSphere(climbProbe, 0.18f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                int overlaps = Physics.OverlapSphereNonAlloc(climbProbe, 0.18f, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < overlaps; i++)
                {
                    if (Overlaps[i] == null)
                        continue;
                    if (Overlaps[i].GetComponentInParent<BuildingFeature>() != null)
                        continue;
                    if (Overlaps[i].GetComponentInParent<FirstPersonWalker>() != null)
                        continue;
                    if (Overlaps[i].GetComponentInParent<LadderStructure>() != null)
                        continue;
                    reason = "Climb path is blocked.";
                    return false;
                }
            }

            float playerDist = Vector2.Distance(new Vector2(playerFeet.x, playerFeet.z), new Vector2(basePosition.x, basePosition.z));
            if (playerDist > settings.targetRangeMeters + 1.5f)
            {
                reason = "Move closer to the wall.";
                return false;
            }

            return true;
        }

        static LadderStructure FindExisting(string slot, IReadOnlyList<LadderStructure> known)
        {
            if (known != null)
            {
                for (int i = 0; i < known.Count; i++)
                {
                    if (known[i] != null && known[i].slotKey == slot)
                        return known[i];
                }
            }

            Structures.Clear();
            Structures.AddRange(Object.FindObjectsByType<LadderStructure>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            for (int i = 0; i < Structures.Count; i++)
            {
                if (Structures[i] != null && Structures[i].slotKey == slot)
                    return Structures[i];
            }

            return null;
        }
    }
}
