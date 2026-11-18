using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NazarethPlusCity
{
    public enum WalkerDrive
    {
        FirstPerson = 0,
        ThirdPerson = 1,
        Parked = 2
    }

    public struct CityControlFrame
    {
        public Vector2 move;
        public Vector2 look;
        public bool jumpPressed;
        public bool sprint;
        public bool ascend;
        public bool descend;
        public float scroll;
    }

    /// <summary>
    /// Shared grounded walker for first and third person. One CharacterController owns collision.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonWalker : MonoBehaviour
    {
        public InputActionAsset actions;
        public float walkSpeed = 4.2f;
        public float sprintSpeed = 7.5f;
        public float jumpHeight = 1.2f;
        public float gravity = -20f;
        public float lookSensitivity = 0.07f;
        public float eyeHeight = 1.64f;
        public float capsuleRadius = 0.3f;
        public float safetyMargin = 0.08f;
        public float fallResetY = -1.5f;
        public float fallDropMeters = 8f;
        public float turnSpeed = 720f;
        public Vector3 spawnPosition;
        public float spawnYaw;
        public Transform eye;
        public Transform cameraHeading;
        public WalkerDrive drive = WalkerDrive.FirstPerson;
        public GameObject pausePanel;
        public GameObject hintPanel;

        CharacterController _controller;
        CityBoundaryMap _boundary;
        InputActionMap _map;
        InputAction _move;
        InputAction _look;
        InputAction _jump;
        InputAction _sprint;
        InputAction _pause;
        InputAction _reset;
        InputAction _ascend;
        InputAction _descend;
        InputAction _flightSpeed;
        float _pitch;
        float _verticalVelocity;
        Vector3 _lastSafe;
        float _savedHeading;
        bool _paused;
        float _hintUntil;
        bool _hasSafe;

        public bool InputBlocked { get; set; }
        public bool AllowLookWhileUnlocked { get; set; }
        public bool SuppressFlightVertical { get; set; }
        public bool MovementOverridden { get; set; }
        public Vector3 LastSafePosition => _hasSafe ? _lastSafe : spawnPosition;
        public bool HasSafePosition => _hasSafe;
        public float Heading => transform.eulerAngles.y;
        public float SavedHeading => _savedHeading;
        public CharacterController Controller => _controller;
        public Vector3 Velocity { get; private set; }
        public bool IsGrounded { get; private set; }
        public bool JumpAcceptedThisFrame { get; private set; }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            Time.timeScale = 1f;
            BindActions();
        }

        void OnEnable()
        {
            _map?.Enable();
        }

        void OnDisable()
        {
            _map?.Disable();
        }

        void Start()
        {
            _boundary = FindAnyObjectByType<CityBoundaryMap>();
            if (eye == null)
            {
                var child = transform.Find("Eye");
                if (child != null)
                    eye = child;
            }

            _lastSafe = transform.position;
            _hasSafe = true;
            _savedHeading = transform.eulerAngles.y;
            if (ViewModeManager.Instance != null)
            {
                HideHint();
                LockCursor(false);
            }
            else
            {
                _hintUntil = Time.unscaledTime + 8f;
                LockCursor(true);
            }
        }

        void Update()
        {
            if (hintPanel != null && hintPanel.activeSelf && Time.unscaledTime >= _hintUntil)
                hintPanel.SetActive(false);

            bool manager = ViewModeManager.Instance != null;
            if (_pause != null && _pause.WasPressedThisFrame())
            {
                if (manager)
                    ViewModeManager.Instance.OnEscape();
                else if (GetComponent<AssetPlacementController>() is AssetPlacementController placement && placement.TryHandleEscape())
                    return;
                else
                    SetPaused(!_paused);
            }

            if (_reset != null && _reset.WasPressedThisFrame())
            {
                if (manager)
                    ViewModeManager.Instance.OnReturnToSpawn();
                else
                    ResetToSpawn();
            }

            ApplyGameplayFrame(ReadControls());
        }

        public bool ApplyGameplayFrame(CityControlFrame frame)
        {
            JumpAcceptedThisFrame = false;
            if (_paused || InputBlocked || drive == WalkerDrive.Parked)
                return false;
            if (MovementOverridden)
            {
                Velocity = Vector3.zero;
                IsGrounded = false;
                return true;
            }

            if (drive == WalkerDrive.FirstPerson)
                Look(frame.look);

            MoveBody(frame);
            EnforceBoundary();
            return true;
        }

        public void ApplyLookOnly(Vector2 lookDelta)
        {
            if (_paused || InputBlocked || drive != WalkerDrive.FirstPerson)
                return;
            Look(lookDelta);
        }

        void BindActions()
        {
            if (actions == null)
                return;
            _map = actions.FindActionMap("CityWalk", true);
            _move = _map.FindAction("Move", true);
            _look = _map.FindAction("Look", true);
            _jump = _map.FindAction("Jump", true);
            _sprint = _map.FindAction("Sprint", true);
            _pause = _map.FindAction("Pause", true);
            _reset = _map.FindAction("ReturnToSpawn", true);
            _ascend = _map.FindAction("Ascend", false);
            _descend = _map.FindAction("Descend", false);
            _flightSpeed = _map.FindAction("FlightSpeed", false);
        }

        public CityControlFrame ReadControls()
        {
            if (InputBlocked)
                return default;
            Vector2 look = _look != null ? _look.ReadValue<Vector2>() : Vector2.zero;
            if (Cursor.lockState != CursorLockMode.Locked && !AllowLookWhileUnlocked)
                look = Vector2.zero;
            bool ascend = _ascend != null && _ascend.IsPressed();
            bool descend = _descend != null && _descend.IsPressed();
            if (SuppressFlightVertical)
            {
                ascend = false;
                descend = false;
            }

            return new CityControlFrame
            {
                move = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero,
                look = look,
                jumpPressed = _jump != null && _jump.WasPressedThisFrame(),
                sprint = _sprint != null && _sprint.IsPressed(),
                ascend = ascend,
                descend = descend,
                scroll = _flightSpeed != null ? _flightSpeed.ReadValue<float>() : 0f
            };
        }

        void Look(Vector2 delta)
        {
            if (eye == null)
                return;
            transform.Rotate(0f, delta.x * lookSensitivity, 0f, Space.World);
            _pitch = Mathf.Clamp(_pitch - delta.y * lookSensitivity, -85f, 85f);
            eye.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void MoveBody(CityControlFrame frame)
        {
            Vector2 input = frame.move;
            if (input.sqrMagnitude > 1f)
                input.Normalize();
            Transform basis = drive == WalkerDrive.ThirdPerson && cameraHeading != null ? cameraHeading : transform;
            Vector3 forward = basis.forward;
            forward.y = 0f;
            Vector3 right = basis.right;
            right.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = new Vector3(transform.forward.x, 0f, transform.forward.z);
            if (right.sqrMagnitude < 0.0001f)
                right = new Vector3(transform.right.x, 0f, transform.right.z);
            forward.Normalize();
            right.Normalize();
            Vector3 wish = right * input.x + forward * input.y;
            if (wish.sqrMagnitude > 1f)
                wish.Normalize();
            if (drive == WalkerDrive.ThirdPerson && wish.sqrMagnitude > 0.0004f)
            {
                Quaternion face = Quaternion.LookRotation(wish, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, face, turnSpeed * Time.deltaTime);
            }

            float speed = frame.sprint ? sprintSpeed : walkSpeed;
            Vector3 planar = wish * speed;
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            bool jumped = false;
            if (frame.jumpPressed && _controller.isGrounded)
            {
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                jumped = true;
            }

            _verticalVelocity += gravity * Time.deltaTime;
            planar.y = _verticalVelocity;
            _controller.Move(planar * Time.deltaTime);
            Velocity = _controller.velocity;
            IsGrounded = _controller.isGrounded;
            JumpAcceptedThisFrame = jumped;
        }

        void EnsureReady()
        {
            if (_controller == null)
                _controller = GetComponent<CharacterController>();
            if (_boundary == null)
                _boundary = FindAnyObjectByType<CityBoundaryMap>();
        }

        public void RememberSafeHere()
        {
            EnsureReady();
            _lastSafe = transform.position;
            _savedHeading = transform.eulerAngles.y;
            _hasSafe = true;
        }

        public void Teleport(Vector3 position, float yaw, bool rememberSafe)
        {
            EnsureReady();
            bool wasEnabled = _controller != null && _controller.enabled;
            if (_controller != null)
                _controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (_controller != null)
                _controller.enabled = wasEnabled || drive != WalkerDrive.Parked;
            _verticalVelocity = 0f;
            Velocity = Vector3.zero;
            IsGrounded = true;
            JumpAcceptedThisFrame = false;
            if (rememberSafe)
            {
                _lastSafe = position;
                _savedHeading = yaw;
                _hasSafe = true;
            }
        }

        public void ParkForFlight()
        {
            EnsureReady();
            _savedHeading = transform.eulerAngles.y;
            if (_controller != null && _controller.enabled && _controller.isGrounded)
            {
                _lastSafe = transform.position;
                _hasSafe = true;
            }

            drive = WalkerDrive.Parked;
            _verticalVelocity = 0f;
            cameraHeading = null;
        }

        public void ResumeWalking(WalkerDrive next, bool restoreLastSafe)
        {
            EnsureReady();
            if (next == WalkerDrive.Parked)
                next = WalkerDrive.FirstPerson;
            if (restoreLastSafe)
                Teleport(LastSafePosition, _savedHeading, true);
            drive = next;
            _verticalVelocity = 0f;
            if (_controller != null)
                _controller.enabled = true;
        }

        public void SetDrive(WalkerDrive next)
        {
            if (next == WalkerDrive.Parked)
            {
                ParkForFlight();
                return;
            }

            drive = next;
            if (_controller != null && !_controller.enabled)
                _controller.enabled = true;
        }

        public Vector3 ApplyContainment()
        {
            EnsureReady();
            EnforceBoundary();
            return transform.position;
        }

        public Vector3 ClampToPlayable(Vector3 world, Vector3 insideFallback)
        {
            EnsureReady();
            if (_boundary == null || _boundary.IsCapsuleInside(new Vector2(world.x, world.z), capsuleRadius, safetyMargin))
                return world;
            Vector3 safe = insideFallback;
            if (!_boundary.IsCapsuleInside(new Vector2(safe.x, safe.z), capsuleRadius, safetyMargin))
                safe = HasSafePosition ? LastSafePosition : spawnPosition;
            Vector3 outside = world;
            for (int i = 0; i < 16; i++)
            {
                Vector3 mid = (safe + outside) * 0.5f;
                if (_boundary.IsCapsuleInside(new Vector2(mid.x, mid.z), capsuleRadius, safetyMargin))
                    safe = mid;
                else
                    outside = mid;
            }

            safe.y = world.y;
            return safe;
        }

        bool FellBelowGround(Vector3 position)
        {
            if (TerrainGround.TrySurface(position.x, position.z, out float surface))
                return position.y < surface - Mathf.Max(1f, fallDropMeters);
            return position.y < fallResetY;
        }

        bool Recoverable(Vector3 position)
        {
            if (TerrainGround.TrySurface(position.x, position.z, out float surface))
                return position.y >= surface - 1f;
            return position.y >= fallResetY;
        }

        void EnforceBoundary()
        {
            EnsureReady();
            Vector3 position = transform.position;
            Vector2 flat = new Vector2(position.x, position.z);
            bool inside = _boundary == null || _boundary.IsCapsuleInside(flat, capsuleRadius, safetyMargin);
            bool fell = FellBelowGround(position);
            if (inside && !fell)
            {
                if (_controller.isGrounded)
                {
                    _lastSafe = position;
                    _savedHeading = transform.eulerAngles.y;
                    _hasSafe = true;
                }

                return;
            }

            Vector3 recover = _hasSafe ? _lastSafe : spawnPosition;
            if (!Recoverable(recover))
                recover = spawnPosition;
            _controller.enabled = false;
            transform.position = recover;
            _controller.enabled = true;
            _verticalVelocity = 0f;
        }

        public void ResetToSpawn()
        {
            EnsureReady();
            _controller.enabled = false;
            transform.position = spawnPosition;
            transform.rotation = Quaternion.Euler(0f, spawnYaw, 0f);
            _controller.enabled = true;
            _pitch = 0f;
            if (eye != null)
                eye.localRotation = Quaternion.identity;
            _verticalVelocity = 0f;
            _lastSafe = spawnPosition;
            _savedHeading = spawnYaw;
            _hasSafe = true;
        }

        public void ShowHint(string text, float seconds = 8f)
        {
            if (hintPanel == null)
                return;
            var label = hintPanel.GetComponent<Text>();
            if (label != null && !string.IsNullOrEmpty(text))
                label.text = text;
            hintPanel.SetActive(true);
            _hintUntil = Time.unscaledTime + seconds;
        }

        public void HideHint()
        {
            if (hintPanel != null)
                hintPanel.SetActive(false);
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            if (pausePanel != null)
                pausePanel.SetActive(paused);
            LockCursor(!paused);
        }

        void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused)
                return;
            if (ViewModeManager.Instance != null)
            {
                ViewModeManager.Instance.ApplyCursor();
                return;
            }

            if (!_paused)
                LockCursor(true);
        }
    }
}
