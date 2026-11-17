using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Clipper2Lib;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NazarethPlusCity.EditorTools
{
    public static class CityImporter
    {
        public const string ScenePath = "Assets/NazarethPlusCity/Scenes/NazarethPlusCity.unity";
        public const string ReportAssetPath = "Assets/NazarethPlusCity/Generated/ImportReport.txt";

        public static string ReportProjectPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "NazarethPlusCity_ImportReport.txt");

        public static void GenerateOrRebuild()
        {
            CityEditGuard.Suppress = true;
            var report = new CityReport();
            try
            {
                Generate(report);
                WriteReport(report);
                if (report.ValidationFailures.Count > 0)
                    throw new InvalidOperationException(string.Join(Environment.NewLine, report.ValidationFailures));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                CityEditGuard.Suppress = false;
            }
        }

        static void Generate(CityReport report)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string boundaryPath = Path.Combine(root, "Data", "city_boundary.geojson");
            string buildingPath = Path.Combine(root, "Data", "buildings.geojson");
            string streetPath = Path.Combine(root, "Data", "streets.geojson");
            if (!File.Exists(boundaryPath) || !File.Exists(buildingPath) || !File.Exists(streetPath))
                throw new FileNotFoundException("Expected Data/city_boundary.geojson, Data/buildings.geojson, and Data/streets.geojson under the Unity project.");

            CityMaterialFactory.EnsureFolder("Assets/NazarethPlusCity/Settings");
            CityMaterialFactory.EnsureFolder("Assets/NazarethPlusCity/Scenes");
            CityMaterialFactory.EnsureFolder("Assets/NazarethPlusCity/Generated");
            var settings = LoadOrCreate<CityGenerationSettings>("Assets/NazarethPlusCity/Settings/CityGenerationSettings.asset");
            var frame = LoadOrCreate<CityCoordinateFrame>("Assets/NazarethPlusCity/Settings/CityCoordinateFrame.asset");
            var materials = CityMaterialFactory.CreateOrUpdate();

            EditorUtility.DisplayProgressBar("NazarethPlusCity", "Reading city boundary", 0.02f);
            var boundaryFeatures = GeoJsonFile.ReadFeatures(boundaryPath, out string boundaryError);
            if (!string.IsNullOrEmpty(boundaryError))
                throw new InvalidDataException(boundaryError);
            report.SourceBoundary = boundaryFeatures.Count;

            var outers = new List<List<Vector2>>();
            var holes = new List<List<Vector2>>();
            var rawOuters = new List<List<GeoLonLat>>();
            var rawHoles = new List<List<GeoLonLat>>();
            GeoFeature borderFeature = null;
            for (int i = 0; i < boundaryFeatures.Count; i++)
            {
                var feature = boundaryFeatures[i];
                if (feature.Polygons == null || feature.Polygons.Count == 0)
                {
                    report.Add("Boundary", feature.Id, "Skipped", "malformed geometry: " + (feature.Properties.TryGetValue("geometry_error", out string why) ? why : "no polygon"));
                    continue;
                }

                borderFeature = borderFeature ?? feature;
                for (int p = 0; p < feature.Polygons.Count; p++)
                {
                    var rings = feature.Polygons[p].Rings;
                    if (rings.Count == 0)
                        continue;
                    rawOuters.Add(rings[0]);
                    for (int h = 1; h < rings.Count; h++)
                        rawHoles.Add(rings[h]);
                }
            }

            if (rawOuters.Count == 0)
                throw new InvalidDataException("city_boundary.geojson has no usable polygon. A rectangle was not substituted.");

            if (!frame.originSaved)
            {
                double lonSum = 0, latSum = 0;
                int count = 0;
                var ring = rawOuters[0];
                int limit = ring.Count > 1 && Same(ring[0], ring[ring.Count - 1]) ? ring.Count - 1 : ring.Count;
                for (int i = 0; i < limit; i++)
                {
                    lonSum += ring[i].Longitude;
                    latSum += ring[i].Latitude;
                    count++;
                }

                frame.originLongitude = lonSum / count;
                frame.originLatitude = latSum / count;
                frame.originSaved = true;
                EditorUtility.SetDirty(frame);
            }

            var projection = GeoProjection.FromOrigin(frame.originLongitude, frame.originLatitude);
            for (int i = 0; i < rawOuters.Count; i++)
                outers.Add(CityGeometry.ProjectRing(rawOuters[i], projection));
            for (int i = 0; i < rawHoles.Count; i++)
                holes.Add(CityGeometry.ProjectRing(rawHoles[i], projection));
            ValidateScale(report, projection, rawOuters[0]);

            var scene = File.Exists(Path.Combine(root, ScenePath))
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var existing = new Dictionary<string, GameObject>();
            var owned = UnityEngine.Object.FindObjectsByType<GeneratorOwned>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < owned.Length; i++)
            {
                if (owned[i] == null || string.IsNullOrEmpty(owned[i].key))
                    continue;
                if (existing.ContainsKey(owned[i].key))
                    UnityEngine.Object.DestroyImmediate(owned[i].gameObject);
                else
                    existing.Add(owned[i].key, owned[i].gameObject);
            }

            var used = new HashSet<string>();
            var names = new HashSet<string>();
            var nazareth = GameObject.Find("Nazareth") ?? new GameObject("Nazareth");
            var buildingsRoot = Folder(nazareth.transform, "Buildings");
            var streetsRoot = Folder(nazareth.transform, "Streets");
            var groundRoot = Folder(nazareth.transform, "Ground");
            var boundaryRoot = Folder(nazareth.transform, "Boundary");
            var environment = Folder(nazareth.transform, "Environment");

            if (AssetDatabase.IsValidFolder("Assets/NazarethPlusCity/Generated/Meshes"))
                AssetDatabase.DeleteAsset("Assets/NazarethPlusCity/Generated/Meshes");
            CityMaterialFactory.EnsureFolder("Assets/NazarethPlusCity/Generated/Meshes");
            var library = new MeshLibrary("Assets/NazarethPlusCity/Generated/Meshes");

            AssetDatabase.StartAssetEditing();
            try
            {
                BuildBoundary(report, settings, materials, library, boundaryRoot, groundRoot, borderFeature, outers, holes, existing, used, out PlayableRegion region, out PathsD boundaryPaths);
                EditorUtility.DisplayProgressBar("NazarethPlusCity", "Reading buildings", 0.08f);
                var buildings = GeoJsonFile.ReadFeatures(buildingPath, out string buildingError);
                if (!string.IsNullOrEmpty(buildingError))
                    throw new InvalidDataException(buildingError);
                report.SourceBuildings = buildings.Count;
                BuildBuildings(report, settings, materials, library, projection, region, buildingsRoot, buildings, existing, used, names);
                EditorUtility.DisplayProgressBar("NazarethPlusCity", "Reading streets", 0.7f);
                var streets = GeoJsonFile.ReadFeatures(streetPath, out string streetError);
                if (!string.IsNullOrEmpty(streetError))
                    throw new InvalidDataException(streetError);
                report.SourceStreets = streets.Count;
                BuildStreets(report, settings, materials, library, projection, region, boundaryPaths, streetsRoot, streets, existing, used, names);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            DestroyStale(existing, used);
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();
            var regionNow = UnityEngine.Object.FindAnyObjectByType<CityBoundaryMap>().Region;
            Vector3 spawn = Vector3.zero;
            float yaw = 0f;
            bool spawned = TrySpawn(settings, regionNow, out spawn, out yaw, out string spawnNote);
            report.Notes.Add(spawnNote);
            BuildEnvironment(settings, materials, environment, existing, used, spawn, yaw, spawned, scene);
            Physics.SyncTransforms();
            ProbeMapBorders(report, settings, spawn);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            ConfigurePipeline();
            var savedScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (savedScene != null)
                EditorSceneManager.playModeStartScene = savedScene;
            CaptureViews(spawn, yaw);
            Validate(report, settings, spawned, spawn);
        }

        static void BuildBoundary(CityReport report, CityGenerationSettings settings, CityMaterials materials, MeshLibrary library, Transform boundaryRoot, Transform groundRoot, GeoFeature feature, List<List<Vector2>> outers, List<List<Vector2>> holes, Dictionary<string, GameObject> existing, HashSet<string> used, out PlayableRegion region, out PathsD boundaryPaths)
        {
            var loops = new List<BoundaryLoop>();
            for (int i = 0; i < outers.Count; i++)
            {
                if (outers[i].Count >= 3)
                    loops.Add(new BoundaryLoop { hole = false, points = outers[i].ToArray() });
            }

            for (int i = 0; i < holes.Count; i++)
            {
                if (holes[i].Count >= 3)
                    loops.Add(new BoundaryLoop { hole = true, points = holes[i].ToArray() });
            }

            region = PlayableRegion.FromLoops(loops);
            boundaryPaths = CityGeometry.BoundaryPaths(region);
            if (region.Outers.Count == 0)
                throw new InvalidDataException("The projected city boundary is empty.");

            var ground = Take(existing, used, "ground", "CityGround", groundRoot);
            ResetMeshes(ground);
            var groundMesh = library.Create("CityGround");
            if (!CityGeometry.FillGround(groundMesh, outers[0], holes, settings.groundUvMeters, out string groundError))
                throw new InvalidDataException("Ground triangulation failed and a rectangular substitute was not used. " + groundError);
            var groundFilter = ground.AddComponent<MeshFilter>();
            groundFilter.sharedMesh = groundMesh;
            var groundRenderer = ground.AddComponent<MeshRenderer>();
            CityGeometry.ConfigureRenderer(groundRenderer, materials.Ground, null);
            CityGeometry.AssignCollider(ground, groundMesh);
            MarkStatic(ground);
            ground.transform.position = Vector3.zero;

            bool showOutline = settings.showBoundaryOutline;
            var boundary = Take(existing, used, "boundary", "CityBoundary", boundaryRoot);
            var previousOutline = boundary.GetComponent<BoundaryOutline>();
            if (previousOutline != null)
                showOutline = previousOutline.visible;
            ResetMeshes(boundary);
            var wallMesh = library.Create("CityBoundaryWalls");
            CityGeometry.FillBorderWalls(wallMesh, region, settings.boundaryWallHeightMeters, settings.boundaryWallThicknessMeters, settings.boundaryCornerExtensionMeters, settings.boundaryWallEmbedMeters);
            CityGeometry.AssignCollider(boundary, wallMesh);
            var map = boundary.GetComponent<CityBoundaryMap>() ?? boundary.AddComponent<CityBoundaryMap>();
            map.SetLoops(loops, settings.containmentSafetyMargin, settings.playerRadius);
            var outline = boundary.GetComponent<BoundaryOutline>() ?? boundary.AddComponent<BoundaryOutline>();
            outline.visible = showOutline;
            for (int i = 0; i < loops.Count; i++)
            {
                var lineObject = new GameObject(loops[i].hole ? "HoleOutline_" + i : "Outline_" + i);
                lineObject.transform.SetParent(boundary.transform, false);
                var line = lineObject.AddComponent<LineRenderer>();
                line.loop = true;
                line.useWorldSpace = true;
                line.widthMultiplier = 0.4f;
                line.positionCount = loops[i].points.Length;
                line.sharedMaterial = materials.Outline;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.startColor = outline.color;
                line.endColor = outline.color;
                for (int p = 0; p < loops[i].points.Length; p++)
                    line.SetPosition(p, new Vector3(loops[i].points[p].x, 0.22f, loops[i].points[p].y));
                line.enabled = showOutline;
            }

            var record = boundary.GetComponent<FeatureRecord>() ?? boundary.AddComponent<FeatureRecord>();
            record.featureId = feature != null ? feature.Id : "boundary";
            record.featureKind = "Boundary";
            record.geometryType = feature != null ? feature.GeometryType : "Polygon";
            record.displayName = feature != null ? DisplayName(feature) : "Nazareth";
            record.propertiesJson = feature != null ? feature.PropertiesJson : "{}";
            MarkStatic(boundary);
            report.Add("Boundary", record.featureId, "Generated", "playable polygon with " + region.Outers.Count + " outer ring(s) and " + region.Holes.Count + " hole(s); outline " + (showOutline ? "visible" : "hidden") + "; blocking walls " + settings.boundaryWallHeightMeters.ToString("0.##") + " m");
            report.Notes.Add("Ground and boundary use the real city polygon, including " + region.Holes.Count + " interior hole ring(s).");
        }

        static void BuildBuildings(CityReport report, CityGenerationSettings settings, CityMaterials materials, MeshLibrary library, GeoProjection projection, PlayableRegion region, Transform parent, List<GeoFeature> features, Dictionary<string, GameObject> existing, HashSet<string> used, HashSet<string> names)
        {
            for (int i = 0; i < features.Count; i++)
            {
                if (i % 250 == 0)
                    EditorUtility.DisplayProgressBar("NazarethPlusCity", "Buildings " + i + " / " + features.Count, 0.1f + 0.55f * i / Mathf.Max(1, features.Count));
                var feature = features[i];
                if (!CityGeometry.PrepareBuilding(feature, projection, out var parts, out string note, out string error))
                {
                    report.SkippedBuildings++;
                    report.Skipped.Add("Building " + feature.Id + ": " + error);
                    report.Add("Building", feature.Id, "Skipped", error);
                    continue;
                }

                var relation = ClassifyParts(region, parts);
                CountRelation(report, relation, true);
                string key = UniqueKey(used, "building:" + feature.Id);
                string objectName = UniqueName(names, "Building_" + CityLabels.Sanitize(feature.Id));
                var go = Take(existing, used, key, objectName, parent);
                bool heightOverride = false;
                float heightValue = 0f;
                bool tintOverride = false;
                Color tint = Color.white;
                var previous = go.GetComponent<BuildingFeature>();
                if (previous != null)
                {
                    heightOverride = previous.useHeightOverride;
                    heightValue = previous.heightOverrideMeters;
                    tintOverride = previous.useTintOverride;
                    tint = previous.tintOverride;
                }

                ResetMeshes(go);
                var decision = MeasureParser.ResolveHeight(
                    GeoJsonFile.Property(feature, "height"),
                    GeoJsonFile.Property(feature, "building:levels"),
                    GeoJsonFile.Property(feature, "number_of_floors"),
                    settings);
                if (decision.resolution == "ExplicitHeight") report.ExplicitHeight++;
                else if (decision.resolution == "FloorCount") report.FloorHeight++;
                else report.FallbackHeight++;

                CityGeometry.ToLocal(parts, out Vector2 origin);
                float meshHeight = heightOverride ? Mathf.Max(0.25f, heightValue) : decision.meters;
                int variant = Mathf.Abs(CityLabels.StableHash(feature.Id)) % materials.Stone.Length;
                var worldOrigin = new Vector3(origin.x, 0f, origin.y);
                go.transform.SetPositionAndRotation(worldOrigin, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                int written = 0;
                for (int p = 0; p < parts.Count; p++)
                {
                    var host = parts.Count == 1 ? go : Child(go.transform, "Part_" + p);
                    var mesh = library.Create(objectName + (parts.Count == 1 ? "" : "_Part_" + p));
                    if (!CityGeometry.FillBuilding(mesh, parts[p], meshHeight, worldOrigin, settings.stoneUvMeters, settings.roofUvMeters, out string meshError))
                    {
                        note = string.IsNullOrEmpty(note) ? meshError : note + "; " + meshError;
                        continue;
                    }

                    var filter = host.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;
                    var renderer = host.AddComponent<MeshRenderer>();
                    CityGeometry.ConfigureRenderer(renderer, materials.Stone[variant], materials.Roof);
                    CityGeometry.AssignCollider(host, mesh);
                    MarkStatic(host);
                    written++;
                }

                var record = go.GetComponent<FeatureRecord>() ?? go.AddComponent<FeatureRecord>();
                record.featureId = feature.Id;
                record.featureKind = "Building";
                record.geometryType = feature.GeometryType;
                record.displayName = DisplayName(feature);
                record.propertiesJson = feature.PropertiesJson;
                var building = go.GetComponent<BuildingFeature>() ?? go.AddComponent<BuildingFeature>();
                building.sourceHeightRaw = decision.sourceHeightRaw;
                building.sourceFloorRaw = decision.sourceFloorRaw;
                building.resolvedHeightMeters = decision.meters;
                building.heightResolution = decision.resolution;
                building.heightDetail = decision.detail;
                building.useHeightOverride = heightOverride;
                building.heightOverrideMeters = heightOverride ? heightValue : decision.meters;
                building.appliedHeightMeters = meshHeight;
                building.useTintOverride = tintOverride;
                building.tintOverride = tint;
                building.appliedTint = materials.Stone[variant].GetColor("_BaseColor");
                building.materialVariant = variant;
                building.parts = parts;
                building.wallMaterial = materials.Stone[variant];
                building.roofMaterial = materials.Roof;
                building.uvMeters = settings.stoneUvMeters;
                if (tintOverride)
                    building.ApplyTintOverride();
                MarkStatic(go);
                report.GeneratedBuildings++;
                string reason = decision.detail + "; mesh height " + meshHeight.ToString("0.##") + " m; parts " + written + "; " + RelationText(relation);
                if (!string.IsNullOrEmpty(note))
                    reason += "; " + note;
                reason += Extras(feature);
                if (written == 0)
                    reason += "; mesh was not created";
                report.Add("Building", feature.Id, "Generated", reason);
                RememberRelation(report, feature.Id, relation);
            }
        }

        static void BuildStreets(CityReport report, CityGenerationSettings settings, CityMaterials materials, MeshLibrary library, GeoProjection projection, PlayableRegion region, PathsD boundaryPaths, Transform parent, List<GeoFeature> features, Dictionary<string, GameObject> existing, HashSet<string> used, HashSet<string> names)
        {
            for (int i = 0; i < features.Count; i++)
            {
                if (i % 100 == 0)
                    EditorUtility.DisplayProgressBar("NazarethPlusCity", "Streets " + i + " / " + features.Count, 0.7f + 0.22f * i / Mathf.Max(1, features.Count));
                var feature = features[i];
                var worldParts = new List<List<Vector2>>();
                if (feature.Lines != null)
                {
                    for (int l = 0; l < feature.Lines.Count; l++)
                    {
                        var line = CityGeometry.ProjectLine(feature.Lines[l], projection);
                        if (line.Count >= 2)
                            worldParts.Add(line);
                    }
                }

                if (worldParts.Count == 0)
                {
                    report.SkippedStreets++;
                    string why = "malformed geometry: " + (feature.Properties.TryGetValue("geometry_error", out string geometryError) ? geometryError : "centerline has fewer than 2 points");
                    report.Skipped.Add("Street " + feature.Id + ": " + why);
                    report.Add("Street", feature.Id, "Skipped", why);
                    continue;
                }

                var flat = new List<Vector2>();
                Vector2 sum = Vector2.zero;
                for (int p = 0; p < worldParts.Count; p++)
                {
                    flat.AddRange(worldParts[p]);
                    for (int v = 0; v < worldParts[p].Count; v++)
                        sum += worldParts[p][v];
                }

                var relation = region.Classify(flat, false, 0.25f);
                CountRelation(report, relation, false);
                RememberRelation(report, "street " + feature.Id, relation);
                string widthRaw = GeoJsonFile.Property(feature, "width");
                string lanes = GeoJsonFile.Property(feature, "lanes");
                string highway = GeoJsonFile.Property(feature, "highway");
                string streetType = GeoJsonFile.Property(feature, "street_type");
                if (string.Equals(highway, "construction", StringComparison.OrdinalIgnoreCase))
                    streetType = GeoJsonFile.Property(feature, "construction") ?? streetType;
                float resolved = MeasureParser.ResolveStreetWidth(widthRaw, lanes, highway, streetType, settings, out string widthDetail);
                string key = UniqueKey(used, "street:" + feature.Id);
                string objectName = UniqueName(names, "Street_" + CityLabels.Sanitize(feature.Id));
                var go = Take(existing, used, key, objectName, parent);
                bool widthOverride = false;
                float widthValue = resolved;
                var previous = go.GetComponent<StreetFeature>();
                if (previous != null && previous.useWidthOverride)
                {
                    widthOverride = true;
                    widthValue = previous.widthOverrideMeters;
                }

                ResetMeshes(go);
                Vector2 origin = sum / flat.Count;
                var worldOrigin = new Vector3(origin.x, 0f, origin.y);
                go.transform.SetPositionAndRotation(worldOrigin, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                float width = widthOverride ? Mathf.Max(0.4f, widthValue) : resolved;
                float lift = settings.roadSurfaceOffset + (Mathf.Abs(CityLabels.StableHash(feature.Id)) % Mathf.Max(1, settings.roadOverlapLiftSteps)) * settings.roadOverlapLiftStep;
                var surfaces = new List<List<Vector2>>();
                string clipNote = null;
                for (int p = 0; p < worldParts.Count; p++)
                {
                    var clipped = CityGeometry.ClipRoad(worldParts[p], width, boundaryPaths, out string oneNote);
                    if (!string.IsNullOrEmpty(oneNote))
                        clipNote = oneNote;
                    surfaces.AddRange(clipped);
                }

                int written = 0;
                for (int s = 0; s < surfaces.Count; s++)
                {
                    var host = surfaces.Count == 1 ? go : Child(go.transform, "Part_" + s);
                    var mesh = library.Create(objectName + (surfaces.Count == 1 ? "" : "_Part_" + s));
                    if (!CityGeometry.FillRoad(mesh, surfaces[s], worldOrigin, lift, settings.asphaltUvMeters, out _))
                        continue;
                    var filter = host.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;
                    var renderer = host.AddComponent<MeshRenderer>();
                    CityGeometry.ConfigureRenderer(renderer, materials.Asphalt, null);
                    CityGeometry.AssignCollider(host, mesh);
                    MarkStatic(host);
                    written++;
                }

                var record = go.GetComponent<FeatureRecord>() ?? go.AddComponent<FeatureRecord>();
                record.featureId = feature.Id;
                record.featureKind = "Street";
                record.geometryType = feature.GeometryType;
                record.displayName = DisplayName(feature);
                record.propertiesJson = feature.PropertiesJson;
                var street = go.GetComponent<StreetFeature>() ?? go.AddComponent<StreetFeature>();
                street.highway = highway ?? "";
                street.streetType = streetType ?? "";
                street.widthDetail = widthDetail;
                street.resolvedWidthMeters = resolved;
                street.useWidthOverride = widthOverride;
                street.widthOverrideMeters = width;
                street.appliedWidthMeters = width;
                street.surfaceY = lift;
                street.roadMaterial = materials.Asphalt;
                street.uvMeters = settings.asphaltUvMeters;
                street.sourceParts = new List<RoadPart>();
                for (int p = 0; p < worldParts.Count; p++)
                {
                    var local = new List<Vector2>(worldParts[p].Count);
                    for (int v = 0; v < worldParts[p].Count; v++)
                        local.Add(worldParts[p][v] - origin);
                    street.sourceParts.Add(new RoadPart { centerline = local });
                }

                MarkStatic(go);
                report.GeneratedStreets++;
                if (written == 0)
                    report.StreetsFullyClipped++;
                string reason = widthDetail + "; width " + width.ToString("0.##") + " m; surface Y " + lift.ToString("0.###") + "; components " + written + "; " + RelationText(relation);
                if (!string.IsNullOrEmpty(clipNote))
                    reason += "; " + clipNote;
                reason += Extras(feature);
                report.Add("Street", feature.Id, "Generated", reason);
            }
        }

        static void BuildEnvironment(CityGenerationSettings settings, CityMaterials materials, Transform environment, Dictionary<string, GameObject> existing, HashSet<string> used, Vector3 spawn, float yaw, bool spawned, Scene scene)
        {
            var sunObject = Take(existing, used, "sun", "Sun", environment);
            var sun = sunObject.GetComponent<Light>();
            if (sun == null)
            {
                if (sunObject.GetComponent<UniversalAdditionalLightData>() == null)
                    sunObject.AddComponent<UniversalAdditionalLightData>();
                sun = sunObject.GetComponent<Light>();
            }

            if (sun == null)
                sun = sunObject.AddComponent<Light>();
            if (sun == null)
                throw new InvalidOperationException("Unity did not attach a Light to the Sun object.");
            ConfigureDirectionalSun(sun);
            if (sun.type != LightType.Directional)
                throw new InvalidOperationException("The sun light stayed " + sun.type + " instead of directional.");
            sunObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            sunObject.transform.position = spawn + new Vector3(0f, 80f, 0f);

            RenderSettings.skybox = materials.Skybox;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.05f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = false;
            try
            {
                DynamicGI.UpdateEnvironment();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Sky environment update was skipped: " + exception.Message);
            }

            string profilePath = "Assets/NazarethPlusCity/Settings/DaylightVolumeProfile.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath) != null)
                AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.Neutral);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.05f);
            color.contrast.Override(6f);
            color.saturation.Override(4f);
            color.colorFilter.Override(Color.white);
            var balance = profile.Add<WhiteBalance>(true);
            balance.temperature.Override(0f);
            balance.tint.Override(0f);
            EditorUtility.SetDirty(profile);

            var volumeObject = Take(existing, used, "daylight-volume", "DaylightVolume", environment);
            var volume = volumeObject.GetComponent<Volume>() ?? volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.weight = 1f;
            volume.sharedProfile = profile;

            var spawnObject = Take(existing, used, "spawn", "SpawnMarker", environment);
            spawnObject.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0f, yaw, 0f));
            var spawnPoint = spawnObject.GetComponent<CitySpawnPoint>() ?? spawnObject.AddComponent<CitySpawnPoint>();
            spawnPoint.yaw = yaw;

            var player = Take(existing, used, "player", "CityWalker", environment);
            player.tag = "Player";
            player.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0f, yaw, 0f));
            var controller = AddIfMissing<CharacterController>(player);
            ConfigureController(controller, settings);
            var walker = player.GetComponent<FirstPersonWalker>() ?? player.AddComponent<FirstPersonWalker>();
            walker.actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/NazarethPlusCity/Input/NazarethPlusCity.inputactions");
            walker.walkSpeed = settings.walkSpeed;
            walker.sprintSpeed = settings.sprintSpeed;
            walker.jumpHeight = settings.jumpHeight;
            walker.gravity = settings.gravity;
            walker.lookSensitivity = settings.lookSensitivity;
            walker.eyeHeight = settings.eyeHeight;
            walker.capsuleRadius = settings.playerRadius;
            walker.safetyMargin = settings.containmentSafetyMargin;
            walker.fallResetY = settings.fallResetY;
            walker.spawnPosition = spawn;
            walker.spawnYaw = yaw;

            var eye = player.transform.Find("Eye");
            if (eye == null)
            {
                var eyeObject = new GameObject("Eye");
                eyeObject.transform.SetParent(player.transform, false);
                eye = eyeObject.transform;
            }

            eye.localPosition = new Vector3(0f, settings.eyeHeight, 0f);
            eye.localRotation = Quaternion.identity;
            var camera = AddIfMissing<Camera>(eye.gameObject);
            ConfigureWalkerCamera(camera);
            if (eye.GetComponent<AudioListener>() == null)
                eye.gameObject.AddComponent<AudioListener>();
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.renderShadows = true;
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            walker.eye = eye;

            var canvas = BuildHud(player.transform);
            walker.pausePanel = canvas.transform.Find("PausePanel").gameObject;
            walker.hintPanel = canvas.transform.Find("Hint").gameObject;
            walker.pausePanel.SetActive(false);
            walker.hintPanel.SetActive(true);

            var events = Take(existing, used, "event-system", "EventSystem", environment);
            if (events.GetComponent<EventSystem>() == null)
                events.AddComponent<EventSystem>();
            if (events.GetComponent<InputSystemUIInputModule>() == null)
                events.AddComponent<InputSystemUIInputModule>();

            try
            {
                var lighting = new LightingSettings { name = "NazarethPlusCityLighting" };
                lighting.bakedGI = false;
                Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;
                Lightmapping.SetLightingSettingsForScene(scene, lighting);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not store lighting settings on the scene: " + exception.Message);
            }

            if (walker.actions == null)
                throw new FileNotFoundException("NazarethPlusCity input actions did not import.");
            if (!spawned)
                Debug.LogWarning("No fully clear street spawn was found. The walker was still placed at the best tested point.");
        }

        static Transform BuildHud(Transform player)
        {
            var existing = player.Find("WalkHud");
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var canvasObject = new GameObject("WalkHud");
            canvasObject.transform.SetParent(player, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();

            var pause = new GameObject("PausePanel");
            pause.transform.SetParent(canvasObject.transform, false);
            var pauseRect = pause.AddComponent<RectTransform>();
            pauseRect.anchorMin = Vector2.zero;
            pauseRect.anchorMax = Vector2.one;
            pauseRect.offsetMin = Vector2.zero;
            pauseRect.offsetMax = Vector2.zero;
            var dim = pause.AddComponent<Image>();
            dim.color = new Color(0.05f, 0.06f, 0.07f, 0.45f);
            var pauseTextObject = new GameObject("Text");
            pauseTextObject.transform.SetParent(pause.transform, false);
            var pauseTextRect = pauseTextObject.AddComponent<RectTransform>();
            pauseTextRect.anchorMin = new Vector2(0.5f, 0.5f);
            pauseTextRect.anchorMax = new Vector2(0.5f, 0.5f);
            pauseTextRect.sizeDelta = new Vector2(800f, 240f);
            var pauseText = pauseTextObject.AddComponent<Text>();
            pauseText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            pauseText.fontSize = 32;
            pauseText.alignment = TextAnchor.MiddleCenter;
            pauseText.color = Color.white;
            pauseText.text = "Paused\nEsc resume\nR return to the street spawn";

            var hint = new GameObject("Hint");
            hint.transform.SetParent(canvasObject.transform, false);
            var hintRect = hint.AddComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0.5f, 0f);
            hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 28f);
            hintRect.sizeDelta = new Vector2(1400f, 40f);
            var hintText = hint.AddComponent<Text>();
            hintText.font = pauseText.font;
            hintText.fontSize = 20;
            hintText.alignment = TextAnchor.MiddleCenter;
            hintText.color = new Color(1f, 0.98f, 0.94f, 0.92f);
            hintText.text = "WASD walk    Mouse look    Shift sprint    Space jump    Esc pause    R return to street";
            hint.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.75f);
            return canvasObject.transform;
        }

        static bool TrySpawn(CityGenerationSettings settings, PlayableRegion region, out Vector3 spawn, out float yaw, out string note)
        {
            spawn = Vector3.zero;
            yaw = 0f;
            var streets = UnityEngine.Object.FindObjectsByType<StreetFeature>(FindObjectsSortMode.None);
            Vector2 center = Vector2.zero;
            var map = UnityEngine.Object.FindAnyObjectByType<CityBoundaryMap>();
            if (map != null && map.Region.Outers.Count > 0)
            {
                var outer = map.Region.Outers[0];
                for (int i = 0; i < outer.Length; i++)
                    center += outer[i];
                center /= outer.Length;
            }

            var ordered = new List<StreetFeature>(streets);
            ordered.Sort((a, b) => SpawnDistance(a, center).CompareTo(SpawnDistance(b, center)));
            if (TryStreet(ordered, region, settings, center, true, out spawn, out yaw))
            {
                note = "Spawn " + spawn.ToString("0.0") + " yaw " + yaw.ToString("0") + " on a clear street inside the boundary.";
                return true;
            }

            if (TryStreet(ordered, region, settings, center, false, out spawn, out yaw))
            {
                note = "Spawn " + spawn.ToString("0.0") + " yaw " + yaw.ToString("0") + " on a narrow path inside the boundary.";
                return true;
            }

            if (TryGrid(region, settings, center, out spawn))
            {
                yaw = 0f;
                note = "Street samples were blocked. Spawn " + spawn.ToString("0.0") + " is an open ground point inside the boundary.";
                return true;
            }

            spawn = new Vector3(center.x, 0.1f, center.y);
            note = "No validated spawn was found. Walker left at the boundary centroid, which may be obstructed.";
            return false;
        }

        static bool TryStreet(List<StreetFeature> streets, PlayableRegion region, CityGenerationSettings settings, Vector2 center, bool preferRoads, out Vector3 spawn, out float yaw)
        {
            spawn = Vector3.zero;
            yaw = 0f;
            for (int s = 0; s < streets.Count; s++)
            {
                var street = streets[s];
                if (preferRoads && IsNarrow(street.highway))
                    continue;
                if (street.sourceParts == null)
                    continue;
                for (int p = 0; p < street.sourceParts.Count; p++)
                {
                    var line = street.sourceParts[p].centerline;
                    for (int i = 0; i < line.Count - 1; i++)
                    {
                        Vector3 worldA = street.transform.TransformPoint(new Vector3(line[i].x, 0f, line[i].y));
                        Vector3 worldB = street.transform.TransformPoint(new Vector3(line[i + 1].x, 0f, line[i + 1].y));
                        float length = Vector2.Distance(new Vector2(worldA.x, worldA.z), new Vector2(worldB.x, worldB.z));
                        int steps = Mathf.Max(1, Mathf.FloorToInt(length / 12f));
                        for (int step = 0; step <= steps; step++)
                        {
                            float t = steps == 0 ? 0.5f : (step + 0.5f) / (steps + 1f);
                            Vector3 point = Vector3.Lerp(worldA, worldB, t);
                            if (!AcceptSpawn(region, settings, new Vector2(point.x, point.z), out float groundY))
                                continue;
                            spawn = new Vector3(point.x, groundY, point.z);
                            Vector3 forward = worldB - worldA;
                            forward.y = 0f;
                            yaw = forward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(forward.normalized, Vector3.up).eulerAngles.y : 0f;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        static bool TryGrid(PlayableRegion region, CityGenerationSettings settings, Vector2 center, out Vector3 spawn)
        {
            spawn = Vector3.zero;
            for (int ring = 1; ring <= 40; ring++)
            {
                float radius = ring * 8f;
                for (int step = 0; step < 16; step++)
                {
                    float angle = step / 16f * Mathf.PI * 2f;
                    var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    if (!AcceptSpawn(region, settings, point, out float groundY))
                        continue;
                    spawn = new Vector3(point.x, groundY, point.y);
                    return true;
                }
            }

            return false;
        }

        static bool AcceptSpawn(PlayableRegion region, CityGenerationSettings settings, Vector2 point, out float groundY)
        {
            groundY = 0f;
            if (!region.Contains(point))
                return false;
            if (region.DistanceToBoundary(point) < settings.playerRadius + settings.containmentSafetyMargin + 0.8f)
                return false;
            var origin = new Vector3(point.x, 4f, point.y);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.point.y > 0.45f || hit.point.y < -0.2f || hit.normal.y < 0.45f)
                return false;
            Vector3 chest = new Vector3(point.x, hit.point.y + 1.15f, point.y);
            var overlaps = Physics.OverlapSphere(chest, settings.playerRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlaps.Length; i++)
            {
                if (overlaps[i].bounds.max.y > hit.point.y + 0.5f)
                    return false;
            }

            groundY = hit.point.y;
            return true;
        }

        static float SpawnDistance(StreetFeature street, Vector2 center)
        {
            if (street.sourceParts == null || street.sourceParts.Count == 0 || street.sourceParts[0].centerline.Count == 0)
                return 999999f;
            Vector3 point = street.transform.TransformPoint(new Vector3(street.sourceParts[0].centerline[0].x, 0f, street.sourceParts[0].centerline[0].y));
            return Vector2.Distance(center, new Vector2(point.x, point.z));
        }

        static bool IsNarrow(string highway)
        {
            switch ((highway ?? "").ToLowerInvariant())
            {
                case "footway":
                case "path":
                case "steps":
                case "pedestrian":
                case "cycleway":
                case "track":
                case "construction":
                    return true;
                default:
                    return false;
            }
        }

        static T AddIfMissing<T>(GameObject host) where T : Component
        {
            var component = host.GetComponent<T>();
            if (component == null)
                component = host.AddComponent<T>();
            if (component == null)
                throw new InvalidOperationException("Could not add " + typeof(T).Name + " to " + host.name + ".");
            return component;
        }

        static void ConfigureController(CharacterController controller, CityGenerationSettings settings)
        {
            var serialized = new SerializedObject(controller);
            SetFloat(serialized, "m_Height", settings.playerHeight);
            SetFloat(serialized, "m_Radius", settings.playerRadius);
            SetFloat(serialized, "m_SlopeLimit", 50f);
            SetFloat(serialized, "m_StepOffset", 0.35f);
            SetFloat(serialized, "m_SkinWidth", 0.04f);
            SetFloat(serialized, "m_MinMoveDistance", 0.001f);
            SetBool(serialized, "m_EnableOverlapRecovery", true);
            var center = serialized.FindProperty("m_Center");
            if (center != null)
                center.vector3Value = new Vector3(0f, settings.playerHeight * 0.5f, 0f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureWalkerCamera(Camera camera)
        {
            var serialized = new SerializedObject(camera);
            SetInt(serialized, "m_ClearFlags", (int)CameraClearFlags.Skybox);
            SetFloat(serialized, "m_NearClip", 0.08f);
            SetFloat(serialized, "m_FarClip", 4500f);
            SetFloat(serialized, "m_FieldOfView", 72f);
            SetBool(serialized, "m_HDR", true);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureDirectionalSun(Light sun)
        {
            var serialized = new SerializedObject(sun);
            SetInt(serialized, "m_Type", (int)LightType.Directional);
            SetBool(serialized, "m_UseColorTemperature", true);
            SetFloat(serialized, "m_ColorTemperature", 6200f);
            SetFloat(serialized, "m_Intensity", 1.3f);
            var color = serialized.FindProperty("m_Color");
            if (color != null)
                color.colorValue = Color.white;
            var shadows = serialized.FindProperty("m_Shadows");
            if (shadows != null)
            {
                var shadowType = shadows.FindPropertyRelative("m_Type");
                if (shadowType != null)
                    shadowType.intValue = (int)LightShadows.Soft;
                var strength = shadows.FindPropertyRelative("m_Strength");
                if (strength != null)
                    strength.floatValue = 0.85f;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetInt(SerializedObject serialized, string name, int value)
        {
            var property = serialized.FindProperty(name);
            if (property != null)
                property.intValue = value;
        }

        static void SetFloat(SerializedObject serialized, string name, float value)
        {
            var property = serialized.FindProperty(name);
            if (property != null)
                property.floatValue = value;
        }

        static void SetBool(SerializedObject serialized, string name, bool value)
        {
            var property = serialized.FindProperty(name);
            if (property != null)
                property.boolValue = value;
        }

        static void ProbeMapBorders(CityReport report, CityGenerationSettings settings, Vector3 spawn)
        {
            var walker = UnityEngine.Object.FindAnyObjectByType<FirstPersonWalker>();
            var map = UnityEngine.Object.FindAnyObjectByType<CityBoundaryMap>();
            var boundaryObject = GameObject.Find("CityBoundary");
            var border = boundaryObject != null ? boundaryObject.GetComponent<Collider>() : null;
            if (walker == null || map == null || border == null)
            {
                report.ValidationFailures.Add("Border probe could not find the walker, saved boundary, or border collider.");
                return;
            }

            var controller = walker.GetComponent<CharacterController>();
            var region = map.Region;
            try
            {
                if (region.Outers.Count == 0)
                {
                    report.ValidationFailures.Add("Border probe has no outer city ring.");
                    return;
                }

                ProbeRing(report, settings, walker, controller, map, border, region, region.Outers[0], false);
                if (region.Holes.Count == 0)
                    report.ValidationFailures.Add("City boundary has no interior hole to test.");
                else
                    ProbeRing(report, settings, walker, controller, map, border, region, region.Holes[0], true);

                walker.ResetToSpawn();
                walker.RememberSafeHere();
                Vector3 safe = walker.transform.position;
                controller.enabled = false;
                walker.transform.position = new Vector3(safe.x, settings.fallResetY - 3f, safe.z);
                Physics.SyncTransforms();
                controller.enabled = true;
                Vector3 afterFall = walker.ApplyContainment();
                bool fallRecovered = Vector3.Distance(new Vector3(afterFall.x, 0f, afterFall.z), new Vector3(safe.x, 0f, safe.z)) < 1.5f && afterFall.y > settings.fallResetY;
                report.Notes.Add(fallRecovered
                    ? "Fall recovery returned the walker from Y " + (settings.fallResetY - 3f).ToString("0.0") + " to " + afterFall.ToString("0.0") + "."
                    : "Fall recovery left the walker at " + afterFall.ToString("0.0") + ".");
                if (!fallRecovered)
                    report.ValidationFailures.Add("Falling below the map did not recover to the last safe position.");

                if (TryOutsidePoint(region, region.Outers[0], out Vector2 outside))
                {
                    walker.ResetToSpawn();
                    walker.RememberSafeHere();
                    safe = walker.transform.position;
                    controller.enabled = false;
                    walker.transform.position = new Vector3(outside.x, 0.2f, outside.y);
                    controller.enabled = true;
                    Vector3 afterOutside = walker.ApplyContainment();
                    bool outsideRecovered = Vector3.Distance(new Vector3(afterOutside.x, 0f, afterOutside.z), new Vector3(safe.x, 0f, safe.z)) < 1.5f && map.Contains(new Vector2(afterOutside.x, afterOutside.z));
                    report.Notes.Add(outsideRecovered
                        ? "Containment recovery returned the walker from outside point " + outside.ToString("0.0") + " to " + afterOutside.ToString("0.0") + "."
                        : "Containment recovery left the walker at " + afterOutside.ToString("0.0") + " after an outside placement.");
                    if (!outsideRecovered)
                        report.ValidationFailures.Add("Crossing outside the boundary did not recover to the last safe position.");
                }
                else
                    report.ValidationFailures.Add("Could not place a test point outside the city boundary.");
            }
            catch (Exception exception)
            {
                report.ValidationFailures.Add("Border probe failed: " + exception.Message);
            }
            finally
            {
                if (walker != null)
                    walker.ResetToSpawn();
                Physics.SyncTransforms();
            }
        }

        static void ProbeRing(CityReport report, CityGenerationSettings settings, FirstPersonWalker walker, CharacterController controller, CityBoundaryMap map, Collider border, PlayableRegion region, Vector2[] ring, bool hole)
        {
            bool straight = TryBorderSample(region, ring, hole ? "hole-edge" : "outer-straight", false, border, out Vector2 inside, out Vector2 outward, out string detail, out float standing, out float jumping);
            RecordBorderSample(report, settings, walker, controller, map, hole ? "hole-edge" : "outer-straight", straight, inside, outward, detail, standing, jumping);
            if (hole)
                return;
            bool concave = TryBorderSample(region, ring, "outer-concave", true, border, out inside, out outward, out detail, out standing, out jumping);
            RecordBorderSample(report, settings, walker, controller, map, "outer-concave", concave, inside, outward, detail, standing, jumping);
        }

        static void RecordBorderSample(CityReport report, CityGenerationSettings settings, FirstPersonWalker walker, CharacterController controller, CityBoundaryMap map, string label, bool found, Vector2 inside, Vector2 outward, string detail, float standing, float jumping)
        {
            if (!found)
            {
                report.ValidationFailures.Add("No clear " + label + " sample whose capsule hits the real border.");
                return;
            }

            bool sweeps = standing > 0.05f && standing < 4f && jumping > 0.05f && jumping < 4.5f && Mathf.Abs(standing - jumping) < 0.75f;
            string motion = DriveController(settings, walker, controller, map, inside, outward, false);
            string jumpMotion = DriveController(settings, walker, controller, map, inside, outward, true);
            report.Notes.Add(label + ": " + detail + "; standing sweep " + standing.ToString("0.00") + " m; jump-height sweep " + jumping.ToString("0.00") + " m; sprint " + motion + "; jump " + jumpMotion + ".");
            if (!sweeps)
                report.ValidationFailures.Add(label + " capsule sweep did not stop on the border (standing " + standing.ToString("0.00") + " m, jump " + jumping.ToString("0.00") + " m).");
            if (motion.Contains("LEFT") || jumpMotion.Contains("LEFT"))
                report.ValidationFailures.Add(label + " let the walker leave the playable area.");
        }

        static string DriveController(CityGenerationSettings settings, FirstPersonWalker walker, CharacterController controller, CityBoundaryMap map, Vector2 inside, Vector2 outward, bool jump)
        {
            float feet = GroundY(inside);
            controller.enabled = false;
            walker.transform.position = new Vector3(inside.x, feet, inside.y);
            controller.enabled = true;
            Physics.SyncTransforms();
            Vector3 direction = new Vector3(outward.x, 0f, outward.y);
            Vector3 origin = walker.transform.position;
            float vertical = jump ? Mathf.Sqrt(settings.jumpHeight * -2f * settings.gravity) : -2f;
            const float step = 0.05f;
            int steps = jump ? 36 : 20;
            float traveled = 0f;
            for (int i = 0; i < steps; i++)
            {
                if (!jump && controller.isGrounded)
                    vertical = -2f;
                else
                    vertical += settings.gravity * step;
                Vector3 before = walker.transform.position;
                controller.Move((direction * settings.sprintSpeed + Vector3.up * vertical) * step);
                traveled += Vector3.Distance(before, walker.transform.position);
            }

            Vector3 end = walker.transform.position;
            float outwardMeters = Vector2.Dot(new Vector2(end.x - origin.x, end.z - origin.z), outward);
            bool insideMap = map.Contains(new Vector2(end.x, end.z));
            string state = insideMap && outwardMeters < 2.4f ? "blocked" : "LEFT";
            if (traveled < 0.02f)
                state = insideMap ? "unmoved" : "LEFT";
            return state + " outward " + outwardMeters.ToString("0.00") + " m travel " + traveled.ToString("0.00") + " m";
        }

        static bool TryBorderSample(PlayableRegion region, Vector2[] ring, string label, bool concave, Collider border, out Vector2 inside, out Vector2 outward, out string detail, out float standing, out float jumping)
        {
            inside = Vector2.zero;
            outward = Vector2.up;
            detail = label;
            standing = -1f;
            jumping = -1f;
            bool relaxed = label == "hole-edge";
            float minLength = relaxed ? 3.5f : 8f;
            float alignment = relaxed ? 0.8f : 0.96f;
            var candidates = new List<BorderCandidate>();
            int count = ring.Length;
            float area = PlayableRegion.Area(ring);
            for (int i = 0; i < count; i++)
            {
                Vector2 previous = ring[(i - 1 + count) % count];
                Vector2 a = ring[i];
                Vector2 b = ring[(i + 1) % count];
                Vector2 next = ring[(i + 2) % count];
                if (!concave)
                {
                    Vector2 edge = b - a;
                    float length = edge.magnitude;
                    if (length < minLength)
                        continue;
                    Vector2 dir = edge / length;
                    Vector2 previousDir = a - previous;
                    Vector2 nextDir = next - b;
                    if (previousDir.magnitude < 0.2f || nextDir.magnitude < 0.2f)
                        continue;
                    if (Vector2.Dot(dir, previousDir.normalized) < alignment || Vector2.Dot(dir, nextDir.normalized) < alignment)
                        continue;
                    Vector2 left = new Vector2(-dir.y, dir.x);
                    Vector2 mid = (a + b) * 0.5f;
                    Vector2 inward = region.Contains(mid + left * 0.5f) ? left : -left;
                    candidates.Add(new BorderCandidate { score = length, origin = mid, inward = inward, outward = -inward, detail = "edge " + length.ToString("0.0") + " m at " + mid.ToString("0") });
                }
                else
                {
                    Vector2 into = a - previous;
                    Vector2 away = b - a;
                    if (into.magnitude < 1f || away.magnitude < 1f)
                        continue;
                    into.Normalize();
                    away.Normalize();
                    float turn = PlayableRegion.Cross(into, away);
                    bool reflex = area >= 0f ? turn < -0.35f : turn > 0.35f;
                    if (!reflex)
                        continue;
                    Vector2 n1 = RightNormal(into, region, previous, a);
                    Vector2 n2 = RightNormal(away, region, a, b);
                    Vector2 bisector = n1 + n2;
                    if (bisector.sqrMagnitude < 0.04f)
                        continue;
                    bisector.Normalize();
                    Vector2 exit = bisector;
                    if (region.Contains(a + bisector * 1.2f) && !region.Contains(a - bisector * 1.2f))
                        exit = -bisector;
                    else if (!(region.Contains(a - bisector * 1.2f) && !region.Contains(a + bisector * 1.2f)))
                        continue;
                    candidates.Add(new BorderCandidate { score = Mathf.Abs(turn), origin = a, inward = -exit, outward = exit, detail = "reflex turn " + turn.ToString("0.00") + " at " + a.ToString("0") });
                }
            }

            candidates.Sort((x, y) => y.score.CompareTo(x.score));
            int limit = Mathf.Min(candidates.Count, 24);
            for (int i = 0; i < limit; i++)
            {
                BorderCandidate candidate = candidates[i];
                if (!TryInset(region, border, candidate.origin, candidate.inward, candidate.outward, out Vector2 sample, out float stand, out float jumpHit))
                    continue;
                inside = sample;
                outward = candidate.outward;
                detail = candidate.detail;
                standing = stand;
                jumping = jumpHit;
                return true;
            }

            return false;
        }

        struct BorderCandidate
        {
            public float score;
            public Vector2 origin;
            public Vector2 inward;
            public Vector2 outward;
            public string detail;
        }

        static Vector2 RightNormal(Vector2 direction, PlayableRegion region, Vector2 a, Vector2 b)
        {
            Vector2 left = new Vector2(-direction.y, direction.x);
            Vector2 mid = (a + b) * 0.5f;
            return region.Contains(mid + left * 0.4f) ? -left : left;
        }

        static bool TryInset(PlayableRegion region, Collider border, Vector2 origin, Vector2 inward, Vector2 outward, out Vector2 inside, out float standing, out float jumping)
        {
            inside = origin;
            standing = -1f;
            jumping = -1f;
            for (float dist = 1.15f; dist <= 16f; dist += 0.8f)
            {
                Vector2 sample = origin + inward * dist;
                if (!region.IsCapsuleInside(sample, 0.3f, 0.08f))
                    continue;
                float feet = GroundY(sample);
                if (!ClearOfSolids(sample, feet, border))
                    continue;
                if (!SweepBorder(border, sample, outward, feet, 0f, out float stand, out bool borderHit) || !borderHit)
                    continue;
                if (stand > dist + 0.9f)
                    continue;
                if (!SweepBorder(border, sample, outward, feet, 1.2f, out float jumpHit, out bool jumpBorder) || !jumpBorder)
                    continue;
                inside = sample;
                standing = stand;
                jumping = jumpHit;
                return true;
            }

            return false;
        }

        static bool TryOutsidePoint(PlayableRegion region, Vector2[] ring, out Vector2 outside)
        {
            outside = Vector2.zero;
            for (int i = 0; i < ring.Length; i++)
            {
                Vector2 a = ring[i];
                Vector2 b = ring[(i + 1) % ring.Length];
                Vector2 edge = b - a;
                if (edge.magnitude < 8f)
                    continue;
                Vector2 dir = edge.normalized;
                Vector2 left = new Vector2(-dir.y, dir.x);
                Vector2 mid = (a + b) * 0.5f;
                Vector2 outward = region.Contains(mid + left * 0.5f) ? -left : left;
                Vector2 sample = mid + outward * 12f;
                if (!region.Contains(sample))
                {
                    outside = sample;
                    return true;
                }
            }

            return false;
        }

        static float GroundY(Vector2 point)
        {
            if (Physics.Raycast(new Vector3(point.x, 40f, point.y), Vector3.down, out RaycastHit hit, 80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return 0f;
        }

        static bool ClearOfSolids(Vector2 point, float feet, Collider border)
        {
            var hits = Physics.OverlapCapsule(new Vector3(point.x, feet + 0.4f, point.y), new Vector3(point.x, feet + 1.55f, point.y), 0.34f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == border || hits[i].transform.IsChildOf(border.transform))
                    return false;
                if (hits[i].bounds.max.y < feet + 0.55f)
                    continue;
                return false;
            }

            return true;
        }

        static bool SweepBorder(Collider border, Vector2 point, Vector2 outward, float feet, float raise, out float distance, out bool borderHit)
        {
            distance = -1f;
            borderHit = false;
            Vector3 direction = new Vector3(outward.x, 0f, outward.y);
            Vector3 bottom = new Vector3(point.x, feet + 0.25f + raise, point.y);
            Vector3 top = new Vector3(point.x, feet + 1.6f + raise, point.y);
            if (!Physics.CapsuleCast(bottom, top, 0.28f, direction, out RaycastHit hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            distance = hit.distance;
            borderHit = hit.collider == border || hit.collider.transform.IsChildOf(border.transform);
            return true;
        }

        static void ValidateScale(CityReport report, GeoProjection projection, List<GeoLonLat> ring)
        {
            int end = Mathf.Min(ring.Count - 2, 160);
            if (end < 1)
                return;
            var a = ring[0];
            var b = ring[end];
            double haversine = GeoProjection.HaversineMeters(a.Longitude, a.Latitude, b.Longitude, b.Latitude);
            Vector3 pa = projection.Project(a.Longitude, a.Latitude);
            Vector3 pb = projection.Project(b.Longitude, b.Latitude);
            float local = Vector2.Distance(new Vector2(pa.x, pa.z), new Vector2(pb.x, pb.z));
            report.ScaleRatio = haversine > 1.0 ? local / haversine : 0.0;
            Vector3 north = projection.Project(projection.OriginLongitude, projection.OriginLatitude + 0.001);
            Vector3 east = projection.Project(projection.OriginLongitude + 0.001, projection.OriginLatitude);
            report.NorthIsPositiveZ = north.z > 1.0;
            report.EastIsPositiveX = east.x > 1.0;
            report.Notes.Add("Scale check: projected " + local.ToString("0.0") + " m versus haversine " + haversine.ToString("0.0") + " m (ratio " + report.ScaleRatio.ToString("0.0000") + ").");
            report.Notes.Add(CityCoordinateFrame.AxisSummary + " North sample Z=" + north.z.ToString("0.0") + " m, east sample X=" + east.x.ToString("0.0") + " m for a 0.001 degree offset.");
        }

        static void Validate(CityReport report, CityGenerationSettings settings, bool spawned, Vector3 spawn)
        {
            int buildings = UnityEngine.Object.FindObjectsByType<BuildingFeature>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int streets = UnityEngine.Object.FindObjectsByType<StreetFeature>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            if (buildings != report.GeneratedBuildings)
                report.ValidationFailures.Add("Building objects " + buildings + " do not match generated count " + report.GeneratedBuildings + ".");
            if (streets != report.GeneratedStreets)
                report.ValidationFailures.Add("Street objects " + streets + " do not match generated count " + report.GeneratedStreets + ".");
            if (report.SourceBuildings != report.GeneratedBuildings + report.SkippedBuildings)
                report.ValidationFailures.Add("Building source count is not generated plus skipped.");
            if (report.SourceStreets != report.GeneratedStreets + report.SkippedStreets)
                report.ValidationFailures.Add("Street source count is not generated plus skipped.");
            if (report.ScaleRatio < 0.995 || report.ScaleRatio > 1.005)
                report.ValidationFailures.Add("Projected scale ratio " + report.ScaleRatio.ToString("0.0000") + " is outside 0.5% of haversine.");
            if (!report.NorthIsPositiveZ || !report.EastIsPositiveX)
                report.ValidationFailures.Add("Axis orientation failed: +Z must be north and +X must be east.");
            var ground = GameObject.Find("CityGround");
            var groundMesh = ground != null ? ground.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (groundMesh == null || groundMesh.vertexCount < 20 || ground.GetComponent<MeshCollider>() == null)
                report.ValidationFailures.Add("City ground mesh or collider is missing.");
            var boundary = GameObject.Find("CityBoundary");
            var wall = boundary != null ? boundary.GetComponent<MeshCollider>()?.sharedMesh : null;
            if (wall == null || wall.vertexCount < 12)
                report.ValidationFailures.Add("Boundary blocking collision is missing.");
            if (RenderSettings.skybox == null || RenderSettings.sun == null)
                report.ValidationFailures.Add("Daylight sky or sun is missing.");
            var walker = UnityEngine.Object.FindAnyObjectByType<FirstPersonWalker>();
            if (walker == null || walker.actions == null || walker.GetComponent<CharacterController>() == null)
                report.ValidationFailures.Add("First-person walker is incomplete.");
            var map = UnityEngine.Object.FindAnyObjectByType<CityBoundaryMap>();
            if (map == null || !map.Contains(new Vector2(spawn.x, spawn.z)))
                report.ValidationFailures.Add("Spawn is outside the saved city boundary.");
            int checkedHeights = 0;
            var buildingObjects = UnityEngine.Object.FindObjectsByType<BuildingFeature>(FindObjectsSortMode.None);
            for (int i = 0; i < buildingObjects.Length && checkedHeights < 12; i++)
            {
                var mesh = buildingObjects[i].GetComponentInChildren<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                    continue;
                if (Mathf.Abs(mesh.bounds.max.y - buildingObjects[i].appliedHeightMeters) > 0.2f)
                    report.ValidationFailures.Add(buildingObjects[i].name + " roof Y " + mesh.bounds.max.y.ToString("0.00") + " != height " + buildingObjects[i].appliedHeightMeters.ToString("0.00"));
                checkedHeights++;
            }

            if (checkedHeights == 0)
                report.ValidationFailures.Add("No building meshes were available to check height.");
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null)
                report.ValidationFailures.Add("Scene asset was not saved.");
            bool inBuild = false;
            foreach (var buildScene in EditorBuildSettings.scenes)
            {
                if (buildScene.path == ScenePath && buildScene.enabled)
                    inBuild = true;
            }

            if (!inBuild)
                report.ValidationFailures.Add("NazarethPlusCity is not the enabled build scene.");
            report.Notes.Add(spawned ? "Spawn validation passed." : "Spawn validation did not find a clear point.");
            report.Notes.Add("Checked " + checkedHeights + " building roof heights against their resolved or override height.");
        }

        static void ConfigurePipeline()
        {
            PlayerSettings.productName = "NazarethPlusCity";
            var buildScene = new EditorBuildSettingsScene(ScenePath, true);
            string sceneGuid = AssetDatabase.AssetPathToGUID(ScenePath);
            if (!string.IsNullOrEmpty(sceneGuid))
                buildScene.guid = new GUID(sceneGuid);
            EditorBuildSettings.scenes = new[] { buildScene };
            var start = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (start != null)
                EditorSceneManager.playModeStartScene = start;
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (pipeline == null)
                return;
            var serialized = new SerializedObject(pipeline);
            var shadows = serialized.FindProperty("m_ShadowDistance");
            if (shadows != null)
                shadows.floatValue = 150f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void CaptureViews(Vector3 spawn, float yaw)
        {
            try
            {
                var cameraObject = new GameObject("VerificationCamera");
                var camera = cameraObject.AddComponent<Camera>();
                ConfigureWalkerCamera(camera);
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.renderShadows = true;
                camera.transform.position = spawn + Vector3.up * 1.64f;
                camera.transform.rotation = Quaternion.Euler(5f, yaw, 0f);
                SaveShot(camera, "Assets/NazarethPlusCity/Generated/StreetLevelView.png");
                camera.transform.position = spawn + new Vector3(-40f, 90f, -40f);
                camera.transform.LookAt(spawn + Vector3.up * 8f);
                SaveShot(camera, "Assets/NazarethPlusCity/Generated/OverviewView.png");
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Verification screenshots were not captured: " + exception.Message);
            }
        }

        static void SaveShot(Camera camera, string assetPath)
        {
            var texture = new RenderTexture(1280, 720, 24);
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes(assetPath, image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(image);
            AssetDatabase.ImportAsset(assetPath);
        }

        static void WriteReport(CityReport report)
        {
            var text = new StringBuilder();
            text.AppendLine("NazarethPlusCity import report");
            text.AppendLine("Engine: Unity 6000.1.7f1, Universal Render Pipeline, Windows desktop.");
            text.AppendLine(CityCoordinateFrame.AxisSummary);
            text.AppendLine("GeoJSON is WGS84 with coordinates ordered longitude, latitude. Source altitude is ignored.");
            text.AppendLine("Building models in Data/building_models are not used. Every building is a footprint extrusion.");
            text.AppendLine();
            text.AppendLine("Boundary features: " + report.SourceBoundary);
            text.AppendLine("Buildings: source " + report.SourceBuildings + ", generated " + report.GeneratedBuildings + ", skipped " + report.SkippedBuildings);
            text.AppendLine("  explicit height " + report.ExplicitHeight + ", floor count " + report.FloorHeight + ", fallback 3 floors / 9 m " + report.FallbackHeight);
            text.AppendLine("  inside " + report.BuildingsInside + ", outside " + report.BuildingsOutside + ", crossing " + report.BuildingsCross + ", in hole " + report.BuildingsHole);
            text.AppendLine("Streets: source " + report.SourceStreets + ", generated " + report.GeneratedStreets + ", skipped " + report.SkippedStreets + ", fully clipped " + report.StreetsFullyClipped);
            text.AppendLine("  inside " + report.StreetsInside + ", outside " + report.StreetsOutside + ", crossing " + report.StreetsCross + ", in hole " + report.StreetsHole);
            text.AppendLine();
            foreach (string note in report.Notes)
                text.AppendLine(note);
            text.AppendLine();
            text.AppendLine("Outside features (retained, not moved):");
            AppendIds(text, report.OutsideIds);
            text.AppendLine("Crossing features (retained, not moved):");
            AppendIds(text, report.CrossingIds);
            text.AppendLine("Features inside a boundary hole (retained, not moved):");
            AppendIds(text, report.HoleIds);
            text.AppendLine("Skipped features:");
            if (report.Skipped.Count == 0)
                text.AppendLine("(none)");
            else
                foreach (string line in report.Skipped)
                    text.AppendLine(line);
            text.AppendLine();
            text.AppendLine("Validation:");
            if (report.ValidationFailures.Count == 0)
                text.AppendLine("Structural checks passed.");
            else
                foreach (string failure in report.ValidationFailures)
                    text.AppendLine("FAILED: " + failure);
            text.AppendLine();
            text.AppendLine("Kind\tFeatureId\tStatus\tReason");
            foreach (string line in report.Lines)
                text.AppendLine(line);
            CityMaterialFactory.EnsureFolder("Assets/NazarethPlusCity/Generated");
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportAssetPath.Replace('/', Path.DirectorySeparatorChar)), text.ToString());
            File.WriteAllText(ReportProjectPath, text.ToString());
            AssetDatabase.ImportAsset(ReportAssetPath);
        }

        static void AppendIds(StringBuilder text, List<string> ids)
        {
            if (ids.Count == 0)
            {
                text.AppendLine("(none)");
                return;
            }

            foreach (string id in ids)
                text.AppendLine(id);
        }

        static FootprintRelation ClassifyParts(PlayableRegion region, List<FootprintPart> parts)
        {
            bool anyInside = false, anyOutside = false, anyHole = false, anyCross = false;
            for (int i = 0; i < parts.Count; i++)
            {
                var relation = region.Classify(parts[i].rings[0].points, true, 0.25f);
                if (relation == FootprintRelation.Inside) anyInside = true;
                else if (relation == FootprintRelation.Outside) anyOutside = true;
                else if (relation == FootprintRelation.InHole) anyHole = true;
                else anyCross = true;
            }

            if (anyCross || (anyInside && (anyOutside || anyHole)) || (anyOutside && anyHole))
                return FootprintRelation.Crosses;
            if (anyHole) return FootprintRelation.InHole;
            if (anyOutside) return FootprintRelation.Outside;
            return FootprintRelation.Inside;
        }

        static void CountRelation(CityReport report, FootprintRelation relation, bool building)
        {
            if (building)
            {
                if (relation == FootprintRelation.Inside) report.BuildingsInside++;
                else if (relation == FootprintRelation.Outside) report.BuildingsOutside++;
                else if (relation == FootprintRelation.InHole) report.BuildingsHole++;
                else report.BuildingsCross++;
            }
            else if (relation == FootprintRelation.Inside) report.StreetsInside++;
            else if (relation == FootprintRelation.Outside) report.StreetsOutside++;
            else if (relation == FootprintRelation.InHole) report.StreetsHole++;
            else report.StreetsCross++;
        }

        static void RememberRelation(CityReport report, string id, FootprintRelation relation)
        {
            if (relation == FootprintRelation.Outside) report.OutsideIds.Add(id);
            else if (relation == FootprintRelation.Crosses) report.CrossingIds.Add(id);
            else if (relation == FootprintRelation.InHole) report.HoleIds.Add(id);
        }

        static string RelationText(FootprintRelation relation)
        {
            switch (relation)
            {
                case FootprintRelation.Outside: return "outside city boundary, retained in place";
                case FootprintRelation.Crosses: return "crosses city boundary, retained in place and not moved";
                case FootprintRelation.InHole: return "inside a boundary hole, retained in place";
                default: return "inside city boundary";
            }
        }

        static string Extras(GeoFeature feature)
        {
            var bits = new List<string>();
            string minHeight = GeoJsonFile.Property(feature, "min_height");
            if (MeasureParser.TryParseLength(minHeight, out float meters, out _) && meters > 0.2f)
                bits.Add("min_height ignored so the base stays on Y=0");
            string underground = GeoJsonFile.Property(feature, "underground");
            if (underground == "true" || underground == "yes")
                bits.Add("underground tag ignored");
            string extrude = GeoJsonFile.Property(feature, "extrude");
            if (extrude == "false" || extrude == "no")
                bits.Add("extrude=false ignored");
            if (GeoJsonFile.Property(feature, "bridge") == "yes")
                bits.Add("bridge kept flat on the ground");
            if (GeoJsonFile.Property(feature, "tunnel") == "yes")
                bits.Add("tunnel kept flat on the ground");
            return bits.Count == 0 ? "" : "; " + string.Join("; ", bits);
        }

        static string DisplayName(GeoFeature feature)
        {
            string name = GeoJsonFile.Property(feature, "name");
            if (string.IsNullOrWhiteSpace(name))
                name = GeoJsonFile.Property(feature, "name:en");
            if (string.IsNullOrWhiteSpace(name))
                name = GeoJsonFile.Property(feature, "name:he");
            return name ?? "";
        }

        static bool Same(GeoLonLat a, GeoLonLat b)
        {
            return Math.Abs(a.Longitude - b.Longitude) < 1e-12 && Math.Abs(a.Latitude - b.Latitude) < 1e-12;
        }

        static GameObject Take(Dictionary<string, GameObject> existing, HashSet<string> used, string key, string objectName, Transform parent)
        {
            if (!existing.TryGetValue(key, out GameObject go) || go == null)
            {
                go = new GameObject(objectName);
                var owned = go.AddComponent<GeneratorOwned>();
                owned.key = key;
                existing[key] = go;
            }

            go.name = objectName;
            go.transform.SetParent(parent, false);
            used.Add(key);
            return go;
        }

        static string UniqueKey(HashSet<string> used, string key)
        {
            string unique = key;
            int suffix = 2;
            while (used.Contains(unique))
                unique = key + "#" + suffix++;
            return unique;
        }

        static string UniqueName(HashSet<string> names, string name)
        {
            string unique = name;
            int suffix = 2;
            while (!names.Add(unique))
                unique = name + "_" + suffix++;
            return unique;
        }

        static Transform Folder(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
                return child;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static void ResetMeshes(GameObject go)
        {
            for (int i = go.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            DestroyComponents<MeshCollider>(go);
            DestroyComponents<MeshFilter>(go);
            DestroyComponents<MeshRenderer>(go);
        }

        static void DestroyComponents<T>(GameObject go) where T : Component
        {
            var components = go.GetComponents<T>();
            for (int i = 0; i < components.Length; i++)
                UnityEngine.Object.DestroyImmediate(components[i]);
        }

        static void DestroyStale(Dictionary<string, GameObject> existing, HashSet<string> used)
        {
            foreach (var pair in existing)
            {
                if (!used.Contains(pair.Key) && pair.Value != null)
                    UnityEngine.Object.DestroyImmediate(pair.Value);
            }
        }

        static void MarkStatic(GameObject go)
        {
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        sealed class MeshLibrary
        {
            readonly string _folder;
            Mesh _holder;
            int _count;
            int _index;

            public MeshLibrary(string folder)
            {
                _folder = folder;
            }

            public Mesh Create(string meshName)
            {
                if (_holder == null || _count >= 200)
                {
                    _holder = new Mesh { name = "Chunk_" + _index.ToString("000", CultureInfo.InvariantCulture) };
                    AssetDatabase.CreateAsset(_holder, _folder + "/Chunk_" + _index.ToString("000", CultureInfo.InvariantCulture) + ".asset");
                    _index++;
                    _count = 0;
                }

                var mesh = new Mesh { name = meshName };
                AssetDatabase.AddObjectToAsset(mesh, _holder);
                _count++;
                return mesh;
            }
        }

        sealed class CityReport
        {
            public int SourceBoundary, SourceBuildings, SourceStreets;
            public int GeneratedBuildings, SkippedBuildings, ExplicitHeight, FloorHeight, FallbackHeight;
            public int BuildingsInside, BuildingsOutside, BuildingsCross, BuildingsHole;
            public int GeneratedStreets, SkippedStreets, StreetsFullyClipped;
            public int StreetsInside, StreetsOutside, StreetsCross, StreetsHole;
            public double ScaleRatio = 1;
            public bool NorthIsPositiveZ, EastIsPositiveX;
            public readonly List<string> Lines = new List<string>();
            public readonly List<string> Notes = new List<string>();
            public readonly List<string> Skipped = new List<string>();
            public readonly List<string> OutsideIds = new List<string>();
            public readonly List<string> CrossingIds = new List<string>();
            public readonly List<string> HoleIds = new List<string>();
            public readonly List<string> ValidationFailures = new List<string>();

            public void Add(string kind, string id, string status, string reason)
            {
                Lines.Add(kind + "\t" + id + "\t" + status + "\t" + reason);
            }
        }
    }
}
