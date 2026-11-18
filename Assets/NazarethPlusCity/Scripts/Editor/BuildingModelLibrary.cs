using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace NazarethPlusCity.EditorTools
{
    public sealed class BuildingModelBinding
    {
        public string FeatureId;
        public string CatalogBuildingId;
        public string SourceFile;
        public GameObject Prefab;
        public float Yaw;
        public float Pitch;
        public float Roll;
        public Vector3 Scale = Vector3.one;
        public float OffsetEast;
        public float OffsetNorth;
        public float Altitude;
        public string MatchNote;
    }

    public static class BuildingModelLibrary
    {
        public const string RelativeFolder = "Data/building_models";
        public const string CatalogFile = "Data/building_models.json";
        const string GeneratedRoot = "Assets/NazarethPlusCity/Generated/BuildingModels";

        /// <summary>
        /// Catalog building ids that are not the GeoJSON stable id. Each one is the feature's properties.id,
        /// and the stable id is the feature id (way/… or relation/…). The property join is unique in the current
        /// file; the explicit pair is what the importer uses so a later duplicate property cannot retarget it.
        /// </summary>
        static readonly Dictionary<string, string> ExplicitStableIds = new Dictionary<string, string>
        {
            { "2febe5f1-1db2-400e-b905-f28445661560", "way/583523590" },
            { "a64cd6ea-b32e-48de-9b73-8a0a5aac7ae1", "way/741277388" },
            { "eb8469ea-cd82-4d6c-86c0-96b7c9e98ca8", "relation/14434382" }
        };

        public static Dictionary<string, BuildingModelBinding> Load(IList<GeoFeature> features, List<string> notes)
        {
            var bindings = new Dictionary<string, BuildingModelBinding>();
            string project = Directory.GetParent(Application.dataPath).FullName;
            string folder = Path.Combine(project, "Data", "building_models");
            string catalogPath = Path.Combine(project, "Data", "building_models.json");
            notes.Add("Building model folder: " + RelativeFolder + ". Catalog: " + CatalogFile + ".");
            if (!File.Exists(catalogPath))
            {
                notes.Add("Missing " + CatalogFile + ". Every building keeps its extrusion.");
                return bindings;
            }

            var byStable = new Dictionary<string, GeoFeature>();
            var duplicateStable = new List<string>();
            var byPropertyId = new Dictionary<string, List<GeoFeature>>();
            var byAt = new Dictionary<string, List<GeoFeature>>();
            for (int i = 0; i < features.Count; i++)
            {
                GeoFeature feature = features[i];
                if (string.IsNullOrEmpty(feature.Id))
                    continue;
                if (byStable.ContainsKey(feature.Id))
                    duplicateStable.Add(feature.Id);
                else
                    byStable.Add(feature.Id, feature);
                AddLookup(byPropertyId, Property(feature, "id"), feature);
                AddLookup(byAt, Property(feature, "@id"), feature);
            }

            notes.Add("Duplicate stable feature ids in buildings.geojson: " + duplicateStable.Count + ".");
            JsonMini catalog = JsonMini.Parse(File.ReadAllText(catalogPath));
            JsonMini models = catalog.Get("models");
            if (models == null)
            {
                notes.Add(CatalogFile + " has no models array. Every building keeps its extrusion.");
                return bindings;
            }

            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenCatalogIds = new HashSet<string>();
            var claimedFeatures = new HashSet<string>();
            int missing = 0;
            int duplicate = 0;
            int ambiguous = 0;
            int reused = 0;
            int imported = 0;
            CityMaterialFactory.EnsureFolder("Assets/NazarethPlusCity/Generated");
            CityMaterialFactory.EnsureFolder(GeneratedRoot);
            for (int i = 0; i < models.Count; i++)
            {
                JsonMini entry = models[i];
                string catalogId = Text(entry, "buildingId");
                string filename = Text(entry, "filename");
                if (string.IsNullOrEmpty(catalogId) || string.IsNullOrEmpty(filename))
                {
                    missing++;
                    notes.Add("Catalog entry " + i + " is missing buildingId or filename.");
                    continue;
                }

                if (!seenCatalogIds.Add(catalogId) || !seenFiles.Add(filename))
                {
                    duplicate++;
                    notes.Add("Duplicate catalog entry for " + catalogId + " / " + filename + ". The later copy was skipped.");
                    continue;
                }

                string fullPath = Path.Combine(folder, filename.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    missing++;
                    notes.Add("Missing model file " + RelativeFolder + "/" + filename + " for " + catalogId + ".");
                    continue;
                }

                if (!TryResolve(catalogId, byStable, byPropertyId, byAt, out GeoFeature feature, out string matchNote, out string problem))
                {
                    if (problem != null && problem.StartsWith("ambiguous", StringComparison.Ordinal))
                        ambiguous++;
                    else
                        missing++;
                    notes.Add(problem + " (" + filename + ").");
                    continue;
                }

                if (!claimedFeatures.Add(feature.Id))
                {
                    duplicate++;
                    notes.Add("More than one model resolved to " + feature.Id + ". " + filename + " was skipped.");
                    continue;
                }

                try
                {
                    EditorUtility.DisplayProgressBar("NazarethPlusCity", "Building model " + filename, 0.08f);
                    GameObject prefab = ImportOrReuse(filename, fullPath, out bool wasReused);
                    if (wasReused)
                        reused++;
                    else
                        imported++;
                    var binding = new BuildingModelBinding
                    {
                        FeatureId = feature.Id,
                        CatalogBuildingId = catalogId,
                        SourceFile = filename,
                        Prefab = prefab,
                        Yaw = Number(entry, "rotation", 0f),
                        Pitch = Number(entry, "pitch", 0f),
                        Roll = Number(entry, "roll", 0f),
                        Scale = new Vector3(Number(entry, "scaleX", Number(entry, "scale", 1f)), Number(entry, "scaleY", Number(entry, "scale", 1f)), Number(entry, "scaleZ", Number(entry, "scale", 1f))),
                        OffsetEast = Number(entry, "offsetEast", 0f),
                        OffsetNorth = Number(entry, "offsetNorth", 0f),
                        Altitude = Number(entry, "altitudeOffset", 0f),
                        MatchNote = matchNote
                    };
                    bindings.Add(feature.Id, binding);
                    notes.Add(matchNote + " File " + filename + " yaw " + binding.Yaw.ToString("0.#") + " scale " + binding.Scale.x.ToString("0.##") + " offset " + binding.OffsetEast.ToString("0.##") + " E, " + binding.OffsetNorth.ToString("0.##") + " N.");
                }
                catch (Exception exception)
                {
                    notes.Add("Import failed for " + filename + " (" + feature.Id + "): " + exception.Message + " The extrusion is kept.");
                }
            }

            NoteUnlistedFiles(folder, seenFiles, notes);
            notes.Add("Model match summary: mapped " + bindings.Count + ", missing " + missing + ", duplicate " + duplicate + ", ambiguous " + ambiguous + ", imported " + imported + ", reused " + reused + ".");
            return bindings;
        }

        public static bool TryAttach(GameObject building, BuildingModelBinding binding, float meshBaseLocal, out string error)
        {
            error = null;
            if (building == null || binding == null || binding.Prefab == null)
            {
                error = "prefab is missing";
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(binding.Prefab, building.transform);
            if (instance == null)
            {
                error = "Unity did not instantiate " + binding.SourceFile;
                return false;
            }

            instance.name = "Model";
            instance.transform.localPosition = new Vector3(binding.OffsetEast, binding.Altitude, binding.OffsetNorth);
            instance.transform.localRotation = Quaternion.Euler(binding.Pitch, binding.Yaw, binding.Roll);
            instance.transform.localScale = binding.Scale;
            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                error = "imported prefab has no renderers";
                return false;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            float lift = (building.transform.position.y + meshBaseLocal + binding.Altitude) - bounds.min.y;
            instance.transform.position += new Vector3(0f, lift, 0f);
            var collider = instance.GetComponent<MeshCollider>();
            if (collider == null || collider.sharedMesh == null)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                error = "imported prefab has no mesh collider";
                return false;
            }

            var visual = instance.GetComponent<BuildingModelVisual>() ?? instance.AddComponent<BuildingModelVisual>();
            visual.featureId = binding.FeatureId;
            visual.catalogBuildingId = binding.CatalogBuildingId;
            visual.sourceFile = binding.SourceFile;
            visual.yawDegrees = binding.Yaw;
            visual.pitchDegrees = binding.Pitch;
            visual.rollDegrees = binding.Roll;
            var transforms = instance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                GameObjectUtility.SetStaticEditorFlags(transforms[i].gameObject, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            var feature = building.GetComponent<BuildingFeature>();
            BuildingModelVisual.HideExtrusion(feature);
            return true;
        }

        static bool TryResolve(string catalogId, Dictionary<string, GeoFeature> byStable, Dictionary<string, List<GeoFeature>> byPropertyId, Dictionary<string, List<GeoFeature>> byAt, out GeoFeature feature, out string matchNote, out string problem)
        {
            feature = null;
            matchNote = null;
            problem = null;
            var candidates = new List<GeoFeature>();
            AddCandidate(candidates, byStable, catalogId);
            AddGroup(candidates, byAt, catalogId);
            AddGroup(candidates, byPropertyId, catalogId);
            if (ExplicitStableIds.TryGetValue(catalogId, out string explicitId))
            {
                if (!byStable.TryGetValue(explicitId, out GeoFeature explicitFeature))
                {
                    problem = "ambiguous explicit map for " + catalogId + " names missing feature " + explicitId;
                    return false;
                }

                if (candidates.Count > 1)
                {
                    problem = "ambiguous catalog id " + catalogId + " matched " + candidates.Count + " features";
                    return false;
                }

                if (candidates.Count == 1 && candidates[0].Id != explicitId)
                {
                    problem = "ambiguous catalog id " + catalogId + " resolved to " + candidates[0].Id + " but the explicit map says " + explicitId;
                    return false;
                }

                feature = explicitFeature;
                matchNote = "Explicit map " + catalogId + " -> " + explicitId + " (catalog id is properties.id, stable feature id is the GeoJSON id).";
                return true;
            }

            if (candidates.Count == 1)
            {
                feature = candidates[0];
                string how = feature.Id == catalogId ? "stable feature id" : Property(feature, "@id") == catalogId ? "@id" : "properties.id";
                matchNote = "Derived map " + catalogId + " -> " + feature.Id + " by " + how + ".";
                return true;
            }

            if (candidates.Count == 0)
            {
                problem = "missing building for catalog id " + catalogId;
                return false;
            }

            problem = "ambiguous catalog id " + catalogId + " matched " + candidates.Count + " features";
            return false;
        }

        static GameObject ImportOrReuse(string filename, string fullPath, out bool reused)
        {
            string hash = Hash(fullPath);
            string folderName = Path.GetFileNameWithoutExtension(filename);
            string assetFolder = GeneratedRoot + "/" + folderName;
            string prefabPath = assetFolder + "/Model.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
            {
                var visual = existing.GetComponent<BuildingModelVisual>();
                var collider = existing.GetComponent<MeshCollider>();
                if (visual != null && visual.contentHash == hash && visual.importVersion == GlbImporter.FormatVersion && collider != null && collider.sharedMesh != null && existing.GetComponentsInChildren<MeshRenderer>(true).Length > 0)
                {
                    reused = true;
                    return existing;
                }
            }

            reused = false;
            byte[] file = File.ReadAllBytes(fullPath);
            return GlbImporter.ImportBuildingPrefab(filename, file, assetFolder, hash);
        }

        static void NoteUnlistedFiles(string folder, HashSet<string> catalogFiles, List<string> notes)
        {
            if (!Directory.Exists(folder))
                return;
            string[] files = Directory.GetFiles(folder, "*.glb", SearchOption.AllDirectories);
            int unlisted = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                if (catalogFiles.Contains(name))
                    continue;
                unlisted++;
                string relative = files[i].Substring(folder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                notes.Add("Unlisted GLB " + RelativeFolder + "/" + relative + " is not a catalog building and was not imported.");
            }

            notes.Add("Unlisted GLB files: " + unlisted + ".");
        }

        static void AddLookup(Dictionary<string, List<GeoFeature>> lookup, string key, GeoFeature feature)
        {
            if (string.IsNullOrEmpty(key))
                return;
            if (!lookup.TryGetValue(key, out List<GeoFeature> list))
            {
                list = new List<GeoFeature>();
                lookup.Add(key, list);
            }

            list.Add(feature);
        }

        static void AddCandidate(List<GeoFeature> candidates, Dictionary<string, GeoFeature> byStable, string id)
        {
            if (byStable.TryGetValue(id, out GeoFeature feature))
                AddUnique(candidates, feature);
        }

        static void AddGroup(List<GeoFeature> candidates, Dictionary<string, List<GeoFeature>> lookup, string id)
        {
            if (!lookup.TryGetValue(id, out List<GeoFeature> list))
                return;
            for (int i = 0; i < list.Count; i++)
                AddUnique(candidates, list[i]);
        }

        static void AddUnique(List<GeoFeature> candidates, GeoFeature feature)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (ReferenceEquals(candidates[i], feature) || candidates[i].Id == feature.Id)
                    return;
            }

            candidates.Add(feature);
        }

        static string Property(GeoFeature feature, string key)
        {
            if (feature.Properties != null && feature.Properties.TryGetValue(key, out string value))
                return value;
            return null;
        }

        static string Text(JsonMini node, string key)
        {
            return node != null && node.Try(key, out JsonMini value) ? value.String : null;
        }

        static float Number(JsonMini node, string key, float fallback)
        {
            if (node != null && node.Try(key, out JsonMini value) && value.Type == JsonMini.Kind.Number)
                return (float)value.Number;
            return fallback;
        }

        static string Hash(string fullPath)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(File.ReadAllBytes(fullPath));
                var chars = new char[bytes.Length * 2];
                const string hex = "0123456789abcdef";
                for (int i = 0; i < bytes.Length; i++)
                {
                    chars[i * 2] = hex[bytes[i] >> 4];
                    chars[i * 2 + 1] = hex[bytes[i] & 15];
                }

                return new string(chars);
            }
        }
    }
}
