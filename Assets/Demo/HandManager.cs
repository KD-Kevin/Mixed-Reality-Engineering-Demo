using System.Collections.Generic;
using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;
using System;
using System.Collections;
using System.Runtime.InteropServices;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Unity.Experimental;
using RunningMode = Mediapipe.Tasks.Vision.Core.RunningMode;

namespace EngineeringDemo
{
    [DisallowMultipleComponent]
    public sealed class HandManager : MonoBehaviour
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
        [SerializeField, Min(0f)] private float _jitterDeadZone = 0.0015f;
        [SerializeField, Min(0.01f)] private float _trackingTimeoutSeconds = 0.35f;
        [SerializeField, Min(0f)] private float _pinchClose = 0.25f;
        [SerializeField, Min(0f)] private float _pinchRelease = 0.35f;
        [Tooltip("How far the middle finger can straighten while the hand still counts as Pistol.")]
        [SerializeField, Range(0.75f, 1f)] private float _pistolMiddleExtensionLimit = 1f;
        [Header("World placement")]
        [Tooltip("Wrist depth along the camera's forward axis, in Unity world units.")]
        [SerializeField, Min(0.05f)] private float _wristDistanceFromCamera = 0.6f;

        private WebCamTexture _webcam;
        private HandLandmarker _task;
        private TextureFrame _textureFrame;
        private byte[] _inferencePixels;
        private static readonly Mediapipe.ImageFrame.Deleter ReleasePixels = FreePixels;
        private Coroutine _startup;
        private readonly object _resultLock = new();
        private HandLandmarkerResult _pendingResult;
        private HandLandmarkerResult _currentResult;
        private bool _acceptResults;
        private bool _hasResult;
        private long _pendingTimestamp;
        private long _lastSubmittedTimestamp = -1;
        private float _heightOverWidth = 0.75f;

        private readonly List<Hand> _hands = new();
        private readonly List<Hand> _previousHands = new();
        private IReadOnlyList<Hand> _view;
        private double _lastFrameTime;
        private double _lastArrivalTime;

        public IReadOnlyList<Hand> Hands => _view ??= _hands.AsReadOnly();
        public Hand PrimaryHand { get; private set; }
        public string Status { get; private set; } = "Stopped";
        public bool IsRunning => _task != null;
        public Texture CameraTexture => _webcam;
        public bool MirrorWebcam => _mirrorWebcam;
        public bool CameraVerticallyMirrored => _webcam != null && _webcam.videoVerticallyMirrored;
        public float ImageAspect => 1f / _heightOverWidth;
        // Subscribers can build their own image from this frame during the callback.
        internal event Action<TextureFrame, long, ImageProcessingOptions> FramePrepared;
        public float WristDistanceFromCamera
        {
            get => _wristDistanceFromCamera;
            set => _wristDistanceFromCamera = Mathf.Max(0.05f, value);
        }

        private void OnEnable() => _startup = StartCoroutine(StartTracking());

        private IEnumerator StartTracking()
        {
            Status = "Loading hand model";
            if (_model == null)
            {
                Fail("Assign the hand landmark model to HandManager.");
                yield break;
            }

            try
            {
                var options = new HandLandmarkerOptions(
                    new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: _model.bytes),
                    runningMode: RunningMode.LIVE_STREAM, numHands: _maximumHands,
                    minHandDetectionConfidence: _minimumConfidence,
                    minHandPresenceConfidence: _minimumConfidence,
                    minTrackingConfidence: _minimumConfidence, resultCallback: OnTrackingResult);
                _task = HandLandmarker.CreateFromOptions(options);
                lock (_resultLock)
                    _acceptResults = true;
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
                yield break;
            }

            if (!_useWebcam)
            {
                Status = "Waiting for external camera frames";
                yield break;
            }

            Status = "Requesting camera access";
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                bool resolved = false;
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ => resolved = true;
                callbacks.PermissionDenied += _ => resolved = true;
                callbacks.PermissionRequestDismissed += _ => resolved = true;
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera, callbacks);
                yield return new WaitUntil(() => resolved);
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                {
                    Fail("Camera access was denied. Allow Camera in Android app settings and reopen the demo.");
                    yield break;
                }
            }
