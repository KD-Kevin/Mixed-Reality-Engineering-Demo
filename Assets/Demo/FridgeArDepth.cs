using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace EngineeringDemo
{
    internal sealed class FridgeArDepth : IDisposable
    {
        private readonly Camera _view;
        private readonly int _captureSize;
        private XRManagerSettings _manager;
        private XRRaycastSubsystem _raycasts;
        private bool _ownsLoader;
        private GameObject _root;
        private Camera _camera;
        private ARCameraBackground _background;
        private AROcclusionManager _occlusion;
        private RenderTexture _capture;

        internal RenderTexture Texture => IsReady ? _capture : null;
        internal bool IsReady { get; private set; }
        internal bool HasFrame => IsReady && _background.backgroundRenderingEnabled;

        internal FridgeArDepth(Camera view, int captureSize)
        {
            _view = view;
            _captureSize = captureSize;
        }

        internal IEnumerator Start()
        {
            _manager = XRGeneralSettings.Instance?.Manager;
            if (_manager == null)
                yield break;

            if (_manager.activeLoader == null)
            {
                _ownsLoader = true;
                yield return _manager.InitializeLoader();
            }

            var loader = _manager.activeLoader;
            _raycasts = loader?.GetLoadedSubsystem<XRRaycastSubsystem>();
            if (_raycasts == null || (_raycasts.subsystemDescriptor.supportedTrackableTypes & TrackableType.Depth) == 0)
                yield break;

            yield return ARSession.CheckAvailability();
            if (ARSession.state != ARSessionState.Ready)
                yield break;

            // Render only the AR camera image. Both the preview and MediaPipe read this texture.
            _root = new GameObject("Fridge AR Camera Session");
            _root.SetActive(false);
            var session = _root.AddComponent<ARSession>();
            session.attemptUpdate = false;
            session.matchFrameRateRequested = false;

            var capture = new GameObject("AR Camera Capture");
            capture.transform.SetParent(_root.transform, false);
            _camera = capture.AddComponent<Camera>();
            _camera.depth = _view.depth - 1f;
            _camera.cullingMask = 0;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.nearClipPlane = _view.nearClipPlane;
            _camera.farClipPlane = _view.farClipPlane;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            var cameraManager = capture.AddComponent<ARCameraManager>();
            cameraManager.requestedFacingDirection = CameraFacingDirection.World;
            _occlusion = capture.AddComponent<AROcclusionManager>();
            _occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
            _background = capture.AddComponent<ARCameraBackground>();
            ResizeCapture();
            _root.SetActive(true);
            _raycasts.Start();

            double deadline = Time.realtimeSinceStartupAsDouble + 8d;
            while (!_background.backgroundRenderingEnabled && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;

            IsReady = _background.backgroundRenderingEnabled;
        }

        internal void ResizeCapture()
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            float scale = Mathf.Min(1f, (float)_captureSize / Mathf.Max(width, height));
            width = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            if (_capture != null && _capture.width == width && _capture.height == height)
                return;

            ReleaseCapture();
            _capture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Fridge AR Camera Frame"
            };
            _camera.targetTexture = _capture;
        }

        internal void MatchViewProjection()
        {
            if (HasFrame)
                _view.projectionMatrix = _camera.projectionMatrix;
        }

        internal bool TryGetDepth(Vector2 viewportPoint, out float depth)
        {
            depth = 0f;
            if (!HasFrame || ARSession.state != ARSessionState.SessionTracking ||
                _occlusion.currentEnvironmentDepthMode == EnvironmentDepthMode.Disabled ||
                viewportPoint.x < 0f || viewportPoint.x > 1f || viewportPoint.y < 0f || viewportPoint.y > 1f)
                return false;

            // The provider handles display rotation and maps the screen point to the depth image.
            var hits = _raycasts.Raycast(viewportPoint, TrackableType.Depth, Allocator.Temp);
            if (!hits.IsCreated)
                return false;

            float nearest = float.PositiveInfinity;
            using (hits)
            {
                foreach (var hit in hits)
                {
                    if (hit.distance > 0f)
                        nearest = Mathf.Min(nearest, hit.distance);
                }
            }

            // ViewportToWorldPoint takes forward depth instead of distance along the viewing ray.
            var ray = _camera.ViewportPointToRay(viewportPoint);
            depth = nearest * Vector3.Dot(ray.direction, _camera.transform.forward);
            return depth > _view.nearClipPlane && depth < _view.farClipPlane;
        }

        private void ReleaseCapture()
        {
            if (_capture == null)
                return;

            _camera.targetTexture = null;
            _capture.Release();
            UnityEngine.Object.Destroy(_capture);
            _capture = null;
        }

        public void Dispose()
        {
            if (IsReady && _view != null)
                _view.ResetProjectionMatrix();
            IsReady = false;
            if (_root != null)
                _root.SetActive(false);
            if (_raycasts?.running == true)
                _raycasts.Stop();
            ReleaseCapture();
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            if (_ownsLoader && _manager != null && _manager.isInitializationComplete)
                _manager.DeinitializeLoader();
        }
    }
}
