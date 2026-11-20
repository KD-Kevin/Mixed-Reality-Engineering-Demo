using UnityEngine;

namespace EngineeringDemo
{
    [CreateAssetMenu(menuName = "Engineering Demo/Fridge Interaction Settings")]
    public sealed class FridgeInteractionSettings : ScriptableObject
    {
        [Tooltip("Extra aiming tolerance around the fridge box, as a fraction of camera image width.")]
        [SerializeField, Range(0f, 0.1f)] private float _fridgeAimPadding = 0.035f;
        [Tooltip("Seconds to hold three fingers before the fridge matches the hand's rotation.")]
        [SerializeField, Min(0f)] private float _threeFingerAlignHoldSeconds = 0.3f;
        [Tooltip("Middle finger PIP extension needed to rearm. One is straight. Zero is bent 90 degrees or more.")]
        [SerializeField, Range(0.1f, 1f)] private float _triggerReadyExtension = 0.78f;
        [Tooltip("Fire when middle finger PIP extension falls to this value. Bending the fingertip does not affect it.")]
        [SerializeField, Range(0f, 0.95f)] private float _triggerFireExtension = 0.54f;
        [SerializeField, Min(0f)] private float _triggerReleaseSeconds = 0.12f;
        [SerializeField, ColorUsage(false)] private Color _shotRayColor = new Color(1f, 0.4f, 0.09f);
        [SerializeField, Min(0f)] private float _shotFlashSeconds = 0.15f;

        internal float FridgeAimPadding => _fridgeAimPadding;
        internal float ThreeFingerAlignHoldSeconds => _threeFingerAlignHoldSeconds;
        internal float TriggerReadyExtension => _triggerReadyExtension;
        internal float TriggerFireExtension => _triggerFireExtension;
        internal float TriggerReleaseSeconds => _triggerReleaseSeconds;
        internal Color ShotRayColor => _shotRayColor;
        internal float ShotFlashSeconds => _shotFlashSeconds;
        internal float FireThreshold => Mathf.Min(_triggerFireExtension, _triggerReadyExtension - 0.05f);
    }
}
