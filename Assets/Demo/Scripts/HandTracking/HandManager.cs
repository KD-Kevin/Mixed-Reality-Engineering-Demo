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
        [SerializeField] private HandTrackingSettings _settings;

        private float _wristDistanceFromCamera = 0.6f;

        private WebCamTexture _webcam;
        private Camera _depthViewCamera;
        private FridgeArDepth _arDepth;
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
        internal double TrackingTimestamp => _lastFrameTime;
        public Hand PrimaryHand { get; private set; }
        public string Status { get; private set; } = "Stopped";
        public bool IsRunning => _task != null;
        public Texture CameraTexture => _arDepth?.Texture != null ? _arDepth.Texture : _webcam;
        public bool MirrorWebcam => _arDepth?.IsReady != true && _settings != null && _settings.MirrorWebcam;
        public bool CameraVerticallyMirrored => _webcam != null && _webcam.videoVerticallyMirrored;
        public float ImageAspect => 1f / _heightOverWidth;
        internal int CameraRotationAngle => _webcam != null ? _webcam.videoRotationAngle : 0;

        internal void UseArDepth(Camera viewCamera) => _depthViewCamera = viewCamera;

        internal void MatchArProjection() => _arDepth?.MatchViewProjection();

        internal bool TryGetEnvironmentDepth(Vector2 viewportPoint, out float depth)
        {
            depth = 0f;
            return _arDepth != null && _arDepth.TryGetDepth(viewportPoint, out depth);
        }
        // Subscribers can build their own image from this frame during the callback.
        internal event Action<TextureFrame, long, ImageProcessingOptions> FramePrepared;
        public float WristDistanceFromCamera
        {
            get => _wristDistanceFromCamera;
            set => _wristDistanceFromCamera = Mathf.Max(0.05f, value);
        }

        private void Awake()
        {
            if (_settings != null)
                WristDistanceFromCamera = _settings.InitialWristDistance;
        }

        private void OnEnable()
        {
            if (_settings == null)
            {
                Fail("Assign Hand Tracking Settings to HandManager.");
                return;
            }

            _startup = StartCoroutine(StartTracking());
        }

        private IEnumerator StartTracking()
        {
            Status = "Loading hand model";
            if (_settings.Model == null)
            {
                Fail("Assign the hand landmark model to HandManager.");
                yield break;
            }

            try
            {
                var options = new HandLandmarkerOptions(
                    new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: _settings.Model.bytes),
                    runningMode: RunningMode.LIVE_STREAM, numHands: _settings.MaximumHands,
                    minHandDetectionConfidence: _settings.MinimumConfidence,
                    minHandPresenceConfidence: _settings.MinimumConfidence,
                    minTrackingConfidence: _settings.MinimumConfidence, resultCallback: OnTrackingResult);
                _task = HandLandmarker.CreateFromOptions(options);
                lock (_resultLock)
                    _acceptResults = true;
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
                yield break;
            }

            if (!_settings.UseWebcam)
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

#if UNITY_ANDROID && !UNITY_EDITOR
            // Let the demo view register its camera before choosing which source opens the device.
            yield return null;
            if (_depthViewCamera != null)
            {
                Status = "Starting AR camera";
                _arDepth = new FridgeArDepth(_depthViewCamera, Mathf.Max(_settings.CameraWidth, _settings.CameraHeight));
                yield return _arDepth.Start();
                if (_arDepth.IsReady)
                {
                    yield return CaptureArFrames();
                    yield break;
                }

                StopArCamera();
                yield return null;
            }
