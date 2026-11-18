using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// Marks the stable gameplay object. The capsule, walker, and camera targets stay here.
    /// The visible model lives under the CharacterVisual child and can be replaced.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerRoot : MonoBehaviour
    {
    }
}
