using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace EngineeringDemo
{
    public sealed class HandDemoView : MonoBehaviour
    {
        private enum TriggerState { Waiting, Ready, Fired }

        private const float PreviewGestureHoldSeconds = 0.2f;

        [SerializeField] private HandManager _handManager;
        [SerializeField] private Camera _camera;
        [SerializeField] private UnityEngine.UI.RawImage _preview;
        [SerializeField] private UnityEngine.UI.Text _status;
        [SerializeField] private Material _lineMaterial;
        [SerializeField] private Renderer[] _targets;
        [SerializeField] private HandFinger _finger = HandFinger.Index;
        [SerializeField] private bool _sphereCast = true;
        [SerializeField, Min(0.001f)] private float _sphereRadius = 0.025f;
        [SerializeField, Min(0.01f)] private float _castDistance = 3f;
        [SerializeField, Range(0.02f, 1f)] private float _rayTipWidth = 0.18f;

        [Header("Fridge Aim")]
        [SerializeField] private FridgeDetector _fridgeDetector;
        [SerializeField] private FridgeHologram _fridgeHologram;
        [Tooltip("Seconds to hold three fingers before the fridge matches the hand's rotation.")]
        [SerializeField, Min(0f)] private float _threeFingerAlignHoldSeconds = 0.3f;
        [Tooltip("Middle finger PIP extension needed to rearm. One is straight. Zero is bent 90 degrees or more.")]
        [SerializeField, Range(0.1f, 1f)] private float _triggerReadyExtension = 0.78f;
        [Tooltip("Fire when middle finger PIP extension falls to this value. Bending the fingertip does not affect it.")]
        [SerializeField, Range(0f, 0.95f)] private float _triggerFireExtension = 0.54f;
        [SerializeField, Min(0f)] private float _triggerReleaseSeconds = 0.12f;
        [SerializeField, ColorUsage(false)] private Color _shotRayColor = new Color(1f, 0.4f, 0.09f);
        [SerializeField, Min(0f)] private float _shotFlashSeconds = 0.15f;
        [SerializeField] private UnityEngine.Events.UnityEvent _onFridgeSelected = new();

        [Header("Gesture Colors")]
        [SerializeField, ColorUsage(false)] private Color _neutralColor = Color.white;
        [SerializeField, ColorUsage(false)] private Color _openHandColor = Color.cyan;
        [SerializeField, ColorUsage(false)] private Color _fistColor = new Color(1f, 0.2f, 0.25f);
        [SerializeField, ColorUsage(false)] private Color _pinchColor = new Color(1f, 0.3f, 0.85f);
        [SerializeField, ColorUsage(false)] private Color _pointColor = new Color(0.35f, 1f, 0.25f);
        [SerializeField, ColorUsage(false)] private Color _pistolColor = new Color(1f, 0.55f, 0.15f);
        [SerializeField, ColorUsage(false)] private Color _middleFingerColor = new Color(0.38f, 0.72f, 1f);
        [SerializeField, ColorUsage(false)] private Color _threeFingersColor = new Color(1f, 0.93f, 0.2f);
        [Tooltip("Hue rotation in degrees for all left-hand gesture and trigger colors.")]
        [SerializeField, Range(-180f, 180f)] private float _leftHandHue = 60f;

        [Header("Pistol Trigger Colors")]
        [SerializeField] private bool _usePistolReadyColor = true;
        [SerializeField, ColorUsage(false)] private Color _pistolReadyColor = new Color(0.3f, 1f, 0.3f);
        [SerializeField] private bool _usePistolFiredColor = true;
        [SerializeField, ColorUsage(false)] private Color _pistolFiredColor = new Color(0.65f, 0.35f, 1f);

        private static readonly int[][] FingerChains =
        {
            new[] { 0, 1, 2, 3, 4 },
            new[] { 0, 5, 6, 7, 8 },
            new[] { 0, 9, 10, 11, 12 },
            new[] { 0, 13, 14, 15, 16 },
            new[] { 0, 17, 18, 19, 20 }
        };

        private readonly List<LineRenderer[]> _handLines = new();
        private readonly List<Transform[]> _handDots = new();
        private readonly List<Color> _appliedHandColors = new();
        private LineRenderer _castLine;
        private Transform _castEnd;
        private LineRenderer _fridgeOutline;
        private FridgePartCallout _partCallout;
        private readonly FridgeRotationControls _fridgeRotation = new();
        private float _closePreviewHold;
        private float _openPreviewHold;
        private Hand _triggerHand;
        private TriggerState _triggerState;
        private double _triggerReleaseStartedAt = -1d;
        private string _shotMessage;
        private float _shotMessageUntil;
        private double _shotFlashUntil;
        private MaterialPropertyBlock _targetColor;
        private MaterialPropertyBlock _visualColor;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");
        private string _lastStatus;
        private float FireThreshold => Mathf.Min(_triggerFireExtension, _triggerReadyExtension - 0.05f);
        private bool FridgeSelected => _fridgeHologram != null && _fridgeHologram.IsSelected;

        private void Start()
        {
            _targetColor = new MaterialPropertyBlock();
            _visualColor = new MaterialPropertyBlock();
            _castLine = CreateLine("Finger Cast", Color.yellow, 0.004f);
            _castLine.positionCount = 2;
            _castEnd = CreateDot("Cast Radius", Color.yellow);
            if (_fridgeDetector != null)
            {
                _fridgeOutline = CreateLine("Detected Refrigerator", Color.cyan, 0.006f);
                _fridgeOutline.positionCount = 4;
                _fridgeOutline.loop = true;
                if (_fridgeHologram != null && _status != null && _status.canvas != null)
                    _partCallout = new FridgePartCallout(_status, () => CloseFridgeSection(), ResetFridgeRotation,
                        () => CloseHologram("button"));
            }
        }

        private void LateUpdate()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (_fridgeDetector == null && keyboard.spaceKey.wasPressedThisFrame)
                    _sphereCast = !_sphereCast;
                var depthChange = (keyboard.upArrowKey.isPressed ? 1f : 0f) -
                    (keyboard.downArrowKey.isPressed ? 1f : 0f);
                _handManager.WristDistanceFromCamera += depthChange * Time.unscaledDeltaTime * 0.3f;
            }

            if (_fridgeHologram != null && BackPressed(keyboard))
            {
                if (!CloseFridgeSection() && !CloseHologram("Back"))
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    Application.Quit();
#endif
                }
            }

            _preview.texture = _handManager.CameraTexture;
            var imageViewport = _handManager.GetImageViewport(_camera);
            var previewRect = _handManager.GetCameraPreviewRect(_camera, imageViewport);
            _preview.rectTransform.anchorMin = previewRect.min;
            _preview.rectTransform.anchorMax = previewRect.max;
            _preview.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -_handManager.CameraRotationAngle);
            _preview.uvRect = new Rect(_handManager.MirrorWebcam ? 1 : 0,
                _handManager.CameraVerticallyMirrored ? 1 : 0,
                _handManager.MirrorWebcam ? -1 : 1, _handManager.CameraVerticallyMirrored ? -1 : 1);

            var primary = _handManager.PrimaryHand;
            _castLine.endWidth = _castLine.startWidth * _rayTipWidth;
            var grabbedPointer = _fridgeHologram != null &&
                _fridgeRotation.Update(_camera, _fridgeHologram, _handManager, _threeFingerAlignHoldSeconds);
            _fridgeHologram?.UpdateInspection(_camera, Time.unscaledDeltaTime);
            if (_fridgeDetector != null)
                UpdateFridgeAim(primary, imageViewport, grabbedPointer);
            if (_fridgeHologram != null)
                UpdatePreviewGestures(_handManager.Hands, Time.unscaledDeltaTime);

            while (_handLines.Count < _handManager.Hands.Count)
                CreateHandVisual();

            for (var i = 0; i < _handLines.Count; i++)
            {
                var visible = i < _handManager.Hands.Count;
                foreach (var line in _handLines[i])
                    line.gameObject.SetActive(visible);
                foreach (var dot in _handDots[i])
                    dot.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                var hand = _handManager.Hands[i];
                ApplyHandColor(i, GetHandColor(hand));
                for (var point = 0; point < Hand.PointCount; point++)
                {
                    var valid = _handManager.TryGetWorldPoint(_camera, hand, (HandPoint)point, out var position);
                    _handDots[i][point].gameObject.SetActive(valid);
                    _handDots[i][point].position = position;
                }
                for (var finger = 0; finger < FingerChains.Length; finger++)
                {
                    var chain = FingerChains[finger];
                    for (var joint = 0; joint < chain.Length; joint++)
                        _handLines[i][finger].SetPosition(joint, _handDots[i][chain[joint]].position);
                }
            }

            if (_fridgeDetector != null)
                return;

            Ray ray = default;
            var hasRay = primary?.Gesture is (HandGesture.Point or HandGesture.Pistol) &&
                _handManager.TryGetFingerRay(_camera, _finger, out ray, primary);
            _castLine.gameObject.SetActive(hasRay);
            _castEnd.gameObject.SetActive(hasRay);
            RaycastHit hit = default;
            var hasHit = false;
            if (hasRay)
            {
                hasHit = _sphereCast
                    ? _handManager.SphereCastFinger(_camera, _finger, _sphereRadius, out hit, _castDistance, hand: primary)
                    : _handManager.RaycastFinger(_camera, _finger, out hit, _castDistance, hand: primary);
                var distance = hasHit ? hit.distance : _castDistance;
                _castLine.SetPosition(0, ray.origin);
                _castLine.SetPosition(1, ray.GetPoint(distance));
                _castEnd.position = ray.GetPoint(distance);
                _castEnd.localScale = Vector3.one * (_sphereCast ? _sphereRadius * 2f : 0.008f);
            }
            foreach (var target in _targets)
            {
                var color = hasHit && hit.collider.gameObject == target.gameObject
                    ? Color.green : new Color(0.2f, 0.55f, 0.9f);
                _targetColor.SetColor(BaseColor, color);
                target.SetPropertyBlock(_targetColor);
            }

            var gesture = primary == null ? "No hand detected" : primary.Side + " - " + primary.Gesture;
            var targetName = hasHit ? hit.collider.name : "none";
            var castStatus = hasRay
                ? (_sphereCast ? "Sphere cast" : "Raycast") + " from " + _finger + " tip   Hit " + targetName
                : "Cast off   Use Point or Pistol to aim";
            var text = "HAND DEMO\n" + _handManager.Status + "\n" + gesture +
                "\nHands " + _handManager.Hands.Count + "   Wrist depth " +
                _handManager.WristDistanceFromCamera.ToString("F2") + " m" +
                "\n" + castStatus +
                "\nSpace switches cast   Up / Down adjusts wrist depth";
            SetStatus(text);
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
            var locked = false;
            _castLine.enabled = aiming;
            _castEnd.gameObject.SetActive(aiming);
            if (aiming)
            {
                Vector2 tip = hand.IndexTip;
                Vector2 joint = hand.GetPoint(HandPoint.IndexDip);
                var direction = (tip - joint).normalized;
                var end = tip + direction * DistanceToImageEdge(tip, direction);
                if (canAimAtSections)
                    fingerHit = FindFingerSection(hand, imageViewport, tip, end);
                locked = fingerHit.Section != null || (hasFridge && TryAimAtFridge(fridge, tip, direction, ref end));

                var endPoint = fingerHit.Section != null ? fingerHit.Point :
                    ImageToWorld(end, Mathf.Max(1.8f, _handManager.WristDistanceFromCamera + 0.25f));
                _castLine.SetPosition(0, fingertip);
                _castLine.SetPosition(1, endPoint);
                _castEnd.position = endPoint;
                _castEnd.localScale = Vector3.one * 0.012f;
            }

            if (PullTrigger(aiming ? hand : null, now))
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
                _shotFlashUntil = now + _shotFlashSeconds;
            }

            var hover = default(FridgeHologram.SectionHit);
            if (canAimAtSections)
                hover = aiming && !grabbedPointer ? fingerHit : FindPointerSection();
            _fridgeHologram?.SetSectionHover(hover);
            _partCallout?.Show(_camera, hover, expanded, canAimAtSections, _fridgeHologram != null && _fridgeHologram.IsVisible);

            _fridgeOutline.enabled = hasFridge && !FridgeSelected && !expanded;
            if (_fridgeOutline.enabled)
            {
                const float outlineDepth = 2f;
                _fridgeOutline.SetPosition(0, ImageToWorld(fridge.min, outlineDepth));
                _fridgeOutline.SetPosition(1, ImageToWorld(new Vector2(fridge.xMax, fridge.yMin), outlineDepth));
                _fridgeOutline.SetPosition(2, ImageToWorld(fridge.max, outlineDepth));
                _fridgeOutline.SetPosition(3, ImageToWorld(new Vector2(fridge.xMin, fridge.yMax), outlineDepth));
                SetVisualColor(_fridgeOutline, locked ? Color.green : Color.cyan);
            }

            if (aiming)
            {
                var rayColor = fingerHit.Section != null ? FridgeSection.HoverColor : locked ? Color.green : Color.yellow;
                if (now < _shotFlashUntil)
                    rayColor = _shotRayColor;
                _castLine.startColor = _castLine.endColor = rayColor;
                SetVisualColor(_castLine, rayColor);
            }

            var fridgeStatus = hasFridge ? "Refrigerator " + score.ToString("P0") : _fridgeDetector.Status;
            if (expanded)
                fridgeStatus = _fridgeHologram.ExpandedSectionName + " expanded";
            else if (FridgeSelected)
                fridgeStatus = _fridgeHologram.IsInspecting ? "Inspecting refrigerator" : "Moving refrigerator into view";
            else if (_fridgeHologram != null && _fridgeHologram.IsVisible)
                fridgeStatus = "Test hologram";
            var aimStatus = locked ? "Aim locked" : aiming ? "Aiming" : "Make the Pistol gesture to aim";
            var triggerStatus = _triggerState switch
            {
                TriggerState.Ready => "Curl your middle finger to fire",
                TriggerState.Fired => "Lift your middle finger to rearm",
                _ => "Lift your middle finger to arm"
            };
            if (_triggerState != TriggerState.Ready && Time.unscaledTime < _shotMessageUntil)
                triggerStatus = _shotMessage;
            var middleExtension = hand?.MiddlePipExtension?.ToString("F2") ?? "–";
            var triggerReadout = "Middle PIP " + middleExtension + "   Ready ≥ " + _triggerReadyExtension.ToString("F2") +
                "   Fire ≤ " + FireThreshold.ToString("F2");
            var gesture = hand == null ? "No hand detected" : hand.Gesture.ToString();
            var controls = "Three fingers opens test. Up / Down adjusts wrist depth.";
            if (expanded)
                controls = "Aim at a part for its reference. Select the door to go back.";
            else if (_fridgeHologram != null && _fridgeHologram.IsVisible)
                controls = "Select a door or rear cover. Fist or drag rotates.";
            SetStatus("FRIDGE DEMO\n" + _handManager.Status + "\n" + fridgeStatus + "\n" + aimStatus +
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

        private FridgeHologram.SectionHit FindFingerSection(Hand hand, Rect viewport, Vector2 start, Vector2 end)
        {
            if (_handManager.TryGetFingerRay(_camera, HandFinger.Index, out var ray, hand))
            {
                var hit = _fridgeHologram.FindSectionHit(ray, out bool hitsModel);
                if (hitsModel)
                    return hit;
            }

            // Use the projected finger line when the webcam's estimated depth misses the model.
            const int samples = 24;
            for (int i = 0; i <= samples; i++)
            {
                var imagePoint = Vector2.Lerp(start, end, i / (float)samples);
                var point = _handManager.ImageToViewport(imagePoint, viewport);
                var hit = _fridgeHologram.FindSectionHit(_camera.ViewportPointToRay(point), out bool hitsModel);
                if (hitsModel)
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
            foreach (var hand in hands)
            {
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

        private bool PullTrigger(Hand hand, double now)
        {
            if (!ReferenceEquals(hand, _triggerHand))
            {
                _triggerState = TriggerState.Waiting;
                _triggerReleaseStartedAt = -1d;
                _triggerHand = hand;
            }
            if (hand?.MiddlePipExtension is not float extension)
            {
                _triggerReleaseStartedAt = -1d;
                return false;
            }

            if (_triggerState != TriggerState.Ready)
            {
                if (extension < _triggerReadyExtension)
                {
                    _triggerReleaseStartedAt = -1d;
                    return false;
                }

                if (_triggerReleaseStartedAt < 0d)
                    _triggerReleaseStartedAt = now;
                if (now - _triggerReleaseStartedAt >= _triggerReleaseSeconds)
                    _triggerState = TriggerState.Ready;
            }
            if (_triggerState != TriggerState.Ready || extension > FireThreshold)
            {
                return false;
            }

            _triggerState = TriggerState.Fired;
            _triggerReleaseStartedAt = -1d;
            return true;
        }

        private static float DistanceToImageEdge(Vector2 origin, Vector2 direction)
        {
            var edgeX = direction.x >= 0 ? 1f : 0f;
            var edgeY = direction.y >= 0 ? 1f : 0f;
            var distanceX = Mathf.Abs(direction.x) > 0.0001f ? (edgeX - origin.x) / direction.x : float.PositiveInfinity;
            var distanceY = Mathf.Abs(direction.y) > 0.0001f ? (edgeY - origin.y) / direction.y : float.PositiveInfinity;
            return Mathf.Clamp(Mathf.Min(distanceX, distanceY), 0f, 1.42f);
        }

        private static bool TryAimAtFridge(Rect fridge, Vector2 origin, Vector2 direction, ref Vector2 end)
        {
            if (fridge.Contains(origin))
            {
                end = origin;
                return true;
            }
            if (direction == Vector2.zero)
                return false;

            var bounds = new Bounds(fridge.center, new Vector3(fridge.width, fridge.height, 1f));
            if (!bounds.IntersectRay(new Ray(origin, direction), out var distance) ||
                distance > Vector2.Distance(origin, end))
                return false;

            end = origin + direction * distance;
            return true;
        }

        private Vector3 ImageToWorld(Vector2 point, float depth)
        {
            var viewportPoint = _handManager.ImageToViewport(point, _handManager.GetImageViewport(_camera));
            return _camera.ViewportToWorldPoint(new Vector3(viewportPoint.x, viewportPoint.y, depth));
        }

        private void SetStatus(string text)
        {
            if (text == _lastStatus)
                return;
            _status.text = text;
            _lastStatus = text;
        }

        private void CreateHandVisual()
        {
            var lines = new LineRenderer[FingerChains.Length];
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] = CreateLine("Finger " + i, _neutralColor, 0.003f);
                lines[i].positionCount = FingerChains[i].Length;
            }
            _handLines.Add(lines);
            var dots = new Transform[Hand.PointCount];
            for (var i = 0; i < dots.Length; i++)
                dots[i] = CreateDot(((HandPoint)i).ToString(), _neutralColor);
            _handDots.Add(dots);
            _appliedHandColors.Add(_neutralColor);
        }

        private Color GetHandColor(Hand hand)
        {
            var color = hand.Gesture switch
            {
                HandGesture.OpenHand => _openHandColor,
                HandGesture.Fist => _fistColor,
                HandGesture.Pinch => _pinchColor,
                HandGesture.Point => _pointColor,
                HandGesture.Pistol => _pistolColor,
                HandGesture.MiddleFinger => _middleFingerColor,
                HandGesture.ThreeFingers => _threeFingersColor,
                _ => _neutralColor
            };

            if (hand.Gesture == HandGesture.Pistol && ReferenceEquals(hand, _triggerHand))
            {
                if (_triggerState == TriggerState.Ready && _usePistolReadyColor)
                    color = _pistolReadyColor;
                else if (_triggerState == TriggerState.Fired && _usePistolFiredColor)
                    color = _pistolFiredColor;
            }

            if (hand.Side != HandSide.Left || _leftHandHue == 0f)
                return color;

            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            var shifted = Color.HSVToRGB(Mathf.Repeat(hue + _leftHandHue / 360f, 1f), saturation, value);
            shifted.a = color.a;
            return shifted;
        }

        private void ApplyHandColor(int index, Color color)
        {
            if (_appliedHandColors[index] == color)
                return;

            foreach (var line in _handLines[index])
            {
                line.startColor = line.endColor = color;
                SetVisualColor(line, color);
            }
            foreach (var dot in _handDots[index])
                SetVisualColor(dot.GetComponent<Renderer>(), color);

            _appliedHandColors[index] = color;
        }

        private void SetVisualColor(Renderer renderer, Color color)
        {
            _visualColor.SetColor(BaseColor, color);
            _visualColor.SetColor(LegacyColor, color);
            renderer.SetPropertyBlock(_visualColor);
        }

        private LineRenderer CreateLine(string label, Color color, float width)
        {
            var line = new GameObject(label).AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.sharedMaterial = _lineMaterial;
            line.startColor = line.endColor = color;
            SetVisualColor(line, color);
            line.startWidth = line.endWidth = width;
            line.numCapVertices = 4;
            line.useWorldSpace = true;
            return line;
        }

        private Transform CreateDot(string label, Color color)
        {
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = label;
            dot.layer = LayerMask.NameToLayer("Ignore Raycast");
            dot.transform.SetParent(transform, false);
            dot.transform.localScale = Vector3.one * 0.008f;
            dot.GetComponent<Collider>().enabled = false;
            var renderer = dot.GetComponent<Renderer>();
            renderer.sharedMaterial = _lineMaterial;
            SetVisualColor(renderer, color);
            return dot.transform;
        }
    }
}
