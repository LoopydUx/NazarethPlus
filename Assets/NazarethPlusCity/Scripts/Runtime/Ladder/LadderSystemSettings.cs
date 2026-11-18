using UnityEngine;

namespace NazarethPlusCity
{
    [CreateAssetMenu(fileName = "LadderSystemSettings", menuName = "NazarethPlusCity/Ladder System Settings")]
    public class LadderSystemSettings : ScriptableObject
    {
        [Header("Section")]
        [Tooltip("Usable climb height of one full dense ladder section.")]
        public float sectionHeightMeters = 0.92f;
        [Tooltip("Original mesh height before dense rung compression.")]
        public float sourceMeshHeightMeters = 2.4203f;
        [Tooltip("Y compression baked into the section mesh for close rung spacing.")]
        public float denseRungScale = 0.38f;
        public float sectionWidthMeters = 0.476f;
        public float sectionThicknessMeters = 0.095f;
        public float wallOffsetMeters = 0.18f;
        public float climbStandOffMeters = 0.42f;

        [Header("Targeting")]
        public float targetRangeMeters = 4.5f;
        public float maxAimPitchDegrees = 35f;
        public float approachClearanceMeters = 1.1f;
        public float landingInsetMeters = 0.55f;
        public float landingProbeRadius = 0.28f;
        public float duplicateCellMeters = 0.75f;

        [Header("Economy")]
        public int woodPerSection = 2;
        public int startingCoins = 25;
        public int starterLadderBudget = 5;

        [Header("Climb")]
        public float climbSpeed = 2.4f;
        public float mountDistanceMeters = 1.35f;
        public float roofMountDistanceMeters = 1.6f;
        public float dismountPushMeters = 0.55f;
    }
}
