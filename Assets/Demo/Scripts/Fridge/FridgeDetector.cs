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
        [SerializeField, Min(0f)] private float _stationaryHoldSeconds = 0.65f;
        [SerializeField, Min(0f)] private float _boxSmoothingSeconds = 0.12f;

        private const int MotionColumns = 8;
        private const int MotionRows = 6;
        private const int MotionSampleCount = MotionColumns * MotionRows;
        private const int MotionHistorySize = 8;
        private const int BrightnessChangeThreshold = 28;

        private ObjectDetector _detector;
        private byte[] _inferencePixels;
        private readonly object _resultLock = new();
        private Rect _box;
        private float _score;
        private long _resultTimestamp = -1;
        private long _nextDetectionTime;
        private readonly byte[] _motionSamples = new byte[MotionSampleCount];
        private readonly byte[] _detectionSamples = new byte[MotionSampleCount];
        private readonly byte[,] _motionHistory = new byte[MotionHistorySize, MotionSampleCount];
        private readonly long[] _motionTimestamps = new long[MotionHistorySize];
        private int _nextMotionSlot;
        private int _frameWidth;
        private int _frameHeight;
        private int _frameRotation;
        private bool _hasDetectionSamples;
        private Rect _displayBox;
        private bool _hasDisplayBox;
        private double _lastDisplayUpdate;

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
                    _hasDisplayBox = false;
                    _frameWidth = 0;
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
            SampleCameraFrame(frame, timestamp, options.rotationDegrees);
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

        private void SampleCameraFrame(TextureFrame frame, long timestamp, int rotation)
        {
            var pixels = ((Texture2D)frame.texture).GetRawTextureData<Color32>();
            lock (_resultLock)
            {
                if (frame.width != _frameWidth || frame.height != _frameHeight || rotation != _frameRotation)
                {
                    _frameWidth = frame.width;
                    _frameHeight = frame.height;
                    _frameRotation = rotation;
                    _box = default;
                    _resultTimestamp = -1;
                    _hasDisplayBox = false;
                    _hasDetectionSamples = false;
                    Array.Fill(_motionTimestamps, -1L);
                    _nextDetectionTime = 0;
                }

                for (int row = 0; row < MotionRows; row++)
                {
                    for (int column = 0; column < MotionColumns; column++)
                    {
                        int brightness = 0;
                        for (int sample = 0; sample < 4; sample++)
                        {
                            float offsetX = (sample % 2 == 0) ? 0.25f : 0.75f;
                            float offsetY = (sample < 2) ? 0.25f : 0.75f;
                            int x = (int)((column + offsetX) * frame.width / MotionColumns);
                            int y = (int)((row + offsetY) * frame.height / MotionRows);
                            var pixel = pixels[y * frame.width + x];
                            brightness += (pixel.r + pixel.g * 2 + pixel.b) / 4;
                        }
                        _motionSamples[row * MotionColumns + column] = (byte)(brightness / 4);
                    }
                }

                if (timestamp >= _nextDetectionTime)
                {
                    for (int i = 0; i < MotionSampleCount; i++)
                        _motionHistory[_nextMotionSlot, i] = _motionSamples[i];
                    _motionTimestamps[_nextMotionSlot] = timestamp;
                    _nextMotionSlot = (_nextMotionSlot + 1) % MotionHistorySize;
                }
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
                int slot = Array.IndexOf(_motionTimestamps, timestamp);
                if (slot < 0)
                    return;

                for (int i = 0; i < MotionSampleCount; i++)
                    _detectionSamples[i] = _motionHistory[slot, i];
                _hasDetectionSamples = true;
                _box = box;
                _score = categories[0].score;
                _resultTimestamp = timestamp;
            }
        }

        /// <summary>Returns the smoothed fridge box in normalized image coordinates, with Y down.</summary>
        public bool TryGetFridge(out Rect box, out float score)
        {
            lock (_resultLock)
            {
                box = default;
                score = _score;
                double now = Time.realtimeSinceStartupAsDouble;
                double age = now - _resultTimestamp / 1000d;
                if (_detector == null || !_handManager.IsRunning || !_hasDetectionSamples ||
                    age > _maximumResultAge + _stationaryHoldSeconds || CameraFrameChanged())
                {
                    _hasDisplayBox = false;
                    _hasDetectionSamples = false;
                    return false;
                }

                // Snap to a new detection if it moved by more than half a box. Blend ordinary jitter.
                var offset = _box.center - _displayBox.center;
                bool newTarget = Mathf.Abs(offset.x) > _box.width * 0.5f || Mathf.Abs(offset.y) > _box.height * 0.5f;
                float blend = !_hasDisplayBox || newTarget || _boxSmoothingSeconds <= 0f
                    ? 1f : 1f - Mathf.Exp(-(float)(now - _lastDisplayUpdate) / _boxSmoothingSeconds);
                Vector2 min = Vector2.Lerp(_displayBox.min, _box.min, blend);
                Vector2 max = Vector2.Lerp(_displayBox.max, _box.max, blend);
                _displayBox = new Rect(min, max - min);
                _hasDisplayBox = true;
                _lastDisplayUpdate = now;
                box = _displayBox;
                return true;
            }
        }

        private bool CameraFrameChanged()
        {
            int changed = 0;
            for (int i = 0; i < MotionSampleCount; i++)
            {
                if (Mathf.Abs(_motionSamples[i] - _detectionSamples[i]) >= BrightnessChangeThreshold)
                    changed++;
            }

            // Ignore localized changes such as the aiming hand. Widespread change invalidates the hold.
            return changed >= MotionSampleCount / 3;
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
