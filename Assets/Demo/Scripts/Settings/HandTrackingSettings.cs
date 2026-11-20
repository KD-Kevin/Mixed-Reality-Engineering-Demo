using UnityEngine;

namespace EngineeringDemo
{
    [CreateAssetMenu(menuName = "Engineering Demo/Hand Tracking Settings")]
    public sealed class HandTrackingSettings : ScriptableObject
    {
        [Header("Tracking")]
        [SerializeField] private TextAsset _model;
        [SerializeField] private bool _useWebcam = true;
        [SerializeField] private string _webcamName = "";
        [SerializeField] private bool _mirrorWebcam = true;
        [SerializeField, Min(1)] private int _cameraWidth = 640;
        [SerializeField, Min(1)] private int _cameraHeight = 480;
        [SerializeField, Range(1, 60)] private int _cameraFrameRate = 30;
        [SerializeField, Range(1, 2)] private int _maximumHands = 2;
        [SerializeField, Range(0f, 1f)] private float _minimumConfidence = 0.5f;
        [Header("Stabilization")]
        [SerializeField, Min(0f)] private float _smoothingSeconds = 0.06f;
        [Tooltip("Seconds a different gesture must be observed before it replaces the current gesture.")]
        [SerializeField, Min(0f)] private float _gestureChangeSeconds = 0.12f;
        [SerializeField, Min(0f)] private float _jitterDeadZone = 0.0015f;
        [SerializeField, Min(0.01f)] private float _trackingTimeoutSeconds = 0.35f;
        [SerializeField, Min(0f)] private float _pinchClose = 0.25f;
        [SerializeField, Min(0f)] private float _pinchRelease = 0.35f;
        [Tooltip("How far the middle finger can straighten while the hand still counts as Pistol.")]
        [SerializeField, Range(0.75f, 1f)] private float _pistolMiddleExtensionLimit = 1f;
        [Header("World placement")]
        [Tooltip("Wrist depth along the camera's forward axis, in Unity world units.")]
        [SerializeField, Min(0.05f)] private float _initialWristDistance = 0.6f;

        internal TextAsset Model => _model;
        internal bool UseWebcam => _useWebcam;
        internal string WebcamName => _webcamName;
        internal bool MirrorWebcam => _mirrorWebcam;
        internal int CameraWidth => _cameraWidth;
        internal int CameraHeight => _cameraHeight;
        internal int CameraFrameRate => _cameraFrameRate;
        internal int MaximumHands => _maximumHands;
        internal float MinimumConfidence => _minimumConfidence;
        internal float SmoothingSeconds => _smoothingSeconds;
        internal float GestureChangeSeconds => _gestureChangeSeconds;
        internal float JitterDeadZone => _jitterDeadZone;
        internal float TrackingTimeoutSeconds => _trackingTimeoutSeconds;
        internal float PinchClose => _pinchClose;
        internal float PinchRelease => _pinchRelease;
        internal float PistolMiddleExtensionLimit => _pistolMiddleExtensionLimit;
        internal float InitialWristDistance => _initialWristDistance;
    }
}
