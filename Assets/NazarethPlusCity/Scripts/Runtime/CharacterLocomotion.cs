using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// Plays the shared humanoid controller from the walker's real velocity.
    /// Root motion stays off so the CharacterController is the only thing that moves the player.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public class CharacterLocomotion : MonoBehaviour
    {
        public const string SpeedParam = "Speed";
        public const string GroundedParam = "Grounded";
        public const string VerticalSpeedParam = "VerticalSpeed";
        public const string JumpParam = "Jump";
        public const string CycleSpeedParam = "CycleSpeed";

        public float accelerateDamp = 0.16f;
        public float stopDamp = 0.08f;

        Animator _animator;
        FirstPersonWalker _walker;
        CharacterPresenter _presenter;
        float _smoothed;
        float _dampVelocity;
        bool _airborne;
        float _airTime;
        bool _ready;

        public Animator Animator => _animator;

        void Awake()
        {
            _walker = GetComponent<FirstPersonWalker>();
            _presenter = GetComponent<CharacterPresenter>();
            BindAnimator();
        }

        public void BindAnimator()
        {
            _animator = GetComponentInChildren<Animator>(true);
            _ready = false;
            if (_animator == null)
                return;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            CacheParameters();
        }

        void Update()
        {
            if (_walker == null)
                _walker = GetComponent<FirstPersonWalker>();
            bool live = _walker != null && _walker.drive != WalkerDrive.Parked && !_walker.InputBlocked;
            float horizontal = 0f;
            float vertical = 0f;
            bool grounded = true;
            bool jump = false;
            if (live)
            {
                Vector3 velocity = _walker.Velocity;
                horizontal = new Vector3(velocity.x, 0f, velocity.z).magnitude;
                vertical = velocity.y;
                grounded = _walker.IsGrounded;
                jump = _walker.JumpAcceptedThisFrame;
            }

            ApplyMotion(horizontal, vertical, grounded, jump, Time.deltaTime);
        }

        public void ApplyMotion(float horizontalSpeed, float verticalSpeed, bool grounded, bool jumpAccepted, float deltaTime)
        {
            if (_animator == null)
                BindAnimator();
            if (_animator == null || !_ready || _animator.runtimeAnimatorController == null)
                return;
            _animator.applyRootMotion = false;
            if (jumpAccepted)
            {
                _airborne = true;
                _airTime = 0f;
            }

            if (_airborne)
            {
                _airTime += deltaTime;
                bool descending = verticalSpeed <= 0.35f;
                bool settled = grounded && descending && _airTime > 0.12f;
                if (settled)
                    _airborne = false;
            }

            bool animGrounded = grounded && !_airborne;
            float damp = horizontalSpeed + 0.35f < _smoothed ? stopDamp : accelerateDamp;
            if (deltaTime < 0.0001f)
                deltaTime = 0.0001f;
            _smoothed = Mathf.SmoothDamp(_smoothed, Mathf.Max(0f, horizontalSpeed), ref _dampVelocity, damp, Mathf.Infinity, deltaTime);
            if (_smoothed < 0.04f && horizontalSpeed < 0.04f)
                _smoothed = 0f;

            _animator.SetFloat(SpeedId, _smoothed);
            _animator.SetFloat(VerticalId, verticalSpeed);
            _animator.SetBool(GroundedId, animGrounded);
            _animator.SetFloat(CycleId, CycleScale(_smoothed));
            if (jumpAccepted)
                _animator.SetTrigger(JumpId);
        }

        public void SnapIdle()
        {
            _smoothed = 0f;
            _dampVelocity = 0f;
            _airborne = false;
            _airTime = 0f;
            if (_animator == null || !_ready)
                return;
            _animator.SetFloat(SpeedId, 0f);
            _animator.SetFloat(VerticalId, 0f);
            _animator.SetBool(GroundedId, true);
            _animator.SetFloat(CycleId, 1f);
            _animator.ResetTrigger(JumpId);
        }

        float CycleScale(float speed)
        {
            var definition = _presenter != null ? _presenter.definition : null;
            if (definition == null || !definition.matchFootCycle || speed < 0.25f)
                return 1f;
            float walkMove = _walker != null ? _walker.walkSpeed : definition.walkSpeedThreshold;
            float runMove = _walker != null ? _walker.sprintSpeed : definition.runSpeedThreshold;
            float walkClip = definition.walkClipSpeed;
            float runClip = definition.runClipSpeed;
            if (walkClip < 0.2f || runClip < 0.2f)
                return 1f;
            float authored = speed <= walkMove || runMove <= walkMove
                ? walkClip
                : Mathf.Lerp(walkClip, runClip, Mathf.InverseLerp(walkMove, runMove, speed));
            if (authored < 0.2f)
                return 1f;
            return Mathf.Clamp(speed / authored, 0.75f, 1.85f);
        }

        int SpeedId;
        int GroundedId;
        int VerticalId;
        int JumpId;
        int CycleId;

        void CacheParameters()
        {
            _ready = false;
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;
            bool speed = false;
            bool grounded = false;
            bool vertical = false;
            bool jump = false;
            bool cycle = false;
            var parameters = _animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                string name = parameters[i].name;
                if (name == SpeedParam)
                    speed = true;
                else if (name == GroundedParam)
                    grounded = true;
                else if (name == VerticalSpeedParam)
                    vertical = true;
                else if (name == JumpParam)
                    jump = true;
                else if (name == CycleSpeedParam)
                    cycle = true;
            }

            if (!speed || !grounded || !vertical || !jump || !cycle)
                return;
            SpeedId = Animator.StringToHash(SpeedParam);
            GroundedId = Animator.StringToHash(GroundedParam);
            VerticalId = Animator.StringToHash(VerticalSpeedParam);
            JumpId = Animator.StringToHash(JumpParam);
            CycleId = Animator.StringToHash(CycleSpeedParam);
            _ready = true;
        }
    }
}
