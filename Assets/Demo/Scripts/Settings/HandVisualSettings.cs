using UnityEngine;

namespace EngineeringDemo
{
    [CreateAssetMenu(menuName = "Engineering Demo/Hand Visual Settings")]
    public sealed class HandVisualSettings : ScriptableObject
    {
        [SerializeField, Range(0.02f, 1f)] private float _rayTipWidth = 0.18f;

        [Header("Gesture Colors")]
        [SerializeField, ColorUsage(false)] private Color _neutralColor = Color.white;
        [SerializeField, ColorUsage(false)] private Color _openHandColor = Color.cyan;
        [SerializeField, ColorUsage(false)] private Color _fistColor = new Color(1f, 0.2f, 0.25f);
        [SerializeField, ColorUsage(false)] private Color _pinchColor = new Color(1f, 0.3f, 0.85f);
        [SerializeField, ColorUsage(false)] private Color _pointColor = new Color(0.35f, 1f, 0.25f);
        [SerializeField, ColorUsage(false)] private Color _pistolColor = new Color(1f, 0.55f, 0.15f);
        [SerializeField, ColorUsage(false)] private Color _middleFingerColor = new Color(0.38f, 0.72f, 1f);
        [SerializeField, ColorUsage(false)] private Color _threeFingersColor = new Color(1f, 0.93f, 0.2f);
        [SerializeField, ColorUsage(false)] private Color _thumbsUpColor = new Color(0.15f, 0.92f, 0.62f);
        [SerializeField, ColorUsage(false)] private Color _thumbsDownColor = new Color(0.82f, 0.47f, 1f);
        [Tooltip("Hue rotation in degrees for all left-hand gesture and trigger colors.")]
        [SerializeField, Range(-180f, 180f)] private float _leftHandHue = 60f;

        [Header("Pistol Trigger Colors")]
        [SerializeField] private bool _usePistolReadyColor = true;
        [SerializeField, ColorUsage(false)] private Color _pistolReadyColor = new Color(0.3f, 1f, 0.3f);
        [SerializeField] private bool _usePistolFiredColor = true;
        [SerializeField, ColorUsage(false)] private Color _pistolFiredColor = new Color(0.65f, 0.35f, 1f);

        internal float RayTipWidth => _rayTipWidth;
        internal Color NeutralColor => _neutralColor;
        internal Color OpenHandColor => _openHandColor;
        internal Color FistColor => _fistColor;
        internal Color PinchColor => _pinchColor;
        internal Color PointColor => _pointColor;
        internal Color PistolColor => _pistolColor;
        internal Color MiddleFingerColor => _middleFingerColor;
        internal Color ThreeFingersColor => _threeFingersColor;
        internal Color ThumbsUpColor => _thumbsUpColor;
        internal Color ThumbsDownColor => _thumbsDownColor;
        internal float LeftHandHue => _leftHandHue;
        internal bool UsePistolReadyColor => _usePistolReadyColor;
        internal Color PistolReadyColor => _pistolReadyColor;
        internal bool UsePistolFiredColor => _usePistolFiredColor;
        internal Color PistolFiredColor => _pistolFiredColor;
    }
}
