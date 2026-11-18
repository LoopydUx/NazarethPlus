using System;
using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// Editable defaults for a full-city rebuild. Hand-tuned values on this asset are kept.
    /// Unity world units are meters. +X east, +Y up, +Z north.
    /// </summary>
    public class CityGenerationSettings : ScriptableObject
    {
        [Header("Terrain")]
        [Tooltip("Source elevation, in metres, that maps to local ground Y = 0. localGroundY = (sourceElevation - elevationOrigin) * worldUnitsPerMetre.")]
        public float elevationOriginMeters = 131.2f;

        [Tooltip("Vertical scale. Horizontal placement stays on the saved geographic projection.")]
        public float worldUnitsPerMetre = 1f;

        [Tooltip("Building level base sits this far above the highest terrain under its occupied footprint.")]
        public float buildingClearanceMeters = 0.03f;

        [Tooltip("Foundations deeper than this are reported for inspection and are still generated.")]
        public float tallFoundationMeters = 8f;

        [Tooltip("Road and wall edges are split so draped vertices stay within this distance.")]
        public float surfaceDrapeSpacingMeters = 8f;

        [Tooltip("Foundation perimeter samples, in metres.")]
        public float foundationSegmentMeters = 4f;

        public float foundationThicknessMeters = 0.2f;

        [Tooltip("How far a draped point may take a height from the nearest terrain triangle when it falls in a gap.")]
        public float terrainEdgeSearchMeters = 24f;

        [Tooltip("Walker recovers when its feet drop this far below the terrain under it.")]
        public float fallDropMeters = 8f;

        [Tooltip("Extra space between the capsule feet and the street or terrain at spawn.")]
        public float spawnClearanceMeters = 0.08f;

        [Header("Heights")]
        [Tooltip("Meters per floor when a building has a floor count but no explicit height.")]
        public float floorHeightMeters = 3f;

        [Tooltip("Used only when a building has neither a usable height nor a usable floor count.")]
        public int fallbackFloors = 3;

        [Header("Streets")]
        [Tooltip("Meters above Y = 0. Keeps asphalt from flickering against the ground.")]
        public float roadSurfaceOffset = 0.035f;

        [Tooltip("Extra lift, in millimeters, spread per street so overlapping intersections do not flicker.")]
        public float roadOverlapLiftStep = 0.0012f;

        public int roadOverlapLiftSteps = 18;
        public float laneWidthMeters = 3.4f;
        public float ordinaryRoadWidthMeters = 6f;
        public float tertiaryWidthMeters = 7f;
        public float secondaryWidthMeters = 8f;
        public float primaryWidthMeters = 10f;
        public float trunkWidthMeters = 12f;
        public float serviceWidthMeters = 4f;
        public float footpathWidthMeters = 2.2f;

        [Header("Boundary")]
        public float boundaryWallHeightMeters = 6f;
        public float boundaryWallThicknessMeters = 0.45f;
        public float boundaryCornerExtensionMeters = 0.35f;
        public float boundaryWallEmbedMeters = 1f;

        [Tooltip("Shrinks the capsule test slightly so a legal contact with the border wall is not treated as an escape.")]
        public float containmentSafetyMargin = 0.08f;

        public float fallResetY = -1.5f;
        public bool showBoundaryOutline;

        [Header("Walker")]
        public float walkSpeed = 4.2f;
        public float sprintSpeed = 7.5f;
        public float jumpHeight = 1.2f;
        public float gravity = -20f;
        public float lookSensitivity = 0.07f;
        public float eyeHeight = 1.64f;
        public float playerRadius = 0.3f;
        public float playerHeight = 1.8f;

        [Header("Appearance")]
        public float stoneUvMeters = 2.5f;
        public float roofUvMeters = 3.5f;
        public float asphaltUvMeters = 4f;
        public float groundUvMeters = 8f;

        public float FallbackHeightMeters => Mathf.Max(0.5f, fallbackFloors * Mathf.Max(0.5f, floorHeightMeters));
    }

    [Serializable]
    public struct HeightDecision
    {
        public float meters;
        public string resolution;
        public string detail;
        public string sourceHeightRaw;
        public string sourceFloorRaw;
    }

    public static class MeasureParser
    {
        public static bool TryParseLength(string raw, out float meters, out string note)
        {
            meters = 0f;
            note = null;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string text = raw.Trim().ToLowerInvariant().Replace("metres", "m").Replace("meters", "m").Replace("meter", "m");
            int dash = text.IndexOf('-');
            if (dash > 0)
            {
                string left = text.Substring(0, dash);
                string right = text.Substring(dash + 1);
                if (TrySingle(left, out float a, out _) && TrySingle(right, out float b, out string unitNote))
                {
                    meters = (a + b) * 0.5f;
                    note = "range midpoint of '" + raw.Trim() + "'" + unitNote;
                    return meters > 0f;
                }
            }

            if (TrySingle(text, out meters, out note))
                return meters > 0f;
            return false;
        }

        static bool TrySingle(string text, out float meters, out string note)
        {
            meters = 0f;
            note = "";
            text = text.Trim();
            bool feet = text.Contains("ft") || text.Contains("feet") || text.Contains("'");
            var chars = new char[text.Length];
            int n = 0;
            bool seenDigit = false;
            bool seenDot = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '0' && c <= '9')
                {
                    chars[n++] = c;
                    seenDigit = true;
                }
                else if (c == '.' && !seenDot)
                {
                    chars[n++] = c;
                    seenDot = true;
                }
                else if (seenDigit)
                {
                    break;
                }
            }

            if (!seenDigit)
                return false;
            if (!float.TryParse(new string(chars, 0, n), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
                return false;
            if (feet)
            {
                meters = value * 0.3048f;
                note = " (converted from feet)";
            }
            else
            {
                meters = value;
                note = " (meters)";
            }

            return meters > 0f;
        }

        public static HeightDecision ResolveHeight(string heightRaw, string levelsRaw, string floorsRaw, CityGenerationSettings settings)
        {
            var decision = new HeightDecision
            {
                sourceHeightRaw = heightRaw ?? "",
                sourceFloorRaw = !string.IsNullOrWhiteSpace(levelsRaw) ? levelsRaw : (floorsRaw ?? ""),
                meters = settings.FallbackHeightMeters,
                resolution = "Fallback",
                detail = ""
            };

            if (TryParseLength(heightRaw, out float height, out string heightNote) && height >= 0.25f && height <= 500f)
            {
                decision.meters = height;
                decision.resolution = "ExplicitHeight";
                decision.detail = "source height " + heightRaw.Trim() + heightNote;
                return decision;
            }

            if (!string.IsNullOrWhiteSpace(heightRaw))
                decision.detail = "height '" + heightRaw.Trim() + "' was not usable; ";

            if (TryFloors(levelsRaw, out float levels))
            {
                decision.meters = levels * settings.floorHeightMeters;
                decision.resolution = "FloorCount";
                decision.sourceFloorRaw = levelsRaw;
                decision.detail += "building:levels " + levelsRaw.Trim() + " × " + settings.floorHeightMeters.ToString("0.###") + " m";
                return decision;
            }

            if (TryFloors(floorsRaw, out float floors))
            {
                decision.meters = floors * settings.floorHeightMeters;
                decision.resolution = "FloorCount";
                decision.sourceFloorRaw = floorsRaw;
                decision.detail += "number_of_floors " + floorsRaw.Trim() + " × " + settings.floorHeightMeters.ToString("0.###") + " m";
                return decision;
            }

            decision.detail += "no usable height or floor count; fallback " + settings.fallbackFloors + " floors / " + settings.FallbackHeightMeters.ToString("0.###") + " m";
            return decision;
        }

        public static bool TryFloors(string raw, out float floors)
        {
            floors = 0f;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            if (!float.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out floors))
                return false;
            return floors > 0f && floors < 200f;
        }

        public static float ResolveStreetWidth(string widthRaw, string lanesRaw, string highway, string streetType, CityGenerationSettings settings, out string detail)
        {
            if (TryParseLength(widthRaw, out float width, out string note) && width >= 0.4f && width <= 80f)
            {
                detail = "source width " + widthRaw.Trim() + note;
                return width;
            }

            if (TryFloors(lanesRaw, out float lanes))
            {
                float fromLanes = Mathf.Max(2.2f, lanes * settings.laneWidthMeters);
                detail = "lanes " + lanesRaw.Trim() + " × " + settings.laneWidthMeters.ToString("0.##") + " m";
                return fromLanes;
            }

            string kind = string.IsNullOrWhiteSpace(streetType) ? highway : streetType;
            if (string.Equals(highway, "construction", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(streetType))
                kind = streetType;
            kind = (kind ?? "").Trim().ToLowerInvariant();
            float fallback = WidthForKind(kind, settings);
            string why = string.IsNullOrEmpty(kind) ? "no width, lanes, or highway type" : "highway/type '" + kind + "'";
            detail = why + "; fallback " + fallback.ToString("0.##") + " m";
            return fallback;
        }

        public static float WidthForKind(string kind, CityGenerationSettings settings)
        {
            switch (kind)
            {
                case "footway":
                case "path":
                case "steps":
                case "pedestrian":
                case "corridor":
                case "cycleway":
                case "bridleway":
                    return settings.footpathWidthMeters;
                case "service":
                case "track":
                    return settings.serviceWidthMeters;
                case "tertiary":
                case "tertiary_link":
                    return settings.tertiaryWidthMeters;
                case "secondary":
                case "secondary_link":
                    return settings.secondaryWidthMeters;
                case "primary":
                case "primary_link":
                    return settings.primaryWidthMeters;
                case "trunk":
                case "trunk_link":
                case "motorway":
                case "motorway_link":
                    return settings.trunkWidthMeters;
                default:
                    return settings.ordinaryRoadWidthMeters;
            }
        }
    }
}
