using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// Spawns and connects the current CharacterDefinition without moving the gameplay root
    /// or changing the active view.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class CharacterPresenter : MonoBehaviour
    {
        public CharacterDefinition definition;
        public Transform visualHost;
        [SerializeField] GameObject appliedVisual;

        CharacterVisual _visual;

        public CharacterVisual Visual => _visual;

        void Awake()
        {
            if (definition == null)
                return;
            if (appliedVisual != definition.visualPrefab)
                ApplyDefinition(definition);
            else
                BindExisting();
        }

        [ContextMenu("Apply Character Definition")]
        void ApplyFromMenu()
        {
            ApplyDefinition(definition);
        }

        public Transform EnsureHost()
        {
            Transform host = transform.Find(CharacterVisual.HostName);
            if (host != null && host.GetComponent<Animator>() != null)
                host = WrapModel(host);
            if (host == null)
            {
                var created = new GameObject(CharacterVisual.HostName);
                created.transform.SetParent(transform, false);
                host = created.transform;
            }

            visualHost = host;
            _visual = host.GetComponent<CharacterVisual>();
            if (_visual == null)
                _visual = host.gameObject.AddComponent<CharacterVisual>();
            StripModelGameplay(host);
            return host;
        }

        public void BindExisting()
        {
            var host = EnsureHost();
            Transform model = FindModel(host);
            if (model != null && definition != null)
                ApplyModelTransform(model);
            ConfigureAnimator(host.GetComponentInChildren<Animator>(true));
            ApplyBody(transform.position, transform.rotation, false);
            if (definition != null)
                appliedVisual = definition.visualPrefab;
            if (_visual != null)
                _visual.CacheRenderers();
            var locomotion = GetComponent<CharacterLocomotion>();
            if (locomotion != null)
                locomotion.BindAnimator();
        }

        public void ApplyDefinition(CharacterDefinition next)
        {
            bool visible = _visual != null && _visual.AnyVisible;
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            definition = next;
            appliedVisual = next != null ? next.visualPrefab : null;
            var host = EnsureHost();
            ClearModels(host);
            if (next != null && next.visualPrefab != null)
            {
                var instance = Instantiate(next.visualPrefab, host);
                instance.name = "CharacterModel";
                ApplyModelTransform(instance.transform);
            }

            StripModelGameplay(host);
            ConfigureAnimator(host.GetComponentInChildren<Animator>(true));
            ApplyBody(position, rotation, true);
            if (_visual != null)
            {
                _visual.CacheRenderers();
                _visual.SetVisible(visible);
            }

            var locomotion = GetComponent<CharacterLocomotion>();
            if (locomotion != null)
            {
                locomotion.BindAnimator();
                locomotion.SnapIdle();
            }
        }

        public static Transform FindModel(Transform host)
        {
            if (host == null)
                return null;
            for (int i = 0; i < host.childCount; i++)
            {
                var child = host.GetChild(i);
                if (child.GetComponentInChildren<Animator>(true) != null || child.GetComponentInChildren<Renderer>(true) != null)
                    return child;
            }

            return null;
        }

        void ApplyModelTransform(Transform model)
        {
            if (definition == null || model == null)
                return;
            model.localPosition = definition.footOffset;
            model.localRotation = Quaternion.Euler(definition.visualEuler);
            model.localScale = definition.ResolvedScale;
        }

        void ConfigureAnimator(Animator animator)
        {
            if (animator == null || definition == null)
                return;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;
            if (definition.avatar != null)
                animator.avatar = definition.avatar;
            var controller = definition.ActiveController;
            if (controller != null)
                animator.runtimeAnimatorController = controller;
        }

        void ApplyBody(Vector3 position, Quaternion rotation, bool restorePose)
        {
            if (definition == null)
                return;
            var controller = GetComponent<CharacterController>();
            var walker = GetComponent<FirstPersonWalker>();
            bool capsuleChanged = false;
            if (controller != null)
            {
                capsuleChanged = Mathf.Abs(controller.height - definition.capsuleHeight) > 0.001f
                    || Mathf.Abs(controller.radius - definition.capsuleRadius) > 0.001f
                    || Mathf.Abs(controller.center.y - definition.capsuleCenterY) > 0.001f;
                controller.height = definition.capsuleHeight;
                controller.radius = definition.capsuleRadius;
                controller.center = new Vector3(0f, definition.capsuleCenterY, 0f);
            }

            if (walker != null)
            {
                walker.capsuleRadius = definition.capsuleRadius;
                walker.eyeHeight = definition.eyeHeight;
                if (walker.eye != null)
                {
                    Vector3 eye = walker.eye.localPosition;
                    eye.y = definition.eyeHeight;
                    walker.eye.localPosition = eye;
                }
            }

            var orbit = GetComponentInChildren<ThirdPersonOrbitCamera>(true);
            if (orbit != null)
                orbit.pivotHeight = definition.cameraTargetHeight;

            if (restorePose || capsuleChanged)
            {
                if (controller != null)
                    controller.enabled = false;
                transform.SetPositionAndRotation(position, rotation);
                if (controller != null && (walker == null || walker.drive != WalkerDrive.Parked))
                    controller.enabled = true;
            }

        }

        static Transform WrapModel(Transform model)
        {
            var parent = model.parent;
            int index = model.GetSiblingIndex();
            var hostObject = new GameObject(CharacterVisual.HostName);
            var host = hostObject.transform;
            host.SetParent(parent, false);
            host.SetSiblingIndex(index);
            model.SetParent(host, true);
            if (model.name == CharacterVisual.HostName)
                model.name = "CharacterModel";
            return host;
        }

        void StripModelGameplay(Transform host)
        {
            var visuals = host.GetComponentsInChildren<CharacterVisual>(true);
            for (int i = 0; i < visuals.Length; i++)
            {
                if (visuals[i] != null && visuals[i].gameObject != host.gameObject)
                    DestroyObject(visuals[i]);
            }

            var motions = GetComponentsInChildren<CharacterLocomotion>(true);
            for (int i = 0; i < motions.Length; i++)
            {
                if (motions[i] != null && motions[i].gameObject != gameObject)
                    DestroyObject(motions[i]);
            }
        }

        static void ClearModels(Transform host)
        {
            for (int i = host.childCount - 1; i >= 0; i--)
                DestroyObject(host.GetChild(i).gameObject);
        }

        static void DestroyObject(Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
