using System;
using System.Collections.Generic;
using UnityEngine;

namespace NazarethPlusCity
{
    public enum FootprintRelation
    {
        Inside,
        Outside,
        Crosses,
        InHole
    }

    [Serializable]
    public class BoundaryLoop
    {
        public bool hole;
        public Vector2[] points;
    }

    /// <summary>
    /// Saved projected city border. Play mode uses this asset data, not the GeoJSON file.
    /// Points are Unity meters: x = east, y = north (world Z).
    /// </summary>
    public class CityBoundaryMap : MonoBehaviour
    {
        public List<BoundaryLoop> loops = new List<BoundaryLoop>();
        public float safetyMargin = 0.08f;
        public float referenceCapsuleRadius = 0.3f;

        PlayableRegion _region;

        public PlayableRegion Region
        {
            get
            {
                if (_region == null)
                    RebuildCache();
                return _region;
            }
        }

        void OnEnable()
        {
            RebuildCache();
        }

        public void SetLoops(List<BoundaryLoop> source, float margin, float capsuleRadius)
        {
            loops = source ?? new List<BoundaryLoop>();
            safetyMargin = margin;
            referenceCapsuleRadius = capsuleRadius;
            RebuildCache();
        }

        public void RebuildCache()
        {
            _region = PlayableRegion.FromLoops(loops);
        }

        public bool Contains(Vector2 eastNorth)
        {
            return Region.Contains(eastNorth);
        }

        public bool IsCapsuleInside(Vector2 eastNorth, float capsuleRadius, float margin)
        {
            return Region.IsCapsuleInside(eastNorth, capsuleRadius, margin);
        }
    }

    public sealed class PlayableRegion
    {
        public readonly List<Vector2[]> Outers = new List<Vector2[]>();
        public readonly List<Vector2[]> Holes = new List<Vector2[]>();

        public static PlayableRegion FromLoops(List<BoundaryLoop> loops)
        {
            var region = new PlayableRegion();
            if (loops == null)
                return region;
            for (int i = 0; i < loops.Count; i++)
            {
                var loop = loops[i];
                if (loop?.points == null || loop.points.Length < 3)
                    continue;
                if (loop.hole)
                    region.Holes.Add(loop.points);
                else
                    region.Outers.Add(loop.points);
            }

            return region;
        }

        public bool Contains(Vector2 point)
        {
            bool insideOuter = false;
            for (int i = 0; i < Outers.Count; i++)
            {
                if (PointInRing(point, Outers[i]))
                {
                    insideOuter = true;
                    break;
                }
            }

            if (!insideOuter)
                return false;
            for (int i = 0; i < Holes.Count; i++)
            {
                if (PointInRing(point, Holes[i]))
                    return false;
            }

            return true;
        }

        public bool InsideHole(Vector2 point)
        {
            for (int i = 0; i < Holes.Count; i++)
            {
                if (PointInRing(point, Holes[i]))
                    return true;
            }

            return false;
        }

