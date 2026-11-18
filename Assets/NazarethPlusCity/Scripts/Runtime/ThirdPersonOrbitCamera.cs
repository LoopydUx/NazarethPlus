using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// Orbit camera that follows the shared walker and pulls in when buildings block the view.
    /// </summary>
    public class ThirdPersonOrbitCamera : MonoBehaviour
    {
        public FirstPersonWalker walker;
        public Transform target;
        public float distance = 4.4f;
        public float minDistance = 0.85f;
        public float pivotHeight = 1.45f;
        public float defaultPitch = 16f;
        public float minPitch = -35f;
        public float maxPitch = 70f;
        public float collisionRadius = 0.22f;
        public float collisionBuffer = 0.18f;
        public float recoverSharpness = 8f;
        public LayerMask collisionMask = ~0;

        public float yaw;
        public float pitch = 16f;

        float _distance = 4.4f;
        static readonly RaycastHit[] Hits = new RaycastHit[32];
        static readonly Collider[] Overlaps = new Collider[16];

        public float CurrentDistance => _distance;
        public Transform IgnoreRoot => target != null ? target : (walker != null ? walker.transform : null);

        void Awake()
        {
            _distance = distance;
            pitch = defaultPitch;
        }

        void LateUpdate()
        {
            if (!enabled || walker == null)
                return;
            var frame = walker.ReadControls();
            yaw += frame.look.x * walker.lookSensitivity;
            pitch = Mathf.Clamp(pitch - frame.look.y * walker.lookSensitivity, minPitch, maxPitch);
            Place(false);
        }

        public void SnapBehind(Transform follow)
        {
            target = follow;
            if (follow != null)
                yaw = follow.eulerAngles.y;
            pitch = defaultPitch;
            _distance = distance;
            Place(true);
        }

        public void SetOrbit(float yawDegrees, float pitchDegrees)
        {
            yaw = yawDegrees;
            pitch = Mathf.Clamp(pitchDegrees, minPitch, maxPitch);
        }

        public void PlaceNow()
        {
            Place(true);
        }

        void Place(bool snap)
        {
            Transform follow = target != null ? target : (walker != null ? walker.transform : null);
            if (follow == null)
                return;
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 back = orbit * Vector3.back;
            Vector3 pivot = follow.position + Vector3.up * pivotHeight;
            float desired = ResolveDistance(pivot, back, distance);
            if (snap || desired < _distance - 0.02f)
                _distance = desired;
            else
                _distance = Mathf.Lerp(_distance, desired, 1f - Mathf.Exp(-recoverSharpness * Time.unscaledDeltaTime));
            transform.SetPositionAndRotation(pivot + back * _distance, orbit);
        }

        public float ResolveDistance(Vector3 pivot, Vector3 direction, float desiredDistance)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return minDistance;
            direction.Normalize();
            float limit = desiredDistance;
            int count = Physics.SphereCastNonAlloc(pivot, collisionRadius, direction, Hits, desiredDistance, collisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.collider == null || IsIgnored(hit.collider))
                    continue;
                float allowed = hit.distance - collisionBuffer;
                if (allowed < limit)
                    limit = allowed;
            }

            limit = Mathf.Clamp(limit, minDistance, desiredDistance);
            Vector3 point = pivot + direction * limit;
            if (OverlapsWorld(point))
            {
                float stepped = limit;
                for (int n = 0; n < 8 && OverlapsWorld(pivot + direction * stepped); n++)
                    stepped = Mathf.Max(minDistance, stepped - 0.25f);
                limit = stepped;
            }

            return limit;
        }

        bool OverlapsWorld(Vector3 point)
        {
            int count = Physics.OverlapSphereNonAlloc(point, collisionRadius, Overlaps, collisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (Overlaps[i] != null && !IsIgnored(Overlaps[i]))
                    return true;
            }

            return false;
        }

        bool IsIgnored(Collider collider)
        {
            Transform root = IgnoreRoot;
            if (root == null || collider == null)
                return false;
            Transform hit = collider.transform;
            return hit == root || hit.IsChildOf(root);
        }
    }
}