#endif

            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Fail("No webcam was found.");
                yield break;
            }

            var deviceName = _settings.WebcamName;
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

            _webcam = new WebCamTexture(deviceName, _settings.CameraWidth, _settings.CameraHeight, _settings.CameraFrameRate);
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

        private IEnumerator CaptureArFrames()
        {
            var endOfFrame = new WaitForEndOfFrame();
            double nextFrame = 0d;
            double interval = 1d / Mathf.Max(1, _settings.CameraFrameRate);
            while (_arDepth != null)
            {
                _arDepth.ResizeCapture();
                yield return endOfFrame;
                double now = Time.realtimeSinceStartupAsDouble;
                if (_arDepth?.HasFrame == true && now >= nextFrame)
                {
                    nextFrame = now + interval;
                    SubmitFrame(_arDepth.Texture);
                }
            }
        }

        private void StopArCamera()
        {
            _arDepth?.Dispose();
            _arDepth = null;
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
        /// Submits an external camera texture on the main thread when Use Webcam is disabled in the tracking settings.
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
                StopArCamera();
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

        /// <summary>The camera viewport occupied by the camera image.</summary>
        public Rect GetImageViewport(Camera camera) => _arDepth?.IsReady == true
            ? new Rect(0f, 0f, 1f, 1f)
            : HandImageProjection.GetImageViewport(ImageAspect, camera.aspect, CameraRotationAngle);

        internal Rect GetCameraPreviewRect(Camera camera, Rect imageViewport) =>
            HandImageProjection.GetPreviewRect(imageViewport, camera.aspect, CameraRotationAngle);

        internal Vector2 ImageToViewport(Vector2 rawPoint, Rect viewport) =>
            HandImageProjection.ImageToViewport(rawPoint, viewport, CameraRotationAngle);

        internal Vector2 ViewportToImage(Vector2 point, Rect viewport) =>
            HandImageProjection.ViewportToImage(point, viewport, CameraRotationAngle);

        internal Rect GetDisplayImageBox(Rect box) => HandImageProjection.GetDisplayBox(box, CameraRotationAngle);

        public bool TryGetWorldPoint(Camera camera, Hand hand, HandPoint point, out Vector3 position)
        {
            position = default;
            if (camera == null || hand == null || !hand.TryGetPoint(point, out var landmark))
                return false;

            var imageRect = GetImageViewport(camera);
            var depth = Mathf.Max(camera.nearClipPlane + 0.01f, _wristDistanceFromCamera);
            var rawLeft = ImageToViewport(new Vector2(0f, 0.5f), imageRect);
            var rawRight = ImageToViewport(new Vector2(1f, 0.5f), imageRect);
            var left = camera.ViewportToWorldPoint(new Vector3(rawLeft.x, rawLeft.y, depth));
            var right = camera.ViewportToWorldPoint(new Vector3(rawRight.x, rawRight.y, depth));
            // Relative landmark Z is scaled by image width. The wrist anchors the depth.
            depth += (landmark.z - hand.Wrist.z) * Vector3.Distance(left, right);
            if (depth <= camera.nearClipPlane)
                return false;

            var pointInView = ImageToViewport(landmark, imageRect);
            position = camera.ViewportToWorldPoint(new Vector3(pointInView.x, pointInView.y, depth));
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
            if (elapsed < 0 || elapsed > _settings.TrackingTimeoutSeconds)
                ClearHands();

            _lastFrameTime = timestampSeconds;
            _lastArrivalTime = Time.realtimeSinceStartupAsDouble;
            heightOverWidth = Mathf.Max(0.01f, heightOverWidth);

            float blend = _settings.SmoothingSeconds <= 0f
                ? 1f
                : 1f - Mathf.Exp(-(float)elapsed / _settings.SmoothingSeconds);

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
                hand.Apply(points, worldPoints, side, blend, _settings.JitterDeadZone, heightOverWidth,
                    _settings.PinchClose, Mathf.Max(_settings.PinchClose, _settings.PinchRelease), _settings.PistolMiddleExtensionLimit,
                    timestampSeconds, _settings.GestureChangeSeconds, CameraRotationAngle);
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
            for (int i = 0; i < _hands.Count; i++)
                _hands[i].Clear();
            _hands.Clear();
            PrimaryHand = null;
        }

        private void Update()
        {
            if (_webcam != null && _webcam.width > 16 && _webcam.didUpdateThisFrame)
                SubmitFrame(_webcam, _settings.MirrorWebcam, _webcam.videoVerticallyMirrored, CameraRotationAngle);

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
                Status = _arDepth?.IsReady == true ? "Tracking hands (AR camera)" : "Tracking hands";
            }
            if (_hands.Count > 0 && Time.realtimeSinceStartupAsDouble - _lastArrivalTime > _settings.TrackingTimeoutSeconds)
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
            int closestIndex = -1;
            for (int i = 0; i < _previousHands.Count; i++)
            {
                var candidate = _previousHands[i];
                if (side != HandSide.Unknown && candidate.Side != HandSide.Unknown && candidate.Side != side)
                    continue;

                Vector2 delta = candidate.LastObservedWrist - wrist;
                delta.y *= heightOverWidth;
                if (delta.sqrMagnitude < bestDistance)
                {
                    bestDistance = delta.sqrMagnitude;
                    closestIndex = i;
                }
            }

            if (closestIndex < 0)
                return null;

            var closest = _previousHands[closestIndex];
            _previousHands.RemoveAt(closestIndex);
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
