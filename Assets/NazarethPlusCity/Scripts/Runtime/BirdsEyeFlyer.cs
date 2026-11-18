using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// Separate flying camera. The grounded walker stays parked and is not lifted to this altitude.
    /// </summary>
    public class BirdsEyeFlyer : MonoBehaviour
    {
        public FirstPersonWalker walker;

        [Header("Speed")]
        public float flightSpeed = 24f;
        public float minFlightSpeed = 8f;
        public float maxFlightSpeed = 80f;
        public float acceleration = 36f;
        public float sprintMultiplier = 2.2f;
        public float verticalSpeed = 16f;
        public float scrollStep = 4f;

        [Header("Altitude")]
        public float initialHeight = 80f;
        public float minGroundClearance = 8f;
        [Tooltip("Lowest world Y. Terrain clearance still wins when the ground is higher than this.")]
        public float minAltitude = 0f;
        [Tooltip("Highest world Y. A taller terrain sample plus clearance can still raise the camera above this.")]
        public float maxAltitude = 1400f;
        public float groundProbeRadius = 2.5f;
        public float boundaryRadius = 2f;
        public float boundaryMargin = 0.08f;

        Vector3 _velocity;
        float _speed;
        CityBoundaryMap _boundary;
        Vector3 _mapFocus;
        bool _hasMapFocus;
        static readonly Vector2[] ProbeOffsets =
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(-1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, -1f)
        };

        public float CurrentSpeed => _speed;

        /// <summary>
        /// Map point this camera is navigating. Flight slides it with the camera.
        /// Height and the back offset are applied again on activate and are not part of the point.
        /// </summary>
        public Vector3 MapFocus => _mapFocus;
        public bool HasMapFocus => _hasMapFocus;

        public float FocusYaw
        {
            get
            {
                Vector3 forward = transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f)
                    return transform.eulerAngles.y;
                return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            }
        }

        void Awake()
        {
            _speed = flightSpeed;
            _boundary = FindAnyObjectByType<CityBoundaryMap>();
        }

        public void Activate(Transform player)
        {
            Vector3 origin = player != null ? player.position : transform.position;
            float yaw = player != null ? player.eulerAngles.y : transform.eulerAngles.y;
            ActivateAt(origin, yaw);
        }

        public void ActivateAt(Vector3 mapPosition, float yaw)
        {
            if (_boundary == null)
                _boundary = FindAnyObjectByType<CityBoundaryMap>();
            _speed = Mathf.Clamp(flightSpeed, minFlightSpeed, maxFlightSpeed);
            _velocity = Vector3.zero;
            Vector3 flatForward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 desired = mapPosition - flatForward * (initialHeight * 0.45f) + Vector3.up * initialHeight;
            Vector3 parked = mapPosition + Vector3.up * Mathf.Max(minGroundClearance, initialHeight);
            transform.position = Confine(desired, parked);
            Vector3 lookAt = mapPosition + flatForward * 18f;
            Vector3 dir = lookAt - transform.position;
            if (dir.sqrMagnitude < 0.01f)
                dir = flatForward + Vector3.down;
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            _mapFocus = mapPosition;
            _hasMapFocus = true;
        }

        void LateUpdate()
        {
            if (walker == null)
                return;
            var frame = walker.ReadControls();
            if (walker.SuppressFlightVertical)
            {
                frame.ascend = false;
                frame.descend = false;
            }

            Vector2 look = frame.look;
            if (look.sqrMagnitude > 0f)
            {
                float sensitivity = walker.lookSensitivity;
                transform.Rotate(0f, look.x * sensitivity, 0f, Space.World);
                Vector3 euler = transform.eulerAngles;
                float pitch = euler.x;
                if (pitch > 180f)
                    pitch -= 360f;
                pitch = Mathf.Clamp(pitch - look.y * sensitivity, -80f, 80f);
                transform.rotation = Quaternion.Euler(pitch, euler.y, 0f);
            }

            ApplyScroll(frame.scroll);
            Vector3 forward = transform.forward;
            forward.y = 0f;
            Vector3 right = transform.right;
            right.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();
            right.Normalize();
            Vector2 move = frame.move;
            if (move.sqrMagnitude > 1f)
                move.Normalize();
            float speed = _speed * (frame.sprint ? sprintMultiplier : 1f);
            Vector3 wish = (forward * move.y + right * move.x) * speed;
            wish.y = (frame.ascend ? 1f : 0f) - (frame.descend ? 1f : 0f);
            wish.y *= verticalSpeed;
            _velocity = Vector3.MoveTowards(_velocity, wish, acceleration * Time.deltaTime);
            ApplyFlightStep(_velocity * Time.deltaTime);
        }

        public Vector3 ApplyFlightStep(Vector3 worldDelta)
        {
            Vector3 previous = transform.position;
            transform.position = Confine(previous + worldDelta, previous);
            if (_hasMapFocus)
            {
                Vector3 delta = transform.position - previous;
                _mapFocus.x += delta.x;
                _mapFocus.z += delta.z;
            }

            return transform.position;
        }

        void ApplyScroll(float raw)
        {
            if (Mathf.Abs(raw) < 0.01f)
                return;
            float notches = Mathf.Abs(raw) > 3f ? raw / 120f : raw;
            _speed = Mathf.Clamp(_speed + Mathf.Sign(notches) * scrollStep * Mathf.Max(1f, Mathf.Abs(notches)), minFlightSpeed, maxFlightSpeed);
        }

        public Vector3 Confine(Vector3 desired, Vector3 fallback)
        {
            Vector3 result = desired;
            result.y = ClampAltitude(result.x, result.z, result.y);
            if (!Inside(result))
            {
                Vector3 xOnly = new Vector3(desired.x, result.y, fallback.z);
                Vector3 zOnly = new Vector3(fallback.x, result.y, desired.z);
                if (Inside(xOnly))
                    result = xOnly;
                else if (Inside(zOnly))
                    result = zOnly;
                else
                    result = new Vector3(fallback.x, result.y, fallback.z);
                result.y = ClampAltitude(result.x, result.z, result.y);
            }

            return result;
        }

        public float ClampAltitude(float east, float north, float y)
        {
            float floor = minAltitude;
            if (TryHighestSurface(east, north, out float surface))
                floor = Mathf.Max(floor, surface + minGroundClearance);
            y = Mathf.Max(y, floor);
            float ceiling = Mathf.Max(maxAltitude, floor);
            return Mathf.Min(y, ceiling);
        }

        public bool TryHighestSurface(float east, float north, out float y)
        {
            y = float.NegativeInfinity;
            bool any = false;
            float radius = Mathf.Max(0f, groundProbeRadius);
            for (int i = 0; i < ProbeOffsets.Length; i++)
            {
                float x = east + ProbeOffsets[i].x * radius;
                float z = north + ProbeOffsets[i].y * radius;
                if (TerrainGround.TrySurface(x, z, out float sample))
                {
                    any = true;
                    if (sample > y)
                        y = sample;
                }
            }

            return any;
        }

        bool Inside(Vector3 world)
        {
            if (_boundary == null)
                _boundary = FindAnyObjectByType<CityBoundaryMap>();
            if (_boundary == null)
                return true;
            return _boundary.IsCapsuleInside(new Vector2(world.x, world.z), boundaryRadius, boundaryMargin);
        }
    }
}
