using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace EngineeringDemo
{
    internal sealed class FridgeRotationControls
    {
        private const float DegreesPerScreenHeight = 180f;
        private const float MinimumTwistSpan = 8f;
        private const float ThumbZoomHoldSeconds = 0.2f;
        private const float ThumbZoomRate = 0.85f;
        private static readonly Quaternion LeftPalmAlignment = Quaternion.AngleAxis(180f, Vector3.up);

        private Mouse _mouseGrab;
        private bool _mouseDragging;
        private TouchControl _tapTouch;
        private int _tapTouchId;
        private Vector2 _tapStart;
        private bool _tapMoved;
        private TouchControl _firstTouch;
        private TouchControl _secondTouch;
        private int _firstTouchId;
        private int _secondTouchId;
        private float _lastPinchSpan;
        private Vector2 _lastTouchOffset;
        private float _touchRoll;
        private Vector2 _pointerStart;
        private Quaternion _pointerModelStart;

        private Hand _rotationHand;
        private double? _threeFingerStartedAt;
        private bool _holdingFist;
        private Quaternion _fistStart;
        private Quaternion _fistModelStart;
        private Hand _zoomHand;
        private HandGesture _zoomGesture;
        private double _zoomStartedAt;
        private double _lastZoomUpdate;

        public void ReleaseGrips()
        {
            _mouseGrab = null;
            _tapTouch = null;
            _firstTouch = _secondTouch = null;
            ReleaseHandGrip();
        }

        private void ReleaseHandGrip()
        {
            ClearHandRotation();
            _zoomHand = null;
        }

        private void ClearHandRotation()
        {
            _rotationHand = null;
            _threeFingerStartedAt = null;
            _holdingFist = false;
        }

        // A grabbed pointer belongs to the hologram, so it can't also select the camera detection.
        public bool Update(Camera camera, FridgeHologram hologram, HandManager tracking, float threeFingerAlignHoldSeconds)
        {
            if (!hologram.CanRotate)
            {
                ReleaseGrips();
                return false;
            }

            if (UpdateTouch(camera, hologram))
            {
                _mouseGrab = null;
                ReleaseHandGrip();
                return true;
            }

            var mouse = Mouse.current;
            if (mouse != null && !IsOverUi(mouse.deviceId))
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f)
                    hologram.ZoomInspection(Mathf.Pow(1.12f, Mathf.Sign(scroll)));
            }

            if (UpdateMouse(camera, hologram, mouse))
            {
                ReleaseHandGrip();
                return true;
            }

            UpdateHandRotation(hologram, tracking.Hands, tracking.TrackingTimestamp, threeFingerAlignHoldSeconds);
            UpdateHandZoom(hologram, tracking.Hands, tracking.TrackingTimestamp);
            return false;
        }

        private bool UpdateMouse(Camera camera, FridgeHologram hologram, Mouse mouse)
        {
            if (mouse == null)
            {
                _mouseGrab = null;
                return false;
            }

            var position = mouse.position.ReadValue();
            if (_mouseGrab == mouse)
            {
                _mouseDragging |= PointerMoved(camera, position, _pointerStart);
                if (!mouse.leftButton.isPressed)
                {
                    if (!_mouseDragging && mouse.leftButton.wasReleasedThisFrame && !IsOverUi(mouse.deviceId))
                        ActivateSectionAt(camera, hologram, position, "mouse click");
                    _mouseGrab = null;
                }
                else if (_mouseDragging)
                {
                    RotatePointer(camera, hologram, position);
                }
                return true;
            }

            if (!mouse.leftButton.isPressed || !mouse.leftButton.wasPressedThisFrame || IsOverUi(mouse.deviceId) ||
                !hologram.IsUnderPointer(camera, position))
                return false;

            _mouseGrab = mouse;
            _mouseDragging = false;
            BeginPointerGrab(hologram, position);
            return true;
        }

        private bool UpdateTouch(Camera camera, FridgeHologram hologram)
        {
            var screen = Touchscreen.current;
            if (screen != null && _firstTouch?.device == screen && _firstTouch.isInProgress &&
                _secondTouch.isInProgress && _firstTouch.touchId.ReadValue() == _firstTouchId &&
                _secondTouch.touchId.ReadValue() == _secondTouchId)
            {
                var firstPoint = _firstTouch.position.ReadValue();
                var secondPoint = _secondTouch.position.ReadValue();
                var offset = secondPoint - firstPoint;
                float span = Mathf.Max(1f, offset.magnitude);
                hologram.ZoomInspection(span / _lastPinchSpan);
                if (span >= MinimumTwistSpan && _lastPinchSpan >= MinimumTwistSpan)
                    _touchRoll += Vector2.SignedAngle(_lastTouchOffset, offset);

                _lastTouchOffset = offset;
                _lastPinchSpan = span;
                RotatePointer(camera, hologram, (firstPoint + secondPoint) * 0.5f, _touchRoll);
                return true;
            }

            _firstTouch = _secondTouch = null;
            if (screen == null)
            {
                _tapTouch = null;
                return false;
            }

            TouchControl first = null;
            TouchControl second = null;
            foreach (var touch in screen.touches)
            {
                if (!touch.isInProgress)
                    continue;
                if (first == null)
                {
                    first = touch;
                    continue;
                }

                second = touch;
                break;
            }

            if (second == null)
                return UpdateTap(camera, hologram, first);

            _tapTouch = null;
            var firstPosition = first.position.ReadValue();
            var secondPosition = second.position.ReadValue();
            var center = (firstPosition + secondPosition) * 0.5f;
            if ((first.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Began ||
                second.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Began) &&
                !IsOverUi(first.touchId.ReadValue()) && !IsOverUi(second.touchId.ReadValue()))
            {
                _firstTouch = first;
                _secondTouch = second;
                _firstTouchId = first.touchId.ReadValue();
                _secondTouchId = second.touchId.ReadValue();
                _lastTouchOffset = secondPosition - firstPosition;
                _lastPinchSpan = Mathf.Max(1f, _lastTouchOffset.magnitude);
                _touchRoll = 0f;
                BeginPointerGrab(hologram, center);
            }

            return true;
        }

        private bool UpdateTap(Camera camera, FridgeHologram hologram, TouchControl first)
        {
            if (_tapTouch != null && _tapTouch.touchId.ReadValue() == _tapTouchId)
            {
                var position = _tapTouch.position.ReadValue();
                _tapMoved |= PointerMoved(camera, position, _tapStart);
                if (!_tapTouch.isInProgress)
                {
                    if (!_tapMoved && _tapTouch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Ended &&
                        !IsOverUi(_tapTouchId))
                        ActivateSectionAt(camera, hologram, position, "touch tap");
                    _tapTouch = null;
                }
                return true;
            }

            _tapTouch = null;
            if (first == null)
                return false;

            var start = first.position.ReadValue();
            int id = first.touchId.ReadValue();
            if (IsOverUi(id) || !hologram.IsUnderPointer(camera, start))
                return false;

            if (first.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Began)
            {
                _tapTouch = first;
                _tapTouchId = id;
                _tapStart = start;
                _tapMoved = false;
            }

            return true;
        }

        private static bool PointerMoved(Camera camera, Vector2 position, Vector2 start)
        {
            float tolerance = Mathf.Max(5f, camera.pixelHeight * 0.012f);
            return (position - start).sqrMagnitude > tolerance * tolerance;
        }

        private static void ActivateSectionAt(Camera camera, FridgeHologram hologram, Vector2 position, string source)
        {
            hologram.TryActivateSection(hologram.FindSectionHit(camera.ScreenPointToRay(position)), source);
        }

        private void BeginPointerGrab(FridgeHologram hologram, Vector2 position)
        {
            _pointerStart = position;
            _pointerModelStart = hologram.InspectionRotation;
        }

        private void RotatePointer(Camera camera, FridgeHologram hologram, Vector2 position, float rollDegrees = 0f)
        {
            var degrees = (position - _pointerStart) * (DegreesPerScreenHeight / Mathf.Max(1f, camera.pixelHeight));
            var yaw = Quaternion.AngleAxis(-degrees.x, Vector3.up);
            var pitch = Quaternion.AngleAxis(degrees.y, Vector3.right);
            var roll = Quaternion.AngleAxis(rollDegrees, Vector3.forward);
            hologram.InspectionRotation = roll * yaw * pitch * _pointerModelStart;
        }

        private void UpdateHandRotation(FridgeHologram hologram, IReadOnlyList<Hand> hands,
            double trackingTimestamp, float threeFingerAlignHoldSeconds)
        {
            if (!CanRotateWithHand(_rotationHand))
            {
                ClearHandRotation();
                foreach (var hand in hands)
                {
                    if (!CanRotateWithHand(hand))
                        continue;

                    _rotationHand = hand;
                    break;
                }
            }

            if (_rotationHand?.PalmRotation is not Quaternion current)
                return;

            if (_rotationHand.Gesture == HandGesture.ThreeFingers)
            {
                _holdingFist = false;
                _threeFingerStartedAt ??= trackingTimestamp;
                // Repeated renders of the same tracking result don't confirm a hold.
                if (trackingTimestamp - _threeFingerStartedAt.Value < threeFingerAlignHoldSeconds)
                    return;

                // The doors face local -Z. The index-to-pinky axis flips between hands.
                hologram.InspectionRotation = _rotationHand.Side == HandSide.Left
                    ? current * LeftPalmAlignment : current;
                return;
            }

            _threeFingerStartedAt = null;
            if (!_holdingFist)
            {
                _holdingFist = true;
                _fistStart = current;
                _fistModelStart = hologram.InspectionRotation;
                return;
            }

            hologram.InspectionRotation = current * Quaternion.Inverse(_fistStart) * _fistModelStart;
        }

        private void UpdateHandZoom(FridgeHologram hologram, IReadOnlyList<Hand> hands, double trackingTimestamp)
        {
            Hand zoomHand = null;
            foreach (var hand in hands)
            {
                if (hand.Gesture is HandGesture.ThumbsUp or HandGesture.ThumbsDown)
                {
                    zoomHand = hand;
                    break;
                }
            }

            if (zoomHand == null)
            {
                _zoomHand = null;
                return;
            }

            var gesture = zoomHand.Gesture;
            if (!ReferenceEquals(_zoomHand, zoomHand) || _zoomGesture != gesture)
            {
                _zoomHand = zoomHand;
                _zoomGesture = gesture;
                _zoomStartedAt = _lastZoomUpdate = trackingTimestamp;
                return;
            }

            double zoomStart = Math.Max(_lastZoomUpdate, _zoomStartedAt + ThumbZoomHoldSeconds);
            if (trackingTimestamp > zoomStart)
            {
                float direction = gesture == HandGesture.ThumbsUp ? 1f : -1f;
                float elapsed = Mathf.Min(0.1f, (float)(trackingTimestamp - zoomStart));
                hologram.ZoomInspection(Mathf.Exp(direction * ThumbZoomRate * elapsed));
            }
            _lastZoomUpdate = trackingTimestamp;
        }

        private static bool CanRotateWithHand(Hand hand) => hand?.PalmRotation is Quaternion &&
            (hand.Gesture == HandGesture.Fist || (hand.Gesture == HandGesture.ThreeFingers && hand.Side != HandSide.Unknown));

        private static bool IsOverUi(int pointerId) => EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject(pointerId);
    }
}
