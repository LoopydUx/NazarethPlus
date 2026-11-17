using UnityEngine;
using UnityEngine.InputSystem;

namespace NazarethPlusCity
{
    /// <summary>
    /// Desktop first-person walk. Actions live on NazarethPlusCity.inputactions.
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
        public Vector3 spawnPosition;
        public float spawnYaw;
        public Transform eye;
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
        float _pitch;
        float _verticalVelocity;
        Vector3 _lastSafe;
        bool _paused;
        float _hintUntil;
        bool _hasSafe;

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
            _hintUntil = Time.unscaledTime + 8f;
            LockCursor(true);
        }

        void Update()
        {
            if (hintPanel != null && hintPanel.activeSelf && Time.unscaledTime >= _hintUntil)
                hintPanel.SetActive(false);

            if (_pause != null && _pause.WasPressedThisFrame())
                SetPaused(!_paused);
            if (_reset != null && _reset.WasPressedThisFrame())
                ResetToSpawn();
            if (_paused)
                return;

            Look();
            MoveBody();
            EnforceBoundary();
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
        }

        void Look()
        {
            if (eye == null || _look == null)
                return;
            Vector2 delta = _look.ReadValue<Vector2>();
            transform.Rotate(0f, delta.x * lookSensitivity, 0f, Space.World);
            _pitch = Mathf.Clamp(_pitch - delta.y * lookSensitivity, -85f, 85f);
            eye.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void MoveBody()
        {
            Vector2 input = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
            if (input.sqrMagnitude > 1f)
                input.Normalize();
            float speed = _sprint != null && _sprint.IsPressed() ? sprintSpeed : walkSpeed;
            Vector3 planar = (transform.right * input.x + transform.forward * input.y) * speed;
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            if (_jump != null && _jump.WasPressedThisFrame() && _controller.isGrounded)
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _verticalVelocity += gravity * Time.deltaTime;
            planar.y = _verticalVelocity;
            _controller.Move(planar * Time.deltaTime);
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
            _hasSafe = true;
        }

        public Vector3 ApplyContainment()
        {
            EnsureReady();
            EnforceBoundary();
            return transform.position;
        }

        void EnforceBoundary()
        {
            EnsureReady();
            Vector3 position = transform.position;
            Vector2 flat = new Vector2(position.x, position.z);
            bool inside = _boundary == null || _boundary.IsCapsuleInside(flat, capsuleRadius, safetyMargin);
            bool fell = position.y < fallResetY;
            if (inside && !fell)
            {
                if (_controller.isGrounded)
                {
                    _lastSafe = position;
                    _hasSafe = true;
                }

                return;
            }

            Vector3 recover = _hasSafe ? _lastSafe : spawnPosition;
            if (recover.y < 0f)
                recover.y = spawnPosition.y;
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
            _hasSafe = true;
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
            if (focused && !_paused)
                LockCursor(true);
        }
    }
}
