using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NazarethPlusCity.EditorTools
{
    [InitializeOnLoad]
    public static class ExplorationModeVerification
    {
        const string PhaseKey = "NazarethPlus.VerifyPhase";
        const string FrameKey = "NazarethPlus.VerifyFrames";
        const string FailKey = "NazarethPlus.VerifyFails";
        const string StayPhaseKey = "NazarethPlus.ViewSwitchPhase";
        const string StayFrameKey = "NazarethPlus.ViewSwitchFrames";

        static ExplorationModeVerification()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(PhaseKey, "")))
                EditorApplication.update += Tick;
            if (!string.IsNullOrEmpty(SessionState.GetString(StayPhaseKey, "")))
                EditorApplication.update += TickStay;
        }

        [MenuItem("NazarethPlusCity/Verify View Switching")]
        public static void BeginInEditor()
        {
            if (EditorSceneManager.GetActiveScene().path != ExplorationModeSetup.ScenePath)
                EditorSceneManager.OpenScene(ExplorationModeSetup.ScenePath, OpenSceneMode.Single);
            SessionState.SetString(StayPhaseKey, "enter");
            SessionState.SetInt(StayFrameKey, 0);
            EditorApplication.update -= TickStay;
            EditorApplication.update += TickStay;
        }

        public static void BeginBatch()
        {
            EditorSceneManager.OpenScene(ExplorationModeSetup.ScenePath, OpenSceneMode.Single);
            Begin();
        }

        public static void Begin()
        {
            SessionState.SetString(PhaseKey, "enter");
            SessionState.SetInt(FrameKey, 0);
            SessionState.SetInt(FailKey, 0);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            string phase = SessionState.GetString(PhaseKey, "");
            if (string.IsNullOrEmpty(phase))
            {
                EditorApplication.update -= Tick;
                return;
            }

            if (phase == "enter")
            {
                if (!EditorApplication.isPlaying)
                    return;
                int frames = SessionState.GetInt(FrameKey, 0) + 1;
                SessionState.SetInt(FrameKey, frames);
                if (frames < 12)
                    return;
                int fails = 1;
                try
                {
                    fails = RunChecks();
                }
                catch (Exception exception)
                {
                    File.AppendAllText(ExplorationModeSetup.ReportPath, "\nFAIL verification threw\n" + exception);
                    Debug.LogException(exception);
                    fails = 1;
                }

                SessionState.SetInt(FailKey, fails);
                SessionState.SetString(PhaseKey, "exit");
                EditorApplication.isPlaying = false;
                return;
            }

            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            int code = SessionState.GetInt(FailKey, 1) > 0 ? 2 : 0;
            SessionState.EraseString(PhaseKey);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(code);
        }

        static void TickStay()
        {
            string phase = SessionState.GetString(StayPhaseKey, "");
            if (string.IsNullOrEmpty(phase))
            {
                EditorApplication.update -= TickStay;
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (phase == "enter")
            {
                if (!EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = true;
                    return;
                }

                int frames = SessionState.GetInt(StayFrameKey, 0) + 1;
                SessionState.SetInt(StayFrameKey, frames);
                if (frames < 12)
                    return;
                try
                {
                    RunChecks();
                }
                catch (Exception exception)
                {
                    File.AppendAllText(ExplorationModeSetup.ReportPath, "\nFAIL view switch verification threw\n" + exception);
                    Debug.LogException(exception);
                }

                SessionState.SetString(StayPhaseKey, "done");
                EditorApplication.isPlaying = false;
                return;
            }

            if (EditorApplication.isPlaying)
                return;
            SessionState.EraseString(StayPhaseKey);
            EditorApplication.update -= TickStay;
        }

        static int RunChecks()
        {
            var report = new StringBuilder();
            if (File.Exists(ExplorationModeSetup.ReportPath))
                report.AppendLine(File.ReadAllText(ExplorationModeSetup.ReportPath).TrimEnd());
            report.AppendLine();
            report.AppendLine("Play mode verification");
            int fails = 0;
            var manager = UnityEngine.Object.FindAnyObjectByType<ViewModeManager>();
            var walker = UnityEngine.Object.FindAnyObjectByType<FirstPersonWalker>();
            Check(report, ref fails, "manager exists", manager != null);
            Check(report, ref fails, "single walker", Count<FirstPersonWalker>() == 1);
            Check(report, ref fails, "single character controller", Count<CharacterController>() == 1);
            if (manager == null || walker == null)
            {
                Write(report, fails);
                return Mathf.Max(1, fails);
            }

            Vector3 launchPosition = walker.transform.position;
            float launchYaw = walker.Heading;

            Check(report, ref fails, "welcome open on play", manager.WelcomeOpen && manager.welcomeRoot != null && manager.welcomeRoot.activeInHierarchy);
            Check(report, ref fails, "welcome title", HasText(manager.welcomeRoot, "Welcome to Nazareth"));
            Check(report, ref fails, "welcome subtitle", HasText(manager.welcomeRoot, "Choose how you want to explore the city."));
            Check(report, ref fails, "first person card", HasText(manager.welcomeRoot, "First Person") && HasText(manager.welcomeRoot, "Explore through your own eyes."));
            Check(report, ref fails, "third person card", HasText(manager.welcomeRoot, "Third Person") && HasText(manager.welcomeRoot, "Explore with your character."));
            Check(report, ref fails, "bird card", HasText(manager.welcomeRoot, "Bird’s-Eye View") && HasText(manager.welcomeRoot, "Fly above the city."));
            Check(report, ref fails, "cursor unlocked", Cursor.lockState == CursorLockMode.None && Cursor.visible);
            Check(report, ref fails, "gameplay blocked", walker.InputBlocked && !walker.ApplyGameplayFrame(new CityControlFrame { move = Vector2.up, look = new Vector2(30f, 0f) }));
            Vector3 blockedPosition = walker.transform.position;
            float blockedYaw = walker.Heading;
            Check(report, ref fails, "blocked frame does not move", (walker.transform.position - blockedPosition).sqrMagnitude < 0.0001f && Mathf.Abs(Mathf.DeltaAngle(blockedYaw, walker.Heading)) < 0.01f);
            var empty = walker.ReadControls();
            Check(report, ref fails, "welcome reads no look or move", empty.move == Vector2.zero && empty.look == Vector2.zero);
            manager.OnEscape();
            Check(report, ref fails, "escape cannot skip the first choice", manager.WelcomeOpen && manager.Current == ExplorationMode.None);
            Check(report, ref fails, "one camera during welcome", EnabledCount<Camera>() == 1 && manager.firstPersonCamera != null && manager.firstPersonCamera.enabled);
            Check(report, ref fails, "one listener during welcome", EnabledCount<AudioListener>() == 1);
            Check(report, ref fails, "character hidden during welcome", manager.character == null || !manager.character.AnyVisible);

            var layout = manager.welcomeRoot.GetComponent<WelcomeWindowLayout>();
            layout?.Apply();
            report.AppendLine("INFO screen " + Screen.width + "x" + Screen.height
                + " panel " + (layout != null && layout.panel != null ? layout.panel.sizeDelta.ToString() : "missing")
                + " columns " + (layout != null && layout.grid != null ? layout.grid.constraintCount.ToString() : "missing"));

            Invoke(manager.firstPersonButton);
            Check(report, ref fails, "first person button", manager.Current == ExplorationMode.FirstPerson && !manager.WelcomeOpen);
            Check(report, ref fails, "first view keeps the launch position", (walker.transform.position - launchPosition).sqrMagnitude < 0.0004f);
            Check(report, ref fails, "first view keeps the launch heading", Mathf.Abs(Mathf.DeltaAngle(launchYaw, walker.Heading)) < 0.2f);
            Check(report, ref fails, "launch position is the street spawn", (launchPosition - walker.spawnPosition).sqrMagnitude < 0.0004f && Mathf.Abs(Mathf.DeltaAngle(launchYaw, walker.spawnYaw)) < 0.2f);
            Check(report, ref fails, "first person camera only", manager.firstPersonCamera.enabled && !manager.thirdPersonCamera.enabled && !manager.birdsEyeCamera.enabled);
            Check(report, ref fails, "first person listener only", EnabledCount<AudioListener>() == 1 && manager.firstPersonListener.enabled);
            Check(report, ref fails, "character hidden in first person", manager.character != null && !manager.character.AnyVisible);
            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked && !Cursor.visible;
            if (cursorLocked)
                report.AppendLine("PASS cursor locked in first person");
            else if (Application.isBatchMode)
                report.AppendLine("UNVERIFIED cursor lock in batch mode (lockState=" + Cursor.lockState + " visible=" + Cursor.visible + "). Play Mode in the editor still calls Cursor.lockState = Locked.");
            else
                Check(report, ref fails, "cursor locked in first person", false);
            Vector3 firstPosition = walker.transform.position;
            float firstYaw = walker.Heading;
            bool moved = walker.ApplyGameplayFrame(new CityControlFrame { move = new Vector2(0f, 1f) });
            float movedDistance = (walker.transform.position - firstPosition).magnitude;
            report.AppendLine("INFO first person step " + movedDistance.ToString("0.000") + " dt " + Time.deltaTime.ToString("0.0000"));
            Check(report, ref fails, "first person frame accepted", moved);
            Check(report, ref fails, "first person frame moves", Time.deltaTime < 0.002f || movedDistance > 0.001f);
            walker.Teleport(firstPosition, firstYaw, true);
            float yawBeforeLook = walker.Heading;
            walker.ApplyGameplayFrame(new CityControlFrame { look = new Vector2(25f, 0f) });
            Check(report, ref fails, "first person look yaws the body", Mathf.Abs(Mathf.DeltaAngle(yawBeforeLook, walker.Heading)) > 0.2f);
            walker.Teleport(firstPosition, firstYaw, true);

            Invoke(manager.thirdPersonButton);
            Check(report, ref fails, "third person keeps position", (walker.transform.position - firstPosition).sqrMagnitude < 0.0004f);
            Check(report, ref fails, "third person keeps heading", Mathf.Abs(Mathf.DeltaAngle(firstYaw, walker.Heading)) < 0.2f);
            Check(report, ref fails, "third person camera only", manager.thirdPersonCamera.enabled && !manager.firstPersonCamera.enabled && !manager.birdsEyeCamera.enabled);
            Check(report, ref fails, "one listener in third person", EnabledCount<AudioListener>() == 1 && manager.thirdPersonListener.enabled && !manager.firstPersonListener.enabled);
            Check(report, ref fails, "trooper visible", manager.character != null && manager.character.AnyVisible);
            if (manager.character != null && manager.character.TryWorldBounds(out Bounds bounds))
            {
                float feet = bounds.min.y - walker.transform.position.y;
                report.AppendLine("INFO trooper feet offset " + feet.ToString("0.000") + " height " + bounds.size.y.ToString("0.00"));
                Check(report, ref fails, "trooper feet on capsule", Mathf.Abs(feet) < 0.2f);
                Check(report, ref fails, "trooper human height", bounds.size.y > 1.45f && bounds.size.y < 2.15f);
                bool urp = true;
                string shaderName = "";
                var renderers = manager.character.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var materials = renderers[i].sharedMaterials;
                    for (int m = 0; m < materials.Length; m++)
                    {
                        shaderName = materials[m] != null && materials[m].shader != null ? materials[m].shader.name : "missing";
                        if (materials[m] == null || materials[m].shader == null || shaderName.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 || shaderName.IndexOf("Hidden/InternalErrorShader", StringComparison.Ordinal) >= 0 || shaderName.IndexOf("Universal Render Pipeline", StringComparison.Ordinal) < 0)
                            urp = false;
                    }
                }

                report.AppendLine("INFO trooper shader " + shaderName);
                Check(report, ref fails, "trooper materials are URP", urp);
            }
            else
            {
                Check(report, ref fails, "trooper bounds", false);
            }

            CheckCharacterAnimation(report, ref fails, walker, manager);
            Check(report, ref fails, "third person camera collision", CameraPullsInNearBuilding(walker, manager.thirdPerson, report));
            Vector3 grounded = walker.transform.position;
            float groundedYaw = walker.Heading;
            float safeY = walker.LastSafePosition.y;

            Invoke(manager.birdsEyeButton);
            Check(report, ref fails, "bird mode camera", manager.Current == ExplorationMode.BirdsEye && manager.birdsEyeCamera.enabled && !manager.firstPersonCamera.enabled && !manager.thirdPersonCamera.enabled);
            Check(report, ref fails, "one listener in bird mode", EnabledCount<AudioListener>() == 1 && manager.birdsEyeListener.enabled);
            Check(report, ref fails, "walker parked", walker.drive == WalkerDrive.Parked && !walker.ApplyGameplayFrame(new CityControlFrame { move = Vector2.up }));
            Check(report, ref fails, "parked player stays grounded", Mathf.Abs(walker.transform.position.y - grounded.y) < 0.05f);
            float flown = manager.birdsEye.transform.position.y;
            Check(report, ref fails, "bird starts above the player", flown > walker.transform.position.y + 20f);
            bool aboveGround = true;
            if (manager.birdsEye.TryHighestSurface(manager.birdsEye.transform.position.x, manager.birdsEye.transform.position.z, out float surface))
                aboveGround = manager.birdsEye.transform.position.y >= surface + manager.birdsEye.minGroundClearance - 0.05f;
            Check(report, ref fails, "bird starts clear of terrain", aboveGround);
            Vector3 buried = new Vector3(grounded.x, grounded.y - 30f, grounded.z);
            Vector3 raised = manager.birdsEye.Confine(buried, grounded + Vector3.up * 40f);
            bool raisedClear = true;
            if (manager.birdsEye.TryHighestSurface(raised.x, raised.z, out float raisedSurface))
                raisedClear = raised.y >= raisedSurface + manager.birdsEye.minGroundClearance - 0.05f;
            report.AppendLine("INFO buried y " + buried.y.ToString("0.00") + " confined y " + raised.y.ToString("0.00"));
            Check(report, ref fails, "bird confine stays above terrain", raisedClear && raised.y > buried.y + 5f);
            Vector3 outside = manager.birdsEye.Confine(new Vector3(90000f, raised.y + 30f, 90000f), raised);
            var map = UnityEngine.Object.FindAnyObjectByType<CityBoundaryMap>();
            bool inside = map == null || map.Contains(new Vector2(outside.x, outside.z));
            report.AppendLine("INFO boundary confine " + outside.ToString("0.0"));
            Check(report, ref fails, "bird respects city boundary", inside && outside.sqrMagnitude < 90000f * 90000f);
            manager.birdsEye.transform.position = new Vector3(grounded.x, grounded.y + 240f, grounded.z);

            Invoke(manager.firstPersonButton);
            Check(report, ref fails, "return from bird keeps the map position", HorizontalDistance(walker.transform.position, grounded) < 0.05f);
            Check(report, ref fails, "return from bird restores grounded height", Mathf.Abs(walker.transform.position.y - safeY) < 0.2f);
            Check(report, ref fails, "return from bird ignores flight altitude", Mathf.Abs(walker.transform.position.y - (grounded.y + 240f)) > 20f);
            Check(report, ref fails, "return heading", Mathf.Abs(Mathf.DeltaAngle(groundedYaw, walker.Heading)) < 1f);
            Check(report, ref fails, "still one walker", Count<FirstPersonWalker>() == 1 && Count<CharacterController>() == 1 && Count<CharacterVisual>() == 1 && Count<CharacterLocomotion>() == 1 && Count<PlayerRoot>() == 1);
            Check(report, ref fails, "one camera after return", EnabledCount<Camera>() == 1);
            Check(report, ref fails, "one listener after return", EnabledCount<AudioListener>() == 1);

            manager.OnEscape();
            Check(report, ref fails, "escape opens pause", manager.PauseOpen && manager.pauseRoot.activeInHierarchy && walker.InputBlocked);
            Invoke(manager.changeViewButton);
            Check(report, ref fails, "change view reopens selector", manager.WelcomeOpen && HasText(manager.welcomeRoot, "Welcome to Nazareth"));
            Invoke(manager.thirdPersonButton);
            Check(report, ref fails, "selector can choose third person again", manager.Current == ExplorationMode.ThirdPerson && !manager.WelcomeOpen);
            CheckLocationPreservation(report, ref fails, walker, manager);

            int buildings = Count<BuildingFeature>();
            int streets = Count<StreetFeature>();
            int chunks = Count<TerrainChunk>();
            report.AppendLine("INFO buildings " + buildings + " streets " + streets + " terrainChunks " + chunks);
            Check(report, ref fails, "city buildings remain", buildings > 1000);
            Check(report, ref fails, "city streets remain", streets > 100);
            Check(report, ref fails, "terrain remains", chunks > 10);
            var kind = PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(ExplorationModeSetup.TrooperPrefabPath));
            report.AppendLine("INFO prefab type " + kind);
            Check(report, ref fails, "trooper prefab is a variant", kind == PrefabAssetType.Variant);
            report.AppendLine("INFO active scene " + SceneManager.GetActiveScene().path);
            Write(report, fails);
            return fails;
        }

        static void CheckLocationPreservation(StringBuilder report, ref int fails, FirstPersonWalker walker, ViewModeManager manager)
        {
            Vector3 spawn = walker.spawnPosition;
            float spawnYaw = walker.spawnYaw;
            for (int i = 0; i < 30; i++)
                walker.ApplyGameplayFrame(new CityControlFrame { move = new Vector2(0f, 1f), sprint = true });
            walker.ApplyGameplayFrame(new CityControlFrame { look = new Vector2(40f, 0f) });
            Vector3 walked = walker.transform.position;
            if (HorizontalDistance(walked, spawn) < 1f)
            {
                Vector3 step = walked + walker.transform.forward * 8f;
                if (TerrainGround.TrySurface(step.x, step.z, out float ground))
                    step.y = ground;
                var boundary = UnityEngine.Object.FindAnyObjectByType<CityBoundaryMap>();
                if (boundary == null || boundary.IsCapsuleInside(new Vector2(step.x, step.z), walker.capsuleRadius, walker.safetyMargin))
                    walker.Teleport(step, walker.Heading, true);
                walked = walker.transform.position;
            }

            float walkedYaw = walker.Heading;
            report.AppendLine("INFO walked " + walked.ToString("0.00") + " yaw " + walkedYaw.ToString("0.0") + " from spawn " + HorizontalDistance(walked, spawn).ToString("0.00"));
            Check(report, ref fails, "player left the street spawn", HorizontalDistance(walked, spawn) > 1f);

            Switch(manager, manager.thirdPersonButton);
            CheckHorizontal(report, ref fails, "third person opens at the walked intersection", walker.transform.position, walked);
            Check(report, ref fails, "third person keeps the walked heading", Mathf.Abs(Mathf.DeltaAngle(walkedYaw, walker.Heading)) < 1f);
            Check(report, ref fails, "third person uses its orbit offset", manager.thirdPerson != null && manager.thirdPerson.CurrentDistance > 0.7f && manager.thirdPersonCamera.transform.position.y > walker.transform.position.y + 0.4f);

            Switch(manager, manager.firstPersonButton);
            CheckHorizontal(report, ref fails, "first person returns to the walked intersection", walker.transform.position, walked);
            Check(report, ref fails, "first person keeps the walked heading", Mathf.Abs(Mathf.DeltaAngle(walkedYaw, walker.Heading)) < 1f);

            Switch(manager, manager.thirdPersonButton);
            Switch(manager, manager.firstPersonButton);
            CheckHorizontal(report, ref fails, "repeated view switches stay at the walked intersection", walker.transform.position, walked);
            Check(report, ref fails, "repeated view switches keep the walked heading", Mathf.Abs(Mathf.DeltaAngle(walkedYaw, walker.Heading)) < 1f);

            Switch(manager, manager.thirdPersonButton);
            float bodyYaw = walker.Heading;
            manager.thirdPerson.SetOrbit(bodyYaw + 90f, manager.thirdPerson.pitch);
            manager.thirdPerson.PlaceNow();
            Switch(manager, manager.firstPersonButton);
            CheckHorizontal(report, ref fails, "orbit switch keeps the map position", walker.transform.position, walked);
            Check(report, ref fails, "first person adopts the orbit heading", Mathf.Abs(Mathf.DeltaAngle(bodyYaw + 90f, walker.Heading)) < 1.5f);
            walkedYaw = walker.Heading;

            Vector3 held = walker.transform.position;
            float heldYaw = walker.Heading;
            ExplorationMode heldMode = manager.Current;
            manager.OnEscape();
            Check(report, ref fails, "escape opens pause before resume", manager.PauseOpen);
            manager.OnEscape();
            Check(report, ref fails, "closing the menu keeps the mode", manager.Current == heldMode && !manager.PauseOpen && !manager.WelcomeOpen);
            CheckHorizontal(report, ref fails, "closing the menu keeps the location", walker.transform.position, held);
            Check(report, ref fails, "closing the menu keeps the heading", Mathf.Abs(Mathf.DeltaAngle(heldYaw, walker.Heading)) < 0.2f);
            manager.OnEscape();
            Invoke(manager.changeViewButton);
            Check(report, ref fails, "change view leaves the current mode selected", manager.WelcomeOpen && manager.Current == heldMode);
            manager.OnEscape();
            Check(report, ref fails, "closing the selector without a choice keeps the mode", manager.Current == heldMode && !manager.WelcomeOpen);
            CheckHorizontal(report, ref fails, "closing the selector without a choice keeps the location", walker.transform.position, held);

            Switch(manager, manager.birdsEyeButton);
            CheckHorizontal(report, ref fails, "bird keeps the player on the walked ground", walker.transform.position, walked);
            Check(report, ref fails, "bird uses its own camera height", manager.birdsEye.transform.position.y > walker.transform.position.y + 20f);
            CheckHorizontal(report, ref fails, "bird map focus is the walked intersection", manager.birdsEye.MapFocus, walked);
            float back = HorizontalDistance(manager.birdsEye.transform.position, walked);
            report.AppendLine("INFO bird back offset " + back.ToString("0.00"));
            Check(report, ref fails, "bird camera keeps its back offset", back > 10f);
            Check(report, ref fails, "bird keeps the walked heading", Mathf.Abs(Mathf.DeltaAngle(walkedYaw, manager.birdsEye.FocusYaw)) < 1.5f);

            Vector3 beforeFlight = manager.birdsEye.MapFocus;
            manager.birdsEye.ApplyFlightStep(new Vector3(36f, 25f, -28f));
            Vector3 afterFlight = manager.birdsEye.MapFocus;
            float flown = HorizontalDistance(afterFlight, beforeFlight);
            report.AppendLine("INFO bird flight " + flown.ToString("0.00") + " focus " + afterFlight.ToString("0.0") + " camera " + manager.birdsEye.transform.position.ToString("0.0"));
            Check(report, ref fails, "bird flight moves the map focus", flown > 10f);

            float flightYaw = manager.birdsEye.FocusYaw;
            float flightCameraY = manager.birdsEye.transform.position.y;
            Switch(manager, manager.firstPersonButton);
            CheckHorizontal(report, ref fails, "first person opens at the flown map focus", walker.transform.position, afterFlight);
            bool hasGround = PlacementGround.TrySupport(walker.transform.position.x, walker.transform.position.z, walker.transform, out float groundY);
            report.AppendLine("INFO feet " + walker.transform.position.y.ToString("0.00") + " ground " + (hasGround ? groundY.ToString("0.00") : "none"));
            Check(report, ref fails, "first person stands on the flown surface", hasGround && Mathf.Abs(walker.transform.position.y - groundY) < 0.35f);
            Check(report, ref fails, "first person ignores the bird altitude", walker.transform.position.y < flightCameraY - 20f);
            Check(report, ref fails, "first person keeps the bird heading", Mathf.Abs(Mathf.DeltaAngle(flightYaw, walker.Heading)) < 1.5f);

            Vector3 landed = walker.transform.position;
            float landedYaw = walker.Heading;
            Switch(manager, manager.birdsEyeButton);
            CheckHorizontal(report, ref fails, "bird reopens at the flown map focus", manager.birdsEye.MapFocus, landed);
            CheckHorizontal(report, ref fails, "player stays under the reopened bird focus", walker.transform.position, landed);
            Check(report, ref fails, "bird reapplies its camera height", manager.birdsEye.transform.position.y > walker.transform.position.y + 20f);
            Check(report, ref fails, "bird keeps the landed heading", Mathf.Abs(Mathf.DeltaAngle(landedYaw, manager.birdsEye.FocusYaw)) < 1.5f);

            Switch(manager, manager.thirdPersonButton);
            CheckHorizontal(report, ref fails, "third person opens at the flown map focus", walker.transform.position, landed);
            Check(report, ref fails, "third person keeps the flown heading", Mathf.Abs(Mathf.DeltaAngle(landedYaw, walker.Heading)) < 1.5f);
            Check(report, ref fails, "third person restores its orbit offset", manager.thirdPerson.CurrentDistance > 0.7f);

            Switch(manager, manager.birdsEyeButton);
            Switch(manager, manager.firstPersonButton);
            Switch(manager, manager.thirdPersonButton);
            Switch(manager, manager.firstPersonButton);
            CheckHorizontal(report, ref fails, "repeated switches keep the flown map focus", walker.transform.position, landed);
            Check(report, ref fails, "repeated switches keep the flown heading", Mathf.Abs(Mathf.DeltaAngle(landedYaw, walker.Heading)) < 1.5f);

            manager.OnReturnToSpawn();
            CheckHorizontal(report, ref fails, "return to spawn uses the street spawn", walker.transform.position, spawn);
            Check(report, ref fails, "return to spawn restores the spawn height", Mathf.Abs(walker.transform.position.y - spawn.y) < 0.05f);
            Check(report, ref fails, "return to spawn restores the spawn heading", Mathf.Abs(Mathf.DeltaAngle(spawnYaw, walker.Heading)) < 0.2f);
        }

        static void Switch(ViewModeManager manager, Button button)
        {
            manager.OnEscape();
            Invoke(manager.changeViewButton);
            Invoke(button);
        }

        static void CheckHorizontal(StringBuilder report, ref int fails, string name, Vector3 actual, Vector3 expected)
        {
            float distance = HorizontalDistance(actual, expected);
            if (distance >= 0.05f)
                report.AppendLine("INFO " + name + " off by " + distance.ToString("0.00") + " actual " + actual.ToString("0.0") + " expected " + expected.ToString("0.0"));
            Check(report, ref fails, name, distance < 0.05f);
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static void CheckCharacterAnimation(StringBuilder report, ref int fails, FirstPersonWalker walker, ViewModeManager manager)
        {
            var presenter = walker.GetComponent<CharacterPresenter>();
            var locomotion = walker.GetComponent<CharacterLocomotion>();
            var animator = manager.character != null ? manager.character.GetComponentInChildren<Animator>(true) : null;
            Check(report, ref fails, "animation separated from the capsule", presenter != null && locomotion != null && manager.character != null && manager.character.transform.parent == walker.transform && animator != null && animator.transform != walker.transform);
            Check(report, ref fails, "root motion stays off", animator != null && !animator.applyRootMotion);
            Check(report, ref fails, "humanoid controller assigned", animator != null && animator.runtimeAnimatorController != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman);
            Check(report, ref fails, "placement remains during animation", walker.GetComponent<AssetPlacementController>() != null);
            if (animator == null || locomotion == null)
                return;

            Vector3 origin = walker.transform.position;
            float yaw = walker.Heading;
            walker.Teleport(origin, yaw, true);
            locomotion.SnapIdle();
            locomotion.ApplyMotion(0f, 0f, true, false, 0.2f);
            Check(report, ref fails, "idle speed settles", animator.GetFloat(CharacterLocomotion.SpeedParam) < 0.05f);
            locomotion.ApplyMotion(walker.walkSpeed, -2f, true, false, 0.25f);
            animator.Update(0.05f);
            Check(report, ref fails, "walk speed drives locomotion", animator.GetFloat(CharacterLocomotion.SpeedParam) > walker.walkSpeed * 0.35f && InAnimatorState(animator, "Locomotion"));
            bool moved = walker.ApplyGameplayFrame(new CityControlFrame { move = Vector2.up });
            float step = (walker.transform.position - origin).magnitude;
            locomotion.ApplyMotion(new Vector3(walker.Velocity.x, 0f, walker.Velocity.z).magnitude, walker.Velocity.y, walker.IsGrounded, false, 0.05f);
            report.AppendLine("INFO animation step " + step.ToString("0.000") + " controller speed " + new Vector3(walker.Velocity.x, 0f, walker.Velocity.z).magnitude.ToString("0.00"));
            Check(report, ref fails, "walk frame reaches the controller", moved && (Time.deltaTime < 0.002f || step > 0.001f));
            walker.Teleport(origin, yaw, true);
            if (Time.deltaTime >= 0.002f)
            {
                bool jumped = walker.ApplyGameplayFrame(new CityControlFrame { jumpPressed = true });
                locomotion.ApplyMotion(0f, walker.Velocity.y, walker.IsGrounded, walker.JumpAcceptedThisFrame, 0.05f);
                animator.Update(0.12f);
                report.AppendLine("INFO jump accepted " + walker.JumpAcceptedThisFrame + " vertical " + walker.Velocity.y.ToString("0.00"));
                Check(report, ref fails, "jump is accepted from the ground", jumped && walker.JumpAcceptedThisFrame);
                Check(report, ref fails, "jump enters the jump state", InAnimatorState(animator, "Jump"));
                bool landed = false;
                for (int i = 0; i < 180; i++)
                {
                    walker.ApplyGameplayFrame(default);
                    if (i > 8 && walker.IsGrounded && walker.Velocity.y <= 0.5f)
                    {
                        landed = true;
                        break;
                    }
                }

                locomotion.ApplyMotion(new Vector3(walker.Velocity.x, 0f, walker.Velocity.z).magnitude, walker.Velocity.y, true, false, 0.25f);
                animator.Update(0.25f);
                report.AppendLine("INFO landed " + landed + " grounded " + walker.IsGrounded + " state " + walker.Velocity.y.ToString("0.00"));
                Check(report, ref fails, "jump returns to the ground", landed && walker.IsGrounded);
                Check(report, ref fails, "landing returns to locomotion", InAnimatorState(animator, "Land") || InAnimatorState(animator, "Locomotion"));
            }
            else
            {
                report.AppendLine("UNVERIFIED jump because the play-mode frame was " + Time.deltaTime.ToString("0.0000") + " seconds.");
            }

            Vector3 local = animator.transform.localPosition;
            animator.Update(0.4f);
            float drift = (animator.transform.localPosition - local).magnitude;
            Check(report, ref fails, "animation does not move the character", drift < 0.02f);
            var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (upper == null || lower == null || hips == null || head == null)
            {
                Check(report, ref fails, "humanoid bones resolve", false);
            }
            else
            {
                animator.Play("Locomotion", 0, 0.2f);
                animator.SetFloat(CharacterLocomotion.SpeedParam, 0f);
                animator.SetBool(CharacterLocomotion.GroundedParam, true);
                animator.Update(0.2f);
                float down = Vector3.Dot((lower.position - upper.position).normalized, Vector3.down);
                bool upright = head.position.y > hips.position.y + 0.15f;
                report.AppendLine("INFO arm down " + down.ToString("0.00") + " head above hips " + upright);
                Check(report, ref fails, "idle pose is not a T-pose", down > 0.25f && upright);
            }

            walker.Teleport(origin, yaw, true);
            locomotion.SnapIdle();
        }

        static bool InAnimatorState(Animator animator, string stateName)
        {
            if (animator.GetCurrentAnimatorStateInfo(0).IsName(stateName))
                return true;
            return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(stateName);
        }

        static bool CameraPullsInNearBuilding(FirstPersonWalker walker, ThirdPersonOrbitCamera orbit, StringBuilder report)
        {
            if (orbit == null)
                return false;
            Vector3 origin = walker.transform.position + Vector3.up * 1.5f;
            RaycastHit wall = default;
            bool found = false;
            for (int step = 0; step < 24 && !found; step++)
            {
                Vector3 direction = Quaternion.Euler(0f, step * 15f, 0f) * Vector3.forward;
                if (!Physics.Raycast(origin, direction, out RaycastHit hit, 45f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                if (hit.collider == null || hit.collider.GetComponentInParent<BuildingFeature>() == null)
                    continue;
                wall = hit;
                found = true;
            }

            if (!found)
            {
                var buildings = UnityEngine.Object.FindObjectsByType<BuildingFeature>(FindObjectsSortMode.None);
                float best = float.MaxValue;
                Collider nearest = null;
                for (int i = 0; i < buildings.Length; i++)
                {
                    var collider = buildings[i].GetComponentInChildren<Collider>();
                    if (collider == null)
                        continue;
                    float distance = (collider.bounds.center - walker.transform.position).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        nearest = collider;
                    }
                }

                if (nearest != null)
                {
                    Vector3 from = nearest.bounds.center + Vector3.right * (nearest.bounds.extents.x + 3f);
                    from.y = nearest.bounds.center.y;
                    Vector3 toward = nearest.bounds.center - from;
                    if (Physics.Raycast(from, toward.normalized, out RaycastHit hit, toward.magnitude + 2f, ~0, QueryTriggerInteraction.Ignore)
                        && hit.collider != null && hit.collider.GetComponentInParent<BuildingFeature>() != null)
                    {
                        wall = hit;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                report.AppendLine("INFO no building face found for the camera collision probe");
                return false;
            }

            Vector3 intoWall = wall.normal;
            intoWall.y = 0f;
            if (intoWall.sqrMagnitude < 0.01f)
                intoWall = -wall.transform.forward;
            intoWall.Normalize();
            Vector3 stand = wall.point + intoWall * 1.15f;
            for (int n = 0; n < 6 && BuildingOverlap(stand + Vector3.up * 0.9f); n++)
                stand += intoWall * 0.3f;
            if (TerrainGround.TrySurface(stand.x, stand.z, out float ground))
                stand.y = ground;
            float yaw = Quaternion.LookRotation(intoWall, Vector3.up).eulerAngles.y;
            walker.Teleport(stand, yaw, true);
            Physics.SyncTransforms();
            orbit.walker = walker;
            orbit.target = walker.transform;
            orbit.SetOrbit(yaw, 12f);
            orbit.PlaceNow();
            float resolved = orbit.CurrentDistance;
            report.AppendLine("INFO camera distance beside " + wall.collider.name + " hit " + wall.distance.ToString("0.00") + " resolved " + resolved.ToString("0.00") + " desired " + orbit.distance.ToString("0.00"));
            return resolved < orbit.distance - 0.75f;
        }

        static bool BuildingOverlap(Vector3 center)
        {
            var hits = Physics.OverlapSphere(center, 0.34f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] != null && hits[i].GetComponentInParent<BuildingFeature>() != null)
                    return true;
            }

            return false;
        }

        static void Invoke(Button button)
        {
            if (button != null)
                button.onClick.Invoke();
        }

        static bool HasText(GameObject root, string value)
        {
            if (root == null)
                return false;
            var texts = root.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].text == value)
                    return true;
            }

            return false;
        }

        static int Count<T>() where T : Component
        {
            return UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        }

        static int EnabledCount<T>() where T : Behaviour
        {
            var all = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].enabled && all[i].gameObject.activeInHierarchy)
                    count++;
            }

            return count;
        }

        static void Check(StringBuilder report, ref int fails, string name, bool pass)
        {
            report.AppendLine((pass ? "PASS " : "FAIL ") + name);
            if (!pass)
                fails++;
        }

        static void Write(StringBuilder report, int fails)
        {
            report.AppendLine(fails == 0 ? "RESULT pass" : "RESULT fail " + fails);
            File.WriteAllText(ExplorationModeSetup.ReportPath, report.ToString());
            Debug.Log(report.ToString());
        }
    }
}
