using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NazarethPlusCity.EditorTools
{
    public static class BuildingModelSample
    {
        static readonly string[] SampleIds =
        {
            "way/1072682430",
            "way/623457838",
            "relation/14434382",
            "bldg-1793470246111-909x5t"
        };

        public static void VerifyBatch()
        {
            int failures = RunSample(out string text);
            if (failures == 0)
                Debug.Log(text);
            else
                Debug.LogError(text);
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        public static int RunSample(out string text)
        {
            var report = new StringBuilder();
            int failures = 0;
            try
            {
                Verify(report, ref failures);
            }
            catch (Exception exception)
            {
                failures++;
                report.AppendLine("FAILED sample aborted: " + exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            report.Insert(0, failures == 0 ? "Building model sample passed." + Environment.NewLine : "Building model sample failed (" + failures + ")." + Environment.NewLine);
            text = report.ToString();
            string project = Directory.GetParent(Application.dataPath).FullName;
            File.WriteAllText(Path.Combine(project, "NazarethPlusCity_ModelSample.txt"), text);
            return failures;
        }

        public static int VerifyLadders(out string text)
        {
            var report = new StringBuilder();
            int failures = 0;
            try
            {
                var scene = EditorSceneManager.GetActiveScene();
                if (!scene.IsValid() || scene.path != CityImporter.ScenePath)
                    EditorSceneManager.OpenScene(CityImporter.ScenePath, OpenSceneMode.Single);
                var settings = AssetDatabase.LoadAssetAtPath<LadderSystemSettings>("Assets/NazarethPlusCity/Settings/LadderSystemSettings.asset");
                Check(report, ref failures, "ladder settings", settings != null);
                string[] ids = { "bldg-1792530028154-g20nka", "way/623457838", "way/1072682430" };
                var buildings = UnityEngine.Object.FindObjectsByType<BuildingFeature>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                var byId = new Dictionary<string, BuildingFeature>();
                for (int i = 0; i < buildings.Length; i++)
                {
                    var record = buildings[i].GetComponent<FeatureRecord>();
                    if (record != null)
                        byId[record.featureId] = buildings[i];
                }

                Physics.SyncTransforms();
                var eye = new GameObject("LadderAimProbe");
                try
                {
                    for (int i = 0; i < ids.Length; i++)
                    {
                        if (!byId.TryGetValue(ids[i], out BuildingFeature building))
                        {
                            Check(report, ref failures, ids[i] + " present", false);
                            continue;
                        }

                        ProbeLadder(report, ref failures, building, settings, eye.transform);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(eye);
                }
            }
            catch (Exception exception)
            {
                failures++;
                report.AppendLine("FAILED ladder probe: " + exception);
            }

            report.Insert(0, failures == 0 ? "Ladder model probe passed." + Environment.NewLine : "Ladder model probe failed (" + failures + ")." + Environment.NewLine);
            text = report.ToString();
            return failures;
        }

        static void ProbeLadder(StringBuilder report, ref int failures, BuildingFeature building, LadderSystemSettings settings, Transform eye)
        {
            var record = building.GetComponent<FeatureRecord>();
            string id = record != null ? record.featureId : building.name;
            var visual = building.GetComponentInChildren<BuildingModelVisual>(true);
            var modelCollider = visual != null ? visual.GetComponent<MeshCollider>() : null;
            Check(report, ref failures, id + " model collider for ladders", modelCollider != null && modelCollider.enabled);
            if (modelCollider == null || settings == null)
                return;
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            float modelTop = bounds.max.y;
            float extrusionTop = building.transform.position.y + building.meshBaseLocalY + building.ActiveHeight;
            Vector3[] directions = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            bool placed = false;
            string lastReason = "no wall";
            for (int d = 0; d < directions.Length && !placed; d++)
            {
                Vector3 center = new Vector3(bounds.center.x, building.levelBaseY + 1.6f, bounds.center.z);
                var ray = new Ray(center - directions[d] * (bounds.extents.magnitude + 4f), directions[d]);
                if (!modelCollider.Raycast(ray, out RaycastHit wall, bounds.extents.magnitude + 8f))
                    continue;
                Vector3 stand = wall.point + wall.normal * 2.1f;
                stand.y = building.levelBaseY + 1.55f;
                eye.position = stand;
                eye.LookAt(wall.point);
                var placement = LadderTargeting.Evaluate(eye, new Vector3(stand.x, building.levelBaseY, stand.z), settings, null);
                if (!placement.Valid)
                {
                    lastReason = placement.Reason;
                    continue;
                }

                placed = true;
                bool modelWall = placement.Building == building;
                float roofError = Mathf.Abs(placement.RoofY - modelTop);
                bool belowExtrusion = placement.RoofY <= extrusionTop + 0.35f;
                bool onModel = roofError < 1.25f || (placement.RoofY > building.levelBaseY + 1.5f && placement.RoofY <= modelTop + 0.35f);
                report.AppendLine("INFO " + id + " ladder roof " + placement.RoofY.ToString("0.00") + " model top " + modelTop.ToString("0.00") + " extrusion top " + extrusionTop.ToString("0.00") + " landing " + placement.LandingPoint.ToString("0.00"));
                Check(report, ref failures, id + " ladder faces this building", modelWall);
                Check(report, ref failures, id + " ladder roof is on the model", onModel && !(roofError > 2f && Mathf.Abs(placement.RoofY - extrusionTop) < 0.2f && modelTop + 0.5f < extrusionTop));
                Vector3 above = placement.LandingPoint + Vector3.up * 1.5f;
                bool surface = Physics.Raycast(above, Vector3.down, out RaycastHit roofHit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && roofHit.collider == modelCollider
                    && roofHit.normal.y > 0.45f
                    && Mathf.Abs(roofHit.point.y - placement.RoofY) < 0.35f;
                Check(report, ref failures, id + " roof landing rests on the model", surface);
                Matrix4x4 before = visual.transform.localToWorldMatrix;
                var manager = UnityEngine.Object.FindAnyObjectByType<ViewModeManager>();
                if (manager != null)
                {
                    bool first = manager.firstPersonCamera != null && manager.firstPersonCamera.enabled;
                    bool third = manager.thirdPersonCamera != null && manager.thirdPersonCamera.enabled;
                    if (manager.firstPersonCamera != null)
                        manager.firstPersonCamera.enabled = !first;
                    if (manager.thirdPersonCamera != null)
                        manager.thirdPersonCamera.enabled = !third;
                    if (manager.firstPersonCamera != null)
                        manager.firstPersonCamera.enabled = first;
                    if (manager.thirdPersonCamera != null)
                        manager.thirdPersonCamera.enabled = third;
                }

                Check(report, ref failures, id + " ladder target stays put across view cameras", visual.transform.localToWorldMatrix == before);
            }

            Check(report, ref failures, id + " ladder placement accepted (" + lastReason + ")", placed);
        }

        static void Verify(StringBuilder report, ref int failures)
        {
            var scene = EditorSceneManager.OpenScene(CityImporter.ScenePath, OpenSceneMode.Single);
            report.AppendLine("Scene " + scene.path);
            string project = Directory.GetParent(Application.dataPath).FullName;
            var features = GeoJsonFile.ReadFeatures(Path.Combine(project, "Data", "buildings.geojson"), out string error);
            if (!string.IsNullOrEmpty(error))
                throw new InvalidDataException(error);
            var notes = new List<string>();
            var models = BuildingModelLibrary.Load(features, notes);
            var reuseNotes = new List<string>();
            BuildingModelLibrary.Load(features, reuseNotes);
            for (int i = 0; i < notes.Count; i++)
                report.AppendLine(notes[i]);
            report.AppendLine("Second import pass:");
            for (int i = 0; i < reuseNotes.Count; i++)
            {
                if (reuseNotes[i].StartsWith("Model match summary", StringComparison.Ordinal) || reuseNotes[i].StartsWith("Derived map", StringComparison.Ordinal) || reuseNotes[i].StartsWith("Explicit map", StringComparison.Ordinal) || reuseNotes[i].StartsWith("Unlisted", StringComparison.Ordinal) || reuseNotes[i].StartsWith("Missing", StringComparison.Ordinal) || reuseNotes[i].StartsWith("Duplicate", StringComparison.Ordinal) || reuseNotes[i].StartsWith("ambiguous", StringComparison.Ordinal))
                    report.AppendLine(reuseNotes[i]);
            }

            Check(report, ref failures, "nine models mapped", models.Count == 9);
            Check(report, ref failures, "second pass reuses imports", Contains(reuseNotes, "reused 9"));
            Check(report, ref failures, "second pass imports nothing new", Contains(reuseNotes, "imported 0"));
            var buildings = UnityEngine.Object.FindObjectsByType<BuildingFeature>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var byId = new Dictionary<string, BuildingFeature>();
            for (int i = 0; i < buildings.Length; i++)
            {
                var record = buildings[i].GetComponent<FeatureRecord>();
                if (record != null && !string.IsNullOrEmpty(record.featureId))
                    byId[record.featureId] = buildings[i];
            }

            report.AppendLine("Scene buildings " + buildings.Length + ".");
            for (int i = 0; i < SampleIds.Length; i++)
            {
                if (!byId.TryGetValue(SampleIds[i], out BuildingFeature building))
                {
                    Check(report, ref failures, SampleIds[i] + " is in the scene", false);
                    continue;
                }

                if (!models.TryGetValue(SampleIds[i], out BuildingModelBinding binding))
                {
                    Check(report, ref failures, SampleIds[i] + " has a model binding", false);
                    continue;
                }

                InspectModel(report, ref failures, building, binding);
            }

            BuildingFeature plain = null;
            foreach (var pair in byId)
            {
                if (!models.ContainsKey(pair.Key))
                {
                    plain = pair.Value;
                    break;
                }
            }

            if (plain == null)
                Check(report, ref failures, "a building without a model exists", false);
            else
                InspectExtrusion(report, ref failures, plain);
            report.AppendLine("Ladder targeting uses the model mesh collider for walls and a roof surface on that model. The wooden ladder GLB in Data/building_models/Assets is the climb prop, not a building.");
            report.AppendLine("View cameras and the shared walker are separate from building objects, so first- and third-person switching does not move model placement or colliders.");
        }

        static void InspectModel(StringBuilder report, ref int failures, BuildingFeature building, BuildingModelBinding binding)
        {
            var record = building.GetComponent<FeatureRecord>();
            string id = record != null ? record.featureId : building.name;
            bool heightOverride = building.useHeightOverride;
            float heightValue = building.heightOverrideMeters;
            bool tintOverride = building.useTintOverride;
            Color tint = building.tintOverride;
            string properties = record != null ? record.propertiesJson : null;
            string resolved = building.heightResolution;
            float resolvedHeight = building.resolvedHeightMeters;
            Transform existing = building.transform.Find("Model");
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            bool attached = BuildingModelLibrary.TryAttach(building.gameObject, binding, building.meshBaseLocalY, out string modelError);
            Check(report, ref failures, id + " model attached", attached);
            if (!attached)
            {
                report.AppendLine("  " + modelError);
                return;
            }

            var visuals = building.GetComponentsInChildren<BuildingModelVisual>(true);
            Check(report, ref failures, id + " has one model child", visuals.Length == 1);
            if (visuals.Length == 0)
                return;
            var visual = visuals[0];
            Check(report, ref failures, id + " keeps feature id", record != null && record.featureId == id && visual.featureId == id);
            Check(report, ref failures, id + " keeps properties", record != null && record.propertiesJson == properties);
            Check(report, ref failures, id + " keeps height override", building.useHeightOverride == heightOverride && Mathf.Abs(building.heightOverrideMeters - heightValue) < 0.001f && building.heightResolution == resolved && Mathf.Abs(building.resolvedHeightMeters - resolvedHeight) < 0.001f);
            Check(report, ref failures, id + " keeps tint override", building.useTintOverride == tintOverride && building.tintOverride == tint);
            Check(report, ref failures, id + " model is parented to the building", visual.transform.parent == building.transform && visual.transform.IsChildOf(building.transform));
            var player = UnityEngine.Object.FindAnyObjectByType<PlayerRoot>();
            Check(report, ref failures, id + " is not parented to the player", player == null || !building.transform.IsChildOf(player.transform));
            float yawError = Quaternion.Angle(visual.transform.localRotation, Quaternion.Euler(binding.Pitch, binding.Yaw, binding.Roll));
            Check(report, ref failures, id + " yaw " + binding.Yaw.ToString("0.#") + " (error " + yawError.ToString("0.00") + " deg)", yawError < 0.2f);
            Vector3 scale = visual.transform.localScale;
            Check(report, ref failures, id + " scale " + scale.ToString("0.00"), Vector3.Distance(scale, binding.Scale) < 0.001f);
            Vector3 local = visual.transform.localPosition;
            Check(report, ref failures, id + " horizontal offset " + local.x.ToString("0.00") + " E, " + local.z.ToString("0.00") + " N", Mathf.Abs(local.x - binding.OffsetEast) < 0.02f && Mathf.Abs(local.z - binding.OffsetNorth) < 0.02f);
            var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            int textured = 0;
            int enabledRenderers = 0;
            bool badMaterial = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].enabled)
                    enabledRenderers++;
                var material = renderers[i].sharedMaterial;
                if (material == null || material.shader == null || material.shader.name.IndexOf("Lit", StringComparison.Ordinal) < 0)
                    badMaterial = true;
                else if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null)
                    textured++;
            }

            Check(report, ref failures, id + " materials are URP Lit (" + renderers.Length + " renderers)", renderers.Length > 0 && !badMaterial && enabledRenderers == renderers.Length);
            bool expectsTexture = binding.SourceFile == "97a598719a7f4a7d89ce3f3021c4e628.glb";
            if (expectsTexture)
                Check(report, ref failures, id + " has textured materials (" + textured + ")", textured > 0);
            else
                report.AppendLine("INFO " + id + " textured materials " + textured + " of " + renderers.Length + ".");
            Check(report, ref failures, id + " extrusion renderers are hidden", ExtrusionRenderersHidden(building));
            Check(report, ref failures, id + " extrusion colliders are off", ExtrusionCollidersOff(building));
            var foundation = building.GetComponentInChildren<BuildingFoundation>(true);
            if (foundation != null)
            {
                var foundationRenderer = foundation.GetComponent<MeshRenderer>();
                var foundationCollider = foundation.GetComponent<MeshCollider>();
                Check(report, ref failures, id + " foundation stays visible and solid", foundationRenderer != null && foundationRenderer.enabled && foundationCollider != null && foundationCollider.enabled);
            }
            else
                report.AppendLine("INFO " + id + " has no foundation object.");

            var collider = visual.GetComponent<MeshCollider>();
            Check(report, ref failures, id + " model collider", collider != null && collider.enabled && !collider.convex && collider.sharedMesh != null && collider.sharedMesh.vertexCount > 100);
            Physics.SyncTransforms();
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            float bottomError = Mathf.Abs(bounds.min.y - building.levelBaseY);
            Check(report, ref failures, id + " ground Y " + bounds.min.y.ToString("0.00") + " vs level base " + building.levelBaseY.ToString("0.00"), bottomError < 0.2f);
            FootprintBounds(building, out Bounds footprint);
            bool overlap = bounds.min.x <= footprint.max.x && bounds.max.x >= footprint.min.x && bounds.min.z <= footprint.max.z && bounds.max.z >= footprint.min.z;
            float centerDistance = Vector2.Distance(new Vector2(bounds.center.x, bounds.center.z), new Vector2(building.transform.position.x, building.transform.position.z));
            report.AppendLine("INFO " + id + " model " + bounds.size.x.ToString("0.0") + " x " + bounds.size.y.ToString("0.0") + " x " + bounds.size.z.ToString("0.0") + " m, footprint " + footprint.size.x.ToString("0.0") + " x " + footprint.size.z.ToString("0.0") + " m, center offset " + centerDistance.ToString("0.0") + " m.");
            Check(report, ref failures, id + " overlaps its footprint", overlap);
            int wallHits = 0;
            int physicsWalls = 0;
            if (collider != null)
            {
                Vector3 center = new Vector3(bounds.center.x, building.levelBaseY + 1.5f, bounds.center.z);
                wallHits += Probe(collider, center, Vector3.right, bounds.extents.x + 3f) ? 1 : 0;
                wallHits += Probe(collider, center, Vector3.left, bounds.extents.x + 3f) ? 1 : 0;
                wallHits += Probe(collider, center, Vector3.forward, bounds.extents.z + 3f) ? 1 : 0;
                wallHits += Probe(collider, center, Vector3.back, bounds.extents.z + 3f) ? 1 : 0;
                physicsWalls += PhysicsProbe(collider, center, Vector3.right, bounds.extents.x + 3f) ? 1 : 0;
                physicsWalls += PhysicsProbe(collider, center, Vector3.left, bounds.extents.x + 3f) ? 1 : 0;
                physicsWalls += PhysicsProbe(collider, center, Vector3.forward, bounds.extents.z + 3f) ? 1 : 0;
                physicsWalls += PhysicsProbe(collider, center, Vector3.back, bounds.extents.z + 3f) ? 1 : 0;
            }

            Check(report, ref failures, id + " wall rays hit the model (" + wallHits + "/4, physics " + physicsWalls + "/4)", wallHits >= 3 && physicsWalls >= 1);
            bool roof = false;
            float roofY = 0f;
            if (collider != null)
            {
                Vector3[] columns =
                {
                    new Vector3(bounds.center.x, bounds.max.y + 4f, bounds.center.z),
                    new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, 0.2f), bounds.max.y + 4f, Mathf.Lerp(bounds.min.z, bounds.max.z, 0.2f)),
                    new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, 0.8f), bounds.max.y + 4f, Mathf.Lerp(bounds.min.z, bounds.max.z, 0.8f))
                };
                for (int i = 0; i < columns.Length; i++)
                {
                    var ray = new Ray(columns[i], Vector3.down);
                    if (!collider.Raycast(ray, out RaycastHit hit, bounds.size.y + 8f))
                        continue;
                    if (hit.point.y > building.levelBaseY + 2f)
                    {
                        roof = true;
                        roofY = hit.point.y;
                        Vector3 start = hit.point + Vector3.up * 0.25f;
                        if (Physics.Raycast(start, Vector3.down, out RaycastHit worldHit, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && worldHit.collider == collider)
                            break;
                    }
                }
            }

            Check(report, ref failures, id + " roof landing Y " + roofY.ToString("0.00"), roof);
            Matrix4x4 before = visual.transform.localToWorldMatrix;
            Bounds beforeBounds = collider != null ? collider.bounds : default;
            var manager = UnityEngine.Object.FindAnyObjectByType<ViewModeManager>();
            if (manager != null)
            {
                Check(report, ref failures, id + " is not under the view manager", !visual.transform.IsChildOf(manager.transform));
                Toggle(manager.firstPersonCamera);
                Toggle(manager.thirdPersonCamera);
            }

            bool poseHeld = visual.transform.localToWorldMatrix == before && (collider == null || collider.bounds == beforeBounds);
            Check(report, ref failures, id + " pose stays put when view cameras toggle", poseHeld);
        }

        static void InspectExtrusion(StringBuilder report, ref int failures, BuildingFeature building)
        {
            var record = building.GetComponent<FeatureRecord>();
            string id = record != null ? record.featureId : building.name;
            Check(report, ref failures, id + " has no GLB", building.GetComponentInChildren<BuildingModelVisual>(true) == null);
            var renderers = building.GetComponentsInChildren<MeshRenderer>(true);
            int visible = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].GetComponent<BuildingFoundation>() != null)
                    continue;
                if (renderers[i].enabled)
                    visible++;
            }

            var colliders = building.GetComponentsInChildren<MeshCollider>(true);
            int solid = 0;
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].GetComponent<BuildingFoundation>() != null)
                    continue;
                if (colliders[i].enabled && colliders[i].sharedMesh != null)
                    solid++;
            }

            Check(report, ref failures, id + " extrusion stays visible (" + visible + ")", visible > 0);
            Check(report, ref failures, id + " extrusion stays solid (" + solid + ")", solid > 0);
            report.AppendLine("INFO extrusion sample " + id + ".");
        }

        static bool Probe(MeshCollider collider, Vector3 center, Vector3 direction, float distance)
        {
            var ray = new Ray(center - direction * distance, direction);
            return collider.Raycast(ray, out _, distance * 2f);
        }

        static bool PhysicsProbe(MeshCollider collider, Vector3 center, Vector3 direction, float distance)
        {
            var ray = new Ray(center - direction * distance, direction);
            if (!collider.Raycast(ray, out RaycastHit hit, distance * 2f))
                return false;
            Vector3 start = hit.point - direction * 0.2f;
            return Physics.Raycast(start, direction, out RaycastHit worldHit, 0.8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && worldHit.collider == collider;
        }

        static void Toggle(Camera camera)
        {
            if (camera == null)
                return;
            bool enabled = camera.enabled;
            camera.enabled = !enabled;
            camera.enabled = enabled;
        }

        static bool ExtrusionRenderersHidden(BuildingFeature building)
        {
            var renderers = building.GetComponentsInChildren<MeshRenderer>(true);
            bool sawExtrusion = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].GetComponent<BuildingFoundation>() != null || BuildingModelVisual.BelongsToModel(renderers[i].transform))
                    continue;
                sawExtrusion = true;
                if (renderers[i].enabled)
                    return false;
            }

            return sawExtrusion;
        }

        static bool ExtrusionCollidersOff(BuildingFeature building)
        {
            var colliders = building.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].GetComponent<BuildingFoundation>() != null || BuildingModelVisual.BelongsToModel(colliders[i].transform))
                    continue;
                if (colliders[i].enabled)
                    return false;
            }

            return true;
        }

        static void FootprintBounds(BuildingFeature building, out Bounds bounds)
        {
            Vector3 origin = building.transform.position;
            bool any = false;
            bounds = new Bounds(origin, Vector3.zero);
            if (building.parts == null)
                return;
            for (int p = 0; p < building.parts.Count; p++)
            {
                var rings = building.parts[p].rings;
                if (rings == null || rings.Count == 0)
                    continue;
                var points = rings[0].points;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 point = origin + new Vector3(points[i].x, 0f, points[i].y);
                    if (!any)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                    else
                        bounds.Encapsulate(point);
                }
            }
        }

        static bool Contains(List<string> notes, string fragment)
        {
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].IndexOf(fragment, StringComparison.Ordinal) >= 0)
                    return true;
            }

            return false;
        }

        static void Check(StringBuilder report, ref int failures, string label, bool passed)
        {
            report.AppendLine((passed ? "PASS " : "FAIL ") + label);
            if (!passed)
                failures++;
        }
    }

    [InitializeOnLoad]
    static class BuildingModelSampleRunner
    {
        static BuildingModelSampleRunner()
        {
            EditorApplication.delayCall += RunIfRequested;
        }

        static void RunIfRequested()
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            string flag = Path.Combine(project, "Library", "RunLadderCheck.flag");
            if (!File.Exists(flag))
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying)
            {
                if (EditorApplication.isPlaying)
                    EditorApplication.isPlaying = false;
                EditorApplication.delayCall += RunIfRequested;
                return;
            }

            File.Delete(flag);
            int failures = BuildingModelSample.VerifyLadders(out string text);
            File.WriteAllText(Path.Combine(project, "NazarethPlusCity_LadderModels.txt"), text);
            if (failures == 0)
                Debug.Log(text);
            else
                Debug.LogError(text);
        }
    }
}
