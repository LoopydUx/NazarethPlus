using UnityEngine;
using UnityEngine.UI;

namespace NazarethPlusCity
{
    public enum ExplorationMode
    {
        None = 0,
        FirstPerson = 1,
        ThirdPerson = 2,
        BirdsEye = 3
    }

    /// <summary>
    /// One owner for the welcome selector, pause menu, cameras, and the shared walker.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class ViewModeManager : MonoBehaviour
    {
        public static ViewModeManager Instance { get; private set; }

        public FirstPersonWalker walker;
        public Camera firstPersonCamera;
        public AudioListener firstPersonListener;
        public ThirdPersonOrbitCamera thirdPerson;
        public Camera thirdPersonCamera;
        public AudioListener thirdPersonListener;
        public BirdsEyeFlyer birdsEye;
        public Camera birdsEyeCamera;
        public AudioListener birdsEyeListener;
        public CharacterVisual character;
        public GameObject welcomeRoot;
        public GameObject pauseRoot;
        public Button firstPersonButton;
        public Button thirdPersonButton;
        public Button birdsEyeButton;
        public Button resumeButton;
        public Button changeViewButton;

        public ExplorationMode Current { get; private set; } = ExplorationMode.None;
        public bool WelcomeOpen { get; private set; }
        public bool PauseOpen { get; private set; }
        public bool GameplayAllowed => !WelcomeOpen && !PauseOpen && Current != ExplorationMode.None;

        bool _bound;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("A second ViewModeManager was disabled so cameras and input stay singular.");
                enabled = false;
                return;
            }

            Instance = this;
            Resolve();
            if (walker != null)
                walker.InputBlocked = true;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void OnEnable()
        {
            if (!Application.isPlaying)
                return;
            Bind();
        }

        void Start()
        {
            Resolve();
            OpenWelcome(initial: true);
        }

        public void OnEscape()
        {
            var placement = walker != null ? walker.GetComponent<AssetPlacementController>() : null;
            if (placement != null && placement.TryHandleEscape())
                return;
            if (WelcomeOpen && Current == ExplorationMode.None)
                return;
            if (WelcomeOpen || PauseOpen)
            {
                CloseMenus();
                return;
            }

            OpenPause();
        }

        public void OnReturnToSpawn()
        {
            if (walker == null || WelcomeOpen || Current == ExplorationMode.None || Current == ExplorationMode.BirdsEye)
                return;
            walker.ResetToSpawn();
            if (Current == ExplorationMode.ThirdPerson && thirdPerson != null)
                thirdPerson.SnapBehind(walker.transform);
        }

        public void ChooseFirstPerson()
        {
            Choose(ExplorationMode.FirstPerson);
        }

        public void ChooseThirdPerson()
        {
            Choose(ExplorationMode.ThirdPerson);
        }

        public void ChooseBirdsEye()
        {
            Choose(ExplorationMode.BirdsEye);
        }

        public void Choose(ExplorationMode mode)
        {
            if (mode == ExplorationMode.None)
                return;
            // The first choice stays on the street spawn. A later choice keeps the map
            // location captured below, so leaving the flying camera does not return home.
            Vector3 mapPosition = default;
            float yaw = 0f;
            bool preserveLocation = Current != ExplorationMode.None && TryCaptureMapLocation(out mapPosition, out yaw);
            WelcomeOpen = false;
            PauseOpen = false;
            if (welcomeRoot != null)
                welcomeRoot.SetActive(false);
            if (pauseRoot != null)
                pauseRoot.SetActive(false);
            Time.timeScale = 1f;
            ApplyMode(mode, preserveLocation, mapPosition, yaw);
            if (walker != null)
                walker.InputBlocked = false;
            ApplyCursor();
        }

        public void OpenChangeView()
        {
            PauseOpen = false;
            if (pauseRoot != null)
                pauseRoot.SetActive(false);
            WelcomeOpen = true;
            if (welcomeRoot != null)
                welcomeRoot.SetActive(true);
            if (walker != null)
            {
                walker.InputBlocked = true;
                walker.HideHint();
            }

            ApplyCursor();
            var layout = welcomeRoot != null ? welcomeRoot.GetComponent<WelcomeWindowLayout>() : null;
            layout?.Apply();
        }

