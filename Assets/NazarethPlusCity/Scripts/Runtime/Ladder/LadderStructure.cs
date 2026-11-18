using System.Collections.Generic;
using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// One ladder stack against a building wall. Sections are added from the base upward.
    /// </summary>
    [DisallowMultipleComponent]
    public class LadderStructure : MonoBehaviour
    {
        public BuildingFeature building;
        public string slotKey;
        public Vector3 wallNormal = Vector3.forward;
        public float climbHeight;
        public float roofY;
        public Vector3 landingPoint;
        public int requiredSections = 1;
        public float sectionHeight = 0.92f;
        public GameObject sectionPrefab;

        readonly List<LadderSection> _sections = new List<LadderSection>();

        public int BuiltSections => _sections.Count;
        public bool IsComplete => BuiltSections >= requiredSections && requiredSections > 0;
        public bool HasSections => _sections.Count > 0;
        public IReadOnlyList<LadderSection> Sections => _sections;

        public float BuiltClimbHeight
        {
            get
            {
                if (_sections.Count == 0)
                    return 0f;
                float top = _sections[_sections.Count - 1].TopWorld.y;
                return Mathf.Max(0f, top - transform.position.y);
            }
        }

        public Vector3 BasePosition => transform.position;

        public Vector3 ClimbFacing
        {
            get
            {
                Vector3 facing = -wallNormal;
                facing.y = 0f;
                if (facing.sqrMagnitude < 0.0001f)
                    facing = transform.forward;
                return facing.normalized;
            }
        }

        public void Configure(LadderPlacement placement, LadderSystemSettings settings, GameObject prefab)
        {
            building = placement.Building;
            slotKey = placement.SlotKey;
            wallNormal = placement.WallNormal.normalized;
            climbHeight = placement.ClimbHeight;
            roofY = placement.RoofY;
            landingPoint = placement.LandingPoint;
            requiredSections = Mathf.Max(1, placement.RequiredSections);
            sectionHeight = settings != null ? settings.sectionHeightMeters : 2.4203f;
            sectionPrefab = prefab;
            transform.SetPositionAndRotation(placement.BasePosition, Quaternion.Euler(0f, placement.Yaw, 0f));
            name = "Ladder_" + (building != null ? building.name : "Wall");
        }

        public bool TryAddSection(LadderSystemSettings settings, out string reason)
        {
            reason = null;
            if (IsComplete)
            {
                reason = "This ladder already reaches the roof.";
                return false;
            }

            if (sectionPrefab == null)
            {
                reason = "Ladder section prefab is missing.";
                return false;
            }

            float denseHeight = settings != null ? settings.sectionHeightMeters : sectionHeight;
            float meshHeight = settings != null && settings.sourceMeshHeightMeters > 0.1f
                ? settings.sourceMeshHeightMeters
                : denseHeight / Mathf.Max(0.0001f, settings != null ? settings.denseRungScale : LadderIds.DenseRungScale);
            int index = BuiltSections;
            float used = LadderMath.SectionUseHeight(index, requiredSections, climbHeight, denseHeight);
            var instance = Instantiate(sectionPrefab, transform);
            instance.name = "Section_" + (index + 1);
            instance.transform.localPosition = new Vector3(0f, BuiltClimbHeight, 0f);
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            var section = instance.GetComponent<LadderSection>();
            if (section == null)
                section = instance.AddComponent<LadderSection>();
            // Scale from the original mesh height so every section keeps dense rung spacing.
            section.ApplyHeightScale(meshHeight, used);
            EnsureSectionTrigger(section, settings);
            _sections.Add(section);
            return true;
        }

        public bool RemoveLastSection()
        {
            if (_sections.Count == 0)
                return false;
            int last = _sections.Count - 1;
            LadderSection section = _sections[last];
            _sections.RemoveAt(last);
            if (section != null)
                Destroy(section.gameObject);
            return true;
        }

        static void EnsureSectionTrigger(LadderSection section, LadderSystemSettings settings)
        {
            var box = section.GetComponent<BoxCollider>();
            if (box == null)
                box = section.gameObject.AddComponent<BoxCollider>();
            float height = section.AppliedHeight;
            float width = settings != null ? settings.sectionWidthMeters : 0.48f;
            float depth = settings != null ? Mathf.Max(0.2f, settings.sectionThicknessMeters + 0.12f) : 0.25f;
            box.isTrigger = true;
            box.center = new Vector3(0f, height * 0.5f, -0.02f);
            box.size = new Vector3(width, height, depth);
        }

        public Vector3 ClimbPoint(float heightAlong)
        {
            float standoff = 0.42f;
            Vector3 origin = transform.position - wallNormal.normalized * standoff;
            origin.y = transform.position.y + Mathf.Clamp(heightAlong, 0f, Mathf.Max(0.05f, BuiltClimbHeight));
            return origin;
        }

        public bool TryMountHeight(Vector3 playerFeet, bool fromRoof, float mountDistance, out float heightAlong)
        {
            heightAlong = 0f;
            if (!HasSections)
                return false;
            Vector3 flatPlayer = playerFeet;
            flatPlayer.y = 0f;
            Vector3 flatBase = transform.position;
            flatBase.y = 0f;
            Vector3 approach = flatBase - wallNormal.normalized * 0.42f;
            approach.y = 0f;
            if ((flatPlayer - approach).sqrMagnitude > mountDistance * mountDistance)
                return false;

            if (fromRoof)
            {
                if (!IsComplete)
                    return false;
                if (Mathf.Abs(playerFeet.y - roofY) > 1.25f)
                    return false;
                heightAlong = BuiltClimbHeight;
                return true;
            }

            // Allow auto-mount from the ground or while standing beside mid-ladder.
            float relativeY = playerFeet.y - transform.position.y;
            if (relativeY > BuiltClimbHeight + 0.75f)
                return false;
            heightAlong = Mathf.Clamp(relativeY, 0.05f, Mathf.Max(0.05f, BuiltClimbHeight));
            return true;
        }

        public Vector3 GroundDismountPoint(float push)
        {
            Vector3 point = transform.position - wallNormal.normalized * Mathf.Max(0.35f, push);
            if (TerrainGround.TrySurface(point.x, point.z, out float y))
                point.y = y;
            else
                point.y = transform.position.y;
            return point;
        }
    }
}
