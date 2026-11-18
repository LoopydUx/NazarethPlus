using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NazarethPlusCity.EditorTools
{
    public static class AssetPlacementSetup
    {
        [MenuItem("NazarethPlusCity/Setup Asset Placement")]
        public static void InstallFromMenu()
        {
            Run();
            Debug.Log("Asset placement setup finished.");
        }

        public static void Install()
        {
            try
            {
                Run();
                Debug.Log("PLACEMENT_SETUP_OK");
            }
            catch (Exception exception)
            {
                Debug.LogError("PLACEMENT_SETUP_FAIL\n" + exception);
                EditorApplication.Exit(1);
            }
        }

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Asset placement setup cannot change the scene during Play mode.");
            WheelieBinPrefabBuilder.BuildOrUpdate();
            WoodenLadderPrefabBuilder.BuildOrUpdate();
            var scene = EditorSceneManager.GetActiveScene();
            string openPath = string.IsNullOrEmpty(scene.path) ? "" : scene.path.Replace('\\', '/');
            if (openPath != CityImporter.ScenePath)
                scene = EditorSceneManager.OpenScene(CityImporter.ScenePath, OpenSceneMode.Single);
            var walker = UnityEngine.Object.FindAnyObjectByType<FirstPersonWalker>();
            if (walker == null)
                throw new InvalidOperationException("CityWalker is missing, so placement could not be wired.");
            var hud = walker.transform.Find("WalkHud");
            if (hud != null)
                PlacementHudBuilder.Ensure(hud, true);
            EnsurePlayerWiring(walker);
            var ladderSettings = AssetDatabase.LoadAssetAtPath<LadderSystemSettings>(LadderIds.SettingsPath);
            var ladderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LadderIds.PrefabPath);
            if (ladderSettings != null && ladderPrefab != null)
                LadderSystemSetup.EnsurePlayerWiring(walker, ladderSettings, ladderPrefab);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Verify(walker);
        }

        public static void EnsurePlayerWiring(FirstPersonWalker walker)
        {
            if (walker == null)
                return;
            var hud = walker.transform.Find("WalkHud");
            if (hud != null)
                PlacementHudBuilder.Ensure(hud, false);
            var placement = walker.GetComponent<AssetPlacementController>() ?? walker.gameObject.AddComponent<AssetPlacementController>();
            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>(PlaceableIds.CatalogPath);
            if (catalog != null)
                placement.catalog = catalog;
            var frame = AssetDatabase.LoadAssetAtPath<CityCoordinateFrame>(PlaceableIds.CoordinatePath);
            if (frame != null)
            {
                if (string.IsNullOrWhiteSpace(frame.cityId))
                    frame.cityId = PlaceableIds.DefaultCityId;
                EditorUtility.SetDirty(frame);

                placement.coordinates = frame;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlaceableIds.PrefabPath);
            if (prefab != null)
                placement.binPrefab = prefab;
            EnsurePlacedRoot();
            EditorUtility.SetDirty(placement);
        }

        static void EnsurePlacedRoot()
        {
            var existing = UnityEngine.Object.FindAnyObjectByType<PlacedAssetsRoot>(FindObjectsInactive.Include);
            if (existing != null)
            {
                var owned = existing.GetComponent<GeneratorOwned>();
                if (owned != null)
                    UnityEngine.Object.DestroyImmediate(owned);
                return;
            }

            var root = new GameObject("PlacedAssets");
            var city = GameObject.Find("Nazareth");
            if (city != null)
                root.transform.SetParent(city.transform, false);
            root.AddComponent<PlacedAssetsRoot>();
        }

        static void Verify(FirstPersonWalker walker)
        {
            var failures = new List<string>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlaceableIds.PrefabPath);
            Check(failures, prefab != null, "bin prefab exists");
            if (prefab != null)
            {
                var identity = prefab.GetComponent<PlaceableAssetIdentity>();
                Check(failures, identity != null && identity.catalogId == PlaceableIds.Bin, "prefab catalog id");
                var box = prefab.GetComponent<BoxCollider>();
                Check(failures, box != null, "prefab box collider");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Bounds bounds = default;
                var renderers = instance.GetComponentsInChildren<Renderer>();
                Check(failures, renderers.Length == 16, "16 bin renderers, found " + renderers.Length);
                bool urp = true;
                if (renderers.Length > 0)
                {
                    bounds = renderers[0].bounds;
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                        var material = renderers[i].sharedMaterial;
                        if (material == null || material.shader == null || material.shader.name != "Universal Render Pipeline/Lit")
                            urp = false;
                    }
                }

                Check(failures, urp, "URP Lit materials");
                Check(failures, bounds.size.y > 0.95f && bounds.size.y < 1.25f, "real-world height " + bounds.size.y.ToString("0.00"));
                Check(failures, Mathf.Abs(bounds.min.y - instance.transform.position.y) < 0.02f, "bottom pivot");
                Check(failures, Mathf.Abs(bounds.center.x - instance.transform.position.x) < 0.03f && Mathf.Abs(bounds.center.z - instance.transform.position.z) < 0.03f, "centered base");
                UnityEngine.Object.DestroyImmediate(instance);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>(PlaceableIds.CatalogPath);
            var entry = catalog != null ? catalog.Find(PlaceableIds.Bin) : null;
            Check(failures, entry != null && entry.prefab == prefab && entry.thumbnail != null, "catalog bin assignment and thumbnail");
            var placement = walker.GetComponent<AssetPlacementController>();
            Check(failures, placement != null && placement.binPrefab == prefab && placement.catalog == catalog && placement.coordinates != null, "CityWalker assignment");
            Check(failures, walker.transform.Find("WalkHud/" + PlacementHudBuilder.AddButtonName) != null, "+ Add Asset button");
            var placedRoot = UnityEngine.Object.FindAnyObjectByType<PlacedAssetsRoot>(FindObjectsInactive.Include);
            Check(failures, placedRoot != null && placedRoot.GetComponent<GeneratorOwned>() == null && CityImporter.IsProtectedFromRebuild(placedRoot.gameObject), "PlacedAssets is protected from city rebuilds");
            if (placement != null && placement.coordinates != null)
                VerifyGeography(placement.coordinates, failures);
            VerifyStore(failures);
            if (prefab != null && placement != null)
                VerifyPlacement(walker, placement, prefab, failures);
            if (failures.Count > 0)
                throw new InvalidOperationException("Placement verification failed:\n- " + string.Join("\n- ", failures));
            Debug.Log("Placement verification passed.");
        }

        static void VerifyGeography(CityCoordinateFrame frame, List<string> failures)
        {
            var projection = GeoProjection.FromOrigin(frame.originLongitude, frame.originLatitude);
            var samples = new[] { new Vector2(0f, 0f), new Vector2(120f, -40f), new Vector2(-350f, 800f), new Vector2(40f, -900f) };
            for (int i = 0; i < samples.Length; i++)
            {
                projection.Unproject(samples[i].x, samples[i].y, out double longitude, out double latitude);
                Vector3 back = projection.Project(longitude, latitude);
                float error = Vector2.Distance(samples[i], new Vector2(back.x, back.z));
                Check(failures, error < 0.05f, "geo round-trip " + samples[i] + " error " + error.ToString("0.000") + " m");
            }
        }

        static void VerifyStore(List<string> failures)
        {
            string directory = Path.Combine(Path.GetTempPath(), "nazareth-placement-" + Guid.NewGuid().ToString("N"));
            var store = new JsonFilePlacementStore(directory);
            var frame = AssetDatabase.LoadAssetAtPath<CityCoordinateFrame>(PlaceableIds.CoordinatePath);
            if (frame == null)
            {
                failures.Add("CityCoordinateFrame did not load from " + PlaceableIds.CoordinatePath);
                return;
            }

            double longitude = frame.originLongitude + 0.0012345678901234;
            double latitude = frame.originLatitude - 0.0009876543210123;
            var first = new PlacementDocument
            {
                schemaVersion = PlacementDocument.CurrentSchema,
                cityId = "nazareth",
                originLongitude = frame.originLongitude,
                originLatitude = frame.originLatitude,
                placements = new List<PlacementRecord>
                {
                    new PlacementRecord
                    {
                        id = "verify-a",
                        cityId = "nazareth",
                        assetType = PlaceableIds.BinType,
                        catalogId = PlaceableIds.Bin,
                        longitude = longitude,
                        latitude = latitude,
                        groundOffset = 0.05f,
                        rotationY = 37.5f,
                        scale = 1f,
                        createdUtc = "2026-11-17T12:00:00.0000000Z"
                    }
                }
            };
            Check(failures, store.TrySave("nazareth", first, out string saveError), "first save " + saveError);
            Check(failures, store.TryLoad("nazareth", out PlacementDocument loaded, out string loadError), "first load " + loadError);
            if (loaded != null && loaded.placements.Count == 1)
            {
                Check(failures, Math.Abs(loaded.placements[0].longitude - longitude) < 1e-12, "longitude precision");
                Check(failures, Math.Abs(loaded.placements[0].latitude - latitude) < 1e-12, "latitude precision");
                Check(failures, Math.Abs(loaded.placements[0].groundOffset - 0.05f) < 0.0001f, "ground offset");
            }
            else
            {
                failures.Add("loaded record missing");
            }

            var second = first;
            second.placements[0].id = "verify-b";
            Check(failures, store.TrySave("nazareth", second, out _), "second save writes a backup");
            string backup = File.ReadAllText(store.Location("nazareth") + ".bak");
            Check(failures, backup.Contains("verify-a"), "backup keeps the previous record");
            File.WriteAllText(store.Location("nazareth"), "{");
            Check(failures, store.TryLoad("nazareth", out PlacementDocument recovered, out string recovery), "corrupt file falls back to backup " + recovery);
            Check(failures, recovered != null && recovered.placements.Count == 1 && recovered.placements[0].id == "verify-a", "backup record recovered");
            Check(failures, File.Exists(store.Location("nazareth")), "corrupt file was not deleted");
            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception)
            {
            }
        }

        static void VerifyPlacement(FirstPersonWalker walker, AssetPlacementController scenePlacement, GameObject prefab, List<string> failures)
        {
            Physics.SyncTransforms();
            var box = prefab.GetComponent<BoxCollider>();
            var boundary = UnityEngine.Object.FindAnyObjectByType<CityBoundaryMap>();
            var outside = PlacementValidator.Evaluate(CheckAt(walker, box, boundary, new Vector3(80000f, 0f, 80000f), 0f));
            Check(failures, !outside.Valid && outside.Reason != null && outside.Reason.IndexOf("Outside", StringComparison.OrdinalIgnoreCase) >= 0, "far point is outside the city (" + outside.Reason + ")");
            var feet = PlacementValidator.Evaluate(CheckAt(walker, box, boundary, walker.transform.position, walker.transform.eulerAngles.y));
            Check(failures, !feet.Valid, "player feet are rejected (" + feet.Reason + ")");
            PlacementEvaluation valid = default;
            bool found = false;
            Vector3 forward = walker.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();
            var reasons = new StringBuilder();
            float[] distances = { 2f, 3f, 4.5f, 6f, 8f };
            for (int d = 0; d < distances.Length && !found; d++)
            {
                for (int step = 0; step < 8 && !found; step++)
                {
                    Vector3 direction = Quaternion.Euler(0f, step * 45f, 0f) * forward;
                    Vector3 point = walker.transform.position + direction * distances[d];
                    var evaluation = PlacementValidator.Evaluate(CheckAt(walker, box, boundary, point, step * 45f));
                    if (evaluation.Valid)
                    {
                        valid = evaluation;
                        found = true;
                    }
                    else if (reasons.Length < 500)
                        reasons.Append(distances[d].ToString("0.0")).Append("m/").Append(step * 45).Append(" ").Append(evaluation.Reason).Append("; ");
                }
            }

            Check(failures, found, "a legal bin pose exists near the street spawn. " + reasons);
            if (found)
            {
                var slopeObject = new GameObject("PlacementSlopeProbe");
                slopeObject.transform.SetPositionAndRotation(valid.Position + Vector3.up * 3f, Quaternion.Euler(50f, 0f, 0f));
                var probe = slopeObject.AddComponent<BoxCollider>();
                probe.size = new Vector3(8f, 0.4f, 8f);
                slopeObject.AddComponent<TerrainChunk>();
                Physics.SyncTransforms();
                var steep = PlacementValidator.Evaluate(CheckAt(walker, box, boundary, slopeObject.transform.position, 0f));
                Check(failures, !steep.Valid && steep.Reason != null && steep.Reason.IndexOf("Slope", StringComparison.OrdinalIgnoreCase) >= 0, "steep surface is rejected (" + steep.Reason + ")");
                UnityEngine.Object.DestroyImmediate(slopeObject);
                Physics.SyncTransforms();
            }

            walker.SuppressFlightVertical = true;
            CityControlFrame gated = walker.ReadControls();
            Check(failures, !gated.ascend && !gated.descend, "Q/E placement does not climb in bird mode");
            walker.SuppressFlightVertical = false;
            if (!found || scenePlacement.coordinates == null)
                return;
            string directory = Path.Combine(Path.GetTempPath(), "nazareth-placement-spawn-" + Guid.NewGuid().ToString("N"));
            var store = new JsonFilePlacementStore(directory);
            var projection = GeoProjection.FromOrigin(scenePlacement.coordinates.originLongitude, scenePlacement.coordinates.originLatitude);
            projection.Unproject(valid.Position.x, valid.Position.z, out double longitude, out double latitude);
            var document = new PlacementDocument
            {
                schemaVersion = PlacementDocument.CurrentSchema,
                cityId = "nazareth",
                originLongitude = scenePlacement.coordinates.originLongitude,
                originLatitude = scenePlacement.coordinates.originLatitude,
                placements = new List<PlacementRecord>
                {
                    new PlacementRecord
                    {
                        id = "verify-spawn",
                        cityId = "nazareth",
                        assetType = PlaceableIds.BinType,
                        catalogId = PlaceableIds.Bin,
                        longitude = longitude,
                        latitude = latitude,
                        groundOffset = 0f,
                        rotationY = valid.Yaw,
                        scale = 1f,
                        createdUtc = "2026-11-17T12:00:00.0000000Z"
                    }
                }
            };
            store.TrySave("nazareth", document, out _);
            var host = new GameObject("PlacementVerifyHost");
            var placement = host.AddComponent<AssetPlacementController>();
            placement.catalog = scenePlacement.catalog;
            placement.coordinates = scenePlacement.coordinates;
            placement.binPrefab = scenePlacement.binPrefab;
            placement.Store = store;
            placement.ReloadFromStore();
            int first = CountMarkers("verify-spawn");
            placement.ReloadFromStore();
            int second = CountMarkers("verify-spawn");
            Check(failures, first == 1 && second == 1, "restore creates the bin once (" + first + ", " + second + ")");
            var marker = FindMarker("verify-spawn");
            if (marker != null)
            {
                Vector3 restored = marker.transform.position;
                Check(failures, Vector2.Distance(new Vector2(restored.x, restored.z), new Vector2(valid.Position.x, valid.Position.z)) < 0.05f, "restored geographic position");
                UnityEngine.Object.DestroyImmediate(marker.gameObject);
            }

            placement.ReloadFromStore();
            Check(failures, CountMarkers("verify-spawn") == 1, "restore after a restart still creates one bin");
            document.placements.Add(new PlacementRecord
            {
                id = "verify-missing-ground",
                cityId = "nazareth",
                assetType = PlaceableIds.BinType,
                catalogId = PlaceableIds.Bin,
                longitude = 0,
                latitude = 0,
                groundOffset = 0f,
                rotationY = 0f,
                scale = 1f,
                createdUtc = "2026-11-17T12:00:00.0000000Z"
            });
            store.TrySave("nazareth", document, out _);
            var kept = FindMarker("verify-spawn");
            if (kept != null)
                UnityEngine.Object.DestroyImmediate(kept.gameObject);
            placement.ReloadFromStore();
            Check(failures, CountMarkers("verify-spawn") == 1 && CountMarkers("verify-missing-ground") == 0, "unresolved ground is not spawned");
            Check(failures, store.TryLoad("nazareth", out PlacementDocument after, out _) && after.placements.Count == 2, "unresolved record stays in the file");
            foreach (var extra in UnityEngine.Object.FindObjectsByType<PlacedAssetMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (extra != null && extra.placementId != null && extra.placementId.StartsWith("verify-", StringComparison.Ordinal))
                    UnityEngine.Object.DestroyImmediate(extra.gameObject);
            }

            UnityEngine.Object.DestroyImmediate(host);
            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception)
            {
            }
        }

        static PlacementCheck CheckAt(FirstPersonWalker walker, BoxCollider box, CityBoundaryMap boundary, Vector3 point, float yaw)
        {
            return new PlacementCheck
            {
                Aim = new PlacementAimSample { HasPoint = true, Point = point },
                YawDegrees = yaw,
                Scale = 1f,
                Footprint = box,
                Boundary = boundary,
                MaxSlopeDegrees = 32f,
                PlayerPosition = walker.transform.position,
                PlayerRadius = walker.capsuleRadius,
                CheckPlayer = true
            };
        }

        static int CountMarkers(string id)
        {
            int count = 0;
            var markers = UnityEngine.Object.FindObjectsByType<PlacedAssetMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] != null && markers[i].placementId == id)
                    count++;
            }

            return count;
        }

        static PlacedAssetMarker FindMarker(string id)
        {
            var markers = UnityEngine.Object.FindObjectsByType<PlacedAssetMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] != null && markers[i].placementId == id)
                    return markers[i];
            }

            return null;
        }

        static void Check(List<string> failures, bool ok, string label)
        {
            if (!ok)
                failures.Add(label);
        }
    }
}
