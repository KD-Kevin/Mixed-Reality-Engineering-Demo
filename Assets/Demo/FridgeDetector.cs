using System;
using System.Collections.Generic;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.ObjectDetector;
using Mediapipe.Unity.Experimental;
using UnityEngine;
using ObjectDetectorResult = Mediapipe.Tasks.Components.Containers.DetectionResult;

namespace EngineeringDemo
{
    public sealed class FridgeDetector : MonoBehaviour
    {
        [SerializeField] private HandManager _handManager;
        [SerializeField] private TextAsset _model;
        [SerializeField, Range(0f, 1f)] private float _minimumScore = 0.45f;
        [SerializeField, Range(1, 15)] private int _detectionsPerSecond = 5;
        [SerializeField, Min(0.1f)] private float _maximumResultAge = 0.5f;

        private ObjectDetector _detector;
        private byte[] _inferencePixels;
        private readonly object _resultLock = new();
        private Rect _box;
        private float _score;
        private long _resultTimestamp = -1;
        private long _nextDetectionTime;

        public string Status { get; private set; } = "Stopped";

        private void OnEnable()
        {
            if (_model == null || _handManager == null)
            {
                Status = "Assign a fridge model and Hand Manager";
                return;
            }

            try
            {
                var options = new ObjectDetectorOptions(
                    new BaseOptions(modelAssetBuffer: _model.bytes), runningMode: RunningMode.LIVE_STREAM,
                    maxResults: 1, scoreThreshold: _minimumScore,
                    categoryAllowList: new List<string> { "refrigerator" }, resultCallback: OnResult);
                _detector = ObjectDetector.CreateFromOptions(options);
                _nextDetectionTime = 0;
                lock (_resultLock)
                {
                    _box = default;
                    _resultTimestamp = -1;
                }
                _handManager.FramePrepared += SubmitFrame;
                Status = "Looking for a refrigerator";
            }
            catch (Exception exception)
            {
                Status = "Fridge detector unavailable";
                Debug.LogException(exception, this);
            }
        }

        private void SubmitFrame(TextureFrame frame, long timestamp, ImageProcessingOptions options)
        {
            if (timestamp < _nextDetectionTime)
                return;
            _nextDetectionTime = timestamp + 1000 / Mathf.Max(1, _detectionsPerSecond);

            try
            {
                using var image = HandManager.CopyFrameForInference(frame, ref _inferencePixels);
                _detector.DetectAsync(image, timestamp, options);
            }
            catch (Exception exception)
            {
                StopDetection();
                Status = "Fridge detector unavailable";
                Debug.LogException(exception, this);
            }
        }

        private void OnResult(ObjectDetectorResult result, Mediapipe.Image image, long timestamp)
        {
            if (result.detections is not { Count: > 0 } ||
                result.detections[0].categories is not { Count: > 0 } categories)
                return;

            var bounds = result.detections[0].boundingBox;
            var width = (float)image.Width();
            var height = (float)image.Height();
            var box = Rect.MinMaxRect(Mathf.Clamp01(bounds.left / width), Mathf.Clamp01(bounds.top / height),
                Mathf.Clamp01(bounds.right / width), Mathf.Clamp01(bounds.bottom / height));
            if (box.width <= 0f || box.height <= 0f)
                return;

            lock (_resultLock)
            {
                _box = box;
                _score = categories[0].score;
                _resultTimestamp = timestamp;
            }
        }

        /// <summary>Returns the latest fridge box in normalized image coordinates, with Y down.</summary>
        public bool TryGetFridge(out Rect box, out float score)
        {
            lock (_resultLock)
            {
                box = _box;
                score = _score;
                return _detector != null && _handManager.IsRunning && box.width > 0 && box.height > 0 &&
                    Time.realtimeSinceStartupAsDouble - _resultTimestamp / 1000d <= _maximumResultAge;
            }
        }

        private void OnDisable()
        {
            StopDetection();
            Status = "Stopped";
        }

        private void StopDetection()
        {
            if (_handManager != null)
                _handManager.FramePrepared -= SubmitFrame;
            var detector = _detector;
            _detector = null;
            try
            {
                detector?.Close();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }
}
