using UnityEngine;
using UnityEngine.InputSystem;

namespace EngineeringDemo
{
    [DisallowMultipleComponent]
    public sealed class HandDemoView : MonoBehaviour
    {
        [SerializeField] private HandManager _handManager;
        [SerializeField] private Camera _camera;
        [SerializeField] private UnityEngine.UI.RawImage _preview;
        [SerializeField] private UnityEngine.UI.Text _status;
        [SerializeField] private HandDemoVisuals _visuals;
        [SerializeField] private HandTargetPractice _targetPractice;
        [SerializeField] private FridgeDemoInteraction _fridgeInteraction;

        private Rect _lastCameraPixelRect;
        private int _lastCameraRotation;

        internal HandManager Tracking => _handManager;
        internal Camera ViewCamera => _camera;
        internal HandDemoVisuals Visuals => _visuals;

        private void Awake()
        {
            if (_fridgeInteraction != null)
                _handManager.UseArDepth(_camera);
        }

        private void Start()
        {
            _fridgeInteraction?.Initialize(this, _status);
            _targetPractice?.Initialize(_visuals);
        }

        private void LateUpdate()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                var depthChange = (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f);
                _handManager.WristDistanceFromCamera += depthChange * Time.unscaledDeltaTime * 0.3f;
            }

            _handManager.MatchArProjection();
            var cameraPixelRect = _camera.pixelRect;
            int cameraRotation = _handManager.CameraRotationAngle;
            if (cameraPixelRect != _lastCameraPixelRect || cameraRotation != _lastCameraRotation)
            {
                _lastCameraPixelRect = cameraPixelRect;
                _lastCameraRotation = cameraRotation;
                _fridgeInteraction?.ResetAim();
            }

            var imageViewport = _handManager.GetImageViewport(_camera);
            UpdatePreview(imageViewport, cameraRotation);

            // Resolve this frame's trigger before drawing the hand's gesture color.
            _fridgeInteraction?.Tick(imageViewport, keyboard);
            _visuals.RenderHands(_handManager, _camera, _fridgeInteraction?.Trigger);
            _targetPractice?.Render(this, keyboard);
        }

        private void UpdatePreview(Rect imageViewport, int cameraRotation)
        {
            var previewRect = _handManager.GetCameraPreviewRect(_camera, imageViewport);
            var previewTransform = _preview.rectTransform;
            _preview.texture = _handManager.CameraTexture;
            previewTransform.anchorMin = previewRect.min;
            previewTransform.anchorMax = previewRect.max;
            previewTransform.localRotation = Quaternion.Euler(0f, 0f, -cameraRotation);
            bool mirrorX = _handManager.MirrorWebcam;
            bool mirrorY = _handManager.CameraVerticallyMirrored;
            _preview.uvRect = new Rect(mirrorX ? 1 : 0, mirrorY ? 1 : 0, mirrorX ? -1 : 1, mirrorY ? -1 : 1);
        }

        internal void SetStatus(string text)
        {
            if (_status.text != text)
                _status.text = text;
        }
    }
}
