using System;
using System.Collections.Generic;
using Clipper2Lib;
using UnityEngine;
using UnityEngine.Rendering;

namespace NazarethPlusCity
{
    public sealed class MeshDraft
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Primary = new List<int>();
        public readonly List<int> Secondary = new List<int>();
    }

    public static class CityGeometry
    {
        public const float MinimumFootprintArea = 0.08f;
        public const float RingSpacing = 0.02f;

        public static List<Vector2> ProjectRing(List<GeoLonLat> source, GeoProjection projection)
        {
            var ring = new List<Vector2>(source != null ? source.Count : 0);
            if (source == null)
                return ring;
            for (int i = 0; i < source.Count; i++)
            {
                Vector3 world = projection.Project(source[i].Longitude, source[i].Latitude);
                ring.Add(new Vector2(world.x, world.z));
            }

            return CleanRing(ring, RingSpacing);
        }

        public static List<Vector2> ProjectLine(List<GeoLonLat> source, GeoProjection projection)
        {
            var line = new List<Vector2>(source != null ? source.Count : 0);
            if (source == null)
                return line;
            for (int i = 0; i < source.Count; i++)
            {
                Vector3 world = projection.Project(source[i].Longitude, source[i].Latitude);
                line.Add(new Vector2(world.x, world.z));
            }

            return CleanOpen(line, 0.05f);
        }

        public static List<Vector2> CleanRing(List<Vector2> source, float minSpacing)
        {
            var ring = CleanOpen(source, minSpacing);
            if (ring.Count > 1 && Vector2.Distance(ring[0], ring[ring.Count - 1]) < Mathf.Max(0.001f, minSpacing))
                ring.RemoveAt(ring.Count - 1);
            return ring;
        }

        public static List<Vector2> CleanOpen(List<Vector2> source, float minSpacing)
        {
            var ring = new List<Vector2>();
            if (source == null)
                return ring;
            float spacing = Mathf.Max(0.001f, minSpacing);
            for (int i = 0; i < source.Count; i++)
            {
                Vector2 point = source[i];
                if (float.IsNaN(point.x) || float.IsNaN(point.y))
                    continue;
                if (ring.Count == 0 || Vector2.Distance(ring[ring.Count - 1], point) >= spacing)
                    ring.Add(point);
            }

            return ring;
        }

        public static bool PrepareBuilding(GeoFeature feature, GeoProjection projection, out List<FootprintPart> worldParts, out string note, out string error)
        {
            worldParts = new List<FootprintPart>();
            note = null;
            error = null;
            if (feature.Polygons == null || feature.Polygons.Count == 0)
            {
                error = "malformed geometry: no polygon rings";
                return false;
            }

            var repairNotes = new List<string>();
            for (int p = 0; p < feature.Polygons.Count; p++)
            {
                var shape = feature.Polygons[p];
                if (shape.Rings == null || shape.Rings.Count == 0)
                {
                    repairNotes.Add("polygon " + p + " has no rings");
                    continue;
                }

                var outer = ProjectRing(shape.Rings[0], projection);
                if (outer.Count < 3 || Mathf.Abs(PlayableRegion.Area(outer)) < MinimumFootprintArea)
                {
                    repairNotes.Add("polygon " + p + " degenerate outer ring (" + outer.Count + " vertices)");
                    continue;
                }

                var holes = new List<List<Vector2>>();
                for (int h = 1; h < shape.Rings.Count; h++)
                {
                    var hole = ProjectRing(shape.Rings[h], projection);
                    if (hole.Count >= 3 && Mathf.Abs(PlayableRegion.Area(hole)) >= MinimumFootprintArea)
                        holes.Add(hole);
                    else
                        repairNotes.Add("polygon " + p + " hole " + h + " degenerate");
                }

                Orient(outer, holes);
                if (TryMakeParts(outer, holes, out var parts, out string partNote, out string partError))
                {
                    if (!string.IsNullOrEmpty(partNote))
                        repairNotes.Add(partNote);
                    worldParts.AddRange(parts);
                }
                else
                {
                    repairNotes.Add(partError);
                }
            }

            if (worldParts.Count == 0)
            {
                error = "malformed geometry: " + string.Join("; ", repairNotes);
                return false;
            }

            if (repairNotes.Count > 0)
                note = string.Join("; ", repairNotes);
            return true;
        }

        public static bool TryMakeParts(List<Vector2> outer, List<List<Vector2>> holes, out List<FootprintPart> parts, out string note, out string error)
        {
            parts = new List<FootprintPart>();
            note = null;
            error = null;
            var triangles = new List<int>();
            if (TryTriangulate(outer, holes, triangles, out _))
            {
                parts.Add(MakePart(outer, holes));
                return true;
            }

            if (!RepairWithClipper(outer, holes, out parts, out string repairError))
            {
                error = "triangulation failed (" + repairError + ")";
                return false;
            }

            note = "repaired non-simple footprint with polygon union";
            return parts.Count > 0;
        }

        public static void ToLocal(List<FootprintPart> worldParts, out Vector2 origin)
        {
            origin = Centroid(worldParts[0].rings[0].points);
            for (int p = 0; p < worldParts.Count; p++)
            {
                var rings = worldParts[p].rings;
                for (int r = 0; r < rings.Count; r++)
                {
                    var points = rings[r].points;
                    for (int i = 0; i < points.Count; i++)
                        points[i] -= origin;
                }
            }
        }

        public static bool FillBuilding(Mesh mesh, FootprintPart part, float height, Vector3 worldOrigin, float wallUv, float roofUv, out string error)
        {
            error = null;
            if (part == null || part.rings == null || part.rings.Count == 0 || part.rings[0].points.Count < 3)
            {
                error = "empty footprint";
                return false;
            }

            var outer = part.rings[0].points;
            var holes = new List<List<Vector2>>();
            for (int i = 1; i < part.rings.Count; i++)
                holes.Add(part.rings[i].points);
            Orient(outer, holes);
            var triangles = new List<int>();
            if (!TryTriangulate(outer, holes, triangles, out error))
                return false;

            var draft = new MeshDraft();
            float perimeter = 0f;
            for (int r = 0; r < part.rings.Count; r++)
                AddWalls(draft, part.rings[r].points, height, wallUv, ref perimeter);

            var flat = new List<Vector2>(outer.Count + 16);
            flat.AddRange(outer);
            for (int h = 0; h < holes.Count; h++)
                flat.AddRange(holes[h]);
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                Vector2 a = flat[triangles[t]];
                Vector2 b = flat[triangles[t + 1]];
                Vector2 c = flat[triangles[t + 2]];
                Vector3 va = new Vector3(a.x, height, a.y);
                Vector3 vb = new Vector3(b.x, height, b.y);
                Vector3 vc = new Vector3(c.x, height, c.y);
                AddTriFacing(draft, va, vb, vc, Vector3.up,
                    WorldUv(va, worldOrigin, roofUv),
                    WorldUv(vb, worldOrigin, roofUv),
                    WorldUv(vc, worldOrigin, roofUv),
                    true);
            }

            Apply(mesh, draft, true);
            return true;
        }

        public static PathsD BoundaryPaths(PlayableRegion region)
        {
            var paths = new PathsD();
            if (region == null)
                return paths;
            for (int i = 0; i < region.Outers.Count; i++)
                paths.Add(OrientedPath(region.Outers[i], true));
            for (int i = 0; i < region.Holes.Count; i++)
                paths.Add(OrientedPath(region.Holes[i], false));
            return paths;
        }

        public static List<List<Vector2>> ClipRoad(List<Vector2> worldCenter, float width, PathsD boundary, out string note)
        {
            note = null;
            var results = new List<List<Vector2>>();
            var open = new PathD();
            Vector2 previous = new Vector2(float.NaN, float.NaN);
            for (int i = 0; i < worldCenter.Count; i++)
            {
                Vector2 point = worldCenter[i];
                if (!float.IsNaN(previous.x) && Vector2.Distance(previous, point) < 0.05f)
                    continue;
                open.Add(new PointD(point.x, point.y));
                previous = point;
            }

            if (open.Count < 2)
            {
                note = "malformed geometry: centerline has fewer than 2 points";
                return results;
            }

            try
            {
                var inflated = Clipper.InflatePaths(new PathsD { open }, width * 0.5, JoinType.Miter, EndType.Square, 2.0, 3, 0.25);
                var tree = new PolyTreeD();
                Clipper.BooleanOp(ClipType.Intersection, inflated, boundary, tree, FillRule.NonZero, 3);
                CollectPolygons(tree, results);
            }
            catch (Exception exception)
            {
                note = "street clip failed: " + exception.Message;
            }

            if (results.Count == 0 && string.IsNullOrEmpty(note))
                note = "no surface inside the city boundary after clipping";
            return results;
        }

        public static bool FillRoad(Mesh mesh, List<Vector2> worldPolygon, Vector3 worldOrigin, float surfaceY, float uvMeters, out string error)
        {
            return FillRoad(mesh, worldPolygon, worldOrigin, surfaceY, uvMeters, null, 0f, out error);
        }

        public static bool FillRoad(Mesh mesh, List<Vector2> worldPolygon, Vector3 worldOrigin, float surfaceY, float uvMeters, Func<Vector2, float> groundY, float maxEdge, out string error)
        {
            var local = new List<Vector2>(worldPolygon.Count);
            for (int i = 0; i < worldPolygon.Count; i++)
                local.Add(worldPolygon[i] - new Vector2(worldOrigin.x, worldOrigin.z));
            local = CleanRing(local, 0.01f);
            if (Mathf.Abs(PlayableRegion.Area(local)) < 0.05f)
            {
                error = "clipped road sliver";
                return false;
            }

            if (PlayableRegion.Area(local) < 0f)
                local.Reverse();
            var triangles = new List<int>();
            if (!TryTriangulate(local, null, triangles, out error))
                return false;
            if (groundY != null && maxEdge > 0.5f)
                SubdivideEdges(local, triangles, maxEdge);

            var draft = new MeshDraft();
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                Vector2 a = local[triangles[t]];
                Vector2 b = local[triangles[t + 1]];
                Vector2 c = local[triangles[t + 2]];
                Vector3 va = new Vector3(a.x, RoadY(a, worldOrigin, surfaceY, groundY), a.y);
                Vector3 vb = new Vector3(b.x, RoadY(b, worldOrigin, surfaceY, groundY), b.y);
                Vector3 vc = new Vector3(c.x, RoadY(c, worldOrigin, surfaceY, groundY), c.y);
                Vector3 normal = Vector3.Cross(vb - va, vc - va);
                if (normal.sqrMagnitude < 1e-8f)
                    normal = Vector3.up;
                AddTriFacing(draft, va, vb, vc, normal,
                    WorldUv(va, worldOrigin, uvMeters),
                    WorldUv(vb, worldOrigin, uvMeters),
                    WorldUv(vc, worldOrigin, uvMeters),
                    false);
            }

            Apply(mesh, draft, false);
            return true;
        }

        static float RoadY(Vector2 local, Vector3 worldOrigin, float surfaceY, Func<Vector2, float> groundY)
        {
            if (groundY == null)
                return surfaceY;
            float ground = groundY(new Vector2(local.x + worldOrigin.x, local.y + worldOrigin.z));
            return ground + surfaceY - worldOrigin.y;
        }

        static void SubdivideEdges(List<Vector2> vertices, List<int> triangles, float maxEdge)
        {
            for (int pass = 0; pass < 8 && vertices.Count < 24000; pass++)
            {
                var mids = new Dictionary<long, int>();
                var next = new List<int>(triangles.Count * 2);
                bool split = false;
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    int ab = EdgeMidpoint(vertices, mids, a, b, maxEdge);
                    int bc = EdgeMidpoint(vertices, mids, b, c, maxEdge);
                    int ca = EdgeMidpoint(vertices, mids, c, a, maxEdge);
                    if (ab >= 0 || bc >= 0 || ca >= 0)
                        split = true;
                    EmitSplit(next, a, b, c, ab, bc, ca);
                }

                triangles.Clear();
                triangles.AddRange(next);
                if (!split)
                    break;
            }
        }

        static int EdgeMidpoint(List<Vector2> vertices, Dictionary<long, int> mids, int a, int b, float maxEdge)
        {
            if (Vector2.Distance(vertices[a], vertices[b]) <= maxEdge)
                return -1;
            int lo = Math.Min(a, b);
            int hi = Math.Max(a, b);
            long key = ((long)lo << 32) | (uint)hi;
            if (mids.TryGetValue(key, out int index))
                return index;
            index = vertices.Count;
            vertices.Add((vertices[a] + vertices[b]) * 0.5f);
            mids.Add(key, index);
            return index;
        }

        static void EmitSplit(List<int> dst, int a, int b, int c, int ab, int bc, int ca)
        {
            bool sab = ab >= 0;
            bool sbc = bc >= 0;
            bool sca = ca >= 0;
            if (sab && sbc && sca)
            {
                AddTri(dst, a, ab, ca);
                AddTri(dst, ab, b, bc);
                AddTri(dst, ca, bc, c);
                AddTri(dst, ab, bc, ca);
            }
            else if (sab && sbc)
            {
                AddTri(dst, a, ab, c);
                AddTri(dst, ab, bc, c);
                AddTri(dst, ab, b, bc);
            }
            else if (sbc && sca)
            {
                AddTri(dst, a, b, bc);
                AddTri(dst, a, bc, ca);
                AddTri(dst, ca, bc, c);
            }
            else if (sca && sab)
            {
                AddTri(dst, a, ab, ca);
                AddTri(dst, ab, b, c);
                AddTri(dst, ab, c, ca);
            }
            else if (sab)
            {
                AddTri(dst, a, ab, c);
                AddTri(dst, ab, b, c);
            }
            else if (sbc)
            {
                AddTri(dst, a, b, bc);
                AddTri(dst, a, bc, c);
            }
            else if (sca)
            {
                AddTri(dst, a, b, ca);
                AddTri(dst, ca, b, c);
            }
            else
            {
                AddTri(dst, a, b, c);
            }
        }

        static void AddTri(List<int> dst, int a, int b, int c)
        {
            if (a == b || b == c || c == a)
                return;
            dst.Add(a);
            dst.Add(b);
            dst.Add(c);
        }

        public static bool FillGround(Mesh mesh, IList<Vector2> outer, List<List<Vector2>> holes, float uvMeters, out string error)
        {
            var outerCopy = new List<Vector2>(outer);
            var holeCopy = new List<List<Vector2>>();
            if (holes != null)
            {
                for (int i = 0; i < holes.Count; i++)
                    holeCopy.Add(new List<Vector2>(holes[i]));
            }

            Orient(outerCopy, holeCopy);
            var triangles = new List<int>();
            if (!TryTriangulate(outerCopy, holeCopy, triangles, out error))
                return false;

            var flat = new List<Vector2>(outerCopy);
            for (int i = 0; i < holeCopy.Count; i++)
                flat.AddRange(holeCopy[i]);
            var draft = new MeshDraft();
            var origin = Vector3.zero;
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                Vector2 a = flat[triangles[t]];
                Vector2 b = flat[triangles[t + 1]];
                Vector2 c = flat[triangles[t + 2]];
                Vector3 va = new Vector3(a.x, 0f, a.y);
                Vector3 vb = new Vector3(b.x, 0f, b.y);
                Vector3 vc = new Vector3(c.x, 0f, c.y);
                AddTriFacing(draft, va, vb, vc, Vector3.up,
                    WorldUv(va, origin, uvMeters),
                    WorldUv(vb, origin, uvMeters),
                    WorldUv(vc, origin, uvMeters),
                    false);
            }

            Apply(mesh, draft, false);
            return true;
        }

        public static void FillBorderWalls(Mesh mesh, PlayableRegion region, float height, float thickness, float extension, float embed)
        {
            FillBorderWalls(mesh, region, height, thickness, extension, embed, null, 0f);
        }

        public static void FillBorderWalls(Mesh mesh, PlayableRegion region, float height, float thickness, float extension, float embed, Func<Vector2, float> ground, float maxSegment)
        {
            var draft = new MeshDraft();
            AddRingWalls(draft, region, region.Outers, height, thickness, extension, embed, ground, maxSegment);
            AddRingWalls(draft, region, region.Holes, height, thickness, extension, embed, ground, maxSegment);
            Apply(mesh, draft, false);
        }

        public static void RebuildBuilding(BuildingFeature feature)
        {
            if (feature == null || feature.parts == null || feature.parts.Count == 0)
                return;
            float height = feature.ActiveHeight;
            if (feature.parts.Count == 1)
            {
                var mesh = EnsureMesh(feature.gameObject, feature.name);
                if (FillBuilding(mesh, feature.parts[0], height, feature.transform.position, feature.uvMeters, feature.uvMeters * 1.4f, out _))
                {
                    var filter = feature.GetComponent<MeshFilter>() ?? feature.gameObject.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;
                    var renderer = feature.GetComponent<MeshRenderer>() ?? feature.gameObject.AddComponent<MeshRenderer>();
                    if (feature.wallMaterial != null && feature.roofMaterial != null)
                        ConfigureRenderer(renderer, feature.wallMaterial, feature.roofMaterial);
                    AssignCollider(feature.gameObject, mesh);
                }
            }
            else
            {
                for (int i = 0; i < feature.parts.Count; i++)
                {
                    Transform existing = feature.transform.Find("Part_" + i);
                    var child = existing != null ? existing.gameObject : new GameObject("Part_" + i);
                    child.transform.SetParent(feature.transform, false);
                    child.name = "Part_" + i;
                    var mesh = EnsureMesh(child, feature.name + "_Part_" + i);
                    if (!FillBuilding(mesh, feature.parts[i], height, feature.transform.position, feature.uvMeters, feature.uvMeters * 1.4f, out _))
                        continue;
                    var filter = child.GetComponent<MeshFilter>() ?? child.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;
                    var renderer = child.GetComponent<MeshRenderer>() ?? child.AddComponent<MeshRenderer>();
                    if (feature.wallMaterial != null && feature.roofMaterial != null)
                        ConfigureRenderer(renderer, feature.wallMaterial, feature.roofMaterial);
                    AssignCollider(child, mesh);
                }
            }

            feature.appliedHeightMeters = height;
            if (feature.useTintOverride)
                feature.ApplyTintOverride();
#if UNITY_EDITOR
            if (feature.GetComponent<MeshFilter>()?.sharedMesh != null)
                UnityEditor.EditorUtility.SetDirty(feature.GetComponent<MeshFilter>().sharedMesh);
#endif
        }

        public static void RebuildStreet(StreetFeature feature)
        {
            if (feature == null)
                return;
            var boundary = UnityEngine.Object.FindAnyObjectByType<CityBoundaryMap>();
            if (boundary == null || feature.sourceParts == null)
                return;
            var paths = BoundaryPaths(boundary.Region);
            float width = feature.ActiveWidth;
            int written = 0;
            for (int i = feature.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(feature.transform.GetChild(i).gameObject);
            var rootFilter = feature.GetComponent<MeshFilter>();
            if (rootFilter != null)
                UnityEngine.Object.DestroyImmediate(rootFilter);
            var rootRenderer = feature.GetComponent<MeshRenderer>();
            if (rootRenderer != null)
                UnityEngine.Object.DestroyImmediate(rootRenderer);
            var rootCollider = feature.GetComponent<MeshCollider>();
            if (rootCollider != null)
                UnityEngine.Object.DestroyImmediate(rootCollider);

            var surfaces = new List<List<Vector2>>();
            for (int p = 0; p < feature.sourceParts.Count; p++)
            {
                var world = new List<Vector2>();
                var line = feature.sourceParts[p].centerline;
                for (int i = 0; i < line.Count; i++)
                {
                    Vector3 point = feature.transform.TransformPoint(new Vector3(line[i].x, 0f, line[i].y));
                    world.Add(new Vector2(point.x, point.z));
                }

                surfaces.AddRange(ClipRoad(world, width, paths, out _));
            }

            for (int s = 0; s < surfaces.Count; s++)
            {
                GameObject host = surfaces.Count == 1 ? feature.gameObject : new GameObject("Part_" + s);
                if (host != feature.gameObject)
                    host.transform.SetParent(feature.transform, false);
                var mesh = EnsureMesh(host, feature.name + (surfaces.Count == 1 ? "" : "_Part_" + s));
                if (!FillRoad(mesh, surfaces[s], feature.transform.position, feature.surfaceY, feature.uvMeters, TerrainGroundSampler, 8f, out _))
                    continue;
                var filter = host.GetComponent<MeshFilter>() ?? host.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = host.GetComponent<MeshRenderer>() ?? host.AddComponent<MeshRenderer>();
                if (feature.roadMaterial != null)
                    ConfigureRenderer(renderer, feature.roadMaterial, null);
                AssignCollider(host, mesh);
                written++;
            }

            if (written > 0)
                feature.appliedWidthMeters = width;
        }

        public static void ConfigureRenderer(MeshRenderer renderer, Material primary, Material secondary)
        {
            renderer.sharedMaterials = secondary != null ? new[] { primary, secondary } : new[] { primary };
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.receiveGI = ReceiveGI.LightProbes;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.staticShadowCaster = true;
        }

        public static void AssignCollider(GameObject host, Mesh mesh)
        {
            var collider = host.GetComponent<MeshCollider>();
            if (collider == null)
                collider = host.AddComponent<MeshCollider>();
            collider.convex = false;
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
        }

        static Mesh EnsureMesh(GameObject host, string name)
        {
            var filter = host.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                return filter.sharedMesh;
            var mesh = new Mesh { name = name };
#if UNITY_EDITOR
            var holder = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>("Assets/NazarethPlusCity/Generated/Meshes/OverrideMeshes.asset");
            if (holder == null)
            {
                if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/NazarethPlusCity/Generated/Meshes"))
                    return mesh;
                holder = new Mesh { name = "OverrideMeshes" };
                UnityEditor.AssetDatabase.CreateAsset(holder, "Assets/NazarethPlusCity/Generated/Meshes/OverrideMeshes.asset");
            }

            UnityEditor.AssetDatabase.AddObjectToAsset(mesh, holder);
            UnityEditor.EditorUtility.SetDirty(holder);
#endif
            return mesh;
        }

        static float TerrainGroundSampler(Vector2 world)
        {
            if (TerrainGround.TryHeight(world.x, world.y, out float y, 24f))
                return y;
            return 0f;
        }

        static void AddRingWalls(MeshDraft draft, PlayableRegion region, List<Vector2[]> rings, float height, float thickness, float extension, float embed, Func<Vector2, float> ground, float maxSegment)
        {
            float limit = maxSegment > 0.5f ? maxSegment : 100000f;
            for (int r = 0; r < rings.Count; r++)
            {
                var ring = rings[r];
                for (int i = 0; i < ring.Length; i++)
                {
                    Vector2 start = ring[i];
                    Vector2 end = ring[(i + 1) % ring.Length];
                    float length = Vector2.Distance(start, end);
                    if (length < 0.05f)
                        continue;
                    int steps = Mathf.Max(1, Mathf.CeilToInt(length / limit));
                    for (int s = 0; s < steps; s++)
                    {
                        Vector2 a = Vector2.Lerp(start, end, s / (float)steps);
                        Vector2 b = Vector2.Lerp(start, end, (s + 1) / (float)steps);
                        Vector2 delta = b - a;
                        float span = delta.magnitude;
                        if (span < 0.02f)
                            continue;
                        Vector2 dir = delta / span;
                        Vector2 left = new Vector2(-dir.y, dir.x);
                        bool leftPlayable = region.Contains((a + b) * 0.5f + left * 0.2f);
                        Vector2 outward = leftPlayable ? -left : left;
                        Vector2 innerA = a - dir * extension;
                        Vector2 innerB = b + dir * extension;
                        Vector2 outerA = innerA + outward * thickness;
                        Vector2 outerB = innerB + outward * thickness;
                        float ga = GroundOrZero(ground, innerA);
                        float gb = GroundOrZero(ground, innerB);
                        float gc = GroundOrZero(ground, outerB);
                        float gd = GroundOrZero(ground, outerA);
                        AddBox(draft, innerA, innerB, outerB, outerA, ga - embed, gb - embed, gc - embed, gd - embed, ga + height, gb + height, gc + height, gd + height, outward);
                    }
                }
            }
        }

        static float GroundOrZero(Func<Vector2, float> ground, Vector2 point)
        {
            return ground != null ? ground(point) : 0f;
        }

        static void AddBox(MeshDraft draft, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float ya0, float yb0, float yc0, float yd0, float ya1, float yb1, float yc1, float yd1, Vector2 outward)
        {
            Vector3 nOut = new Vector3(outward.x, 0f, outward.y);
            Vector3 a0 = new Vector3(a.x, ya0, a.y);
            Vector3 b0 = new Vector3(b.x, yb0, b.y);
            Vector3 c0 = new Vector3(c.x, yc0, c.y);
            Vector3 d0 = new Vector3(d.x, yd0, d.y);
            Vector3 a1 = new Vector3(a.x, ya1, a.y);
            Vector3 b1 = new Vector3(b.x, yb1, b.y);
            Vector3 c1 = new Vector3(c.x, yc1, c.y);
            Vector3 d1 = new Vector3(d.x, yd1, d.y);
            Vector2 uv = Vector2.zero;
            AddTriFacing(draft, a0, b0, b1, -nOut, uv, uv, uv, false);
            AddTriFacing(draft, a0, b1, a1, -nOut, uv, uv, uv, false);
            AddTriFacing(draft, d0, c1, c0, nOut, uv, uv, uv, false);
            AddTriFacing(draft, d0, d1, c1, nOut, uv, uv, uv, false);
            AddTriFacing(draft, a0, a1, d1, Vector3.Cross(Vector3.up, nOut), uv, uv, uv, false);
            AddTriFacing(draft, a0, d1, d0, Vector3.Cross(Vector3.up, nOut), uv, uv, uv, false);
            AddTriFacing(draft, b0, c0, c1, Vector3.Cross(nOut, Vector3.up), uv, uv, uv, false);
            AddTriFacing(draft, b0, c1, b1, Vector3.Cross(nOut, Vector3.up), uv, uv, uv, false);
            AddTriFacing(draft, a1, b1, c1, Vector3.up, uv, uv, uv, false);
            AddTriFacing(draft, a1, c1, d1, Vector3.up, uv, uv, uv, false);
            AddTriFacing(draft, a0, c0, b0, Vector3.down, uv, uv, uv, false);
            AddTriFacing(draft, a0, d0, c0, Vector3.down, uv, uv, uv, false);
        }

        static void AddWalls(MeshDraft draft, List<Vector2> ring, float height, float uvMeters, ref float perimeter)
        {
            if (ring == null || ring.Count < 2)
                return;
            for (int i = 0; i < ring.Count; i++)
            {
                Vector2 a = ring[i];
                Vector2 b = ring[(i + 1) % ring.Count];
                float length = Vector2.Distance(a, b);
                if (length < 0.01f)
                    continue;
                Vector2 dir = (b - a) / length;
                Vector2 right = new Vector2(dir.y, -dir.x);
                Vector3 normal = new Vector3(right.x, 0f, right.y);
                float u0 = perimeter / uvMeters;
                float u1 = (perimeter + length) / uvMeters;
                float v1 = height / uvMeters;
                Vector3 a0 = new Vector3(a.x, -0.05f, a.y);
                Vector3 b0 = new Vector3(b.x, -0.05f, b.y);
                Vector3 b1 = new Vector3(b.x, height, b.y);
                Vector3 a1 = new Vector3(a.x, height, a.y);
                AddTriFacing(draft, a0, b0, b1, normal, new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, v1), false);
                AddTriFacing(draft, a0, b1, a1, normal, new Vector2(u0, 0f), new Vector2(u1, v1), new Vector2(u0, v1), false);
                perimeter += length;
            }
        }

        static bool TryTriangulate(List<Vector2> outer, List<List<Vector2>> holes, List<int> triangles, out string error)
        {
            triangles.Clear();
            error = null;
            if (outer == null || outer.Count < 3)
            {
                error = "fewer than 3 vertices";
                return false;
            }

            float outerArea = Mathf.Abs(PlayableRegion.Area(outer));
            float holeArea = 0f;
            var data = new List<float>(outer.Count * 2);
            var holeIndices = new List<int>();
            for (int i = 0; i < outer.Count; i++)
            {
                data.Add(outer[i].x);
                data.Add(outer[i].y);
            }

            if (holes != null)
            {
                for (int h = 0; h < holes.Count; h++)
                {
                    var hole = holes[h];
                    if (hole == null || hole.Count < 3)
                        continue;
                    holeIndices.Add(data.Count / 2);
                    holeArea += Mathf.Abs(PlayableRegion.Area(hole));
                    for (int i = 0; i < hole.Count; i++)
                    {
                        data.Add(hole[i].x);
                        data.Add(hole[i].y);
                    }
                }
            }

            var raw = EarcutLibrary.Earcut(data, holeIndices, 2);
            if (raw == null || raw.Count < 3)
            {
                error = "triangulation produced no triangles";
                return false;
            }

            float triangleArea = 0f;
            int vertexCount = data.Count / 2;
            for (int i = 0; i + 2 < raw.Count; i += 3)
            {
                int a = raw[i];
                int b = raw[i + 1];
                int c = raw[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount)
                {
                    error = "triangulation index out of range";
                    triangles.Clear();
                    return false;
                }

                Vector2 pa = new Vector2(data[a * 2], data[a * 2 + 1]);
                Vector2 pb = new Vector2(data[b * 2], data[b * 2 + 1]);
                Vector2 pc = new Vector2(data[c * 2], data[c * 2 + 1]);
                triangleArea += Mathf.Abs(PlayableRegion.Cross(pb - pa, pc - pa) * 0.5f);
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            float expected = Mathf.Max(0.01f, outerArea - holeArea);
            if (triangleArea < expected * 0.5f || triangleArea > expected * 1.75f)
            {
                error = "triangulated area " + triangleArea.ToString("0.##") + " m2 does not match footprint " + expected.ToString("0.##") + " m2";
                triangles.Clear();
                return false;
            }

            return true;
        }

        static bool RepairWithClipper(List<Vector2> outer, List<List<Vector2>> holes, out List<FootprintPart> parts, out string error)
        {
            parts = new List<FootprintPart>();
            error = null;
            try
            {
                var clipper = new ClipperD(3);
                clipper.AddSubject(OrientedPath(outer, true));
                if (holes != null)
                {
                    for (int i = 0; i < holes.Count; i++)
                        clipper.AddSubject(OrientedPath(holes[i], false));
                }

                var tree = new PolyTreeD();
                clipper.Execute(ClipType.Union, FillRule.NonZero, tree);
                CollectFootprints(tree, parts);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            var kept = new List<FootprintPart>();
            for (int i = 0; i < parts.Count; i++)
            {
                var triangles = new List<int>();
                var partOuter = parts[i].rings[0].points;
                var partHoles = new List<List<Vector2>>();
                for (int h = 1; h < parts[i].rings.Count; h++)
                    partHoles.Add(parts[i].rings[h].points);
                if (TryTriangulate(partOuter, partHoles, triangles, out _))
                    kept.Add(parts[i]);
            }

            parts = kept;
            if (parts.Count == 0)
            {
                error = "union repair still did not triangulate";
                return false;
            }

            return true;
        }

        static void CollectFootprints(PolyPathD node, List<FootprintPart> parts)
        {
            foreach (var child in Children(node))
            {
                if (child.IsHole || child.Polygon == null || child.Polygon.Count < 3)
                {
                    CollectFootprints(child, parts);
                    continue;
                }

                var outer = FromPath(child.Polygon);
                if (PlayableRegion.Area(outer) < 0f)
                    outer.Reverse();
                var part = MakePart(outer, null);
                foreach (var hole in Children(child))
                {
                    if (hole.Polygon == null || hole.Polygon.Count < 3)
                        continue;
                    var holeRing = FromPath(hole.Polygon);
                    if (PlayableRegion.Area(holeRing) > 0f)
                        holeRing.Reverse();
                    part.rings.Add(new LocalRing { points = holeRing });
                    CollectFootprints(hole, parts);
                }

                if (Mathf.Abs(PlayableRegion.Area(outer)) >= MinimumFootprintArea)
                    parts.Add(part);
            }
        }

        static void CollectPolygons(PolyPathD node, List<List<Vector2>> results)
        {
            foreach (var child in Children(node))
            {
                if (!child.IsHole && child.Polygon != null && child.Polygon.Count >= 3)
                {
                    var ring = FromPath(child.Polygon);
                    if (Mathf.Abs(PlayableRegion.Area(ring)) >= 0.05f)
                        results.Add(ring);
                }

                CollectPolygons(child, results);
            }
        }

        static IEnumerable<PolyPathD> Children(PolyPathD node)
        {
            if (node == null)
                yield break;
            foreach (var child in node)
            {
                if (child is PolyPathD path)
                    yield return path;
            }
        }

        static PathD OrientedPath(IList<Vector2> ring, bool positive)
        {
            var path = new PathD();
            bool reverse = positive ? PlayableRegion.Area(ring) < 0f : PlayableRegion.Area(ring) > 0f;
            if (!reverse)
            {
                for (int i = 0; i < ring.Count; i++)
                    path.Add(new PointD(ring[i].x, ring[i].y));
            }
            else
            {
                for (int i = ring.Count - 1; i >= 0; i--)
                    path.Add(new PointD(ring[i].x, ring[i].y));
            }

            return path;
        }

        static List<Vector2> FromPath(PathD path)
        {
            var ring = new List<Vector2>(path.Count);
            for (int i = 0; i < path.Count; i++)
                ring.Add(new Vector2((float)path[i].x, (float)path[i].y));
            return CleanRing(ring, 0.01f);
        }

        static FootprintPart MakePart(List<Vector2> outer, List<List<Vector2>> holes)
        {
            var part = new FootprintPart();
            part.rings.Add(new LocalRing { points = outer });
            if (holes != null)
            {
                for (int i = 0; i < holes.Count; i++)
                    part.rings.Add(new LocalRing { points = holes[i] });
            }

            return part;
        }

        static void Orient(List<Vector2> outer, List<List<Vector2>> holes)
        {
            if (PlayableRegion.Area(outer) < 0f)
                outer.Reverse();
            if (holes == null)
                return;
            for (int i = 0; i < holes.Count; i++)
            {
                if (PlayableRegion.Area(holes[i]) > 0f)
                    holes[i].Reverse();
            }
        }

        static Vector2 Centroid(List<Vector2> ring)
        {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < ring.Count; i++)
                sum += ring[i];
            return sum / Mathf.Max(1, ring.Count);
        }

        static Vector2 WorldUv(Vector3 local, Vector3 origin, float meters)
        {
            float scale = Mathf.Max(0.25f, meters);
            return new Vector2((local.x + origin.x) / scale, (local.z + origin.z) / scale);
        }

        static void AddTriFacing(MeshDraft draft, Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, bool secondary)
        {
            Vector3 facing = Vector3.Cross(b - a, c - a);
            if (facing.sqrMagnitude < 1e-8f)
                return;
            if (Vector3.Dot(facing, normal) < 0f)
            {
                Vector3 swap = b;
                b = c;
                c = swap;
                Vector2 swapUv = ub;
                ub = uc;
                uc = swapUv;
            }

            int index = draft.Vertices.Count;
            draft.Vertices.Add(a);
            draft.Vertices.Add(b);
            draft.Vertices.Add(c);
            Vector3 unit = normal.sqrMagnitude > 0.5f ? normal.normalized : Vector3.up;
            draft.Normals.Add(unit);
            draft.Normals.Add(unit);
            draft.Normals.Add(unit);
            draft.Uvs.Add(ua);
            draft.Uvs.Add(ub);
            draft.Uvs.Add(uc);
            var list = secondary ? draft.Secondary : draft.Primary;
            list.Add(index);
            list.Add(index + 1);
            list.Add(index + 2);
        }

        public static void Apply(Mesh mesh, MeshDraft draft, bool twoSubmeshes)
        {
            mesh.Clear();
            if (draft.Vertices.Count == 0 || draft.Primary.Count == 0)
                return;
            mesh.indexFormat = draft.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(draft.Vertices);
            mesh.SetNormals(draft.Normals);
            mesh.SetUVs(0, draft.Uvs);
            if (twoSubmeshes)
            {
                mesh.subMeshCount = 2;
                mesh.SetTriangles(draft.Primary, 0, false);
                mesh.SetTriangles(draft.Secondary, 1, false);
            }
            else
            {
                mesh.subMeshCount = 1;
                mesh.SetTriangles(draft.Primary, 0, false);
            }

            mesh.RecalculateBounds();
            if (draft.Uvs.Count == draft.Vertices.Count)
            {
                try { mesh.RecalculateTangents(); }
                catch (Exception) { }
            }
        }
    }
}
