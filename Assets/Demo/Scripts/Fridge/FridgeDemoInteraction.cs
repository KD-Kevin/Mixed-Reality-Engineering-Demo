using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace EngineeringDemo
{
    [DisallowMultipleComponent]
    public sealed class FridgeDemoInteraction : MonoBehaviour
    {
        private const float PreviewGestureHoldSeconds = 0.2f;
        private const float MissRayLength = 0.28f;
        private const double FridgeAimGraceSeconds = 0.18;

        [SerializeField] private FridgeDetector _fridgeDetector;
        [SerializeField] private FridgeHologram _fridgeHologram;
        [SerializeField] private FridgeInteractionSettings _settings;
        [SerializeField] private UnityEngine.Events.UnityEvent _onFridgeSelected = new();

        private HandDemoView _view;
        private HandManager _handManager;
        private Camera _camera;
        private HandDemoVisuals _visuals;
        private LineRenderer _castLine;
        private Transform _castEnd;
        private LineRenderer _fridgeOutline;
        private FridgePartCallout _partCallout;
        private readonly FridgeRotationControls _fridgeRotation = new();
        private float _closePreviewHold;
        private float _openPreviewHold;
        private Hand _fridgeAimHand;
        private Vector2 _lastFridgeAimPoint;
        private double _lastFridgeAimTime;
        private string _shotMessage;
        private float _shotMessageUntil;
        private double _shotFlashUntil;

        internal PistolTrigger Trigger { get; } = new();
        private bool FridgeSelected => _fridgeHologram != null && _fridgeHologram.IsSelected;

        internal void Initialize(HandDemoView view, UnityEngine.UI.Text status)
        {
            _view = view;
            _handManager = view.Tracking;
            _camera = view.ViewCamera;
            _visuals = view.Visuals;
            _castLine = _visuals.CreateLine("Finger Cast", Color.yellow, 0.004f);
            _castLine.positionCount = 2;
            _castEnd = _visuals.CreateDot("Cast Radius", Color.yellow);
            _fridgeOutline = _visuals.CreateLine("Detected Refrigerator", Color.cyan, 0.006f);
            _fridgeOutline.positionCount = 4;
            _fridgeOutline.loop = true;
            if (_fridgeHologram != null && status != null && status.canvas != null)
                _partCallout = new FridgePartCallout(status, () => CloseFridgeSection(), ResetFridgeRotation, () => CloseHologram("button"));
        }

        internal void ResetAim()
        {
            _fridgeRotation.ReleaseGrips();
            _fridgeAimHand = null;
            Trigger.Reset();
        }

        internal void Tick(Rect imageViewport, Keyboard keyboard)
        {
            if (_fridgeHologram != null && BackPressed(keyboard) && !CloseFridgeSection() && !CloseHologram("Back"))
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Application.Quit();
#endif
            }

            _castLine.endWidth = _castLine.startWidth * _visuals.RayTipWidth;
            var grabbedPointer = _fridgeHologram != null && _fridgeRotation.Update(_camera, _fridgeHologram, _handManager, _settings.ThreeFingerAlignHoldSeconds);
            _fridgeHologram?.UpdateInspection(_camera, Time.unscaledDeltaTime);
            UpdateFridgeAim(_handManager.PrimaryHand, imageViewport, grabbedPointer);
            if (_fridgeHologram != null)
                UpdatePreviewGestures(_handManager.Hands, Time.unscaledDeltaTime);
        }

        private void UpdateFridgeAim(Hand hand, Rect imageViewport, bool grabbedPointer)
        {
            var now = Time.unscaledTimeAsDouble;
            var detected = _fridgeDetector.TryGetFridge(out var fridge, out var score);
            bool expanded = _fridgeHologram?.ExpandedSectionName != null;
            if (!grabbedPointer && !expanded)
                SelectWithPointer(imageViewport, detected, fridge);
            var hasFridge = detected && !FridgeSelected && !expanded;
            bool canAimAtSections = _fridgeHologram != null && _fridgeHologram.CanRotate;
            FridgeHologram.SectionHit fingerHit = default;

            Vector3 fingertip = default;
            var aiming = hand?.Gesture == HandGesture.Pistol &&
                _handManager.TryGetWorldPoint(_camera, hand, HandPoint.IndexTip, out fingertip);
            if (!aiming || !hasFridge || grabbedPointer)
                _fridgeAimHand = null;
            var locked = false;
            _castLine.enabled = aiming;
            _castEnd.gameObject.SetActive(aiming);
            if (aiming)
            {
                Vector2 tip = hand.IndexTip;
                Vector2 joint = hand.GetPoint(HandPoint.IndexPip);
                var projectedFinger = tip - joint;
                var direction = projectedFinger.sqrMagnitude >= 0.000004f ? projectedFinger.normalized : Vector2.zero;
                float edgeDistance = FridgeAim.DistanceToImageEdge(tip, direction);
                var imageEdge = tip + direction * edgeDistance;
                if (canAimAtSections)
                    fingerHit = FindFingerSection(imageViewport, tip, imageEdge);

                float distancePerImageWidth = new Vector2(direction.x, direction.y / _handManager.ImageAspect).magnitude;
                float missDistance = distancePerImageWidth > 0f ? MissRayLength / distancePerImageWidth : 0f;
                var end = tip + direction * Mathf.Min(edgeDistance, missDistance);
                locked = fingerHit.Section != null;
                if (!locked && hasFridge && !grabbedPointer &&
                    TryLockFridge(fridge, hand, tip, direction, edgeDistance, now, out var aimPoint))
                {
                    end = aimPoint;
                    locked = true;
                }

                var endPoint = fingerHit.Section != null ? fingerHit.Point : GetEnvironmentEndpoint(end, imageViewport);
                _castLine.SetPosition(0, fingertip);
                _castLine.SetPosition(1, endPoint);
                _castEnd.position = endPoint;
                _castEnd.localScale = Vector3.one * 0.012f;
            }

            // Keep the visible pose stable while avoiding a shot when the hand starts closing into a fist.
            if ((!aiming || hand.ObservedGesture == HandGesture.Pistol) && Trigger.Update(aiming ? hand : null, aiming ? hand.MiddlePipExtension : null, now, _settings.TriggerReadyExtension, _settings.FireThreshold, _settings.TriggerReleaseSeconds))
            {
                if (fingerHit.Section != null)
                {
                    if (_fridgeHologram.TryActivateSection(fingerHit, "pistol shot"))
                    {
                        expanded = _fridgeHologram.ExpandedSectionName != null;
                        _shotMessage = fingerHit.Section.name + (expanded ? " opened." : " closed.");
                        fingerHit = default;
                    }
                    else
                    {
                        _shotMessage = fingerHit.Part.Name;
                    }
                }
                else
                {
                    var selected = locked && TrySelectFridge(imageViewport, fridge, "pistol shot");
                    _shotMessage = selected ? "Fired. Refrigerator selected." : "Fired. No target hit.";
                    if (!selected)
                    {
                        var reason = locked ? "the hologram could not spawn" : "no target hit";
                        Debug.Log($"[FridgeDemo] Pistol shot missed: {reason}.", this);
                    }
                }
                _shotMessageUntil = Time.unscaledTime + 1.2f;
                _shotFlashUntil = now + _settings.ShotFlashSeconds;
            }

            var hover = default(FridgeHologram.SectionHit);
            if (canAimAtSections)
                hover = aiming && !grabbedPointer ? fingerHit : FindPointerSection();
            _fridgeHologram?.SetSectionHover(hover);
            _partCallout?.Show(_camera, hover, expanded, canAimAtSections, _fridgeHologram != null && _fridgeHologram.IsVisible);

            _fridgeOutline.enabled = hasFridge && !FridgeSelected && !expanded;
            var outlineColor = locked ? Color.green : Color.cyan;
            Vector2? detectionLabelPosition = null;
            if (_fridgeOutline.enabled)
            {
                const float outlineDepth = 2f;
                _fridgeOutline.SetPosition(0, ImageToWorld(fridge.min, imageViewport, outlineDepth));
                _fridgeOutline.SetPosition(1, ImageToWorld(new Vector2(fridge.xMax, fridge.yMin), imageViewport, outlineDepth));
                _fridgeOutline.SetPosition(2, ImageToWorld(fridge.max, imageViewport, outlineDepth));
                _fridgeOutline.SetPosition(3, ImageToWorld(new Vector2(fridge.xMin, fridge.yMax), imageViewport, outlineDepth));
                _visuals.SetVisualColor(_fridgeOutline, outlineColor);
                var displayBox = _handManager.GetDisplayImageBox(fridge);
                detectionLabelPosition = _camera.ViewportToScreenPoint(new Vector3(
                    imageViewport.xMin + displayBox.center.x * imageViewport.width,
                    imageViewport.yMax - displayBox.yMin * imageViewport.height, outlineDepth));
            }
            _partCallout?.ShowDetection(detectionLabelPosition, outlineColor);

            if (aiming)
            {
                var rayColor = fingerHit.Section != null ? FridgeSection.HoverColor : locked ? Color.green : Color.yellow;
                if (now < _shotFlashUntil)
                    rayColor = _settings.ShotRayColor;
                _castLine.startColor = _castLine.endColor = rayColor;
                _visuals.SetVisualColor(_castLine, rayColor);
            }

            var fridgeStatus = hasFridge ? "Refrigerator " + score.ToString("P0") : _fridgeDetector.Status;
            if (expanded)
                fridgeStatus = _fridgeHologram.ExpandedSectionName + " expanded";
            else if (FridgeSelected)
                fridgeStatus = _fridgeHologram.IsInspecting ? "Inspecting refrigerator" : "Moving refrigerator into view";
            else if (_fridgeHologram != null && _fridgeHologram.IsVisible)
                fridgeStatus = "Test hologram";
            var aimStatus = locked ? "Aim locked" : aiming ? "Aiming" : "Make the Pistol gesture to aim";
            var triggerStatus = Trigger.State switch
            {
                PistolTrigger.TriggerState.Ready => "Curl your middle finger to fire",
                PistolTrigger.TriggerState.Fired => "Lift your middle finger to rearm",
                _ => "Lift your middle finger to arm"
            };
            if (Trigger.State != PistolTrigger.TriggerState.Ready && Time.unscaledTime < _shotMessageUntil)
                triggerStatus = _shotMessage;
            var middleExtension = hand?.MiddlePipExtension?.ToString("F2") ?? "–";
            var triggerReadout = "Middle PIP " + middleExtension + "   Ready ≥ " + _settings.TriggerReadyExtension.ToString("F2") +
                "   Fire ≤ " + _settings.FireThreshold.ToString("F2");
            var gesture = hand == null ? "No hand detected" : hand.Gesture.ToString();
            var controls = "Three fingers opens test. Up / Down adjusts wrist depth.";
            if (expanded)
                controls = "Aim at a part for its reference. Select the door to go back.";
            else if (_fridgeHologram != null && _fridgeHologram.IsVisible)
                controls = "Select a door or rear cover. Drag to rotate. Hold thumbs up or down to zoom.";
            _view.SetStatus("FRIDGE DEMO\n" + _handManager.Status + "\n" + fridgeStatus + "\n" + aimStatus +
                "   [" + gesture + "]\n" + triggerStatus + "\n" + triggerReadout + "\n" + controls);
        }

        private FridgeHologram.SectionHit FindPointerSection()
        {
            var eventSystem = EventSystem.current;
            var touch = Touchscreen.current?.primaryTouch;
            if (touch != null && touch.isInProgress)
            {
                return eventSystem != null && eventSystem.IsPointerOverGameObject(touch.touchId.ReadValue())
                    ? default : _fridgeHologram.FindSectionHit(_camera.ScreenPointToRay(touch.position.ReadValue()));
            }

            var mouse = Mouse.current;
            return mouse == null || (eventSystem != null && eventSystem.IsPointerOverGameObject(mouse.deviceId))
                ? default : _fridgeHologram.FindSectionHit(_camera.ScreenPointToRay(mouse.position.ReadValue()));
        }

        private FridgeHologram.SectionHit FindFingerSection(Rect viewport, Vector2 start, Vector2 end)
        {
            // Use the same line that the player sees in the camera image for hologram hits.
            const int samples = 24;
            for (int i = 0; i <= samples; i++)
            {
                var imagePoint = Vector2.Lerp(start, end, i / (float)samples);
                var point = _handManager.ImageToViewport(imagePoint, viewport);
                var hit = _fridgeHologram.FindSectionHit(_camera.ViewportPointToRay(point), out bool hitsModel);
                if (hitsModel || start == end)
                    return hit;
            }

            return default;
        }

        private void ResetFridgeRotation()
        {
            _fridgeHologram.ResetRotation();
            _fridgeRotation.ReleaseGrips();
        }

        private static bool BackPressed(Keyboard keyboard)
        {
#if UNITY_ANDROID && !UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            // Some Android devices report system Back through the legacy Escape event.
            if (Input.GetKeyDown(KeyCode.Escape))
                return true;
#endif
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
        }

        private bool CloseHologram(string source)
        {
            if (!_fridgeHologram.TryClose())
                return false;

            _fridgeRotation.ReleaseGrips();
            Debug.Log($"[FridgeDemo] Hologram closed by {source}.", this);
            return true;
        }

        private bool CloseFridgeSection()
        {
            if (!_fridgeHologram.TryCollapseSection())
                return false;

            _fridgeRotation.ReleaseGrips();
            return true;
        }

        private void UpdatePreviewGestures(IReadOnlyList<Hand> hands, float deltaTime)
        {
            bool middleFinger = false;
            bool threeFingers = false;
            for (int i = 0; i < hands.Count; i++)
            {
                var hand = hands[i];
                middleFinger |= hand.Gesture == HandGesture.MiddleFinger;
                threeFingers |= hand.Gesture == HandGesture.ThreeFingers;
            }

            bool close = PreviewGesturePressed(middleFinger, deltaTime, ref _closePreviewHold);
            bool open = PreviewGesturePressed(threeFingers, deltaTime, ref _openPreviewHold);
            if (close && (CloseFridgeSection() || CloseHologram("middle finger")))
                return;
            if (open && !_fridgeHologram.IsVisible)
            {
                ShowTestHologram();
            }
        }

        private static bool PreviewGesturePressed(bool held, float deltaTime, ref float secondsHeld)
        {
            if (!held)
            {
                secondsHeld = 0f;
                return false;
            }
            if (secondsHeld >= PreviewGestureHoldSeconds)
                return false;

            secondsHeld += deltaTime;
            return secondsHeld >= PreviewGestureHoldSeconds;
        }

        public void ShowTestHologram()
        {
            if (_fridgeHologram == null || !_fridgeHologram.TryShowTest(_camera))
            {
                Debug.LogWarning("[FridgeDemo] Test hologram could not spawn. Check the camera and model references.", this);
                return;
            }

            Debug.Log("[FridgeDemo] Test hologram shown in the center.", this);
        }

        private void SelectWithPointer(Rect imageViewport, bool detected, Rect fridge)
        {
            var eventSystem = EventSystem.current;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame &&
                (eventSystem == null || !eventSystem.IsPointerOverGameObject(mouse.deviceId)))
            {
                var position = mouse.position.ReadValue();
                var miss = SelectFridgeAt(position, imageViewport, detected, fridge, "mouse click");
                if (miss == null)
                    return;

                Debug.Log($"[FridgeDemo] Mouse click missed at ({position.x:F0}, {position.y:F0}): {miss}.", this);
            }

            var touch = Touchscreen.current?.primaryTouch;
            if (touch != null && touch.press.wasPressedThisFrame &&
                (eventSystem == null || !eventSystem.IsPointerOverGameObject(touch.touchId.ReadValue())))
                SelectFridgeAt(touch.position.ReadValue(), imageViewport, detected, fridge, "touch tap");
        }

        // A null result means this press selected the fridge.
        private string SelectFridgeAt(Vector2 screenPosition, Rect imageViewport, bool detected, Rect fridge, string source)
        {
            if (FridgeSelected)
                return "the refrigerator is already selected";

            Vector2 viewportPoint = _camera.ScreenToViewportPoint(screenPosition);
            if (!imageViewport.Contains(viewportPoint))
                return "outside the camera preview";
            if (!detected)
                return $"no refrigerator detected ({_fridgeDetector.Status})";

            var imagePoint = _handManager.ViewportToImage(viewportPoint, imageViewport);
            if (!fridge.Contains(imagePoint))
                return "outside the detected refrigerator";

            return TrySelectFridge(imageViewport, fridge, source) ? null : "the hologram could not spawn";
        }

        private bool TrySelectFridge(Rect imageViewport, Rect fridge, string source)
        {
            if (_fridgeHologram == null ||
                !_fridgeHologram.TrySelect(_camera, imageViewport, _handManager.GetDisplayImageBox(fridge)))
                return false;

            Debug.Log($"[FridgeDemo] Refrigerator selected by {source}. Hologram spawned.", this);
            _onFridgeSelected.Invoke();
            return true;
        }

        private bool TryLockFridge(Rect fridge, Hand hand, Vector2 tip, Vector2 direction, float maxDistance,
            double now, out Vector2 point)
        {
            var padding = new Vector2(_settings.FridgeAimPadding, _settings.FridgeAimPadding / _handManager.ImageAspect);
            var aimBounds = new Rect(fridge.min - padding, fridge.size + 2f * padding);
            if (FridgeAim.TryAimAtFridge(fridge, tip, direction, maxDistance, out point) ||
                FridgeAim.TryAimAtFridge(aimBounds, tip, direction, maxDistance, out point))
            {
                // Keep assisted hits on the detected surface so AR depth doesn't sample just outside it.
                var inset = Vector2.Min(fridge.size * 0.1f, new Vector2(0.006f, 0.006f));
                point = Vector2.Max(fridge.min + inset, Vector2.Min(fridge.max - inset, point));
                _fridgeAimHand = hand;
                _lastFridgeAimPoint = point;
                _lastFridgeAimTime = now;
                return true;
            }

            // A small shift during the trigger curl should not drop a lock on the same fridge.
            if (ReferenceEquals(hand, _fridgeAimHand) && Trigger.State == PistolTrigger.TriggerState.Ready &&
                now - _lastFridgeAimTime <= FridgeAimGraceSeconds && aimBounds.Contains(_lastFridgeAimPoint))
            {
                point = _lastFridgeAimPoint;
                return true;
            }

            return false;
        }

        private Vector3 ImageToWorld(Vector2 point, Rect viewport, float depth)
        {
            var viewportPoint = _handManager.ImageToViewport(point, viewport);
            return _camera.ViewportToWorldPoint(new Vector3(viewportPoint.x, viewportPoint.y, depth));
        }

        private Vector3 GetEnvironmentEndpoint(Vector2 imagePoint, Rect imageViewport)
        {
            var viewportPoint = _handManager.ImageToViewport(imagePoint, imageViewport);
            if (!_handManager.TryGetEnvironmentDepth(viewportPoint, out float depth))
                depth = Mathf.Max(1.8f, _handManager.WristDistanceFromCamera + 0.25f);

            return _camera.ViewportToWorldPoint(new Vector3(viewportPoint.x, viewportPoint.y, depth));
        }
    }
}