#else
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Fail("Webcam permission was denied.");
                yield break;
            }
#endif

            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Fail("No webcam was found.");
                yield break;
            }

            var deviceName = _webcamName;
            if (string.IsNullOrEmpty(deviceName))
            {
                var device = devices[0];
#if UNITY_ANDROID && !UNITY_EDITOR
                int rearCamera = Array.FindIndex(devices, candidate => !candidate.isFrontFacing);
                if (rearCamera >= 0)
                    device = devices[rearCamera];
#endif
                deviceName = device.name;
            }
            if (!Array.Exists(devices, device => device.name == deviceName))
            {
                Fail("The selected webcam is unavailable: " + deviceName);
                yield break;
            }

            _webcam = new WebCamTexture(deviceName, _cameraWidth, _cameraHeight, _cameraFrameRate);
            _webcam.Play();
            Status = "Starting " + deviceName;
            var deadline = Time.realtimeSinceStartupAsDouble + 10;
            while (_webcam.width <= 16 && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;

            if (_webcam.width <= 16)
            {
                Fail("The webcam did not start. Check whether another application is using it.");
                yield break;
            }

            Status = "Tracking hands";
        }

        // The callback's memory belongs to MediaPipe. Copy before returning to its worker.
        private void OnTrackingResult(HandLandmarkerResult result, Mediapipe.Image image, long timestamp)
        {
            lock (_resultLock)
            {
                if (!_acceptResults)
                    return;
                result.CloneTo(ref _pendingResult);
                _pendingTimestamp = timestamp;
                _hasResult = true;
            }
        }

        /// <summary>
        /// Submit an external camera texture on the main thread when Use Webcam is disabled.
        /// </summary>
        public void SubmitFrame(Texture texture, bool mirrorHorizontally = false,
            bool verticallyMirrored = false, int rotationDegrees = 0)
        {
            if (!isActiveAndEnabled || _task == null || texture == null || texture.width <= 16)
                return;

            try
            {
                if (_textureFrame == null || _textureFrame.width != texture.width || _textureFrame.height != texture.height)
                {
                    _textureFrame?.Dispose();
                    _textureFrame = new TextureFrame(texture.width, texture.height, TextureFormat.RGBA32);
                }
                _heightOverWidth = (float)texture.height / texture.width;
                _textureFrame.ReadTextureOnCPU(texture, mirrorHorizontally, !verticallyMirrored);
                using var image = CopyFrameForInference(_textureFrame, ref _inferencePixels);
                var timestamp = Math.Max(_lastSubmittedTimestamp + 1, (long)(Time.realtimeSinceStartupAsDouble * 1000));
                _lastSubmittedTimestamp = timestamp;
                var imageOptions = new ImageProcessingOptions(rotationDegrees: rotationDegrees);
                _task.DetectAsync(image, timestamp, imageOptions);
                FramePrepared?.Invoke(_textureFrame, timestamp, imageOptions);
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        internal static Mediapipe.Image CopyFrameForInference(TextureFrame frame, ref byte[] scratch)
        {
            // The live graph can still read its image after this texture is reused for a new frame.
            var source = ((Texture2D)frame.texture).GetRawTextureData<byte>();
            if (scratch == null || scratch.Length != source.Length)
                scratch = new byte[source.Length];
            source.CopyTo(scratch);
            var pixels = Marshal.AllocHGlobal(scratch.Length);
            try
            {
                Marshal.Copy(scratch, 0, pixels, scratch.Length);
                return new Mediapipe.Image(frame.imageFormat, frame.width, frame.height, frame.width * 4,
                    pixels, ReleasePixels);
            }
            catch
            {
                Marshal.FreeHGlobal(pixels);
                throw;
            }
        }

        [AOT.MonoPInvokeCallback(typeof(Mediapipe.ImageFrame.Deleter))]
        private static void FreePixels(IntPtr pixels) => Marshal.FreeHGlobal(pixels);

        private void Fail(string message)
        {
            StopTracking();
            Status = message;
            Debug.LogError("HandManager: " + message, this);
        }

        private void StopTracking()
        {
            lock (_resultLock)
            {
                _acceptResults = false;
                _hasResult = false;
            }
            var task = _task;
            _task = null;
            try
            {
                task?.Close();
            }
            finally
            {
                _textureFrame?.Dispose();
                _textureFrame = null;
                if (_webcam != null)
                {
                    _webcam.Stop();
                    Destroy(_webcam);
                    _webcam = null;
                }
                _lastSubmittedTimestamp = -1;
                _lastFrameTime = 0;
                ClearHands();
            }
        }

        /// <summary>The camera viewport occupied by an aspect-fitted image.</summary>
        public Rect GetImageViewport(Camera camera)
        {
            var width = Mathf.Min(1f, ImageAspect / camera.aspect);
            var height = Mathf.Min(1f, camera.aspect / ImageAspect);
            return new Rect((1f - width) * 0.5f, (1f - height) * 0.5f, width, height);
        }

        public bool TryGetWorldPoint(Camera camera, Hand hand, HandPoint point, out Vector3 position)
        {
            position = default;
            if (camera == null || hand == null || !hand.TryGetPoint(point, out var landmark))
                return false;

            var imageRect = GetImageViewport(camera);
            var depth = Mathf.Max(camera.nearClipPlane + 0.01f, _wristDistanceFromCamera);
            var left = camera.ViewportToWorldPoint(new Vector3(imageRect.xMin, 0.5f, depth));
            var right = camera.ViewportToWorldPoint(new Vector3(imageRect.xMax, 0.5f, depth));
            // Relative landmark Z is scaled by image width. The wrist anchors the depth.
            depth += (landmark.z - hand.Wrist.z) * Vector3.Distance(left, right);
            if (depth <= camera.nearClipPlane)
                return false;

            position = camera.ViewportToWorldPoint(new Vector3(
                imageRect.xMin + landmark.x * imageRect.width,
                imageRect.yMin + (1f - landmark.y) * imageRect.height, depth));
            return true;
        }

        public bool TryGetFingerRay(Camera camera, HandFinger finger, out Ray ray, Hand hand = null)
        {
            ray = default;
            hand ??= PrimaryHand;
            var tip = Hand.GetTipPoint(finger);
            if (!TryGetWorldPoint(camera, hand, tip, out var origin) ||
                !TryGetWorldPoint(camera, hand, (HandPoint)((int)tip - 1), out var joint))
                return false;

            var direction = origin - joint;
            if (direction.sqrMagnitude < 0.000001f)
                return false;
            ray = new Ray(origin, direction.normalized);
            return true;
        }

        public bool RaycastFinger(Camera camera, HandFinger finger, out RaycastHit hit,
            float maxDistance = 10f, int layerMask = Physics.DefaultRaycastLayers,
            QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore, Hand hand = null)
        {
            hit = default;
            return TryGetFingerRay(camera, finger, out var ray, hand) &&
                Physics.Raycast(ray, out hit, maxDistance, layerMask, triggers);
        }

        public bool SphereCastFinger(Camera camera, HandFinger finger, float radius, out RaycastHit hit,
            float maxDistance = 10f, int layerMask = Physics.DefaultRaycastLayers,
            QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore, Hand hand = null)
        {
            hit = default;
            return radius > 0f && TryGetFingerRay(camera, finger, out var ray, hand) &&
                Physics.SphereCast(ray, radius, out hit, maxDistance, layerMask, triggers);
        }

        public bool TryGetHand(HandSide side, out Hand hand)
        {
            for (int i = 0; i < _hands.Count; i++)
            {
                Hand trackedHand = _hands[i];
                if (trackedHand.Side == side)
                {
                    hand = trackedHand;
                    return true;
                }
            }

            hand = null;
            return false;
        }

        /// <summary>
        /// Submit a MediaPipe result from the Unity main thread.
        /// </summary>
        public void UpdateHands(HandLandmarkerResult result, double timestampSeconds, float heightOverWidth)
        {
            if (!isActiveAndEnabled)
                return;

            var elapsed = timestampSeconds - _lastFrameTime;
            if (elapsed < 0 || elapsed > _trackingTimeoutSeconds)
                ClearHands();

            _lastFrameTime = timestampSeconds;
            _lastArrivalTime = Time.realtimeSinceStartupAsDouble;
            heightOverWidth = Mathf.Max(0.01f, heightOverWidth);

            float blend = _smoothingSeconds <= 0f
                ? 1f
                : 1f - Mathf.Exp(-(float)elapsed / _smoothingSeconds);

            _previousHands.AddRange(_hands);
            _hands.Clear();

            int count = result.handLandmarks?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var points = result.handLandmarks[i].landmarks;
                if (points == null || points.Count != Hand.PointCount)
                {
                    continue;
                }

                HandSide side = GetSide(result, i);
                Vector2 wrist = new Vector2(points[0].x, points[0].y);
                Hand hand = FindPreviousHand(side, wrist, heightOverWidth) ?? new Hand();
                var worldLandmarks = result.handWorldLandmarks;
                var worldPoints = worldLandmarks != null && i < worldLandmarks.Count
                    ? worldLandmarks[i].landmarks : null;
                hand.Apply(points, worldPoints, side, blend, _jitterDeadZone, heightOverWidth,
                    _pinchClose, Mathf.Max(_pinchClose, _pinchRelease), _pistolMiddleExtensionLimit);
                _hands.Add(hand);
            }

            for (int i = 0; i < _previousHands.Count; i++)
            {
                Hand lostHand = _previousHands[i];
                lostHand.Clear();
            }
            _previousHands.Clear();

            if (PrimaryHand == null || !PrimaryHand.IsTracked)
            {
                PrimaryHand = _hands.Count > 0 ? _hands[0] : null;
            }
        }

        public void ClearHands()
        {
            foreach (var hand in _hands)
                hand.Clear();
            _hands.Clear();
            PrimaryHand = null;
        }

        private void Update()
        {
            if (_webcam != null && _webcam.width > 16 && _webcam.didUpdateThisFrame)
                SubmitFrame(_webcam, _mirrorWebcam, _webcam.videoVerticallyMirrored, _webcam.videoRotationAngle);

            long timestamp = -1;
            lock (_resultLock)
            {
                if (_hasResult)
                {
                    (_currentResult, _pendingResult) = (_pendingResult, _currentResult);
                    timestamp = _pendingTimestamp;
                    _hasResult = false;
                }
            }
            if (timestamp >= 0)
            {
                UpdateHands(_currentResult, timestamp / 1000d, _heightOverWidth);
                Status = "Tracking hands";
            }
            if (_hands.Count > 0 && Time.realtimeSinceStartupAsDouble - _lastArrivalTime > _trackingTimeoutSeconds)
            {
                ClearHands();
            }
        }

        private void OnDisable()
        {
            if (_startup != null)
                StopCoroutine(_startup);
            _startup = null;
            StopTracking();
            Status = "Stopped";
        }

        private Hand FindPreviousHand(HandSide side, Vector2 wrist, float heightOverWidth)
        {
            var bestDistance = 0.25f * 0.25f;
            Hand closest = null;
            foreach (var candidate in _previousHands)
            {
                if (side != HandSide.Unknown && candidate.Side != HandSide.Unknown && candidate.Side != side)
                    continue;

                Vector2 delta = candidate.LastObservedWrist - wrist;
                delta.y *= heightOverWidth;
                if (delta.sqrMagnitude < bestDistance)
                {
                    bestDistance = delta.sqrMagnitude;
                    closest = candidate;
                }
            }

            if (closest != null)
                _previousHands.Remove(closest);
            return closest;
        }

        private static HandSide GetSide(HandLandmarkerResult result, int index)
        {
            if (result.handedness == null || index >= result.handedness.Count)
                return HandSide.Unknown;

            var categories = result.handedness[index].categories;
            if (categories == null || categories.Count == 0)
                return HandSide.Unknown;

            return categories[0].categoryName switch
            {
                "Left" => HandSide.Left,
                "Right" => HandSide.Right,
                _ => HandSide.Unknown
            };
        }
    }
}