        public void ApplyCursor()
        {
            var placement = walker != null ? walker.GetComponent<AssetPlacementController>() : null;
            bool placementOwnsCursor = placement != null && placement.WantsCursorFree;
            bool locked = GameplayAllowed && !placementOwnsCursor;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void OpenWelcome(bool initial)
        {
            Resolve();
            Current = initial ? ExplorationMode.None : Current;
            WelcomeOpen = true;
            PauseOpen = false;
            Time.timeScale = 1f;
            if (welcomeRoot != null)
                welcomeRoot.SetActive(true);
            if (pauseRoot != null)
                pauseRoot.SetActive(false);
            if (walker != null)
            {
                walker.InputBlocked = true;
                walker.HideHint();
            }

            if (character != null)
                character.SetVisible(false);
            UseRig(true, false, false);
            ApplyCursor();
            var layout = welcomeRoot != null ? welcomeRoot.GetComponent<WelcomeWindowLayout>() : null;
            layout?.Apply();
        }

        void OpenPause()
        {
            PauseOpen = true;
            WelcomeOpen = false;
            if (pauseRoot != null)
                pauseRoot.SetActive(true);
            if (welcomeRoot != null)
                welcomeRoot.SetActive(false);
            if (walker != null)
            {
                walker.InputBlocked = true;
                walker.HideHint();
            }

            Time.timeScale = 0f;
            ApplyCursor();
        }

        void CloseMenus()
        {
            WelcomeOpen = false;
            PauseOpen = false;
            if (welcomeRoot != null)
                welcomeRoot.SetActive(false);
            if (pauseRoot != null)
                pauseRoot.SetActive(false);
            Time.timeScale = 1f;
            if (walker != null)
                walker.InputBlocked = Current == ExplorationMode.None;
            ApplyCursor();
            ShowModeHint();
        }

        void ApplyMode(ExplorationMode mode, bool preserveLocation, Vector3 mapPosition, float yaw)
        {
            Current = mode;
            if (walker == null)
                return;
            if (preserveLocation)
                walker.Teleport(mapPosition, yaw, true);
            if (mode == ExplorationMode.BirdsEye)
            {
                walker.ParkForFlight();
                if (character != null)
                    character.SetVisible(true);
                UseRig(false, false, true);
                if (birdsEye != null)
                {
                    if (preserveLocation)
                        birdsEye.ActivateAt(walker.transform.position, yaw);
                    else
                        birdsEye.Activate(walker.transform);
                }
            }
            else
            {
                WalkerDrive drive = mode == ExplorationMode.ThirdPerson ? WalkerDrive.ThirdPerson : WalkerDrive.FirstPerson;
                walker.SetDrive(drive);
                bool third = mode == ExplorationMode.ThirdPerson;
                if (character != null)
                    character.SetVisible(third);
                walker.cameraHeading = third && thirdPerson != null ? thirdPerson.transform : null;
                UseRig(!third, third, false);
                if (third && thirdPerson != null)
                    thirdPerson.SnapBehind(walker.transform);
            }

            ShowModeHint();
        }

        bool TryCaptureMapLocation(out Vector3 position, out float yaw)
        {
            position = default;
            yaw = 0f;
            if (Current == ExplorationMode.BirdsEye && birdsEye != null && birdsEye.HasMapFocus)
            {
                position = birdsEye.MapFocus;
                yaw = birdsEye.FocusYaw;
                if (walker != null)
                {
                    Vector3 fallback = birdsEye.transform.position;
                    fallback.y = position.y;
                    position = walker.ClampToPlayable(position, fallback);
                    float dx = position.x - walker.transform.position.x;
                    float dz = position.z - walker.transform.position.z;
                    if (dx * dx + dz * dz > 0.0001f && PlacementGround.TrySupport(position.x, position.z, walker.transform, out float ground))
                        position.y = ground;
                    else
                        position.y = walker.transform.position.y;
                }

                return true;
            }

            if (walker == null)
                return false;
            position = walker.transform.position;
            yaw = walker.Heading;
            if (Current == ExplorationMode.ThirdPerson && thirdPerson != null)
                yaw = thirdPerson.yaw;
            return true;
        }

        void UseRig(bool firstPerson, bool thirdPersonOn, bool birdsEyeOn)
        {
            SetRig(firstPersonCamera, firstPersonListener, null, firstPerson);
            SetRig(thirdPersonCamera, thirdPersonListener, thirdPerson, thirdPersonOn);
            SetRig(birdsEyeCamera, birdsEyeListener, birdsEye, birdsEyeOn);
        }

        static void SetRig(Camera camera, AudioListener listener, Behaviour behaviour, bool on)
        {
            if (camera != null)
                camera.enabled = on;
            if (listener != null)
                listener.enabled = on;
            if (behaviour != null)
                behaviour.enabled = on;
        }

        public void ShowModeHint()
        {
            if (walker == null)
                return;
            switch (Current)
            {
                case ExplorationMode.FirstPerson:
                    walker.ShowHint("WASD walk    Mouse look    Shift sprint    Space jump    B add asset    Esc menu    R return to street");
                    break;
                case ExplorationMode.ThirdPerson:
                    walker.ShowHint("WASD move    Mouse orbit    Shift sprint    Space jump    B add asset    Esc menu    R return to street");
                    break;
                case ExplorationMode.BirdsEye:
                    walker.ShowHint("WASD fly    Mouse look    E up    Q down    Wheel speed    Shift faster    B add asset    Esc menu");
                    break;
            }
        }

        void Bind()
        {
            if (_bound)
                return;
            _bound = true;
            Listen(firstPersonButton, ChooseFirstPerson);
            Listen(thirdPersonButton, ChooseThirdPerson);
            Listen(birdsEyeButton, ChooseBirdsEye);
            Listen(resumeButton, CloseMenus);
            Listen(changeViewButton, OpenChangeView);
        }

        static void Listen(Button button, UnityEngine.Events.UnityAction call)
        {
            if (button == null)
                return;
            button.onClick.RemoveListener(call);
            button.onClick.AddListener(call);
        }

        void Resolve()
        {
            if (walker == null)
                walker = GetComponent<FirstPersonWalker>();
            if (walker != null && firstPersonCamera == null && walker.eye != null)
                firstPersonCamera = walker.eye.GetComponent<Camera>();
            if (firstPersonListener == null && firstPersonCamera != null)
                firstPersonListener = firstPersonCamera.GetComponent<AudioListener>();
            if (character == null)
                character = GetComponentInChildren<CharacterVisual>(true);
            if (thirdPerson == null)
                thirdPerson = GetComponentInChildren<ThirdPersonOrbitCamera>(true);
            if (thirdPersonCamera == null && thirdPerson != null)
                thirdPersonCamera = thirdPerson.GetComponent<Camera>();
            if (thirdPersonListener == null && thirdPersonCamera != null)
                thirdPersonListener = thirdPersonCamera.GetComponent<AudioListener>();
            if (birdsEye == null)
                birdsEye = FindAnyObjectByType<BirdsEyeFlyer>(FindObjectsInactive.Include);
            if (birdsEyeCamera == null && birdsEye != null)
                birdsEyeCamera = birdsEye.GetComponent<Camera>();
            if (birdsEyeListener == null && birdsEyeCamera != null)
                birdsEyeListener = birdsEyeCamera.GetComponent<AudioListener>();
            var hud = walker != null ? walker.transform.Find("WalkHud") : null;
            if (hud != null)
            {
                if (welcomeRoot == null)
                {
                    var welcome = hud.Find("WelcomeWindow");
                    if (welcome != null)
                        welcomeRoot = welcome.gameObject;
                }

                if (pauseRoot == null && walker.pausePanel != null)
                    pauseRoot = walker.pausePanel;
                if (firstPersonButton == null)
                    firstPersonButton = FindButton(welcomeRoot, "FirstPersonCard");
                if (thirdPersonButton == null)
                    thirdPersonButton = FindButton(welcomeRoot, "ThirdPersonCard");
                if (birdsEyeButton == null)
                    birdsEyeButton = FindButton(welcomeRoot, "BirdsEyeCard");
                if (resumeButton == null)
                    resumeButton = FindButton(pauseRoot, "ResumeButton");
                if (changeViewButton == null)
                    changeViewButton = FindButton(pauseRoot, "ChangeViewButton");
            }
        }

        static Button FindButton(GameObject root, string name)
        {
            if (root == null)
                return null;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == name)
                    return transforms[i].GetComponent<Button>();
            }

            return null;
        }
    }
}