        public float DistanceToBoundary(Vector2 point)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < Outers.Count; i++)
                best = Mathf.Min(best, DistanceToRing(point, Outers[i]));
            for (int i = 0; i < Holes.Count; i++)
                best = Mathf.Min(best, DistanceToRing(point, Holes[i]));
            return best;
        }

        public bool IsCapsuleInside(Vector2 center, float capsuleRadius, float safetyMargin)
        {
            if (!Contains(center))
                return false;
            float required = Mathf.Max(0.02f, capsuleRadius - Mathf.Max(0f, safetyMargin));
            return DistanceToBoundary(center) >= required;
        }

        public FootprintRelation Classify(IList<Vector2> points, bool closed, float edgeTolerance)
        {
            if (points == null || points.Count == 0)
                return FootprintRelation.Outside;

            int playable = 0;
            int holes = 0;
            int outside = 0;
            float reach = 0f;
            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 point = points[i];
                centroid += point;
                if (Contains(point) || (!InsideHole(point) && DistanceToBoundary(point) <= edgeTolerance && InsideExpanded(point, edgeTolerance)))
                    playable++;
                else if (InsideHole(point))
                    holes++;
                else
                    outside++;
            }

            centroid /= points.Count;
            for (int i = 0; i < points.Count; i++)
                reach = Mathf.Max(reach, Vector2.Distance(centroid, points[i]));

            bool crosses = false;
            if (!(playable == points.Count && DistanceToBoundary(centroid) > reach + edgeTolerance))
                crosses = EdgesCross(points, closed);

            if (crosses)
                return FootprintRelation.Crosses;
            if (playable == points.Count)
                return FootprintRelation.Inside;
            if (holes == points.Count)
                return FootprintRelation.InHole;
            if (outside == points.Count)
                return FootprintRelation.Outside;
            return FootprintRelation.Crosses;
        }

        bool InsideExpanded(Vector2 point, float tolerance)
        {
            for (int i = 0; i < Outers.Count; i++)
            {
                if (PointInRing(point, Outers[i]) || DistanceToRing(point, Outers[i]) <= tolerance)
                    return !InsideHole(point);
            }

            return false;
        }

        bool EdgesCross(IList<Vector2> points, bool closed)
        {
            int count = points.Count;
            int last = closed ? count : count - 1;
            for (int i = 0; i < last; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % count];
                if (SegmentHitsRings(a, b, Outers) || SegmentHitsRings(a, b, Holes))
                    return true;
            }

            return false;
        }

        static bool SegmentHitsRings(Vector2 a, Vector2 b, List<Vector2[]> rings)
        {
            for (int r = 0; r < rings.Count; r++)
            {
                var ring = rings[r];
                for (int i = 0; i < ring.Length; i++)
                {
                    Vector2 c = ring[i];
                    Vector2 d = ring[(i + 1) % ring.Length];
                    if (SegmentsCross(a, b, c, d))
                        return true;
                }
            }

            return false;
        }

        public static bool PointInRing(Vector2 point, Vector2[] ring)
        {
            bool inside = false;
            int j = ring.Length - 1;
            for (int i = 0; i < ring.Length; j = i++)
            {
                Vector2 a = ring[j];
                Vector2 b = ring[i];
                if (((a.y > point.y) != (b.y > point.y)) &&
                    (point.x < (b.x - a.x) * (point.y - a.y) / ((b.y - a.y) + 1e-12f) + a.x))
                    inside = !inside;
            }

            return inside;
        }

        public static float DistanceToRing(Vector2 point, Vector2[] ring)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < ring.Length; i++)
                best = Mathf.Min(best, DistanceToSegment(point, ring[i], ring[(i + 1) % ring.Length]));
            return best;
        }

        public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared < 1e-8f)
                return Vector2.Distance(point, a);
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
            return Vector2.Distance(point, a + ab * t);
        }

        public static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross(b - a, c - a);
            float d2 = Cross(b - a, d - a);
            float d3 = Cross(d - c, a - c);
            float d4 = Cross(d - c, b - c);
            const float eps = 0.0001f;
            return ((d1 > eps && d2 < -eps) || (d1 < -eps && d2 > eps))
                   && ((d3 > eps && d4 < -eps) || (d3 < -eps && d4 > eps));
        }

        public static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        public static float Area(IList<Vector2> ring)
        {
            double sum = 0.0;
            for (int i = 0; i < ring.Count; i++)
            {
                Vector2 a = ring[i];
                Vector2 b = ring[(i + 1) % ring.Count];
                sum += (double)a.x * b.y - (double)b.x * a.y;
            }

            return (float)(sum * 0.5);
        }
    }

    public class BoundaryOutline : MonoBehaviour
    {
        public bool visible;
        public Color color = new Color(0.15f, 0.85f, 1f, 1f);

        public void ApplyVisibility()
        {
            var lines = GetComponentsInChildren<LineRenderer>(true);
            for (int i = 0; i < lines.Length; i++)
                lines[i].enabled = visible;
        }

        void OnEnable()
        {
            ApplyVisibility();
        }
    }
}
