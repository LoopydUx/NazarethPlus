using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// Renderers for the current character model. First person and the welcome screen turn them off.
    /// This component stays on the CharacterVisual host, not on a particular skeleton.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterVisual : MonoBehaviour
    {
        public const string HostName = "CharacterVisual";

        public float fittedHeight = 1.8f;
        public Renderer[] renderers;

        public void CacheRenderers()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        public bool AnyVisible
        {
            get
            {
                if (renderers == null)
                    return false;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null && renderers[i].enabled)
                        return true;
                }

                return false;
            }
        }

        public void SetVisible(bool visible)
        {
            if (renderers == null || renderers.Length == 0)
                CacheRenderers();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = visible;
            }
        }

        public bool TryWorldBounds(out Bounds bounds)
        {
            bounds = default;
            if (renderers == null || renderers.Length == 0)
                CacheRenderers();
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;
                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return any && bounds.size.sqrMagnitude > 0.000001f;
        }

        void Awake()
        {
            CacheRenderers();
            SetVisible(false);
        }
    }
}
