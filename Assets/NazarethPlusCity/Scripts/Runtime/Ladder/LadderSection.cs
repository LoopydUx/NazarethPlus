using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// One reusable ladder section. Bottom sits at local Y 0; TopSnap marks the next section attach point.
    /// </summary>
    [DisallowMultipleComponent]
    public class LadderSection : MonoBehaviour
    {
        public float sectionHeight = 0.92f;
        public float climbWidth = 0.45f;
        public float climbDepth = 0.35f;
        public Transform bottomSnap;
        public Transform topSnap;
        public Transform climbCenter;

        public float AppliedHeight => Mathf.Max(0.05f, sectionHeight * Mathf.Abs(transform.localScale.y));

        public Vector3 BottomWorld => bottomSnap != null ? bottomSnap.position : transform.position;

        public Vector3 TopWorld => topSnap != null
            ? topSnap.position
            : transform.position + transform.up * AppliedHeight;

        public Vector3 ClimbNormal
        {
            get
            {
                Vector3 forward = transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f)
                    forward = Vector3.forward;
                return forward.normalized;
            }
        }

        public void ApplyHeightScale(float fullSectionHeight, float usedHeight)
        {
            float full = Mathf.Max(0.05f, fullSectionHeight);
            float used = Mathf.Clamp(usedHeight, 0.05f, full + 0.001f);
            Vector3 scale = transform.localScale;
            scale.y = used / full;
            transform.localScale = scale;
            sectionHeight = full;
            if (topSnap != null)
                topSnap.localPosition = new Vector3(0f, full, 0f);
            if (bottomSnap != null)
                bottomSnap.localPosition = Vector3.zero;
            if (climbCenter != null)
                climbCenter.localPosition = new Vector3(0f, full * 0.5f, -0.05f);
        }

        public Bounds ClimbBounds()
        {
            Vector3 center = climbCenter != null
                ? climbCenter.position
                : (BottomWorld + TopWorld) * 0.5f;
            Vector3 size = new Vector3(climbWidth, AppliedHeight, climbDepth);
            return new Bounds(center, size);
        }
    }
}
