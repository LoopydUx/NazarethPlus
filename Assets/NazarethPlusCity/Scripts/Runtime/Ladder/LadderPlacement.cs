using UnityEngine;

namespace NazarethPlusCity
{
    public struct LadderPlacement
    {
        public bool Valid;
        public string Reason;
        public BuildingFeature Building;
        public Vector3 BasePosition;
        public Vector3 WallNormal;
        public float Yaw;
        public float ClimbHeight;
        public float RoofY;
        public Vector3 LandingPoint;
        public int RequiredSections;
        public string SlotKey;
        public LadderStructure Existing;

        public static LadderPlacement Reject(string reason)
        {
            return new LadderPlacement { Valid = false, Reason = reason };
        }
    }

    public static class LadderMath
    {
        public static int RequiredSections(float climbHeight, float sectionHeight)
        {
            float section = Mathf.Max(0.05f, sectionHeight);
            float climb = Mathf.Max(0f, climbHeight);
            if (climb <= 0.01f)
                return 0;
            return Mathf.Max(1, Mathf.CeilToInt(climb / section - 0.0001f));
        }

        public static float SectionUseHeight(int index, int required, float climbHeight, float sectionHeight)
        {
            float section = Mathf.Max(0.05f, sectionHeight);
            float climb = Mathf.Max(0f, climbHeight);
            float start = index * section;
            float remaining = climb - start;
            if (index >= required - 1)
                return Mathf.Clamp(remaining, 0.05f, section);
            return section;
        }

        public static int StartingWood(float averageClimbHeight, float sectionHeight, int woodPerSection, int starterLadderBudget)
        {
            int sections = RequiredSections(averageClimbHeight, sectionHeight);
            return Mathf.Max(0, starterLadderBudget) * sections * Mathf.Max(0, woodPerSection);
        }

        public static string SlotKey(BuildingFeature building, Vector3 basePosition, Vector3 wallNormal, float cell)
        {
            string id = building != null ? building.GetInstanceID().ToString() : "none";
            float size = Mathf.Max(0.25f, cell);
            int x = Mathf.RoundToInt(basePosition.x / size);
            int z = Mathf.RoundToInt(basePosition.z / size);
            int nx = Mathf.RoundToInt(wallNormal.x * 4f);
            int nz = Mathf.RoundToInt(wallNormal.z * 4f);
            return id + ":" + x + ":" + z + ":" + nx + ":" + nz;
        }
    }
}
