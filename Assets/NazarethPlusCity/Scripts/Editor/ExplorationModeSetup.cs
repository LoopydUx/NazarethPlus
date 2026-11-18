using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NazarethPlusCity.EditorTools
{
    public static class ExplorationModeSetup
    {
        public const string ScenePath = "Assets/NazarethPlusCity/Scenes/NazarethPlusCity.unity";
        public const string FbxPath = "Assets/Sci-FI_Trooper_Man_v.3/Meshes/SK_SciFiTrooperManV3.fbx";
        public const string SourcePrefabPath = "Assets/Sci-FI_Trooper_Man_v.3/Prefabs/SK_SciFiTrooperManV3 Variant.prefab";
        public const string TrooperPrefabPath = "Assets/NazarethPlusCity/Characters/NazarethTrooper.prefab";
        public const string MaterialFolder = "Assets/Sci-FI_Trooper_Man_v.3/Materials";
        public const string TextureFolder = "Assets/Sci-FI_Trooper_Man_v.3/Textures";
        public const float TargetHeight = 1.8f;

        public static string ReportPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ExplorationModeReport.txt");

        static string PackagePath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Data", "scifitroopermanv3.unitypackage");

        public static bool PackageImported => AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) != null;

        static bool _importFinished;
        static bool _importFailed;
        static string _importError;
        static int _settleFrames;
        static double _importStarted;

        [MenuItem("NazarethPlusCity/Setup Exploration Modes")]
        public static void InstallFromMenu()
        {
            BeginImport();
        }

        public static void InstallBatch()
        {
            _settleFrames = 0;
            _importFailed = false;
            _importError = null;
            _importStarted = EditorApplication.timeSinceStartup;
            _importFinished = PackageImported;
            if (!_importFinished)
            {
                if (!File.Exists(PackagePath))
                {
                    File.WriteAllText(ReportPath, "FAIL package missing at " + PackagePath);
                    EditorApplication.Exit(1);
                    return;
                }

                AssetDatabase.importPackageCompleted += OnPackageImported;
                AssetDatabase.importPackageFailed += OnPackageFailed;
                AssetDatabase.ImportPackage(PackagePath, false);
            }

            EditorApplication.update += SettleAndInstall;
        }

        static void OnPackageImported(string packageName)
        {
            _importFinished = true;
            AssetDatabase.importPackageCompleted -= OnPackageImported;
            AssetDatabase.importPackageFailed -= OnPackageFailed;
        }

        static void OnPackageFailed(string packageName, string error)
        {
            _importFailed = true;
            _importError = error;
            AssetDatabase.importPackageCompleted -= OnPackageImported;
            AssetDatabase.importPackageFailed -= OnPackageFailed;
        }

        static void SettleAndInstall()
        {
            if (_importFailed)
            {
                EditorApplication.update -= SettleAndInstall;
                File.WriteAllText(ReportPath, "FAIL trooper import: " + _importError);
                EditorApplication.Exit(1);
                return;
            }

            if (!_importFinished && PackageImported)
                _importFinished = true;
            if (!_importFinished)
            {
                if (EditorApplication.timeSinceStartup - _importStarted > 240d)
                {
                    EditorApplication.update -= SettleAndInstall;
                    File.WriteAllText(ReportPath, "FAIL trooper import timed out.");
                    EditorApplication.Exit(1);
                }

                return;
            }

            _settleFrames++;
            if (_settleFrames < 4)
                return;
            EditorApplication.update -= SettleAndInstall;
            try
            {
                var notes = InstallNow(true);
                File.WriteAllText(ReportPath, notes.ToString());
                Debug.Log(notes.ToString());
                ExplorationModeVerification.Begin();
            }
            catch (Exception exception)
            {
                File.WriteAllText(ReportPath, "FAIL setup\n" + exception);
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        static void BeginImport()
        {
            if (!PackageImported)
            {
                if (!File.Exists(PackagePath))
                    throw new FileNotFoundException("Trooper package was not found.", PackagePath);
                AssetDatabase.ImportPackage(PackagePath, false);
                EditorUtility.DisplayDialog("Nazareth exploration", "The trooper package is importing. Run NazarethPlusCity → Setup Exploration Modes again when the import finishes.", "OK");
                return;
            }

            var notes = InstallNow(true);
            Debug.Log(notes.ToString());
            File.WriteAllText(ReportPath, notes.ToString());
        }

        public static StringBuilder InstallNow(bool openScene)
        {
            var notes = new StringBuilder();
            notes.AppendLine("Nazareth exploration setup");
            if (!PackageImported)
                throw new InvalidOperationException("Sci-fi trooper FBX is not imported at " + FbxPath);

            KeepCityBuildScene();
            ConfigureModel(notes);
            ConfigureTextures(notes);
            ConvertMaterials(notes);
            ReportAnimations(notes);
            BuildTrooperPrefab(notes);
            if (openScene)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            WireOpenScene(notes);
            AssetDatabase.SaveAssets();
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            notes.AppendLine("Saved " + ScenePath);
            return notes;
        }

        static void KeepCityBuildScene()
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes != null && scenes.Length == 1 && scenes[0].path == ScenePath && scenes[0].enabled)
                return;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ConfigureModel(StringBuilder notes)
        {
            var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException("FBX importer missing for " + FbxPath);
            bool changed = false;
            if (importer.importCameras)
            {
                importer.importCameras = false;
                changed = true;
            }

            if (importer.importLights)
            {
                importer.importLights = false;
                changed = true;
            }

            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                notes.AppendLine("Updated trooper import settings without changing the humanoid rig.");
            }

            CharacterAnimationSetup.ConfigureRig(notes);
        }

        static void ConfigureTextures(StringBuilder notes)
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder });
            var dirtyPaths = new System.Collections.Generic.List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;
                string name = Path.GetFileNameWithoutExtension(path);
                bool dirty = false;
                if (name.EndsWith("_n", StringComparison.OrdinalIgnoreCase))
                {
                    if (importer.textureType != TextureImporterType.NormalMap)
                    {
                        importer.textureType = TextureImporterType.NormalMap;
                        dirty = true;
                    }
                }
                else
                {
                    bool mask = name.EndsWith("_RGBA", StringComparison.OrdinalIgnoreCase);
                    if (importer.textureType != TextureImporterType.Default)
                    {
                        importer.textureType = TextureImporterType.Default;
                        dirty = true;
                    }

                    if (importer.sRGBTexture == mask)
                    {
                        importer.sRGBTexture = !mask;
                        dirty = true;
                    }
                }

                if (dirty)
                    dirtyPaths.Add(path);
            }

            for (int i = 0; i < dirtyPaths.Count; i++)
            {
                var importer = AssetImporter.GetAtPath(dirtyPaths[i]);
                importer.SaveAndReimport();
            }

            notes.AppendLine("Texture importers updated: " + dirtyPaths.Count + " of " + guids.Length + ".");
        }

        static void ConvertMaterials(StringBuilder notes)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("Universal Render Pipeline/Lit was not found.");
            var guids = AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                    continue;
                Texture baseMap = material.GetTexture("_MainTex");
                if (baseMap == null)
                    baseMap = material.GetTexture("_BaseMap");
                Texture bump = material.GetTexture("_BumpMap");
                Texture emission = material.GetTexture("_EmissionMap");
                Texture metal = material.GetTexture("_MetallicGlossMap");
                Texture occlusion = material.GetTexture("_OcclusionMap");
                Color baseColor = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                Color emissionColor = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.white;
                float bumpScale = material.HasProperty("_BumpScale") ? material.GetFloat("_BumpScale") : 1f;
                if (material.shader != shader)
                    material.shader = shader;
                material.SetColor("_BaseColor", baseColor);
                if (baseMap != null)
                    material.SetTexture("_BaseMap", baseMap);
                if (bump != null)
                {
                    material.SetTexture("_BumpMap", bump);
                    material.SetFloat("_BumpScale", bumpScale);
                    material.EnableKeyword("_NORMALMAP");
                }

                if (metal != null)
                {
                    material.SetTexture("_MetallicGlossMap", metal);
                    material.SetFloat("_Metallic", 1f);
                    material.SetFloat("_Smoothness", 1f);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }

                if (occlusion != null)
                {
                    material.SetTexture("_OcclusionMap", occlusion);
                    material.SetFloat("_OcclusionStrength", 1f);
                    material.EnableKeyword("_OCCLUSIONMAP");
                }

                if (emission != null)
                {
                    material.SetTexture("_EmissionMap", emission);
                    material.SetColor("_EmissionColor", emissionColor.maxColorComponent > 0.01f ? emissionColor : Color.white);
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                }

                if (material.HasProperty("_Surface"))
                    material.SetFloat("_Surface", 0f);
                EditorUtility.SetDirty(material);
                notes.AppendLine("URP material " + material.name
                    + " albedo=" + NameOf(baseMap)
                    + " normal=" + NameOf(bump)
                    + " metal=" + NameOf(metal)
                    + " occlusion=" + NameOf(occlusion)
                    + " emission=" + NameOf(emission)
                    + " shader=" + material.shader.name);
            }
        }

        static string NameOf(Texture texture)
        {
            return texture != null ? texture.name : "MISSING";
        }

        static void ReportAnimations(StringBuilder notes)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
            notes.AppendLine("Animation clips imported with the trooper:");
            int count = 0;
            for (int i = 0; i < assets.Length; i++)
            {
                var clip = assets[i] as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    continue;
                count++;
                bool rootMotion = ClipMovesRoot(clip);
                notes.AppendLine(" - " + clip.name + " length=" + clip.length.ToString("0.###") + "s rootMotion=" + rootMotion);
            }

            if (count == 0)
                notes.AppendLine(" - none");
            CharacterAnimationSetup.DescribeClips(notes);
        }

        static bool ClipMovesRoot(AnimationClip clip)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                if (binding.propertyName == null || binding.propertyName.IndexOf("m_LocalPosition", StringComparison.Ordinal) < 0)
                    continue;
                string path = binding.path ?? "";
                if (path.Length > 0 && path.IndexOf("root", StringComparison.OrdinalIgnoreCase) < 0 && path.IndexOf("pelvis", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length < 2)
                    continue;
                float min = curve.keys[0].value;
                float max = min;
                for (int k = 1; k < curve.length; k++)
                {
                    min = Mathf.Min(min, curve.keys[k].value);
                    max = Mathf.Max(max, curve.keys[k].value);
                }

                if (max - min > 0.01f)
                    return true;
            }

            return false;
        }

        static void BuildTrooperPrefab(StringBuilder notes)
        {
            if (!AssetDatabase.IsValidFolder("Assets/NazarethPlusCity/Characters"))
                AssetDatabase.CreateFolder("Assets/NazarethPlusCity", "Characters");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (source == null)
                source = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (source == null)
                throw new InvalidOperationException("Trooper model prefab was not found.");

            var staging = new GameObject("TrooperStaging");
            staging.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, staging.transform);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                StripImportedGadgets(instance);
                if (!TryRendererBounds(instance, out Bounds bounds))
                    throw new InvalidOperationException("The trooper has no renderers to fit.");
                notes.AppendLine("Imported bounds " + bounds.size.ToString("0.00"));
                if (bounds.size.y < bounds.size.z * 0.65f && bounds.size.y < bounds.size.x * 0.65f)
                {
                    instance.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    notes.AppendLine("Applied a -90 X rotation because the mesh imported flat.");
                }

                if (!TryRendererBounds(instance, out bounds))
                    throw new InvalidOperationException("The trooper bounds disappeared after the upright correction.");
                float scale = TargetHeight / Mathf.Max(0.001f, bounds.size.y);
                instance.transform.localScale = Vector3.one * scale;
                float yaw = AlignFacing(instance.transform, notes);
                if (Mathf.Abs(yaw) > 0.1f)
                    instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * instance.transform.localRotation;
                AlignFeet(instance.transform);
                if (!TryRendererBounds(instance, out bounds))
                    throw new InvalidOperationException("The trooper bounds disappeared after foot alignment.");

                var animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null)
                    animator = instance.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var locomotion = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CharacterAnimationSetup.ControllerPath);
                if (locomotion != null)
                    animator.runtimeAnimatorController = locomotion;
                foreach (var skinned in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skinned.updateWhenOffscreen = true;
                    skinned.quality = SkinQuality.Auto;
                }

                AssignUrpMaterials(instance, notes);
                var fittedRenderers = instance.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < fittedRenderers.Length; i++)
                    fittedRenderers[i].enabled = false;

                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, TrooperPrefabPath);
                if (prefab == null)
                    throw new InvalidOperationException("Could not save " + TrooperPrefabPath);
                var kind = PrefabUtility.GetPrefabAssetType(prefab);
                notes.AppendLine("Saved " + TrooperPrefabPath + " as " + kind
                    + " height=" + bounds.size.y.ToString("0.00")
                    + "m feetY=" + bounds.min.y.ToString("0.000")
                    + " centerXZ=(" + bounds.center.x.ToString("0.000") + ", " + bounds.center.z.ToString("0.000") + ")");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(staging);
            }
        }

        static float AlignFacing(Transform root, StringBuilder notes)
        {
            var left = FindChild(root, "clavicle_l");
            var right = FindChild(root, "clavicle_r");
            if (left == null || right == null)
            {
                notes.AppendLine("Facing was left unchanged because clavicle_l/clavicle_r were not found.");
                return 0f;
            }

            Vector3 side = right.position - left.position;
            side.y = 0f;
            if (side.sqrMagnitude < 0.00001f)
            {
                notes.AppendLine("Facing was left unchanged because the clavicles overlap.");
                return 0f;
            }

            Vector3 forward = Vector3.Cross(side.normalized, Vector3.up);
            float yaw = Vector3.SignedAngle(forward, Vector3.forward, Vector3.up);
            notes.AppendLine("Facing correction " + yaw.ToString("0.0") + " degrees toward +Z.");
            return yaw;
        }

        static void AssignUrpMaterials(GameObject instance, StringBuilder notes)
        {
            var top = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_Top.mat");
            var bottom = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_Bottom.mat");
            if (top == null || bottom == null)
                throw new InvalidOperationException("URP trooper materials were not created.");
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var slots = renderers[r].sharedMaterials;
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    string name = slots[i] != null ? slots[i].name : "";
                    Material replacement = bottom;
                    if (name.IndexOf("03", StringComparison.Ordinal) >= 0 || name.IndexOf("Top", StringComparison.OrdinalIgnoreCase) >= 0)
                        replacement = top;
                    else if (name.IndexOf("02", StringComparison.Ordinal) >= 0 || name.IndexOf("Bottom", StringComparison.OrdinalIgnoreCase) >= 0)
                        replacement = bottom;
                    else if (slots[i] != null && slots[i].shader != null && slots[i].shader.name.IndexOf("Universal Render Pipeline/Lit", StringComparison.Ordinal) >= 0)
                        continue;
                    if (slots[i] == replacement)
                        continue;
                    notes.AppendLine("Renderer material " + name + " -> " + replacement.name);
                    slots[i] = replacement;
                    changed = true;
                }

                if (changed)
                    renderers[r].sharedMaterials = slots;
            }
        }

        static void AlignFeet(Transform root)
        {
            if (!TryRendererBounds(root.gameObject, out Bounds bounds))
                return;
            root.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        }

        static void StripImportedGadgets(GameObject instance)
        {
            var cameras = instance.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
                UnityEngine.Object.DestroyImmediate(cameras[i]);
            var lights = instance.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
                UnityEngine.Object.DestroyImmediate(lights[i]);
            var listeners = instance.GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++)
                UnityEngine.Object.DestroyImmediate(listeners[i]);
        }

        static bool TryRendererBounds(GameObject instance, out Bounds bounds)
        {
            bounds = default;
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;
                if (!any)
                {
                    bounds = renderers[i].bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            return any && bounds.size.sqrMagnitude > 0.000001f;
        }

        static Transform FindChild(Transform root, string name)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (string.Equals(all[i].name, name, StringComparison.OrdinalIgnoreCase))
                    return all[i];
            }

            return null;
        }

        static void WireOpenScene(StringBuilder notes)
        {
            var walker = UnityEngine.Object.FindAnyObjectByType<FirstPersonWalker>();
            if (walker == null)
                throw new InvalidOperationException("CityWalker is not in the open scene.");
            var eyeCamera = walker.eye != null ? walker.eye.GetComponent<Camera>() : null;
            if (eyeCamera == null)
                throw new InvalidOperationException("The first-person eye camera is missing.");
            var eyeListener = eyeCamera.GetComponent<AudioListener>();
            var hud = walker.transform.Find("WalkHud") as RectTransform;
            if (hud == null)
                throw new InvalidOperationException("WalkHud is missing.");
            PrepareHud(hud);
            var welcome = BuildWelcome(hud);
            var pause = BuildPause(walker);
            DestroyNamed(walker.transform, "ThirdPersonCamera");
            Transform environment = walker.transform.parent != null ? walker.transform.parent : walker.transform;
            DestroyNamed(environment, "BirdsEyeCamera");
            var visual = CharacterAnimationSetup.EnsurePlayer(walker, notes);

            var thirdObject = new GameObject("ThirdPersonCamera");
            thirdObject.transform.SetParent(walker.transform, false);
            var thirdCamera = thirdObject.AddComponent<Camera>();
            var thirdListener = thirdObject.AddComponent<AudioListener>();
            var orbit = thirdObject.AddComponent<ThirdPersonOrbitCamera>();
            MatchCamera(eyeCamera, thirdCamera);
            thirdCamera.enabled = false;
            thirdListener.enabled = false;
            orbit.enabled = false;
            orbit.walker = walker;
            orbit.target = walker.transform;
            var presenter = walker.GetComponent<CharacterPresenter>();
            if (presenter != null && presenter.definition != null)
                orbit.pivotHeight = presenter.definition.cameraTargetHeight;

            var birdObject = new GameObject("BirdsEyeCamera");
            birdObject.transform.SetParent(environment, false);
            var birdCamera = birdObject.AddComponent<Camera>();
            var birdListener = birdObject.AddComponent<AudioListener>();
            var flyer = birdObject.AddComponent<BirdsEyeFlyer>();
            MatchCamera(eyeCamera, birdCamera);
            birdCamera.farClipPlane = Mathf.Max(eyeCamera.farClipPlane, 4500f);
            birdCamera.enabled = false;
            birdListener.enabled = false;
            flyer.enabled = false;
            flyer.walker = walker;

            var manager = walker.GetComponent<ViewModeManager>() ?? walker.gameObject.AddComponent<ViewModeManager>();
            manager.walker = walker;
            manager.firstPersonCamera = eyeCamera;
            manager.firstPersonListener = eyeListener;
            manager.thirdPerson = orbit;
            manager.thirdPersonCamera = thirdCamera;
            manager.thirdPersonListener = thirdListener;
            manager.birdsEye = flyer;
            manager.birdsEyeCamera = birdCamera;
            manager.birdsEyeListener = birdListener;
            manager.character = visual;
            manager.welcomeRoot = welcome;
            manager.pauseRoot = pause;
            manager.firstPersonButton = welcome.transform.Find("Panel/Cards/FirstPersonCard").GetComponent<Button>();
            manager.thirdPersonButton = welcome.transform.Find("Panel/Cards/ThirdPersonCard").GetComponent<Button>();
            manager.birdsEyeButton = welcome.transform.Find("Panel/Cards/BirdsEyeCard").GetComponent<Button>();
            manager.resumeButton = pause.transform.Find("Menu/ResumeButton").GetComponent<Button>();
            manager.changeViewButton = pause.transform.Find("Menu/ChangeViewButton").GetComponent<Button>();
            welcome.SetActive(true);
            pause.SetActive(false);
            eyeCamera.enabled = true;
            if (eyeListener != null)
                eyeListener.enabled = true;
            PlacementHudBuilder.Ensure(hud, false);
            AssetPlacementSetup.EnsurePlayerWiring(walker);
            notes.AppendLine("Wired welcome, pause, trooper, third-person camera, and bird camera on " + walker.name + ".");
            int buildings = UnityEngine.Object.FindObjectsByType<BuildingFeature>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int streets = UnityEngine.Object.FindObjectsByType<StreetFeature>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int chunks = UnityEngine.Object.FindObjectsByType<TerrainChunk>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            notes.AppendLine("Scene still has buildings=" + buildings + " streets=" + streets + " terrainChunks=" + chunks + ".");
        }

        static void PrepareHud(RectTransform hud)
        {
            hud.anchorMin = Vector2.zero;
            hud.anchorMax = Vector2.one;
            hud.offsetMin = Vector2.zero;
            hud.offsetMax = Vector2.zero;
            hud.pivot = new Vector2(0.5f, 0.5f);
            if (hud.localScale.sqrMagnitude < 0.001f)
                hud.localScale = Vector3.one;
        }

        static void MatchCamera(Camera source, Camera destination)
        {
            destination.clearFlags = source.clearFlags;
            destination.backgroundColor = source.backgroundColor;
            destination.fieldOfView = source.fieldOfView;
            destination.nearClipPlane = source.nearClipPlane;
            destination.farClipPlane = source.farClipPlane;
            destination.allowHDR = source.allowHDR;
            destination.allowMSAA = source.allowMSAA;
            destination.cullingMask = source.cullingMask;
            var from = source.GetUniversalAdditionalCameraData();
            var to = destination.GetUniversalAdditionalCameraData();
            to.renderPostProcessing = from.renderPostProcessing;
            to.renderShadows = from.renderShadows;
            to.antialiasing = from.antialiasing;
            to.antialiasingQuality = from.antialiasingQuality;
        }

        static GameObject BuildWelcome(RectTransform hud)
        {
            DestroyNamed(hud, "WelcomeWindow");
            var font = BuiltinFont();
            var sprite = UiSprite();
            var root = CreateUi("WelcomeWindow", hud, typeof(WelcomeWindowLayout));
            Stretch((RectTransform)root.transform);
            var dim = CreateImage("Dim", root.transform, sprite, new Color(0.02f, 0.025f, 0.03f, 0.56f));
            Stretch(dim.rectTransform);
            var panel = CreateImage("Panel", root.transform, sprite, new Color(0.10f, 0.09f, 0.08f, 0.94f));
            var panelRect = panel.rectTransform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1040f, 620f);
            var title = CreateText("Title", panel.transform, font, "Welcome to Nazareth", 42, FontStyle.Bold, new Color(1f, 0.98f, 0.94f, 1f), TextAnchor.MiddleCenter);
            AnchorTop(title.rectTransform, 28f, 72f);
            var subtitle = CreateText("Subtitle", panel.transform, font, "Choose how you want to explore the city.", 22, FontStyle.Normal, new Color(0.90f, 0.84f, 0.75f, 1f), TextAnchor.MiddleCenter);
            AnchorTop(subtitle.rectTransform, 104f, 40f);
            var cardsObject = new GameObject("Cards", typeof(RectTransform), typeof(GridLayoutGroup));
            cardsObject.transform.SetParent(panel.transform, false);
            var cards = cardsObject.GetComponent<RectTransform>();
            cards.anchorMin = new Vector2(0f, 0f);
            cards.anchorMax = new Vector2(1f, 1f);
            cards.offsetMin = new Vector2(28f, 28f);
            cards.offsetMax = new Vector2(-28f, -168f);
            var grid = cardsObject.GetComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(16f, 16f);
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.cellSize = new Vector2(300f, 200f);
            CreateCard(cards, font, sprite, "FirstPersonCard", "First Person", "Explore through your own eyes.", new Color(0.93f, 0.76f, 0.42f, 1f));
            CreateCard(cards, font, sprite, "ThirdPersonCard", "Third Person", "Explore with your character.", new Color(0.73f, 0.78f, 0.68f, 1f));
            CreateCard(cards, font, sprite, "BirdsEyeCard", "Bird’s-Eye View", "Fly above the city.", new Color(0.55f, 0.75f, 0.86f, 1f));
            var layout = root.GetComponent<WelcomeWindowLayout>();
            layout.panel = panelRect;
            layout.cards = cards;
            layout.grid = grid;
            return root;
        }

        static GameObject BuildPause(FirstPersonWalker walker)
        {
            var pause = walker.pausePanel;
            if (pause == null)
                throw new InvalidOperationException("Pause panel is missing.");
            var font = BuiltinFont();
            var sprite = UiSprite();
            var dim = pause.GetComponent<Image>();
            if (dim != null)
            {
                dim.color = new Color(0.02f, 0.025f, 0.03f, 0.5f);
                dim.raycastTarget = true;
            }

            for (int i = pause.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(pause.transform.GetChild(i).gameObject);
            var menu = CreateImage("Menu", pause.transform, sprite, new Color(0.10f, 0.09f, 0.08f, 0.94f));
            var menuRect = menu.rectTransform;
            menuRect.anchorMin = menuRect.anchorMax = new Vector2(0.5f, 0.5f);
            menuRect.pivot = new Vector2(0.5f, 0.5f);
            menuRect.sizeDelta = new Vector2(560f, 420f);
            var title = CreateText("Title", menu.transform, font, "Paused", 40, FontStyle.Bold, new Color(1f, 0.98f, 0.94f, 1f), TextAnchor.MiddleCenter);
            AnchorTop(title.rectTransform, 28f, 64f);
            var detail = CreateText("Detail", menu.transform, font, "Change View opens the same city selector.\nR returns to the street spawn.", 20, FontStyle.Normal, new Color(0.90f, 0.84f, 0.75f, 1f), TextAnchor.MiddleCenter);
            var detailRect = detail.rectTransform;
            detailRect.anchorMin = new Vector2(0.5f, 1f);
            detailRect.anchorMax = new Vector2(0.5f, 1f);
            detailRect.pivot = new Vector2(0.5f, 1f);
            detailRect.anchoredPosition = new Vector2(0f, -110f);
            detailRect.sizeDelta = new Vector2(480f, 80f);
            CreateMenuButton(menu.transform, font, sprite, "ResumeButton", "Resume", -210f);
            CreateMenuButton(menu.transform, font, sprite, "ChangeViewButton", "Change View", -300f);
            pause.SetActive(false);
            return pause;
        }

        static void CreateCard(Transform parent, Font font, Sprite sprite, string name, string title, string body, Color accent)
        {
            var image = CreateImage(name, parent, sprite, new Color(0.22f, 0.18f, 0.15f, 0.98f));
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = new Color(0.22f, 0.18f, 0.15f, 0.98f);
            colors.highlightedColor = new Color(0.40f, 0.33f, 0.24f, 1f);
            colors.pressedColor = new Color(0.55f, 0.44f, 0.28f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            var bar = CreateImage("Accent", image.transform, sprite, accent);
            bar.raycastTarget = false;
            var barRect = bar.rectTransform;
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.sizeDelta = new Vector2(0f, 8f);
            barRect.anchoredPosition = Vector2.zero;
            var titleText = CreateText("Label", image.transform, font, title, 26, FontStyle.Bold, new Color(1f, 0.97f, 0.92f, 1f), TextAnchor.MiddleLeft);
            var titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -28f);
            titleRect.sizeDelta = new Vector2(-36f, 44f);
            var bodyText = CreateText("Body", image.transform, font, body, 18, FontStyle.Normal, new Color(0.88f, 0.82f, 0.74f, 1f), TextAnchor.UpperLeft);
            var bodyRect = bodyText.rectTransform;
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(18f, 16f);
            bodyRect.offsetMax = new Vector2(-18f, -84f);
        }

        static void CreateMenuButton(Transform parent, Font font, Sprite sprite, string name, string label, float y)
        {
            var image = CreateImage(name, parent, sprite, new Color(0.24f, 0.20f, 0.16f, 1f));
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(420f, 72f);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = new Color(0.24f, 0.20f, 0.16f, 1f);
            colors.highlightedColor = new Color(0.42f, 0.34f, 0.24f, 1f);
            colors.pressedColor = new Color(0.56f, 0.44f, 0.28f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            var text = CreateText("Label", image.transform, font, label, 24, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
        }

        static GameObject CreateUi(string name, Transform parent, Type extra)
        {
            var go = extra != null ? new GameObject(name, typeof(RectTransform), extra) : new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        static Text CreateText(string name, Transform parent, Font font, string value, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        static void AnchorTop(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.sizeDelta = new Vector2(-48f, height);
        }

        static void DestroyNamed(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        static Font BuiltinFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        static Sprite UiSprite()
        {
            return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        }
    }
}
