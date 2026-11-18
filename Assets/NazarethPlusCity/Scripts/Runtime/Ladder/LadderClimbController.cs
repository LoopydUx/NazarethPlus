using UnityEngine;
using UnityEngine.InputSystem;

namespace NazarethPlusCity
{
    /// <summary>
    /// Auto-mounts nearby ladders and restores normal movement on dismount.
    /// While climbing, F can still add sections through LadderBuilder.
    /// </summary>
    [DefaultExecutionOrder(45)]
    [DisallowMultipleComponent]
    public class LadderClimbController : MonoBehaviour
    {
        public LadderSystemSettings settings;
        public LadderBuilder builder;

        FirstPersonWalker _walker;
        CharacterController _controller;
        LadderStructure _active;
        float _heightAlong;
        bool _climbing;
        float _mountCooldown;

        public bool IsClimbing => _climbing;
        public LadderStructure ActiveLadder => _active;
        public float HeightAlong => _heightAlong;

        void Awake()
        {
            _walker = GetComponent<FirstPersonWalker>();
            _controller = GetComponent<CharacterController>();
            if (builder == null)
                builder = GetComponent<LadderBuilder>();
        }

        void Update()
        {
            if (!Application.isPlaying || _walker == null)
                return;
            if (!GameplayReady())
            {
                if (_climbing)
                    Dismount(false);
                return;
            }

            if (_mountCooldown > 0f)
                _mountCooldown -= Time.deltaTime;

            if (_climbing)
            {
                ClimbFrame();
                return;
            }

            var placement = GetComponent<AssetPlacementController>();
            if (placement != null && placement.CapturesRotateKeys)
                return;
            if (_mountCooldown <= 0f)
                TryAutoMount();
        }

        bool GameplayReady()
        {
            var manager = ViewModeManager.Instance;
            if (manager != null)
            {
                if (!manager.GameplayAllowed)
                    return false;
                if (manager.Current == ExplorationMode.BirdsEye)
                    return false;
            }

            return !_walker.InputBlocked && _walker.drive != WalkerDrive.Parked;
        }

        public void TryAutoMount()
        {
            if (_climbing || builder == null)
                return;
            float mount = settings != null ? settings.mountDistanceMeters : 1.35f;
            float roofMount = settings != null ? settings.roofMountDistanceMeters : 1.6f;
            LadderStructure best = null;
            float bestHeight = 0f;
            bool fromRoof = false;
            float bestScore = float.MaxValue;
            var ladders = builder.Ladders;
            for (int i = 0; i < ladders.Count; i++)
            {
                LadderStructure ladder = ladders[i];
                if (ladder == null || !ladder.HasSections)
                    continue;
                if (ladder.TryMountHeight(transform.position, false, mount, out float groundHeight))
                {
                    float score = HorizontalDistance(transform.position, ladder.BasePosition);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = ladder;
                        bestHeight = groundHeight;
                        fromRoof = false;
                    }
                }

                if (ladder.TryMountHeight(transform.position, true, roofMount, out float roofHeight))
                {
                    float score = HorizontalDistance(transform.position, ladder.landingPoint) * 0.5f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = ladder;
                        bestHeight = roofHeight;
                        fromRoof = true;
                    }
                }
            }

            if (best == null)
                return;
            BeginClimb(best, bestHeight, fromRoof);
        }

        public void BeginClimb(LadderStructure ladder, float heightAlong, bool fromRoof)
        {
            if (ladder == null || !ladder.HasSections)
                return;
            _active = ladder;
            _heightAlong = Mathf.Clamp(heightAlong, 0.05f, Mathf.Max(0.05f, ladder.BuiltClimbHeight));
            _climbing = true;
            _walker.MovementOverridden = true;
            SnapToLadder();
            if (fromRoof && _walker != null)
                _walker.ShowHint("Climbing down · F adds sections", 1.8f);
            else if (_walker != null)
                _walker.ShowHint("Climbing · W/S move · F add section", 1.8f);
        }

        public void NotifyLadderExtended(LadderStructure ladder)
        {
            if (!_climbing || ladder == null || ladder != _active)
                return;
            // Keep the climber attached; BuiltClimbHeight growth unlocks further upward travel.
            _heightAlong = Mathf.Min(_heightAlong, ladder.BuiltClimbHeight);
            SnapToLadder();
        }

        public void DebugSetHeight(float heightAlong)
        {
            if (!_climbing || _active == null)
                return;
            _heightAlong = Mathf.Clamp(heightAlong, 0f, _active.BuiltClimbHeight);
            SnapToLadder();
        }

        public void DebugStepOntoRoof()
        {
            StepOntoRoof();
        }

        public void DebugDismount()
        {
            Dismount(false);
        }

        void ClimbFrame()
        {
            if (_active == null || !_active.HasSections)
            {
                Dismount(false);
                return;
            }

            CityControlFrame frame = _walker.ReadControls();
            if (_walker.drive == WalkerDrive.FirstPerson)
                _walker.ApplyLookOnly(frame.look);

            float speed = settings != null ? settings.climbSpeed : 2.4f;
            float maxHeight = _active.BuiltClimbHeight;
            _heightAlong = Mathf.Clamp(_heightAlong + frame.move.y * speed * Time.deltaTime, 0f, maxHeight);

            if (_active.IsComplete && _heightAlong >= maxHeight - 0.02f && frame.move.y > 0.15f)
            {
                StepOntoRoof();
                return;
            }

            if (_heightAlong <= 0.02f && frame.move.y < -0.15f)
            {
                Dismount(false);
                return;
            }

            SnapToLadder();
        }

        void SnapToLadder()
        {
            if (_active == null || _controller == null)
                return;
            float standoff = settings != null ? settings.climbStandOffMeters : 0.42f;
            Vector3 point = _active.BasePosition - _active.wallNormal.normalized * standoff;
            point.y = _active.BasePosition.y + _heightAlong;
            Quaternion face = Quaternion.LookRotation(-_active.wallNormal, Vector3.up);
            bool was = _controller.enabled;
            _controller.enabled = false;
            transform.SetPositionAndRotation(point, face);
            _controller.enabled = was;
        }

        void StepOntoRoof()
        {
            if (_active == null || !_active.IsComplete)
                return;
            Vector3 landing = _active.landingPoint;
            float yaw = Quaternion.LookRotation(-_active.wallNormal, Vector3.up).eulerAngles.y;
            EndClimbState();
            _mountCooldown = 0.45f;
            _walker.Teleport(landing, yaw, true);
            _walker.ShowHint("Reached the roof", 2f);
        }

        void Dismount(bool toRoof)
        {
            if (toRoof)
            {
                StepOntoRoof();
                return;
            }

            Vector3 point = _active != null
                ? _active.GroundDismountPoint(settings != null ? settings.dismountPushMeters : 0.55f)
                : transform.position;
            float yaw = transform.eulerAngles.y;
            EndClimbState();
            _mountCooldown = 0.35f;
            _walker.Teleport(point, yaw, true);
        }

        void EndClimbState()
        {
            _climbing = false;
            _active = null;
            _heightAlong = 0f;
            if (_walker != null)
                _walker.MovementOverridden = false;
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
