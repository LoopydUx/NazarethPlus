using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Clipper2Lib;
using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// World-space ground built from terrain.json. Y is elevation:
    /// localGroundY = (sourceElevation - elevationOrigin) * worldUnitsPerMetre.
    /// </summary>
    public sealed class TerrainHeightField
    {
        struct Tri
        {
            public Vector3 A;
            public Vector3 B;
            public Vector3 C;
            public bool Interpolated;
        }

        struct WeldKey : IEquatable<WeldKey>
        {
            public int X;
            public int Y;
            public int Z;

            public bool Equals(WeldKey other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791);
                }
            }
        }

        public struct ChunkMesh
        {
            public int X;
            public int Z;
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector2[] Uvs;
            public int[] Triangles;
        }

        Tri[] _tris = Array.Empty<Tri>();
        Dictionary<long, int[]> _buckets = new Dictionary<long, int[]>();
        int[] _stamp = Array.Empty<int>();
        int _token;
        float _cell = 16f;

        public float MinY { get; private set; }
        public float MaxY { get; private set; }
        public int SampleCount { get; private set; }
        public int DeclaredSampleCount { get; private set; }
        public bool SampleCountMatches { get; private set; } = true;
        public int DuplicateSamples { get; private set; }
        public int TriangleCount => _tris.Length;
        public int EdgeInterpolatedTriangles { get; private set; }
        public float CoveredArea { get; private set; }
        public float EdgeInterpolatedArea { get; private set; }
        public float UnsupportedArea { get; private set; }
        public float CityArea { get; private set; }
        public float MeasuredSpacing { get; private set; }
        public float ElevationOrigin { get; private set; }
        public float WorldUnitsPerMetre { get; private set; }
        public bool HighestCheck { get; private set; }
        public bool LowestCheck { get; private set; }
        public float HighestExpectedY { get; private set; }
        public float HighestQueriedY { get; private set; }
        public float LowestExpectedY { get; private set; }
        public float LowestQueriedY { get; private set; }
        public string Summary { get; private set; } = "";

        public static float ToWorldY(double sourceElevation, float elevationOrigin, float worldUnitsPerMetre)
        {
            float scale = worldUnitsPerMetre > 0.0001f ? worldUnitsPerMetre : 1f;
            return (float)((sourceElevation - elevationOrigin) * scale);
        }

        public static TerrainHeightField Build(string path, GeoProjection projection, CityGenerationSettings settings, PlayableRegion region, Action<float, string> progress)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException("Terrain file was not found.", path);
            float origin = settings != null ? settings.elevationOriginMeters : 131.2f;
            float scale = settings != null && settings.worldUnitsPerMetre > 0.0001f ? settings.worldUnitsPerMetre : 1f;
            progress?.Invoke(0.02f, "Reading terrain samples");
            var samples = ReadSamples(path, out TerrainHeader header);
            if (samples.Count == 0)
                throw new InvalidDataException("terrain.json did not contain any Point elevation samples.");

            var field = new TerrainHeightField
            {
                SampleCount = samples.Count,
                ElevationOrigin = origin,
                WorldUnitsPerMetre = scale
            };
            field.DeclaredSampleCount = header.SampleCount;
            field.SampleCountMatches = header.SampleCount <= 0 || header.SampleCount == samples.Count;
            field.BuildGrid(samples, header, projection, origin, scale, region, progress);
            return field;
        }

        public static TerrainHeightField FromMeshes(IList<MeshFilter> filters)
        {
            var field = new TerrainHeightField();
            var list = new List<Tri>(8192);
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            if (filters != null)
            {
                for (int f = 0; f < filters.Count; f++)
                {
                    var filter = filters[f];
                    if (filter == null || filter.sharedMesh == null)
                        continue;
                    var mesh = filter.sharedMesh;
                    var vertices = mesh.vertices;
                    var indices = mesh.triangles;
                    var transform = filter.transform;
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        Vector3 a = transform.TransformPoint(vertices[indices[i]]);
                        Vector3 b = transform.TransformPoint(vertices[indices[i + 1]]);
                        Vector3 c = transform.TransformPoint(vertices[indices[i + 2]]);
                        if (!OrientUp(ref a, ref b, ref c))
                            continue;
                        list.Add(new Tri { A = a, B = b, C = c });
                        minY = Mathf.Min(minY, Mathf.Min(a.y, Mathf.Min(b.y, c.y)));
                        maxY = Mathf.Max(maxY, Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
                    }
                }
            }

            field._tris = list.ToArray();
            field.MinY = list.Count > 0 ? minY : 0f;
            field.MaxY = list.Count > 0 ? maxY : 0f;
            field.MeasuredSpacing = 16f;
            field.IndexTriangles();
            field.Summary = "Height field rebuilt from " + field._tris.Length + " saved terrain triangles.";
            return field;
        }

        public bool Sample(float east, float north, out float y)
        {
            y = 0f;
            if (_buckets.Count == 0)
                return false;
            long key = BucketKey(Cell(east), Cell(north));
            if (!_buckets.TryGetValue(key, out int[] list))
                return false;
            for (int i = 0; i < list.Length; i++)
            {
                Tri tri = _tris[list[i]];
                if (Contains(east, north, tri, 0.0008f, out float height))
                {
                    y = height;
                    return true;
                }
            }

            return false;
        }

        public bool SampleNearest(float east, float north, float maxDistance, out float y)
        {
            if (Sample(east, north, out y))
                return true;
            y = 0f;
            if (maxDistance <= 0f || _tris.Length == 0)
                return false;
            float best = maxDistance * maxDistance;
            bool found = false;
            float bestY = 0f;
            int reach = Mathf.CeilToInt(maxDistance / Mathf.Max(1f, _cell)) + 1;
            int cx = Cell(east);
            int cz = Cell(north);
            _token++;
            if (_token == int.MaxValue)
            {
                Array.Clear(_stamp, 0, _stamp.Length);
                _token = 1;
            }

            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    if (!_buckets.TryGetValue(BucketKey(cx + dx, cz + dz), out int[] list))
                        continue;
                    for (int i = 0; i < list.Length; i++)
                    {
                        int index = list[i];
                        if (_stamp[index] == _token)
                            continue;
                        _stamp[index] = _token;
                        Tri tri = _tris[index];
                        if (!Closest(east, north, tri, out float distanceSquared, out float height))
                            continue;
                        if (distanceSquared < best)
                        {
                            best = distanceSquared;
                            bestY = height;
                            found = true;
                        }
                    }
                }
            }

            y = bestY;
            return found;
        }

        public bool TryMaxUnderFootprint(List<FootprintPart> localParts, Vector2 origin, out float maxY, out bool partial)
        {
            maxY = float.NegativeInfinity;
            partial = false;
            if (localParts == null || localParts.Count == 0 || _tris.Length == 0)
                return false;

            var parts = new WorldPart[localParts.Count];
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;
            int ringPoints = 0;
            int missed = 0;
            for (int p = 0; p < localParts.Count; p++)
            {
                var source = localParts[p];
                if (source?.rings == null || source.rings.Count == 0)
                    continue;
                parts[p].Outer = WorldRing(source.rings[0].points, origin, ref minX, ref maxX, ref minZ, ref maxZ);
                if (source.rings.Count > 1)
                {
                    parts[p].Holes = new Vector2[source.rings.Count - 1][];
                    for (int h = 1; h < source.rings.Count; h++)
                        parts[p].Holes[h - 1] = WorldRing(source.rings[h].points, origin, ref minX, ref maxX, ref minZ, ref maxZ);
                }
            }

            if (float.IsPositiveInfinity(minX))
                return false;

            _token++;
            if (_token == int.MaxValue)
            {
                Array.Clear(_stamp, 0, _stamp.Length);
                _token = 1;
            }

            int x0 = Cell(minX) - 1;
            int x1 = Cell(maxX) + 1;
            int z0 = Cell(minZ) - 1;
            int z1 = Cell(maxZ) + 1;
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (!_buckets.TryGetValue(BucketKey(x, z), out int[] list))
                        continue;
                    for (int i = 0; i < list.Length; i++)
                    {
                        int index = list[i];
                        if (_stamp[index] == _token)
                            continue;
                        _stamp[index] = _token;
                        ConsiderTriangle(_tris[index], parts, ref maxY);
                    }
                }
            }

            for (int p = 0; p < parts.Length; p++)
            {
                CountCoverage(parts[p].Outer, ref ringPoints, ref missed);
                if (parts[p].Holes == null)
                    continue;
                for (int h = 0; h < parts[p].Holes.Length; h++)
                    CountCoverage(parts[p].Holes[h], ref ringPoints, ref missed);
            }

            partial = missed > 0;
            return maxY > float.NegativeInfinity;
        }

        public void CollectSegmentCuts(Vector2 a, Vector2 b, List<float> cuts)
        {
            if (cuts == null || _tris.Length == 0)
                return;
            float minX = Mathf.Min(a.x, b.x) - 0.05f;
            float maxX = Mathf.Max(a.x, b.x) + 0.05f;
            float minZ = Mathf.Min(a.y, b.y) - 0.05f;
            float maxZ = Mathf.Max(a.y, b.y) + 0.05f;
            int x0 = Cell(minX);
            int x1 = Cell(maxX);
            int z0 = Cell(minZ);
            int z1 = Cell(maxZ);
            _token++;
            if (_token == int.MaxValue)
            {
                Array.Clear(_stamp, 0, _stamp.Length);
                _token = 1;
            }

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (!_buckets.TryGetValue(BucketKey(x, z), out int[] list))
                        continue;
                    for (int i = 0; i < list.Length; i++)
                    {
                        int index = list[i];
                        if (_stamp[index] == _token)
                            continue;
                        _stamp[index] = _token;
                        Tri tri = _tris[index];
                        AddCut(a, b, Xz(tri.A), Xz(tri.B), cuts);
                        AddCut(a, b, Xz(tri.B), Xz(tri.C), cuts);
                        AddCut(a, b, Xz(tri.C), Xz(tri.A), cuts);
                    }
                }
            }
        }

        public bool FillFoundation(Mesh mesh, List<FootprintPart> localParts, Vector2 origin, Vector3 buildingPosition, float topWorldY, float segmentMeters, float thickness, float sink, float searchMeters, float minDepth, out float depthMeters, out bool allHeightsFound)
        {
            depthMeters = 0f;
            allHeightsFound = true;
            if (mesh == null || localParts == null)
                return false;
            float segment = Mathf.Max(1.5f, segmentMeters);
            float width = Mathf.Clamp(thickness, 0.05f, 1.5f);
            var vertices = new List<Vector3>(256);
            var normals = new List<Vector3>(256);
            var uvs = new List<Vector2>(256);
            var triangles = new List<int>(512);
            bool any = false;
            for (int p = 0; p < localParts.Count; p++)
            {
                var part = localParts[p];
                if (part?.rings == null)
                    continue;
                for (int r = 0; r < part.rings.Count; r++)
                {
                    var ring = WorldRing(part.rings[r].points, origin);
                    if (ring.Count < 3)
                        continue;
                    if (AppendSkirt(ring, topWorldY, segment, width, sink, searchMeters, buildingPosition, vertices, normals, uvs, triangles, ref depthMeters, ref allHeightsFound))
                        any = true;
                }
            }

            if (!any || depthMeters < minDepth || vertices.Count < 3)
            {
                mesh.Clear();
                return false;
            }

            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, false);
            mesh.RecalculateBounds();
            try { mesh.RecalculateTangents(); }
            catch (Exception) { }
            return true;
        }

        public List<ChunkMesh> CreateChunks(float chunkMeters, float uvMeters)
        {
            var result = new List<ChunkMesh>();
            if (_tris.Length == 0)
                return result;
            float chunk = Mathf.Max(64f, chunkMeters);
            float uv = Mathf.Max(0.25f, uvMeters);
            var groups = new Dictionary<long, List<int>>();
            for (int i = 0; i < _tris.Length; i++)
            {
                Vector3 c = (_tris[i].A + _tris[i].B + _tris[i].C) / 3f;
                int cx = Mathf.FloorToInt(c.x / chunk);
                int cz = Mathf.FloorToInt(c.z / chunk);
                long key = BucketKey(cx, cz);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    groups.Add(key, list);
                }

                list.Add(i);
            }

            foreach (var pair in groups)
            {
                int cx = (int)(pair.Key >> 32);
                int cz = (int)(pair.Key & 0xffffffff);
                var mesh = BakeChunk(pair.Value, uv);
                mesh.X = cx;
                mesh.Z = cz;
                if (mesh.Triangles != null && mesh.Triangles.Length >= 3)
                    result.Add(mesh);
            }

            result.Sort((a, b) => a.X == b.X ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
            return result;
        }

        void BuildGrid(List<TerrainSample> samples, TerrainHeader header, GeoProjection projection, float origin, float scale, PlayableRegion region, Action<float, string> progress)
        {
            var lonKeys = new HashSet<long>();
            var latKeys = new HashSet<long>();
            for (int i = 0; i < samples.Count; i++)
            {
                lonKeys.Add(LonKey(samples[i].Longitude));
                latKeys.Add(LatKey(samples[i].Latitude));
            }

            var lons = new long[lonKeys.Count];
            var lats = new long[latKeys.Count];
            lonKeys.CopyTo(lons);
            latKeys.CopyTo(lats);
            Array.Sort(lons);
            Array.Sort(lats);
            if (lons.Length < 2 || lats.Length < 2 || lons.Length > 4000 || lats.Length > 4000)
                throw new InvalidDataException("Terrain samples did not form a usable grid (" + lons.Length + " by " + lats.Length + ").");

            var lonIndex = new Dictionary<long, int>(lons.Length);
            var latIndex = new Dictionary<long, int>(lats.Length);
            var columnLon = new double[lons.Length];
            var rowLat = new double[lats.Length];
            for (int i = 0; i < lons.Length; i++)
            {
                lonIndex.Add(lons[i], i);
                columnLon[i] = lons[i] / 1e7;
            }

            for (int i = 0; i < lats.Length; i++)
            {
                latIndex.Add(lats[i], i);
                rowLat[i] = lats[i] / 1e7;
            }

            int width = lons.Length;
            int height = lats.Length;
            var east = new float[width * height];
            var north = new float[width * height];
            var elev = new float[width * height];
            var real = new bool[width * height];
            float minSource = float.PositiveInfinity;
            float maxSource = float.NegativeInfinity;
            for (int i = 0; i < samples.Count; i++)
            {
                int ix = lonIndex[LonKey(samples[i].Longitude)];
                int iy = latIndex[LatKey(samples[i].Latitude)];
                int idx = iy * width + ix;
                Vector3 world = projection.Project(samples[i].Longitude, samples[i].Latitude);
                float y = ToWorldY(samples[i].Elevation, origin, scale);
                if (real[idx])
                {
                    DuplicateSamples++;
                    continue;
                }

                real[idx] = true;
                east[idx] = world.x;
                north[idx] = world.z;
                elev[idx] = y;
                minSource = Mathf.Min(minSource, (float)samples[i].Elevation);
                maxSource = Mathf.Max(maxSource, (float)samples[i].Elevation);
            }

            MeasuredSpacing = MedianSpacing(projection, columnLon, rowLat, real, east, north, width, height);
            _cell = Mathf.Clamp(MeasuredSpacing, 8f, 64f);
            var boundary = new BoundaryClip(region);
            var tris = new List<Tri>(samples.Count * 2);
            int realCells = 0;
            int interpolatedCells = 0;
            int clipFailures = 0;
            float covered = 0f;
            float edgeArea = 0f;
            int cells = (width - 1) * (height - 1);
            int seen = 0;
            for (int iy = 0; iy < height - 1; iy++)
            {
                for (int ix = 0; ix < width - 1; ix++)
                {
                    seen++;
                    if ((seen & 4095) == 0)
                        progress?.Invoke(0.05f + 0.2f * seen / Mathf.Max(1, cells), "Triangulating terrain");
                    int i00 = iy * width + ix;
                    int i10 = i00 + 1;
                    int i01 = i00 + width;
                    int i11 = i01 + 1;
                    int realCorners = (real[i00] ? 1 : 0) + (real[i10] ? 1 : 0) + (real[i11] ? 1 : 0) + (real[i01] ? 1 : 0);
                    if (realCorners == 0)
                        continue;
                    bool interpolated = realCorners < 4;
                    if (!TryCorner(ix, iy, real, east, north, elev, width, height, columnLon, rowLat, projection, origin, scale, out Vector3 p00))
                        continue;
                    if (!TryCorner(ix + 1, iy, real, east, north, elev, width, height, columnLon, rowLat, projection, origin, scale, out Vector3 p10))
                        continue;
                    if (!TryCorner(ix + 1, iy + 1, real, east, north, elev, width, height, columnLon, rowLat, projection, origin, scale, out Vector3 p11))
                        continue;
                    if (!TryCorner(ix, iy + 1, real, east, north, elev, width, height, columnLon, rowLat, projection, origin, scale, out Vector3 p01))
                        continue;
                    if (interpolated)
                        interpolatedCells++;
                    else
                        realCells++;
                    AddCell(p00, p10, p11, p01, interpolated, boundary, tris, ref covered, ref edgeArea, ref clipFailures);
                }
            }

            _tris = tris.ToArray();
            if (_tris.Length == 0)
                throw new InvalidDataException("Terrain triangulation produced no triangles inside the city boundary.");
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < _tris.Length; i++)
            {
                minY = Mathf.Min(minY, Mathf.Min(_tris[i].A.y, Mathf.Min(_tris[i].B.y, _tris[i].C.y)));
                maxY = Mathf.Max(maxY, Mathf.Max(_tris[i].A.y, Mathf.Max(_tris[i].B.y, _tris[i].C.y)));
            }

            MinY = minY;
            MaxY = maxY;
            CoveredArea = covered;
            EdgeInterpolatedArea = edgeArea;
            EdgeInterpolatedTriangles = 0;
            for (int i = 0; i < _tris.Length; i++)
            {
                if (_tris[i].Interpolated)
                    EdgeInterpolatedTriangles++;
            }

            CityArea = RegionArea(region);
            UnsupportedArea = Mathf.Max(0f, CityArea - CoveredArea);
            IndexTriangles();
            VerifyNamedPoint(header.HasHighest, header.HighestElevation, header.HighestLongitude, header.HighestLatitude, projection, origin, scale, true);
            VerifyNamedPoint(header.HasLowest, header.LowestElevation, header.LowestLongitude, header.LowestLatitude, projection, origin, scale, false);
            var text = new System.Text.StringBuilder();
            text.Append("Terrain samples ").Append(SampleCount);
            if (header.SampleCount > 0)
                text.Append(" (file declares ").Append(header.SampleCount).Append(", missing ").Append(header.MissingCount).Append(")");
            text.Append(". Grid ").Append(width).Append(" by ").Append(height);
            text.Append(", spacing ").Append(MeasuredSpacing.ToString("0.00")).Append(" m.");
            text.Append(" Source elevation ").Append(minSource.ToString("0.0")).Append("–").Append(maxSource.ToString("0.0")).Append(" m.");
            text.Append(" Origin ").Append(origin.ToString("0.0")).Append(" m, scale ").Append(scale.ToString("0.###"));
            text.Append(". Local Y ").Append(MinY.ToString("0.0")).Append(" to ").Append(MaxY.ToString("0.0")).Append('.');
            text.Append(" Triangles ").Append(_tris.Length).Append(" (").Append(realCells).Append(" full cells, ").Append(interpolatedCells).Append(" edge-interpolated cells, ");
            text.Append(EdgeInterpolatedTriangles).Append(" interpolated triangles, clip failures ").Append(clipFailures).Append(").");
            text.Append(" Covered ").Append((CoveredArea / 1e6f).ToString("0.000")).Append(" km2, edge interpolation ");
            text.Append((EdgeInterpolatedArea / 1e6f).ToString("0.000")).Append(" km2, unsupported ");
            text.Append((UnsupportedArea / 1e6f).ToString("0.000")).Append(" km2 of ").Append((CityArea / 1e6f).ToString("0.000")).Append(" km2.");
            text.Append(" Edge interpolation fills only cells that touch a measured sample, using inverse-distance heights from samples within two grid steps, then clips the result to the city polygon.");
            text.Append(" Missing samples are left open. They are not written as zero-height pits. Holes and concave gaps are not bridged.");
            if (header.ReferenceOnly)
                text.Append(" The file is marked referenceOnly; its elevation samples were imported on purpose and the file was not modified.");
            if (DuplicateSamples > 0)
                text.Append(" Duplicate grid samples ignored: ").Append(DuplicateSamples).Append('.');
            if (header.SampleCount > 0 && header.SampleCount != SampleCount)
                text.Append(" Parsed sample count does not match the file header.");
            text.Append(" Highest check ").Append(HighestCheck ? "ok" : "FAILED");
            text.Append(" expected Y ").Append(HighestExpectedY.ToString("0.00")).Append(" queried ").Append(HighestQueriedY.ToString("0.00")).Append('.');
            text.Append(" Lowest check ").Append(LowestCheck ? "ok" : "FAILED");
            text.Append(" expected Y ").Append(LowestExpectedY.ToString("0.00")).Append(" queried ").Append(LowestQueriedY.ToString("0.00")).Append('.');
            Summary = text.ToString();
        }

        void VerifyNamedPoint(bool has, double elevation, double longitude, double latitude, GeoProjection projection, float origin, float scale, bool highest)
        {
            float expected = ToWorldY(elevation, origin, scale);
            float queried = expected;
            bool ok = false;
            if (has)
            {
                Vector3 world = projection.Project(longitude, latitude);
                ok = Sample(world.x, world.z, out queried) || SampleNearest(world.x, world.z, MeasuredSpacing, out queried);
                if (ok)
                    ok = Mathf.Abs(queried - expected) <= 0.05f;
            }

            if (highest)
            {
                HighestExpectedY = expected;
                HighestQueriedY = queried;
                HighestCheck = ok;
            }
            else
            {
                LowestExpectedY = expected;
                LowestQueriedY = queried;
                LowestCheck = ok;
            }
        }

        void AddCell(Vector3 p00, Vector3 p10, Vector3 p11, Vector3 p01, bool interpolated, BoundaryClip boundary, List<Tri> tris, ref float covered, ref float edgeArea, ref int clipFailures)
        {
            AddTriangle(p00, p10, p11, interpolated, boundary, tris, ref covered, ref edgeArea, ref clipFailures);
            AddTriangle(p00, p11, p01, interpolated, boundary, tris, ref covered, ref edgeArea, ref clipFailures);
        }

        void AddTriangle(Vector3 a, Vector3 b, Vector3 c, bool interpolated, BoundaryClip boundary, List<Tri> tris, ref float covered, ref float edgeArea, ref int clipFailures)
        {
            if (!OrientUp(ref a, ref b, ref c))
                return;
            bool hitsBoundary = boundary.HitsTriangle(Xz(a), Xz(b), Xz(c));
            if (!interpolated && !hitsBoundary)
            {
                Append(tris, a, b, c, false, ref covered, ref edgeArea);
                return;
            }

            if (!boundary.Clip(a, b, c, interpolated, tris, ref covered, ref edgeArea))
            {
                clipFailures++;
                if (!interpolated && !hitsBoundary)
                    Append(tris, a, b, c, false, ref covered, ref edgeArea);
            }
        }

        static void Append(List<Tri> tris, Vector3 a, Vector3 b, Vector3 c, bool interpolated, ref float covered, ref float edgeArea)
        {
            float area = HorizontalArea(a, b, c);
            if (area < 0.02f)
                return;
            tris.Add(new Tri { A = a, B = b, C = c, Interpolated = interpolated });
            covered += area;
            if (interpolated)
                edgeArea += area;
        }

        bool TryCorner(int ix, int iy, bool[] real, float[] east, float[] north, float[] elev, int width, int height, double[] columnLon, double[] rowLat, GeoProjection projection, float origin, float scale, out Vector3 position)
        {
            int idx = iy * width + ix;
            if (real[idx])
            {
                position = new Vector3(east[idx], elev[idx], north[idx]);
                return true;
            }

            position = default;
            double weight = 0.0;
            double heightSum = 0.0;
            Vector3 seed = projection.Project(columnLon[ix], rowLat[iy]);
            var seedFlat = new Vector2(seed.x, seed.z);
            for (int dy = -2; dy <= 2; dy++)
            {
                int y = iy + dy;
                if (y < 0 || y >= height)
                    continue;
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = ix + dx;
                    if (x < 0 || x >= width)
                        continue;
                    int donor = y * width + x;
                    if (!real[donor])
                        continue;
                    float distance = Vector2.Distance(seedFlat, new Vector2(east[donor], north[donor]));
                    if (distance > MeasuredSpacing * 2.25f)
                        continue;
                    double w = distance < 0.05f ? 1e6 : 1.0 / (distance * distance);
                    weight += w;
                    heightSum += w * elev[donor];
                }
            }

            if (weight <= 0.0)
                return false;
            position = new Vector3(seed.x, (float)(heightSum / weight), seed.z);
            return true;
        }

        void IndexTriangles()
        {
            _cell = Mathf.Clamp(_cell < 1f ? 16f : _cell, 8f, 64f);
            var lists = new Dictionary<long, List<int>>();
            for (int i = 0; i < _tris.Length; i++)
            {
                Tri tri = _tris[i];
                float minX = Mathf.Min(tri.A.x, Mathf.Min(tri.B.x, tri.C.x)) - 0.05f;
                float maxX = Mathf.Max(tri.A.x, Mathf.Max(tri.B.x, tri.C.x)) + 0.05f;
                float minZ = Mathf.Min(tri.A.z, Mathf.Min(tri.B.z, tri.C.z)) - 0.05f;
                float maxZ = Mathf.Max(tri.A.z, Mathf.Max(tri.B.z, tri.C.z)) + 0.05f;
                int x0 = Cell(minX);
                int x1 = Cell(maxX);
                int z0 = Cell(minZ);
                int z1 = Cell(maxZ);
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        long key = BucketKey(x, z);
                        if (!lists.TryGetValue(key, out var bucket))
                        {
                            bucket = new List<int>();
                            lists.Add(key, bucket);
                        }

                        bucket.Add(i);
                    }
                }
            }

            _buckets = new Dictionary<long, int[]>(lists.Count);
            foreach (var pair in lists)
                _buckets.Add(pair.Key, pair.Value.ToArray());
            _stamp = new int[_tris.Length];
        }

        void ConsiderTriangle(Tri tri, WorldPart[] parts, ref float maxY)
        {
            Consider(Xz(tri.A), tri.A.y, parts, ref maxY);
            Consider(Xz(tri.B), tri.B.y, parts, ref maxY);
            Consider(Xz(tri.C), tri.C.y, parts, ref maxY);
            for (int p = 0; p < parts.Length; p++)
            {
                ConsiderRingVertices(parts[p].Outer, tri, parts, ref maxY);
                CutRing(parts[p].Outer, tri, parts, ref maxY);
                if (parts[p].Holes == null)
                    continue;
                for (int h = 0; h < parts[p].Holes.Length; h++)
                {
                    ConsiderRingVertices(parts[p].Holes[h], tri, parts, ref maxY);
                    CutRing(parts[p].Holes[h], tri, parts, ref maxY);
                }
            }
        }

        void ConsiderRingVertices(Vector2[] ring, Tri tri, WorldPart[] parts, ref float maxY)
        {
            if (ring == null)
                return;
            for (int i = 0; i < ring.Length; i++)
            {
                if (Contains(ring[i].x, ring[i].y, tri, 0.0008f, out float height))
                    Consider(ring[i], height, parts, ref maxY);
            }
        }

        void CutRing(Vector2[] ring, Tri tri, WorldPart[] parts, ref float maxY)
        {
            if (ring == null || ring.Length < 2)
                return;
            Vector2 a = Xz(tri.A);
            Vector2 b = Xz(tri.B);
            Vector2 c = Xz(tri.C);
            for (int i = 0; i < ring.Length; i++)
            {
                Vector2 p = ring[i];
                Vector2 q = ring[(i + 1) % ring.Length];
                if (SegmentIntersect(p, q, a, b, out _, out Vector2 hit) && Contains(hit.x, hit.y, tri, 0.002f, out float h0))
                    Consider(hit, h0, parts, ref maxY);
                if (SegmentIntersect(p, q, b, c, out _, out hit) && Contains(hit.x, hit.y, tri, 0.002f, out float h1))
                    Consider(hit, h1, parts, ref maxY);
                if (SegmentIntersect(p, q, c, a, out _, out hit) && Contains(hit.x, hit.y, tri, 0.002f, out float h2))
                    Consider(hit, h2, parts, ref maxY);
            }
        }

        static void Consider(Vector2 point, float height, WorldPart[] parts, ref float maxY)
        {
            if (!Occupied(point, parts))
                return;
            if (height > maxY)
                maxY = height;
        }

        void CountCoverage(Vector2[] ring, ref int points, ref int missed)
        {
            if (ring == null)
                return;
            for (int i = 0; i < ring.Length; i++)
            {
                points++;
                if (!Sample(ring[i].x, ring[i].y, out _))
                    missed++;
            }
        }

        static bool Occupied(Vector2 point, WorldPart[] parts)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                var outer = parts[i].Outer;
                if (outer == null || outer.Length < 3)
                    continue;
                if (!PlayableRegion.PointInRing(point, outer) && PlayableRegion.DistanceToRing(point, outer) > 0.04f)
                    continue;
                bool hole = false;
                var holes = parts[i].Holes;
                if (holes != null)
                {
                    for (int h = 0; h < holes.Length; h++)
                    {
                        if (holes[h] == null)
                            continue;
                        if (PlayableRegion.DistanceToRing(point, holes[h]) <= 0.03f)
                            continue;
                        if (PlayableRegion.PointInRing(point, holes[h]))
                        {
                            hole = true;
                            break;
                        }
                    }
                }

                if (!hole)
                    return true;
            }

            return false;
        }

        bool AppendSkirt(List<Vector2> ring, float top, float segment, float thickness, float sink, float search, Vector3 buildingPosition, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles, ref float depth, ref bool allFound)
        {
            var points = new List<Vector2>(ring.Count * 2);
            for (int i = 0; i < ring.Count; i++)
            {
                Vector2 a = ring[i];
                Vector2 b = ring[(i + 1) % ring.Count];
                points.Add(a);
                float length = Vector2.Distance(a, b);
                if (length < 0.05f)
                    continue;
                var cuts = new List<float> { 0f, 1f };
                int steps = Mathf.CeilToInt(length / segment);
                for (int s = 1; s < steps; s++)
                    cuts.Add(s / (float)steps);
                CollectSegmentCuts(a, b, cuts);
                cuts.Sort();
                float previous = 0f;
                for (int c = 0; c < cuts.Count; c++)
                {
                    float t = Mathf.Clamp01(cuts[c]);
                    if (t <= 0.001f || t >= 0.999f || t - previous < 0.02f)
                        continue;
                    previous = t;
                    points.Add(Vector2.Lerp(a, b, t));
                }
            }

            if (points.Count < 3)
                return false;
            var outer = new Vector2[points.Count];
            var bottom = new float[points.Count];
            var ringArray = ring.ToArray();
            bool positive = PlayableRegion.Area(ringArray) >= 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 previous = points[(i - 1 + points.Count) % points.Count];
                Vector2 next = points[(i + 1) % points.Count];
                Vector2 dir = next - previous;
                if (dir.sqrMagnitude < 1e-8f)
                    dir = next - points[i];
                dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector2.right;
                Vector2 right = new Vector2(dir.y, -dir.x);
                bool insideRing = PlayableRegion.PointInRing(points[i] + right * 0.08f, ringArray);
                bool wantInside = !positive;
                Vector2 outward = insideRing == wantInside ? right : -right;
                if (outward.sqrMagnitude < 1e-8f)
                    outward = right;
                outer[i] = points[i] + outward.normalized * thickness;
                if (!Resolve(points[i].x, points[i].y, search, out float ground) && !Resolve(outer[i].x, outer[i].y, search, out ground))
                {
                    allFound = false;
                    ground = top;
                }

                float drop = top - ground;
                if (drop > depth)
                    depth = drop;
                bottom[i] = Mathf.Min(ground - sink, top - 0.01f);
            }

            bool wrote = false;
            for (int i = 0; i < points.Count; i++)
            {
                int n = (i + 1) % points.Count;
                if (Vector2.Distance(points[i], points[n]) < 0.02f)
                    continue;
                Vector3 it0 = Local(points[i], top, buildingPosition);
                Vector3 ib0 = Local(points[i], bottom[i], buildingPosition);
                Vector3 it1 = Local(points[n], top, buildingPosition);
                Vector3 ib1 = Local(points[n], bottom[n], buildingPosition);
                Vector3 ot0 = Local(outer[i], top, buildingPosition);
                Vector3 ob0 = Local(outer[i], bottom[i], buildingPosition);
                Vector3 ot1 = Local(outer[n], top, buildingPosition);
                Vector3 ob1 = Local(outer[n], bottom[n], buildingPosition);
                Vector3 outward = Vector3.Cross(ob1 - ob0, ot0 - ob0);
                if (outward.sqrMagnitude < 1e-10f)
                    outward = Vector3.Cross(new Vector3(outer[i].x - points[i].x, 0f, outer[i].y - points[i].y), Vector3.up);
                outward.Normalize();
                AddQuad(vertices, normals, uvs, triangles, ob0, ob1, ot1, ot0, outward);
                AddQuad(vertices, normals, uvs, triangles, ib1, ib0, it0, it1, -outward);
                AddQuad(vertices, normals, uvs, triangles, ib0, ib1, ob1, ob0, Vector3.down);
                AddQuad(vertices, normals, uvs, triangles, it0, ot0, ot1, it1, Vector3.up);
                wrote = true;
            }

            return wrote;
        }

        bool Resolve(float east, float north, float search, out float y)
        {
            if (Sample(east, north, out y))
                return true;
            return SampleNearest(east, north, search, out y);
        }

        static Vector3 Local(Vector2 flat, float y, Vector3 origin)
        {
            return new Vector3(flat.x - origin.x, y - origin.y, flat.y - origin.z);
        }

        static void AddQuad(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int i = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            Vector3 unit = normal.sqrMagnitude > 0.1f ? normal.normalized : Vector3.up;
            normals.Add(unit);
            normals.Add(unit);
            normals.Add(unit);
            normals.Add(unit);
            uvs.Add(new Vector2(a.x * 0.4f, a.z * 0.4f));
            uvs.Add(new Vector2(b.x * 0.4f, b.z * 0.4f));
            uvs.Add(new Vector2(c.x * 0.4f, c.z * 0.4f));
            uvs.Add(new Vector2(d.x * 0.4f, d.z * 0.4f));
            triangles.Add(i);
            triangles.Add(i + 1);
            triangles.Add(i + 2);
            triangles.Add(i);
            triangles.Add(i + 2);
            triangles.Add(i + 3);
        }

        ChunkMesh BakeChunk(List<int> triangleIndices, float uvMeters)
        {
            var map = new Dictionary<WeldKey, int>(triangleIndices.Count);
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>(triangleIndices.Count * 3);
            for (int t = 0; t < triangleIndices.Count; t++)
            {
                Tri tri = _tris[triangleIndices[t]];
                Vector3 normal = Vector3.Cross(tri.B - tri.A, tri.C - tri.A);
                if (normal.y < 0f)
                    normal = -normal;
                if (normal.sqrMagnitude < 1e-10f)
                    continue;
                normal.Normalize();
                int ia = Weld(map, positions, normals, uvs, tri.A, normal, uvMeters);
                int ib = Weld(map, positions, normals, uvs, tri.B, normal, uvMeters);
                int ic = Weld(map, positions, normals, uvs, tri.C, normal, uvMeters);
                if (ia == ib || ib == ic || ic == ia)
                    continue;
                indices.Add(ia);
                indices.Add(ib);
                indices.Add(ic);
            }

            for (int i = 0; i < normals.Count; i++)
                normals[i] = normals[i].sqrMagnitude > 1e-8f ? normals[i].normalized : Vector3.up;
            return new ChunkMesh
            {
                Vertices = positions.ToArray(),
                Normals = normals.ToArray(),
                Uvs = uvs.ToArray(),
                Triangles = indices.ToArray()
            };
        }

        static int Weld(Dictionary<WeldKey, int> map, List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, Vector3 position, Vector3 normal, float uvMeters)
        {
            var key = new WeldKey
            {
                X = Mathf.RoundToInt(position.x * 1000f),
                Y = Mathf.RoundToInt(position.y * 1000f),
                Z = Mathf.RoundToInt(position.z * 1000f)
            };
            if (map.TryGetValue(key, out int index))
            {
                normals[index] += normal;
                return index;
            }

            index = positions.Count;
            map.Add(key, index);
            positions.Add(position);
            normals.Add(normal);
            uvs.Add(new Vector2(position.x / uvMeters, position.z / uvMeters));
            return index;
        }

        int Cell(float value)
        {
            return Mathf.FloorToInt(value / _cell);
        }

        static long BucketKey(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }

        static bool Contains(float east, float north, Tri tri, float epsilon, out float height)
        {
            height = 0f;
            if (!Barycentric(new Vector2(east, north), Xz(tri.A), Xz(tri.B), Xz(tri.C), epsilon, out float u, out float v, out float w))
                return false;
            height = u * tri.A.y + v * tri.B.y + w * tri.C.y;
            return true;
        }

        static bool Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float epsilon, out float u, out float v, out float w)
        {
            float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            u = v = w = 0f;
            if (Mathf.Abs(den) < 1e-8f)
                return false;
            u = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / den;
            v = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / den;
            w = 1f - u - v;
            return u >= -epsilon && v >= -epsilon && w >= -epsilon;
        }

        static bool Closest(float east, float north, Tri tri, out float distanceSquared, out float height)
        {
            var p = new Vector2(east, north);
            Vector2 a = Xz(tri.A);
            Vector2 b = Xz(tri.B);
            Vector2 c = Xz(tri.C);
            if (Barycentric(p, a, b, c, 0f, out float u, out float v, out float w))
            {
                distanceSquared = 0f;
                height = u * tri.A.y + v * tri.B.y + w * tri.C.y;
                return true;
            }

            distanceSquared = float.PositiveInfinity;
            height = tri.A.y;
            ConsiderEdge(p, a, b, tri.A.y, tri.B.y, ref distanceSquared, ref height);
            ConsiderEdge(p, b, c, tri.B.y, tri.C.y, ref distanceSquared, ref height);
            ConsiderEdge(p, c, a, tri.C.y, tri.A.y, ref distanceSquared, ref height);
            return distanceSquared < float.PositiveInfinity;
        }

        static void ConsiderEdge(Vector2 p, Vector2 a, Vector2 b, float ha, float hb, ref float best, ref float height)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            float t = lengthSquared < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSquared);
            Vector2 q = a + ab * t;
            float distance = (q - p).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                height = Mathf.Lerp(ha, hb, t);
            }
        }

        static void AddCut(Vector2 a, Vector2 b, Vector2 c, Vector2 d, List<float> cuts)
        {
            if (SegmentIntersect(a, b, c, d, out float t, out _))
                cuts.Add(t);
        }

        static bool SegmentIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float tAb, out Vector2 point)
        {
            Vector2 ab = b - a;
            Vector2 cd = d - c;
            float den = PlayableRegion.Cross(ab, cd);
            tAb = 0f;
            point = a;
            if (Mathf.Abs(den) < 1e-8f)
                return false;
            float t = PlayableRegion.Cross(c - a, cd) / den;
            float u = PlayableRegion.Cross(c - a, ab) / den;
            if (t < -0.0001f || t > 1.0001f || u < -0.0001f || u > 1.0001f)
                return false;
            tAb = Mathf.Clamp01(t);
            point = a + ab * tAb;
            return true;
        }

        static bool OrientUp(ref Vector3 a, ref Vector3 b, ref Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-8f)
                return false;
            if (normal.y < 0f)
            {
                Vector3 swap = b;
                b = c;
                c = swap;
            }

            return true;
        }

        static float HorizontalArea(Vector3 a, Vector3 b, Vector3 c)
        {
            return Mathf.Abs((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)) * 0.5f;
        }

        static Vector2 Xz(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        static Vector2[] WorldRing(List<Vector2> local, Vector2 origin, ref float minX, ref float maxX, ref float minZ, ref float maxZ)
        {
            if (local == null)
                return Array.Empty<Vector2>();
            var ring = new Vector2[local.Count];
            for (int i = 0; i < local.Count; i++)
            {
                var point = local[i] + origin;
                ring[i] = point;
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minZ = Mathf.Min(minZ, point.y);
                maxZ = Mathf.Max(maxZ, point.y);
            }

            return ring;
        }

        static List<Vector2> WorldRing(List<Vector2> local, Vector2 origin)
        {
            var ring = new List<Vector2>(local != null ? local.Count : 0);
            if (local == null)
                return ring;
            for (int i = 0; i < local.Count; i++)
                ring.Add(local[i] + origin);
            return ring;
        }

        static float RegionArea(PlayableRegion region)
        {
            float area = 0f;
            if (region == null)
                return 0f;
            for (int i = 0; i < region.Outers.Count; i++)
                area += Mathf.Abs(PlayableRegion.Area(region.Outers[i]));
            for (int i = 0; i < region.Holes.Count; i++)
                area -= Mathf.Abs(PlayableRegion.Area(region.Holes[i]));
            return Mathf.Max(0f, area);
        }

        static float MedianSpacing(GeoProjection projection, double[] columnLon, double[] rowLat, bool[] real, float[] east, float[] north, int width, int height)
        {
            var steps = new List<float>(256);
            for (int iy = 0; iy < height; iy += Math.Max(1, height / 12))
            {
                for (int ix = 0; ix < width - 1; ix++)
                {
                    int a = iy * width + ix;
                    int b = a + 1;
                    if (!real[a] || !real[b])
                        continue;
                    steps.Add(Vector2.Distance(new Vector2(east[a], north[a]), new Vector2(east[b], north[b])));
                    if (steps.Count > 4000)
                        break;
                }
            }

            if (steps.Count == 0 && columnLon.Length > 1)
            {
                double lat = rowLat[rowLat.Length / 2];
                Vector3 a = projection.Project(columnLon[0], lat);
                Vector3 b = projection.Project(columnLon[1], lat);
                return Mathf.Max(1f, Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)));
            }

            steps.Sort();
            return Mathf.Max(1f, steps[steps.Count / 2]);
        }

        static long LonKey(double longitude)
        {
            return (long)Math.Round(longitude * 1e7);
        }

        static long LatKey(double latitude)
        {
            return (long)Math.Round(latitude * 1e7);
        }

        struct WorldPart
        {
            public Vector2[] Outer;
            public Vector2[][] Holes;
        }

        struct TerrainSample
        {
            public double Longitude;
            public double Latitude;
            public double Elevation;
        }

        struct TerrainHeader
        {
            public int SampleCount;
            public int MissingCount;
            public bool ReferenceOnly;
            public bool HasHighest;
            public bool HasLowest;
            public double HighestElevation;
            public double HighestLongitude;
            public double HighestLatitude;
            public double LowestElevation;
            public double LowestLongitude;
            public double LowestLatitude;
        }

        sealed class BoundaryClip
        {
            public readonly PlayableRegion Region;
            readonly PathsD _paths;
            readonly Vector2[] _a;
            readonly Vector2[] _b;
            readonly Dictionary<long, int[]> _buckets;
            readonly float _cell;

            public BoundaryClip(PlayableRegion region)
            {
                Region = region;
                _paths = CityGeometry.BoundaryPaths(region);
                _cell = 32f;
                var a = new List<Vector2>();
                var b = new List<Vector2>();
                AddEdges(region != null ? region.Outers : null, a, b);
                AddEdges(region != null ? region.Holes : null, a, b);
                _a = a.ToArray();
                _b = b.ToArray();
                var lists = new Dictionary<long, List<int>>();
                for (int i = 0; i < _a.Length; i++)
                {
                    int x0 = Mathf.FloorToInt(Mathf.Min(_a[i].x, _b[i].x) / _cell) - 1;
                    int x1 = Mathf.FloorToInt(Mathf.Max(_a[i].x, _b[i].x) / _cell) + 1;
                    int z0 = Mathf.FloorToInt(Mathf.Min(_a[i].y, _b[i].y) / _cell) - 1;
                    int z1 = Mathf.FloorToInt(Mathf.Max(_a[i].y, _b[i].y) / _cell) + 1;
                    for (int z = z0; z <= z1; z++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            long key = ((long)x << 32) ^ (uint)z;
                            if (!lists.TryGetValue(key, out var list))
                            {
                                list = new List<int>();
                                lists.Add(key, list);
                            }

                            list.Add(i);
                        }
                    }
                }

                _buckets = new Dictionary<long, int[]>(lists.Count);
                foreach (var pair in lists)
                    _buckets.Add(pair.Key, pair.Value.ToArray());
            }

            public bool HitsTriangle(Vector2 a, Vector2 b, Vector2 c)
            {
                float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
                float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                float minZ = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                float maxZ = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                int x0 = Mathf.FloorToInt(minX / _cell) - 1;
                int x1 = Mathf.FloorToInt(maxX / _cell) + 1;
                int z0 = Mathf.FloorToInt(minZ / _cell) - 1;
                int z1 = Mathf.FloorToInt(maxZ / _cell) + 1;
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!_buckets.TryGetValue(((long)x << 32) ^ (uint)z, out int[] list))
                            continue;
                        for (int i = 0; i < list.Length; i++)
                        {
                            Vector2 c0 = _a[list[i]];
                            Vector2 d0 = _b[list[i]];
                            if (PointInTriangle(c0, a, b, c) || PointInTriangle(d0, a, b, c))
                                return true;
                            if (PlayableRegion.SegmentsCross(a, b, c0, d0) || PlayableRegion.SegmentsCross(b, c, c0, d0) || PlayableRegion.SegmentsCross(c, a, c0, d0))
                                return true;
                        }
                    }
                }

                return false;
            }

            public bool Clip(Vector3 a, Vector3 b, Vector3 c, bool interpolated, List<Tri> into, ref float covered, ref float edgeArea)
            {
                var subject = new PathsD();
                var path = new PathD();
                path.Add(new PointD(a.x, a.z));
                path.Add(new PointD(b.x, b.z));
                path.Add(new PointD(c.x, c.z));
                subject.Add(path);
                var tree = new PolyTreeD();
                try
                {
                    Clipper.BooleanOp(ClipType.Intersection, subject, _paths, tree, FillRule.NonZero, 3);
                }
                catch (Exception)
                {
                    return false;
                }

                int before = into.Count;
                Walk(tree, a, b, c, interpolated, into, ref covered, ref edgeArea);
                return into.Count > before;
            }

            void Walk(PolyPathD node, Vector3 a, Vector3 b, Vector3 c, bool interpolated, List<Tri> into, ref float covered, ref float edgeArea)
            {
                if (node == null)
                    return;
                foreach (var child in node)
                {
                    if (!(child is PolyPathD path))
                        continue;
                    if (path.IsHole || path.Polygon == null || path.Polygon.Count < 3)
                    {
                        Walk(path, a, b, c, interpolated, into, ref covered, ref edgeArea);
                        continue;
                    }

                    var outer = ToRing(path.Polygon);
                    var holes = new List<List<Vector2>>();
                    foreach (var nested in path)
                    {
                        if (nested is PolyPathD hole && hole.IsHole && hole.Polygon != null && hole.Polygon.Count >= 3)
                            holes.Add(ToRing(hole.Polygon));
                    }

                    Emit(outer, holes, a, b, c, interpolated, into, ref covered, ref edgeArea);
                    foreach (var nested in path)
                    {
                        if (nested is PolyPathD hole)
                            Walk(hole, a, b, c, interpolated, into, ref covered, ref edgeArea);
                    }
                }
            }

            static void Emit(List<Vector2> outer, List<List<Vector2>> holes, Vector3 a, Vector3 b, Vector3 c, bool interpolated, List<Tri> into, ref float covered, ref float edgeArea)
            {
                if (outer == null || outer.Count < 3)
                    return;
                if (PlayableRegion.Area(outer) < 0f)
                    outer.Reverse();
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
                        if (PlayableRegion.Area(hole) > 0f)
                            hole.Reverse();
                        holeIndices.Add(data.Count / 2);
                        for (int i = 0; i < hole.Count; i++)
                        {
                            data.Add(hole[i].x);
                            data.Add(hole[i].y);
                        }
                    }
                }

                var raw = EarcutLibrary.Earcut(data, holeIndices, 2);
                Vector2 pa = Xz(a);
                Vector2 pb = Xz(b);
                Vector2 pc = Xz(c);
                for (int i = 0; i + 2 < raw.Count; i += 3)
                {
                    int ia = raw[i];
                    int ib = raw[i + 1];
                    int ic = raw[i + 2];
                    if (ia < 0 || ib < 0 || ic < 0 || ia * 2 + 1 >= data.Count || ib * 2 + 1 >= data.Count || ic * 2 + 1 >= data.Count)
                        continue;
                    var qa = new Vector2(data[ia * 2], data[ia * 2 + 1]);
                    var qb = new Vector2(data[ib * 2], data[ib * 2 + 1]);
                    var qc = new Vector2(data[ic * 2], data[ic * 2 + 1]);
                    Vector3 va = Lift(qa, pa, pb, pc, a.y, b.y, c.y);
                    Vector3 vb = Lift(qb, pa, pb, pc, a.y, b.y, c.y);
                    Vector3 vc = Lift(qc, pa, pb, pc, a.y, b.y, c.y);
                    if (!OrientUp(ref va, ref vb, ref vc))
                        continue;
                    Append(into, va, vb, vc, interpolated, ref covered, ref edgeArea);
                }
            }

            static Vector3 Lift(Vector2 point, Vector2 a, Vector2 b, Vector2 c, float ha, float hb, float hc)
            {
                if (!Barycentric(point, a, b, c, 0.05f, out float u, out float v, out float w))
                {
                    u = 1f;
                    v = 0f;
                    w = 0f;
                }

                float sum = u + v + w;
                if (Mathf.Abs(sum) > 1e-6f)
                {
                    u /= sum;
                    v /= sum;
                    w /= sum;
                }

                return new Vector3(point.x, u * ha + v * hb + w * hc, point.y);
            }

            static List<Vector2> ToRing(PathD path)
            {
                var ring = new List<Vector2>(path.Count);
                for (int i = 0; i < path.Count; i++)
                {
                    var point = new Vector2((float)path[i].x, (float)path[i].y);
                    if (ring.Count > 0 && Vector2.Distance(ring[ring.Count - 1], point) < 0.001f)
                        continue;
                    ring.Add(point);
                }

                if (ring.Count > 1 && Vector2.Distance(ring[0], ring[ring.Count - 1]) < 0.001f)
                    ring.RemoveAt(ring.Count - 1);
                return ring;
            }

            static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
            {
                return Barycentric(p, a, b, c, 0.0005f, out _, out _, out _);
            }

            static void AddEdges(List<Vector2[]> rings, List<Vector2> a, List<Vector2> b)
            {
                if (rings == null)
                    return;
                for (int r = 0; r < rings.Count; r++)
                {
                    var ring = rings[r];
                    if (ring == null)
                        continue;
                    for (int i = 0; i < ring.Length; i++)
                    {
                        Vector2 p = ring[i];
                        Vector2 q = ring[(i + 1) % ring.Length];
                        if (Vector2.Distance(p, q) < 0.001f)
                            continue;
                        a.Add(p);
                        b.Add(q);
                    }
                }
            }
        }

        static List<TerrainSample> ReadSamples(string path, out TerrainHeader header)
        {
            string text = File.ReadAllText(path);
            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring(1);
            header = ReadHeader(text);
            var samples = new List<TerrainSample>(header.SampleCount > 0 ? header.SampleCount : 1024);
            const string token = "\"type\":\"Point\",\"coordinates\":[";
            int cursor = 0;
            while (cursor < text.Length)
            {
                int at = text.IndexOf(token, cursor, StringComparison.Ordinal);
                if (at < 0)
                    break;
                int number = at + token.Length;
                if (!TryDouble(text, ref number, out double longitude))
                {
                    cursor = at + token.Length;
                    continue;
                }

                SkipComma(text, ref number);
                if (!TryDouble(text, ref number, out double latitude))
                {
                    cursor = number;
                    continue;
                }

                int elevationAt = text.IndexOf("\"elevation\":", number, StringComparison.Ordinal);
                if (elevationAt < 0 || elevationAt > number + 96)
                {
                    cursor = number;
                    continue;
                }

                int elevationIndex = elevationAt + 12;
                if (!TryDouble(text, ref elevationIndex, out double elevation) || double.IsNaN(elevation) || double.IsInfinity(elevation))
                {
                    cursor = elevationIndex;
                    continue;
                }

                samples.Add(new TerrainSample { Longitude = longitude, Latitude = latitude, Elevation = elevation });
                cursor = elevationIndex;
            }

            return samples;
        }

        static TerrainHeader ReadHeader(string text)
        {
            var header = new TerrainHeader();
            int features = text.IndexOf("\"features\"", StringComparison.Ordinal);
            string head = features > 0 ? text.Substring(0, features) : text;
            header.ReferenceOnly = head.IndexOf("\"referenceOnly\":true", StringComparison.Ordinal) >= 0 || head.IndexOf("\"referenceOnly\": true", StringComparison.Ordinal) >= 0;
            header.SampleCount = ReadInt(head, "\"sampleCount\":");
            header.MissingCount = ReadInt(head, "\"missingCount\":");
            header.HasHighest = TryNamed(head, "\"highest\"", out header.HighestElevation, out header.HighestLongitude, out header.HighestLatitude);
            header.HasLowest = TryNamed(head, "\"lowest\"", out header.LowestElevation, out header.LowestLongitude, out header.LowestLatitude);
            return header;
        }

        static bool TryNamed(string head, string label, out double elevation, out double longitude, out double latitude)
        {
            elevation = longitude = latitude = 0;
            int at = head.IndexOf(label, StringComparison.Ordinal);
            if (at < 0)
                return false;
            int end = Math.Min(head.Length, at + 220);
            string slice = head.Substring(at, end - at);
            int e = slice.IndexOf("\"elevation\":", StringComparison.Ordinal);
            int c = slice.IndexOf("\"coordinates\"", StringComparison.Ordinal);
            if (e < 0 || c < 0)
                return false;
            int index = e + 12;
            if (!TryDouble(slice, ref index, out elevation))
                return false;
            index = c + 13;
            while (index < slice.Length && slice[index] != '[')
                index++;
            index++;
            if (!TryDouble(slice, ref index, out longitude))
                return false;
            SkipComma(slice, ref index);
            return TryDouble(slice, ref index, out latitude);
        }

        static int ReadInt(string text, string label)
        {
            int at = text.IndexOf(label, StringComparison.Ordinal);
            if (at < 0)
                return 0;
            int index = at + label.Length;
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
            int start = index;
            while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '-'))
                index++;
            if (start == index)
                return 0;
            int.TryParse(text.Substring(start, index - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value);
            return value;
        }

        static void SkipComma(string text, ref int index)
        {
            while (index < text.Length && (char.IsWhiteSpace(text[index]) || text[index] == ','))
                index++;
        }

        static bool TryDouble(string text, ref int index, out double value)
        {
            value = 0;
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
            int start = index;
            if (index < text.Length && (text[index] == '-' || text[index] == '+'))
                index++;
            bool digit = false;
            while (index < text.Length && char.IsDigit(text[index]))
            {
                digit = true;
                index++;
            }

            if (index < text.Length && text[index] == '.')
            {
                index++;
                while (index < text.Length && char.IsDigit(text[index]))
                {
                    digit = true;
                    index++;
                }
            }

            if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
            {
                index++;
                if (index < text.Length && (text[index] == '+' || text[index] == '-'))
                    index++;
                while (index < text.Length && char.IsDigit(text[index]))
                    index++;
            }

            if (!digit)
                return false;
            return double.TryParse(text.Substring(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    public class CityTerrain : MonoBehaviour
    {
        public static CityTerrain Active;

        public float elevationOriginMeters = 131.2f;
        public float worldUnitsPerMetre = 1f;
        public float minWorldY;
        public float maxWorldY;
        public int sampleCount;
        public int triangleCount;
        public int chunkCount;
        public float coveredArea;
        public float unsupportedArea;
        public float edgeInterpolatedArea;
        public int edgeInterpolatedTriangles;
        public float measuredSpacing = 16.08f;
        [TextArea(4, 10)]
        public string coverageNote;

        void OnEnable()
        {
            Active = this;
        }

        void OnDisable()
        {
            if (Active == this)
                Active = null;
        }
    }

    public class TerrainChunk : MonoBehaviour
    {
    }

    public class BuildingFoundation : MonoBehaviour
    {
        public float depthMeters;
        public bool tall;
        public bool reachedGround = true;
    }

    public static class TerrainGround
    {
        static readonly RaycastHit[] Hits = new RaycastHit[64];

        public static TerrainHeightField Field;

        public static bool TryHeight(float east, float north, out float y, float searchMeters)
        {
            if (Field != null)
            {
                if (Field.Sample(east, north, out y))
                    return true;
                if (searchMeters > 0f && Field.SampleNearest(east, north, searchMeters, out y))
                    return true;
                y = 0f;
                return false;
            }

            return TrySurface(east, north, out y);
        }

        public static bool TrySurface(float east, float north, out float y)
        {
            y = 0f;
            var terrain = CityTerrain.Active != null ? CityTerrain.Active : UnityEngine.Object.FindAnyObjectByType<CityTerrain>();
            float top = terrain != null ? terrain.maxWorldY + 40f : 800f;
            float bottom = terrain != null ? terrain.minWorldY - 40f : -80f;
            int count = Physics.RaycastNonAlloc(new Vector3(east, top, north), Vector3.down, Hits, Mathf.Max(1f, top - bottom), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            int bestRank = 0;
            float bestY = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].normal.y < 0.35f)
                    continue;
                int rank = SurfaceRank(Hits[i].collider);
                if (rank <= 0)
                    continue;
                if (rank > bestRank || (rank == bestRank && Hits[i].point.y > bestY))
                {
                    bestRank = rank;
                    bestY = Hits[i].point.y;
                    found = true;
                }
            }

            if (!found)
                return false;
            y = bestY;
            return true;
        }

        public static bool IsWalkSurface(Collider collider)
        {
            return SurfaceRank(collider) >= 2;
        }

        static int SurfaceRank(Collider collider)
        {
            if (collider == null)
                return 0;
            if (collider.GetComponentInParent<StreetFeature>() != null)
                return 3;
            if (collider.GetComponentInParent<TerrainChunk>() != null)
                return 2;
            if (collider.GetComponentInParent<BuildingFeature>() != null || collider.GetComponentInParent<BuildingFoundation>() != null)
                return 0;
            return 1;
        }
    }
}
