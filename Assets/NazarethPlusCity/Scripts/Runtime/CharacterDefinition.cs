using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// Everything that changes when the player model changes.
    /// Movement, cameras, and placement stay on the gameplay root.
    /// </summary>
    [CreateAssetMenu(fileName = "Character", menuName = "Nazareth Plus/Character Definition")]
    public class CharacterDefinition : ScriptableObject
    {
        [Header("Visual")]
        [Tooltip("Model prefab. It needs its own Animator and a valid humanoid Avatar.")]
        public GameObject visualPrefab;
        [Tooltip("Avatar for this model. Leave empty to use the Avatar already on the prefab.")]
        public Avatar avatar;
        [Tooltip("Local scale of the spawned model.")]
        public Vector3 visualScale = Vector3.one;
        [Tooltip("Local euler angles so the model faces the gameplay root's forward axis.")]
        public Vector3 visualEuler;
        [Tooltip("Local position of the model. Use Y to put the feet on the bottom of the capsule.")]
        public Vector3 footOffset;

        [Header("Animation")]
        [Tooltip("Shared locomotion controller. Parameters: Speed, Grounded, VerticalSpeed, Jump, CycleSpeed.")]
        public RuntimeAnimatorController locomotionController;
        [Tooltip("Optional per-character clip swap. When set, it is used instead of the shared controller.")]
        public AnimatorOverrideController animationOverride;
        public AnimationClip idle;
        public AnimationClip walk;
        public AnimationClip run;
        public AnimationClip jump;
        [Tooltip("Looping airborne clip. Used when the character walks off an edge or passes the top of a jump.")]
        public AnimationClip fall;
        [Tooltip("Landing clip used while nearly stopped.")]
        public AnimationClip land;
        [Tooltip("Optional landing clip blended in at walk speed.")]
        public AnimationClip landWalk;
        [Tooltip("Optional landing clip blended in at run speed.")]
        public AnimationClip landRun;
        [Tooltip("Speeds the blend tree was built with. Rebuild the controller after changing them.")]
        public float walkSpeedThreshold = 4.2f;
        public float runSpeedThreshold = 7.5f;
        [Tooltip("Scale the walk and run cycle so the feet keep up with the real movement speed.")]
        public bool matchFootCycle = true;
        [Tooltip("World speed, in metres per second, that the walk clip was authored to match.")]
        public float walkClipSpeed = 2f;
        [Tooltip("World speed, in metres per second, that the run clip was authored to match.")]
        public float runClipSpeed = 6f;

        [Header("Collision and camera")]
        public float capsuleHeight = 1.8f;
        public float capsuleRadius = 0.3f;
        public float capsuleCenterY = 0.9f;
        public float eyeHeight = 1.64f;
        [Tooltip("Height of the third-person orbit pivot on the gameplay root.")]
        public float cameraTargetHeight = 1.45f;

        public RuntimeAnimatorController ActiveController =>
            animationOverride != null ? animationOverride : locomotionController;

        public Vector3 ResolvedScale =>
            visualScale.sqrMagnitude < 0.0001f ? Vector3.one : visualScale;
    }
}
