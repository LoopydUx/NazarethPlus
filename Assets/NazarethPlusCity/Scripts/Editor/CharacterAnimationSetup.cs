using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NazarethPlusCity.EditorTools
{
    [InitializeOnLoad]
    public static class CharacterAnimationSetup
    {
        public const string AnimationFolder = "Assets/NazarethPlusCity/Characters/Animations";
        public const string ControllerPath = "Assets/NazarethPlusCity/Characters/CharacterLocomotion.controller";
        public const string DefinitionPath = "Assets/NazarethPlusCity/Characters/DefaultCharacter.asset";
        public const string IdleFbx = AnimationFolder + "/Stand--Idle.anim.fbx";
        public const string WalkFbx = AnimationFolder + "/Locomotion--Walk_N.anim.fbx";
        public const string RunFbx = AnimationFolder + "/Locomotion--Run_N.anim.fbx";
        public const string WalkLandFbx = AnimationFolder + "/Locomotion--Walk_N_Land.anim.fbx";
        public const string RunLandFbx = AnimationFolder + "/Locomotion--Run_N_Land.anim.fbx";
        public const string JumpFbx = AnimationFolder + "/Jump--Jump.anim.fbx";
        public const string FallFbx = AnimationFolder + "/Jump--InAir.anim.fbx";

        static readonly string[] RequiredHuman =
        {
            "Hips", "Spine", "Head",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot",
            "RightUpperLeg", "RightLowerLeg", "RightFoot",
            "LeftUpperArm", "LeftLowerArm", "LeftHand",
            "RightUpperArm", "RightLowerArm", "RightHand"
        };

        public static string ReportPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "CharacterAnimationReport.txt");

        public static string MarkerPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "CharacterAnimation.installed");

        static int _wait;
        static bool _queued;
        static bool _installing;

        static CharacterAnimationSetup()
        {
            EditorApplication.delayCall += Queue;
        }

        static void Queue()
        {
            if (_queued)
                return;
            _queued = true;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (Application.isBatchMode)
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying)
                return;
            if (File.Exists(MarkerPath))
            {
                EditorApplication.update -= Tick;
                return;
            }

            if (!ClipsReady() && _wait < 120)
            {
                _wait++;
                if (_wait == 1)
                    AssetDatabase.Refresh();
                return;
            }

            EditorApplication.update -= Tick;
            try
            {
                Install(false);
            }
            catch (Exception exception)
            {
                File.WriteAllText(ReportPath, "FAIL character animation setup\n" + exception);
                Debug.LogException(exception);
            }
        }

        [MenuItem("NazarethPlusCity/Rebuild Character Animation")]
        public static void RebuildFromMenu()
        {
            Install(true);
        }

        [MenuItem("NazarethPlusCity/Create Character Animation Override")]
        public static void CreateOverrideFromMenu()
        {
            var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(DefinitionPath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (definition == null || controller == null)
            {
                Debug.LogError("Rebuild the character animation before creating an override.");
                return;
            }

            const string path = "Assets/NazarethPlusCity/Characters/CharacterLocomotionOverride.overrideController";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (existing != null)
                AssetDatabase.DeleteAsset(path);
            var over = new AnimatorOverrideController(controller);
            AssetDatabase.CreateAsset(over, path);
            definition.animationOverride = over;
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            Debug.Log("Assigned " + path + ". Clear Animation Override on DefaultCharacter to use the shared controller again.");
        }

        public static void Install(bool force)
        {
            if (_installing)
                return;
            if (!force && File.Exists(MarkerPath))
                return;
            _installing = true;
            try
            {
            InstallBody();
            }
            finally
            {
                _installing = false;
            }
        }

        static void InstallBody()
        {
            var notes = new StringBuilder();
            notes.AppendLine("Nazareth character animation");
            DescribeClips(notes);
            var walker = UnityEngine.Object.FindAnyObjectByType<FirstPersonWalker>();
            if (walker == null && File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, ExplorationModeSetup.ScenePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                EditorSceneManager.OpenScene(ExplorationModeSetup.ScenePath, OpenSceneMode.Single);
                walker = UnityEngine.Object.FindAnyObjectByType<FirstPersonWalker>();
            }

            if (walker == null)
                throw new InvalidOperationException("CityWalker is not in the open scene.");
            var visual = EnsurePlayer(walker, notes);
            AssetDatabase.SaveAssets();
            Probe(visual, notes);
            var scene = walker.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            notes.AppendLine("Saved " + scene.path);
            File.WriteAllText(ReportPath, notes.ToString());
            File.WriteAllText(MarkerPath, DateTime.Now.ToString("u"));
            Debug.Log(notes.ToString());
        }

        public static void DescribeClips(StringBuilder notes)
        {
            notes.AppendLine("Starter Assets clips copied into " + AnimationFolder + ":");
            DescribeOne(notes, "Idle", LoadClip(IdleFbx, "Idle"));
            DescribeOne(notes, "Walk", LoadClip(WalkFbx, "Walk_N"));
            DescribeOne(notes, "Run", LoadClip(RunFbx, "Run_N"));
            DescribeOne(notes, "Jump", LoadClip(JumpFbx, "JumpStart"));
            DescribeOne(notes, "Fall", LoadClip(FallFbx, "InAir"));
            DescribeOne(notes, "Land", LoadClip(JumpFbx, "JumpLand"));
            DescribeOne(notes, "Land walk", LoadClip(WalkLandFbx, "Walk_N_Land"));
            DescribeOne(notes, "Land run", LoadClip(RunLandFbx, "Run_N_Land"));
            var trooper = AssetDatabase.LoadAllAssetsAtPath(ExplorationModeSetup.FbxPath);
            bool anyTrooperClip = false;
            for (int i = 0; i < trooper.Length; i++)
            {
                var clip = trooper[i] as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    continue;
                anyTrooperClip = true;
                notes.AppendLine("Trooper FBX clip is not used for locomotion: " + clip.name + " length=" + clip.length.ToString("0.###") + "s");
            }

            if (!anyTrooperClip)
                notes.AppendLine("The trooper FBX has no authored locomotion clips.");
        }

        public static CharacterVisual EnsurePlayer(FirstPersonWalker walker, StringBuilder notes)
        {
            if (walker == null)
                throw new InvalidOperationException("CityWalker is not available.");
            var definition = BuildDefinition(walker, notes);
            StripPrefabGameplay(definition, notes);
            if (walker.GetComponent<PlayerRoot>() == null)
                walker.gameObject.AddComponent<PlayerRoot>();
            var presenter = walker.GetComponent<CharacterPresenter>() ?? walker.gameObject.AddComponent<CharacterPresenter>();
            if (walker.GetComponent<CharacterLocomotion>() == null)
                walker.gameObject.AddComponent<CharacterLocomotion>();
            presenter.definition = definition;
            var host = presenter.EnsureHost();
            if (CharacterPresenter.FindModel(host) == null && definition.visualPrefab != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(definition.visualPrefab, host);
                instance.name = "CharacterModel";
            }

            presenter.BindExisting();
            var visual = host.GetComponent<CharacterVisual>();
            var manager = walker.GetComponent<ViewModeManager>();
            if (manager != null)
            {
                manager.character = visual;
                EditorUtility.SetDirty(manager);
            }

            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(walker);
            notes.AppendLine("Gameplay stays on " + walker.name + ". The model is under " + CharacterVisual.HostName + ".");
            return visual;
        }

        static CharacterDefinition BuildDefinition(FirstPersonWalker walker, StringBuilder notes)
        {
            PrepareClip(IdleFbx, notes);
            PrepareClip(WalkFbx, notes);
            PrepareClip(RunFbx, notes);
            PrepareClip(WalkLandFbx, notes);
            PrepareClip(RunLandFbx, notes);
            PrepareClip(JumpFbx, notes);
            PrepareClip(FallFbx, notes);
            var avatar = ConfigureRig(notes);
            var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<CharacterDefinition>();
                AssetDatabase.CreateAsset(definition, DefinitionPath);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ExplorationModeSetup.TrooperPrefabPath);
            definition.visualPrefab = prefab;
            if (avatar != null)
                definition.avatar = avatar;
            if (prefab != null)
            {
                definition.visualScale = prefab.transform.localScale;
                definition.visualEuler = prefab.transform.localEulerAngles;
                definition.footOffset = prefab.transform.localPosition;
            }

            definition.idle = LoadClip(IdleFbx, "Idle");
            definition.walk = LoadClip(WalkFbx, "Walk_N");
            definition.run = LoadClip(RunFbx, "Run_N");
            definition.jump = LoadClip(JumpFbx, "JumpStart");
            definition.fall = LoadClip(FallFbx, "InAir");
            definition.land = LoadClip(JumpFbx, "JumpLand");
            definition.landWalk = LoadClip(WalkLandFbx, "Walk_N_Land");
            definition.landRun = LoadClip(RunLandFbx, "Run_N_Land");
            definition.walkSpeedThreshold = walker != null ? walker.walkSpeed : 4.2f;
            definition.runSpeedThreshold = walker != null ? walker.sprintSpeed : 7.5f;
            float walkAuthored = PlanarSpeed(definition.walk);
            float runAuthored = PlanarSpeed(definition.run);
            notes.AppendLine("Walk clip average planar speed " + walkAuthored.ToString("0.00") + " m/s.");
            notes.AppendLine("Run clip average planar speed " + runAuthored.ToString("0.00") + " m/s.");
            if (walkAuthored > 0.3f)
                definition.walkClipSpeed = walkAuthored;
            else
                notes.AppendLine("Walk root speed was not readable. Foot cycle uses " + definition.walkClipSpeed.ToString("0.00") + " m/s.");
            if (runAuthored > 0.3f)
                definition.runClipSpeed = runAuthored;
            else
                notes.AppendLine("Run root speed was not readable. Foot cycle uses " + definition.runClipSpeed.ToString("0.00") + " m/s.");
            if (walker != null)
            {
                var body = walker.GetComponent<CharacterController>();
                if (body != null)
                {
                    definition.capsuleHeight = body.height;
                    definition.capsuleRadius = body.radius;
                    definition.capsuleCenterY = body.center.y;
                }

                definition.eyeHeight = walker.eyeHeight;
                var orbit = walker.GetComponentInChildren<ThirdPersonOrbitCamera>(true);
                if (orbit != null)
                    definition.cameraTargetHeight = orbit.pivotHeight;
            }

            bool hadOverride = definition.animationOverride != null;
            definition.locomotionController = BuildController(definition, notes);
            if (hadOverride)
            {
                definition.animationOverride = null;
                notes.AppendLine("Cleared the animation override because the shared controller was rebuilt. Use NazarethPlusCity → Create Character Animation Override to make a new one.");
            }

            EditorUtility.SetDirty(definition);
            return definition;
        }

        public static Avatar ConfigureRig(StringBuilder notes)
        {
            string path = ExplorationModeSetup.FbxPath;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException("FBX importer missing for " + path);
            var avatar = LoadAvatar(path);
            if (importer.animationType == ModelImporterAnimationType.Human && IsUsable(avatar, importer, out _))
            {
                notes.AppendLine("Humanoid avatar is valid. Hips=" + BoneName(importer, "Hips") + ".");
                return avatar;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.autoGenerateAvatarMappingIfUnspecified = true;
            importer.SaveAndReimport();
            importer = AssetImporter.GetAtPath(path) as ModelImporter;
            avatar = LoadAvatar(path);
            if (IsUsable(avatar, importer, out string autoMissing))
            {
                notes.AppendLine("Automatic humanoid mapping succeeded. Hips=" + BoneName(importer, "Hips") + ".");
                return avatar;
            }

            notes.AppendLine("Automatic humanoid mapping was incomplete (" + autoMissing + ").");
            if (!TryApplyAliasMap(importer, notes))
            {
                RevertGeneric(importer);
                notes.AppendLine("Reverted the model to a Generic rig so a bad humanoid map would not distort it.");
                return null;
            }

            importer.SaveAndReimport();
            importer = AssetImporter.GetAtPath(path) as ModelImporter;
            avatar = LoadAvatar(path);
            if (!IsUsable(avatar, importer, out string aliasMissing))
            {
                RevertGeneric(importer);
                notes.AppendLine("Standard bone-name mapping failed (" + aliasMissing + "). Reverted to Generic.");
                return null;
            }

            notes.AppendLine("Standard bone-name mapping succeeded. Hips=" + BoneName(importer, "Hips") + ".");
            return avatar;
        }

        static void RevertGeneric(ModelImporter importer)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
        }

        static bool TryApplyAliasMap(ModelImporter importer, StringBuilder notes)
        {
            string path = ExplorationModeSetup.FbxPath;
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null)
            {
                notes.AppendLine("The model could not be loaded for bone mapping.");
                return false;
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var human = new List<HumanBone>();
            bool complete = true;
            for (int i = 0; i < RequiredHuman.Length; i++)
            {
                if (!TryMap(transforms, used, RequiredHuman[i], true, human, notes))
                    complete = false;
            }

            TryMap(transforms, used, "Chest", false, human, notes);
            TryMap(transforms, used, "UpperChest", false, human, notes);
            TryMap(transforms, used, "Neck", false, human, notes);
            TryMap(transforms, used, "LeftShoulder", false, human, notes);
            TryMap(transforms, used, "RightShoulder", false, human, notes);
            TryMap(transforms, used, "LeftToes", false, human, notes);
            TryMap(transforms, used, "RightToes", false, human, notes);
            if (!complete)
                return false;

            var skeleton = new List<SkeletonBone>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < transforms.Length; i++)
            {
                string name = transforms[i].name;
                if (!seen.Add(name))
                    continue;
                skeleton.Add(new SkeletonBone
                {
                    name = name,
                    position = transforms[i].localPosition,
                    rotation = transforms[i].localRotation,
                    scale = transforms[i].localScale
                });
            }

            var description = importer.humanDescription;
            description.human = human.ToArray();
            description.skeleton = skeleton.ToArray();
            description.upperArmTwist = 0.5f;
            description.lowerArmTwist = 0.5f;
            description.upperLegTwist = 0.5f;
            description.lowerLegTwist = 0.5f;
            description.armStretch = 0.05f;
            description.legStretch = 0.05f;
            description.feetSpacing = 0f;
            importer.humanDescription = description;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.autoGenerateAvatarMappingIfUnspecified = false;
            notes.AppendLine("Wrote " + human.Count + " human bones and " + skeleton.Count + " skeleton bones into the model importer.");
            return true;
        }

        static bool TryMap(Transform[] transforms, HashSet<string> used, string humanName, bool required, List<HumanBone> human, StringBuilder notes)
        {
            string bone = FindAlias(transforms, used, humanName);
            if (string.IsNullOrEmpty(bone))
            {
                if (required)
                    notes.AppendLine("MISSING human bone " + humanName + ".");
                return !required;
            }

            used.Add(bone);
            human.Add(new HumanBone
            {
                boneName = bone,
                humanName = humanName,
                limit = new HumanLimit { useDefaultValues = true }
            });
            return true;
        }

        static string FindAlias(Transform[] transforms, HashSet<string> used, string humanName)
        {
            var aliases = Aliases(humanName);
            for (int a = 0; a < aliases.Length; a++)
            {
                for (int i = 0; i < transforms.Length; i++)
                {
                    string name = transforms[i].name;
                    if (used.Contains(name))
                        continue;
                    if (string.Equals(name, aliases[a], StringComparison.OrdinalIgnoreCase))
                        return name;
                }
            }

            return null;
        }

        static string[] Aliases(string humanName)
        {
            switch (humanName)
            {
                case "Hips": return new[] { "pelvis", "hips", "hip" };
                case "Spine": return new[] { "spine_01", "spine1", "spine" };
                case "Chest": return new[] { "spine_02", "spine2", "chest" };
                case "UpperChest": return new[] { "spine_03", "spine3", "upperchest" };
                case "Neck": return new[] { "neck_01", "neck" };
                case "Head": return new[] { "head" };
                case "LeftShoulder": return new[] { "clavicle_l", "leftshoulder", "shoulder_l" };
                case "LeftUpperArm": return new[] { "upperarm_l", "leftupperarm", "leftarm" };
                case "LeftLowerArm": return new[] { "lowerarm_l", "leftforearm", "leftlowerarm" };
                case "LeftHand": return new[] { "hand_l", "lefthand" };
                case "RightShoulder": return new[] { "clavicle_r", "rightshoulder", "shoulder_r" };
                case "RightUpperArm": return new[] { "upperarm_r", "rightupperarm", "rightarm" };
                case "RightLowerArm": return new[] { "lowerarm_r", "rightforearm", "rightlowerarm" };
                case "RightHand": return new[] { "hand_r", "righthand" };
                case "LeftUpperLeg": return new[] { "thigh_l", "leftupperleg", "leftupleg" };
                case "LeftLowerLeg": return new[] { "calf_l", "leftlowerleg", "leftleg" };
                case "LeftFoot": return new[] { "foot_l", "leftfoot" };
                case "RightUpperLeg": return new[] { "thigh_r", "rightupperleg", "rightupleg" };
                case "RightLowerLeg": return new[] { "calf_r", "rightlowerleg", "rightleg" };
                case "RightFoot": return new[] { "foot_r", "rightfoot" };
                case "LeftToes": return new[] { "ball_l", "toe_l", "toes_l" };
                case "RightToes": return new[] { "ball_r", "toe_r", "toes_r" };
                default: return new string[0];
            }
        }

        static AnimatorController BuildController(CharacterDefinition definition, StringBuilder notes)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter(CharacterLocomotion.SpeedParam, AnimatorControllerParameterType.Float);
            controller.AddParameter(CharacterLocomotion.GroundedParam, AnimatorControllerParameterType.Bool);
            controller.AddParameter(CharacterLocomotion.VerticalSpeedParam, AnimatorControllerParameterType.Float);
            controller.AddParameter(CharacterLocomotion.JumpParam, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(CharacterLocomotion.CycleSpeedParam, AnimatorControllerParameterType.Float);
            var parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == CharacterLocomotion.CycleSpeedParam)
                    parameters[i].defaultFloat = 1f;
                else if (parameters[i].name == CharacterLocomotion.GroundedParam)
                    parameters[i].defaultBool = true;
            }

            controller.parameters = parameters;
            float walk = Mathf.Max(0.5f, definition.walkSpeedThreshold);
            float run = Mathf.Max(walk + 0.5f, definition.runSpeedThreshold);
            var machine = controller.layers[0].stateMachine;
            var locomotion = machine.AddState("Locomotion", new Vector3(300f, 0f, 0f));
            var tree = new BlendTree
            {
                name = "Idle Walk Run",
                blendParameter = CharacterLocomotion.SpeedParam,
                blendType = BlendTreeType.Simple1D,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.children = new[]
            {
                Child(definition.idle, 0f),
                Child(definition.walk, walk),
                Child(definition.run, run)
            };
            tree.minThreshold = 0f;
            tree.maxThreshold = run;
            locomotion.motion = tree;
            locomotion.speedParameter = CharacterLocomotion.CycleSpeedParam;
            locomotion.speedParameterActive = true;
            locomotion.writeDefaultValues = true;

            var jump = machine.AddState("Jump", new Vector3(560f, -80f, 0f));
            jump.motion = definition.jump;
            jump.writeDefaultValues = true;
            var fall = machine.AddState("Fall", new Vector3(560f, 80f, 0f));
            fall.motion = definition.fall != null ? definition.fall : definition.jump;
            fall.writeDefaultValues = true;
            var land = machine.AddState("Land", new Vector3(300f, 180f, 0f));
            land.motion = LandMotion(definition, controller, walk, run);
            land.writeDefaultValues = true;
            machine.defaultState = locomotion;

            var toJump = Link(locomotion, jump, 0.08f, false, 0f);
            toJump.AddCondition(AnimatorConditionMode.If, 0f, CharacterLocomotion.JumpParam);
            var toFall = Link(locomotion, fall, 0.1f, false, 0f);
            toFall.AddCondition(AnimatorConditionMode.IfNot, 0f, CharacterLocomotion.GroundedParam);

            var jumpLand = Link(jump, land, 0.08f, false, 0f);
            jumpLand.AddCondition(AnimatorConditionMode.If, 0f, CharacterLocomotion.GroundedParam);
            var jumpFall = Link(jump, fall, 0.12f, false, 0f);
            jumpFall.AddCondition(AnimatorConditionMode.IfNot, 0f, CharacterLocomotion.GroundedParam);
            jumpFall.AddCondition(AnimatorConditionMode.Less, 0.15f, CharacterLocomotion.VerticalSpeedParam);

            var fallLand = Link(fall, land, 0.06f, false, 0f);
            fallLand.AddCondition(AnimatorConditionMode.If, 0f, CharacterLocomotion.GroundedParam);

            var landMove = Link(land, locomotion, 0.12f, true, 0.35f);
            landMove.AddCondition(AnimatorConditionMode.Greater, walk * 0.5f, CharacterLocomotion.SpeedParam);
            Link(land, locomotion, 0.14f, true, 0.72f);
            var landJump = Link(land, jump, 0.06f, false, 0f);
            landJump.AddCondition(AnimatorConditionMode.If, 0f, CharacterLocomotion.JumpParam);
            var landFall = Link(land, fall, 0.08f, false, 0f);
            landFall.AddCondition(AnimatorConditionMode.IfNot, 0f, CharacterLocomotion.GroundedParam);

            if (definition.fall == null)
                notes.AppendLine(definition.jump != null
                    ? "Fall uses the jump clip because no separate fall clip is assigned."
                    : "MISSING fall animation clip.");
            if (definition.land == null && definition.landWalk == null && definition.landRun == null)
                notes.AppendLine("MISSING land animation clip. Landing returns through an empty Land state.");
            EditorUtility.SetDirty(controller);
            notes.AppendLine("Built " + ControllerPath + " thresholds 0 / " + walk.ToString("0.00") + " / " + run.ToString("0.00") + ".");
            return controller;
        }

        static Motion LandMotion(CharacterDefinition definition, AnimatorController controller, float walk, float run)
        {
            if (definition.landWalk == null && definition.landRun == null)
                return definition.land;
            var tree = new BlendTree
            {
                name = "Land",
                blendParameter = CharacterLocomotion.SpeedParam,
                blendType = BlendTreeType.Simple1D,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            var children = new List<ChildMotion>();
            var standing = definition.land != null ? definition.land : definition.landWalk;
            if (standing != null)
                children.Add(Child(standing, 0f));
            if (definition.landWalk != null)
                children.Add(Child(definition.landWalk, walk));
            if (definition.landRun != null)
                children.Add(Child(definition.landRun, run));
            else if (definition.landWalk != null)
                children.Add(Child(definition.landWalk, run));
            tree.children = children.ToArray();
            tree.minThreshold = 0f;
            tree.maxThreshold = run;
            return tree;
        }

        static ChildMotion Child(AnimationClip clip, float threshold)
        {
            return new ChildMotion
            {
                motion = clip,
                threshold = threshold,
                timeScale = 1f,
                directBlendParameter = CharacterLocomotion.SpeedParam
            };
        }

        static AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float duration, bool exit, float exitTime)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = exit;
            transition.exitTime = exitTime;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
            return transition;
        }

        static void StripPrefabGameplay(CharacterDefinition definition, StringBuilder notes)
        {
            string path = ExplorationModeSetup.TrooperPrefabPath;
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
                return;
            bool changed = false;
            var visuals = root.GetComponentsInChildren<CharacterVisual>(true);
            for (int i = 0; i < visuals.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(visuals[i]);
                changed = true;
            }

            var motions = root.GetComponentsInChildren<CharacterLocomotion>(true);
            for (int i = 0; i < motions.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(motions[i]);
                changed = true;
            }

            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (definition.locomotionController != null)
                    animator.runtimeAnimatorController = definition.locomotionController;
                if (definition.avatar != null)
                    animator.avatar = definition.avatar;
                changed = true;
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                notes.AppendLine("Visual prefab keeps the model and Animator only.");
            }

            PrefabUtility.UnloadPrefabContents(root);
        }

        static void Probe(CharacterVisual visual, StringBuilder notes)
        {
            if (visual == null)
            {
                notes.AppendLine("FAIL character visual is missing.");
                return;
            }

            var walker = visual.GetComponentInParent<FirstPersonWalker>();
            bool onRoot = walker != null
                && walker.GetComponent<CharacterController>() != null
                && walker.GetComponent<PlayerRoot>() != null
                && walker.GetComponent<CharacterLocomotion>() != null
                && walker.GetComponent<CharacterPresenter>() != null
                && visual.transform.parent == walker.transform
                && visual.GetComponent<Animator>() == null;
            notes.AppendLine(onRoot ? "PASS gameplay root owns movement and the visual is a child." : "FAIL character hierarchy is not separated.");
            var placement = walker != null ? walker.GetComponent<AssetPlacementController>() : null;
            notes.AppendLine(placement != null ? "PASS bin placement component remains on the gameplay root." : "FAIL bin placement component is missing.");
            var cameras = walker != null ? walker.GetComponentsInChildren<Camera>(true) : new Camera[0];
            int cameraCount = 0;
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null)
                    cameraCount++;
            }

            var bird = UnityEngine.Object.FindAnyObjectByType<BirdsEyeFlyer>(FindObjectsInactive.Include);
            notes.AppendLine(cameraCount >= 1 && bird != null
                ? "PASS first-person camera and bird camera are still present."
                : "FAIL a gameplay camera is missing.");

            bool[] enabled = null;
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                enabled = new bool[renderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                {
                    enabled[i] = renderers[i].enabled;
                    renderers[i].enabled = true;
                }
            }

            bool feet = false;
            if (visual.TryWorldBounds(out Bounds bounds) && walker != null)
            {
                float offset = bounds.min.y - walker.transform.position.y;
                notes.AppendLine("INFO feet offset " + offset.ToString("0.000") + " height " + bounds.size.y.ToString("0.00"));
                feet = Mathf.Abs(offset) < 0.2f && bounds.size.y > 1.45f && bounds.size.y < 2.15f;
            }

            if (enabled != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].enabled = enabled[i];
            }

            notes.AppendLine(feet ? "PASS feet sit on the capsule and the body is human height." : "FAIL feet or body height look wrong.");
            ProbeMovement(walker, notes);

            var definition = walker != null && walker.GetComponent<CharacterPresenter>() != null
                ? walker.GetComponent<CharacterPresenter>().definition
                : null;
            var prefab = definition != null ? definition.visualPrefab : null;
            if (prefab == null || definition.ActiveController == null)
            {
                notes.AppendLine("FAIL definition or controller is missing, so the pose was not probed.");
                return;
            }

            var temp = UnityEngine.Object.Instantiate(prefab);
            temp.name = "CharacterProbe";
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animator = temp.GetComponentInChildren<Animator>(true);
                if (animator == null)
                    animator = temp.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.avatar = definition.avatar != null ? definition.avatar : animator.avatar;
                animator.runtimeAnimatorController = definition.ActiveController;
                animator.Rebind();
                animator.Update(0f);
                animator.Play("Locomotion", 0, 0f);
                animator.Update(0.5f);
                Vector3 local = animator.transform.localPosition;
                animator.Update(0.5f);
                float drift = (animator.transform.localPosition - local).magnitude;
                notes.AppendLine(drift < 0.01f ? "PASS animation does not move the model." : "FAIL animation moved the model by " + drift.ToString("0.000") + ".");
                bool human = animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
                notes.AppendLine(human ? "PASS avatar is a valid humanoid." : "FAIL avatar is missing or not a valid humanoid.");
                var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                var rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                if (upper == null || lower == null || hips == null || head == null || foot == null)
                {
                    notes.AppendLine("FAIL humanoid bones could not be read. The pose was not checked.");
                }
                else
                {
                    float down = Vector3.Dot((lower.position - upper.position).normalized, Vector3.down);
                    bool headAbove = head.position.y > hips.position.y + 0.15f;
                    bool footBelow = foot.position.y < hips.position.y - 0.15f;
                    float leftLength = Vector3.Distance(upper.position, lower.position);
                    float rightLength = rightUpper != null && rightLower != null ? Vector3.Distance(rightUpper.position, rightLower.position) : leftLength;
                    bool symmetric = rightLength > 0.01f && Mathf.Abs(leftLength - rightLength) / rightLength < 0.4f;
                    notes.AppendLine("INFO idle arm-down " + down.ToString("0.00") + " headAbove=" + headAbove + " footBelow=" + footBelow + " armSymmetry=" + symmetric);
                    notes.AppendLine(down > 0.25f && headAbove && footBelow && symmetric
                        ? "PASS idle pose is a standing humanoid, not a T-pose."
                        : "FAIL idle pose looks like a T-pose or a scrambled skeleton.");
                }

                var locomotion = temp.AddComponent<CharacterLocomotion>();
                locomotion.BindAnimator();
                locomotion.ApplyMotion(4.2f, -2f, true, false, 0.25f);
                animator.Update(0.05f);
                float speed = animator.GetFloat(CharacterLocomotion.SpeedParam);
                bool walking = speed > 1.5f && InState(animator, "Locomotion");
                locomotion.ApplyMotion(0f, 6.5f, true, true, 0.05f);
                animator.Update(0.12f);
                bool jumping = InState(animator, "Jump");
                locomotion.ApplyMotion(0f, -4f, false, false, 0.2f);
                animator.Update(0.2f);
                bool falling = InState(animator, "Fall") || InState(animator, "Jump");
                locomotion.ApplyMotion(0f, -1f, true, false, 0.2f);
                animator.Update(0.25f);
                bool landed = InState(animator, "Land") || InState(animator, "Locomotion");
                notes.AppendLine(walking ? "PASS walk speed enters the locomotion blend." : "FAIL walk speed did not drive locomotion (" + speed.ToString("0.00") + ").");
                notes.AppendLine(jumping ? "PASS jump trigger enters Jump." : "FAIL jump trigger did not enter Jump.");
                notes.AppendLine(falling ? "PASS leaving the ground reaches the air states." : "FAIL falling did not leave locomotion.");
                notes.AppendLine(landed ? "PASS landing returns toward locomotion." : "FAIL landing did not return toward locomotion.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temp);
            }
        }

        static void ProbeMovement(FirstPersonWalker walker, StringBuilder notes)
        {
            if (walker == null)
                return;
            var controller = walker.GetComponent<CharacterController>();
            if (controller == null || !controller.enabled)
            {
                notes.AppendLine("UNVERIFIED obstacle stop because the capsule is disabled.");
                return;
            }

            Vector3 origin = walker.transform.position;
            float yaw = walker.transform.eulerAngles.y;
            try
            {
                Vector3 chest = origin + Vector3.up * 0.9f;
                bool foundWall = false;
                RaycastHit wall = default;
                for (int i = 0; i < 24 && !foundWall; i++)
                {
                    Vector3 direction = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
                    if (!Physics.Raycast(chest, direction, out RaycastHit hit, 25f, ~0, QueryTriggerInteraction.Ignore))
                        continue;
                    if (hit.collider == null || hit.collider.transform == walker.transform || hit.collider.transform.IsChildOf(walker.transform))
                        continue;
                    if (Mathf.Abs(hit.normal.y) > 0.35f)
                        continue;
                    wall = hit;
                    foundWall = true;
                }

                if (!foundWall)
                {
                    notes.AppendLine("UNVERIFIED obstacle stop. No upright surface was found within 25 m.");
                }
                else
                {
                    Vector3 flatNormal = wall.normal;
                    flatNormal.y = 0f;
                    flatNormal.Normalize();
                    Vector3 stand = wall.point + flatNormal * (controller.radius + 0.2f);
                    stand.y = origin.y;
                    if (Physics.Raycast(stand + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 6f, ~0, QueryTriggerInteraction.Ignore))
                        stand.y = ground.point.y;
                    float face = Quaternion.LookRotation(-flatNormal, Vector3.up).eulerAngles.y;
                    walker.Teleport(stand, face, false);
                    Physics.SyncTransforms();
                    Vector3 before = walker.transform.position;
                    for (int i = 0; i < 8; i++)
                        controller.Move(-flatNormal * walker.walkSpeed * 0.05f + Vector3.down * 0.02f);
                    float slipped = Vector3.ProjectOnPlane(walker.transform.position - before, Vector3.up).magnitude;
                    notes.AppendLine("INFO wall stop moved " + slipped.ToString("0.000") + " m toward " + wall.collider.name);
                    notes.AppendLine(slipped < 0.35f
                        ? "PASS the capsule stops against an obstacle."
                        : "FAIL the capsule kept moving into an obstacle.");
                }

                bool foundSlope = false;
                RaycastHit slope = default;
                for (int i = 0; i < 16 && !foundSlope; i++)
                {
                    Vector3 sample = origin + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * (2f + i);
                    if (!Physics.Raycast(sample + Vector3.up * 8f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                        continue;
                    float angle = Vector3.Angle(hit.normal, Vector3.up);
                    if (angle < 8f || angle > 40f)
                        continue;
                    slope = hit;
                    foundSlope = true;
                }

                if (!foundSlope)
                {
                    notes.AppendLine("UNVERIFIED slope walk. No 8–40 degree surface was found near the player.");
                }
                else
                {
                    Vector3 stand = slope.point + slope.normal * 0.05f;
                    Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, slope.normal).normalized;
                    if (downhill.sqrMagnitude < 0.01f)
                        downhill = Vector3.forward;
                    float face = Quaternion.LookRotation(downhill, Vector3.up).eulerAngles.y;
                    walker.Teleport(stand, face, false);
                    Physics.SyncTransforms();
                    controller.Move(Vector3.down * 0.2f);
                    bool grounded = controller.isGrounded;
                    Vector3 before = walker.transform.position;
                    controller.Move(downhill * walker.walkSpeed * 0.08f + Vector3.down * 0.05f);
                    float moved = Vector3.ProjectOnPlane(walker.transform.position - before, Vector3.up).magnitude;
                    notes.AppendLine("INFO slope " + Vector3.Angle(slope.normal, Vector3.up).ToString("0") + " deg moved " + moved.ToString("0.000") + " grounded " + grounded);
                    notes.AppendLine(grounded && moved > 0.01f
                        ? "PASS the capsule stays on a slope while it moves."
                        : "FAIL the capsule left the slope or did not move along it.");
                }
            }
            finally
            {
                walker.Teleport(origin, yaw, true);
                Physics.SyncTransforms();
            }
        }

        static bool InState(Animator animator, string stateName)
        {
            if (animator.GetCurrentAnimatorStateInfo(0).IsName(stateName))
                return true;
            return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(stateName);
        }

        static void PrepareClip(string path, StringBuilder notes)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                notes.AppendLine("MISSING animation file " + path);
                return;
            }

            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                changed = true;
            }

            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }

            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                changed = true;
            }

            var clipAnimations = importer.clipAnimations;
            bool eventsCleared = false;
            if (clipAnimations != null)
            {
                for (int i = 0; i < clipAnimations.Length; i++)
                {
                    if (clipAnimations[i].events != null && clipAnimations[i].events.Length > 0)
                    {
                        clipAnimations[i].events = new AnimationEvent[0];
                        eventsCleared = true;
                    }
                }
            }

            if (eventsCleared)
            {
                importer.clipAnimations = clipAnimations;
                changed = true;
            }

            if (!changed)
                return;
            importer.SaveAndReimport();
            notes.AppendLine("Prepared " + path + " as an in-place humanoid clip.");
        }

        static bool ClipsReady()
        {
            return LoadClip(IdleFbx, "Idle") != null && LoadClip(WalkFbx, "Walk_N") != null && LoadClip(RunFbx, "Run_N") != null;
        }

        public static AnimationClip LoadClip(string path, string clipName)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            AnimationClip fallback = null;
            for (int i = 0; i < assets.Length; i++)
            {
                var clip = assets[i] as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    continue;
                if (clip.name == clipName)
                    return clip;
                fallback = clip;
            }

            return fallback;
        }

        static void DescribeOne(StringBuilder notes, string label, AnimationClip clip)
        {
            if (clip == null)
            {
                notes.AppendLine("MISSING " + label + " animation clip.");
                return;
            }

            notes.AppendLine(label + " " + clip.name
                + " length=" + clip.length.ToString("0.###")
                + "s human=" + clip.isHumanMotion
                + " loop=" + clip.isLooping
                + " rootCurves=" + clip.hasRootCurves);
        }

        static float PlanarSpeed(AnimationClip clip)
        {
            if (clip == null)
                return 0f;
            Vector3 speed = clip.averageSpeed;
            speed.y = 0f;
            return speed.magnitude;
        }

        static Avatar LoadAvatar(string path)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            Avatar found = null;
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Avatar avatar)
                    found = avatar;
            }

            return found;
        }

        static bool IsUsable(Avatar avatar, ModelImporter importer, out string missing)
        {
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                missing = avatar == null ? "no avatar" : (!avatar.isValid ? "avatar invalid" : "not human");
                return false;
            }

            return RequiredMapped(importer != null ? importer.humanDescription : default, out missing);
        }

        static bool RequiredMapped(HumanDescription description, out string missing)
        {
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var bones = description.human;
            if (bones != null)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (!string.IsNullOrEmpty(bones[i].boneName) && !string.IsNullOrEmpty(bones[i].humanName))
                        have.Add(bones[i].humanName);
                }
            }

            var gaps = new StringBuilder();
            for (int i = 0; i < RequiredHuman.Length; i++)
            {
                if (have.Contains(RequiredHuman[i]))
                    continue;
                if (gaps.Length > 0)
                    gaps.Append(", ");
                gaps.Append(RequiredHuman[i]);
            }

            missing = gaps.ToString();
            return gaps.Length == 0;
        }

        static string BoneName(ModelImporter importer, string humanName)
        {
            var bones = importer.humanDescription.human;
            if (bones == null)
                return "unmapped";
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i].humanName == humanName)
                    return string.IsNullOrEmpty(bones[i].boneName) ? "unmapped" : bones[i].boneName;
            }

            return "unmapped";
        }
    }
}
