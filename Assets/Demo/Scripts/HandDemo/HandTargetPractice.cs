using UnityEngine;
using UnityEngine.InputSystem;

namespace EngineeringDemo
{
    [DisallowMultipleComponent]
    public sealed class HandTargetPractice : MonoBehaviour
    {
        [SerializeField] private Renderer[] _targets;
        [SerializeField] private HandFinger _finger = HandFinger.Index;
        [SerializeField] private bool _startWithSphereCast = true;
        [SerializeField, Min(0.001f)] private float _sphereRadius = 0.025f;
        [SerializeField, Min(0.01f)] private float _castDistance = 3f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color TargetColor = new Color(0.2f, 0.55f, 0.9f);
        private MaterialPropertyBlock _targetProperties;
        private LineRenderer _castLine;
        private Transform _castEnd;
        private bool _sphereCast;

        internal void Initialize(HandDemoVisuals visuals)
        {
            _targetProperties = new MaterialPropertyBlock();
            _sphereCast = _startWithSphereCast;
            _castLine = visuals.CreateLine("Finger Cast", Color.yellow, 0.004f);
            _castLine.positionCount = 2;
            _castEnd = visuals.CreateDot("Cast Radius", Color.yellow);
        }

        internal void Render(HandDemoView view, Keyboard keyboard)
        {
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                _sphereCast = !_sphereCast;

            var tracking = view.Tracking;
            var camera = view.ViewCamera;
            var primary = tracking.PrimaryHand;
            _castLine.endWidth = _castLine.startWidth * view.Visuals.RayTipWidth;
            Ray ray = default;
            var hasRay = primary?.Gesture is (HandGesture.Point or HandGesture.Pistol) &&
                tracking.TryGetFingerRay(camera, _finger, out ray, primary);
            _castLine.gameObject.SetActive(hasRay);
            _castEnd.gameObject.SetActive(hasRay);
            RaycastHit hit = default;
            var hasHit = false;
            if (hasRay)
            {
                hasHit = _sphereCast
                    ? Physics.SphereCast(ray, _sphereRadius, out hit, _castDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    : Physics.Raycast(ray, out hit, _castDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                var distance = hasHit ? hit.distance : _castDistance;
                _castLine.SetPosition(0, ray.origin);
                _castLine.SetPosition(1, ray.GetPoint(distance));
                _castEnd.position = ray.GetPoint(distance);
                _castEnd.localScale = Vector3.one * (_sphereCast ? _sphereRadius * 2f : 0.008f);
            }
            for (int i = 0; i < _targets.Length; i++)
            {
                var target = _targets[i];
                var color = hasHit && hit.collider.gameObject == target.gameObject ? Color.green : TargetColor;
                _targetProperties.SetColor(BaseColor, color);
                target.SetPropertyBlock(_targetProperties);
            }

            var gesture = primary == null ? "No hand detected" : primary.Side + " - " + primary.Gesture;
            var targetName = hasHit ? hit.collider.name : "none";
            var castStatus = hasRay
                ? (_sphereCast ? "Sphere cast" : "Raycast") + " from " + _finger + " tip   Hit " + targetName
                : "Cast off   Use Point or Pistol to aim";
            var text = "HAND DEMO\n" + tracking.Status + "\n" + gesture +
                "\nHands " + tracking.Hands.Count + "   Wrist depth " +
                tracking.WristDistanceFromCamera.ToString("F2") + " m" +
                "\n" + castStatus +
                "\nSpace switches cast   Up / Down adjusts wrist depth";
            view.SetStatus(text);
        }
    }
}
