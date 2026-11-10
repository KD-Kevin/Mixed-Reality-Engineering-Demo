using System;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using UnityEngine;

namespace EngineeringDemo
{
    // The values match MediaPipe's landmark indices.
    public enum HandPoint
    {
        Wrist,
        ThumbCmc, ThumbMcp, ThumbIp, ThumbTip,
        IndexMcp, IndexPip, IndexDip, IndexTip,
        MiddleMcp, MiddlePip, MiddleDip, MiddleTip,
        RingMcp, RingPip, RingDip, RingTip,
        PinkyMcp, PinkyPip, PinkyDip, PinkyTip
    }

    public enum HandGesture { None, Neutral, OpenHand, Fist, Pinch, Point, Pistol, MiddleFinger, ThreeFingers }
    public enum HandSide { Unknown, Left, Right }
    public enum HandFinger { Thumb, Index, Middle, Ring, Pinky }

    public sealed class Hand
    {
        [Flags]
        private enum FingerMask
        {
            None = 0, Index = 1, Middle = 2, Ring = 4, Pinky = 8,
            All = Index | Middle | Ring | Pinky
        }

        internal const int PointCount = 21;
        private readonly Vector3[] _points = new Vector3[PointCount];

        internal Hand() => Points = Array.AsReadOnly(_points);

        public HandSide Side { get; private set; }
        public HandGesture Gesture { get; private set; }
        public bool IsTracked => Gesture != HandGesture.None;
        public bool IsPinching => Gesture == HandGesture.Pinch;
        public float MiddleFingerExtension { get; private set; }
        /// <summary>Middle finger PIP extension. One is straight, zero is bent 90 degrees or more.</summary>
        public float? MiddlePipExtension { get; private set; }

        /// <summary>
        /// Normalized image coordinates. Y increases down and Z is relative depth.
        /// </summary>
        public IReadOnlyList<Vector3> Points { get; }
        public Vector3 Wrist => GetPoint(HandPoint.Wrist);
        public Vector3 ThumbTip => GetPoint(HandPoint.ThumbTip);
        public Vector3 IndexTip => GetPoint(HandPoint.IndexTip);
        public Vector3 MiddleTip => GetPoint(HandPoint.MiddleTip);
        public Vector3 RingTip => GetPoint(HandPoint.RingTip);
        public Vector3 PinkyTip => GetPoint(HandPoint.PinkyTip);

        internal Vector2 LastObservedWrist { get; private set; }
        internal Quaternion? PalmRotation { get; private set; }

        public Vector3 GetPoint(HandPoint point) => _points[(int)point];

        public Vector3 GetFingerTip(HandFinger finger) => GetPoint(GetTipPoint(finger));

        public Vector3 GetFingerDirection(HandFinger finger)
        {
            var tip = GetTipPoint(finger);
            return (GetPoint(tip) - GetPoint((HandPoint)((int)tip - 1))).normalized;
        }

        internal static HandPoint GetTipPoint(HandFinger finger)
        {
            if ((uint)finger > (uint)HandFinger.Pinky)
                throw new ArgumentOutOfRangeException(nameof(finger));
            return (HandPoint)(4 + (int)finger * 4);
        }

        public bool TryGetPoint(HandPoint point, out Vector3 position)
        {
            int index = (int)point;
            if (IsTracked && (uint)index < PointCount)
            {
                position = _points[index];
                return true;
            }

            position = default;
            return false;
        }

        internal void Apply(List<NormalizedLandmark> landmarks, List<Landmark> worldLandmarks, HandSide side,
            float blend, float deadZone, float heightOverWidth, float pinchClose, float pinchRelease,
            float pistolMiddleExtensionLimit, int cameraRotationAngle = 0)
        {
            var wasTracked = IsTracked;
            if (side != HandSide.Unknown)
                Side = side;

            LastObservedWrist = new Vector2(landmarks[0].x, landmarks[0].y);
            for (var i = 0; i < PointCount; i++)
            {
                var landmark = landmarks[i];
                Vector3 target = new Vector3(landmark.x, landmark.y, landmark.z);
                if (!wasTracked || blend >= 1f)
                {
                    _points[i] = target;
                }
                else if (ImageDelta(target, _points[i], heightOverWidth).sqrMagnitude > deadZone * deadZone)
                {
                    _points[i] = Vector3.Lerp(_points[i], target, blend);
                }
            }

            UpdatePalmRotation(worldLandmarks, heightOverWidth, wasTracked ? blend : 1f, cameraRotationAngle);
            MiddleFingerExtension = FingerExtension(HandPoint.MiddleMcp, heightOverWidth);
            var pipExtension = MeasureMiddlePipExtension(landmarks, worldLandmarks, heightOverWidth);
            MiddlePipExtension = wasTracked && MiddlePipExtension.HasValue && pipExtension.HasValue
                ? Mathf.Lerp(MiddlePipExtension.Value, pipExtension.Value, blend)
                : pipExtension;
            Vector3 palmAxis = ImageDelta(GetPoint(HandPoint.MiddleMcp), Wrist, heightOverWidth);
            float palm = palmAxis.magnitude;
            float gap = ImageDelta(ThumbTip, IndexTip, heightOverWidth).magnitude;
            float pinchThreshold = IsPinching ? pinchRelease : pinchClose;
            if (palm > 0.0001f && gap <= palm * pinchThreshold &&
                IsPinchOutsidePalm(palmAxis, heightOverWidth))
            {
                Gesture = HandGesture.Pinch;
                return;
            }

            float index = FingerExtension(HandPoint.IndexMcp, heightOverWidth);
            float middle = MiddleFingerExtension;
            float ring = FingerExtension(HandPoint.RingMcp, heightOverWidth);
            float pinky = FingerExtension(HandPoint.PinkyMcp, heightOverWidth);
            float thumb = FingerExtension(HandPoint.ThumbCmc, heightOverWidth);
            float thumbSpread = ImageDelta(ThumbTip, GetPoint(HandPoint.IndexMcp), heightOverWidth).magnitude;
            bool thumbOut = thumb > 0.85f && thumbSpread > palm * 0.6f;
            var folded = GetFoldedFingers(worldLandmarks, heightOverWidth, out var extended);
            if (folded == FingerMask.All)
            {
                Gesture = HandGesture.Fist;
            }
            else if (folded == (FingerMask.Index | FingerMask.Ring | FingerMask.Pinky) && extended == FingerMask.Middle)
            {
                Gesture = HandGesture.MiddleFinger;
            }
            else if (folded == FingerMask.Pinky && extended == (FingerMask.Index | FingerMask.Middle | FingerMask.Ring))
            {
                Gesture = HandGesture.ThreeFingers;
            }
            else if (index > 0.9f && middle > 0.9f && ring > 0.9f && pinky > 0.9f)
            {
                Gesture = HandGesture.OpenHand;
            }
            else if (index > 0.9f && middle <= pistolMiddleExtensionLimit && ring < 0.75f && pinky < 0.75f && thumbOut)
            {
                Gesture = HandGesture.Pistol;
            }
            else if (index > 0.9f && middle < 0.75f && ring < 0.75f && pinky < 0.75f)
            {
                Gesture = HandGesture.Point;
            }
            else
            {
                Gesture = HandGesture.Neutral;
            }
        }

        internal void Clear()
        {
            Gesture = HandGesture.None;
            MiddleFingerExtension = 0f;
            MiddlePipExtension = null;
            PalmRotation = null;
            Array.Clear(_points, 0, PointCount);
        }

        private void UpdatePalmRotation(List<Landmark> worldLandmarks, float heightOverWidth, float blend, int cameraRotationAngle)
        {
            bool useWorld = worldLandmarks?.Count == PointCount;
            Vector3 CameraPoint(HandPoint point)
            {
                var position = useWorld ? Position(worldLandmarks[(int)point]) :
                    ImageDelta(GetPoint(point), Vector3.zero, heightOverWidth);
                return new Vector3(position.x, -position.y, position.z);
            }

            var index = CameraPoint(HandPoint.IndexMcp);
            var pinky = CameraPoint(HandPoint.PinkyMcp);
            var along = (CameraPoint(HandPoint.MiddleMcp) + CameraPoint(HandPoint.RingMcp)) * 0.5f -
                CameraPoint(HandPoint.Wrist);
            var normal = Vector3.Cross((index - pinky).normalized, along.normalized);
            if (!(normal.sqrMagnitude > 0.01f))
            {
                PalmRotation = null;
                return;
            }

            var rotation = Quaternion.AngleAxis(-cameraRotationAngle, Vector3.forward) * Quaternion.LookRotation(normal, along);
            PalmRotation = useWorld && PalmRotation is Quaternion previous
                ? Quaternion.Slerp(previous, rotation, blend)
                : rotation;
        }

        private static float? MeasureMiddlePipExtension(List<NormalizedLandmark> landmarks,
            List<Landmark> worldLandmarks, float heightOverWidth)
        {
            const int mcp = (int)HandPoint.MiddleMcp;
            const int pip = (int)HandPoint.MiddlePip;
            const int dip = (int)HandPoint.MiddleDip;

            // Measure MCP → PIP → DIP. The fingertip is irrelevant to the bend at PIP.
            if (worldLandmarks?.Count == PointCount)
            {
                var joint = Position(worldLandmarks[pip]);
                var extension = JointExtension(joint - Position(worldLandmarks[mcp]), Position(worldLandmarks[dip]) - joint);
                if (extension.HasValue)
                    return extension;
            }

            Vector3 ImagePoint(int index)
            {
                var point = landmarks[index];
                return new Vector3(point.x, point.y * heightOverWidth, point.z);
            }

            var imageJoint = ImagePoint(pip);
            return JointExtension(imageJoint - ImagePoint(mcp), ImagePoint(dip) - imageJoint);
        }

        private static float? JointExtension(Vector3 proximal, Vector3 middle)
        {
            if (!(proximal.sqrMagnitude > 0.00000001f && middle.sqrMagnitude > 0.00000001f))
                return null;

            return 1f - Mathf.Clamp01(Vector3.Angle(proximal, middle) / 90f);
        }

        private static Vector3 Position(Landmark point) => new Vector3(point.x, point.y, point.z);

        private bool IsPinchOutsidePalm(Vector3 palmAxis, float heightOverWidth)
        {
            // In a fist the thumb can touch the index tip while both are folded over the palm.
            // Accept contact beyond the knuckles or out past the thumb side of the hand.
            Vector3 indexKnuckle = GetPoint(HandPoint.IndexMcp);
            Vector3 contact = (ThumbTip + IndexTip) * 0.5f;
            Vector3 contactOffset = ImageDelta(contact, indexKnuckle, heightOverWidth);
            Vector3 thumbSideAxis = ImageDelta(indexKnuckle, GetPoint(HandPoint.PinkyMcp), heightOverWidth);
            float margin = IsPinching ? 0.10f : 0.18f;
            return Vector3.Dot(contactOffset, palmAxis) > margin * palmAxis.sqrMagnitude ||
                Vector3.Dot(contactOffset, thumbSideAxis) > margin * thumbSideAxis.sqrMagnitude;
        }

        private FingerMask GetFoldedFingers(List<Landmark> worldLandmarks, float heightOverWidth, out FingerMask extended)
        {
            // Use the model's 3D hand shape so foreshortening has less effect on curl.
            bool useWorld = worldLandmarks?.Count == PointCount;
            Vector3 PosePoint(int index)
            {
                if (useWorld)
                    return Position(worldLandmarks[index]);
                return ImageDelta(_points[index], Vector3.zero, heightOverWidth);
            }

            var wrist = PosePoint((int)HandPoint.Wrist);
            bool holdingFist = Gesture == HandGesture.Fist;
            float straightnessLimit = holdingFist ? 0.83f : 0.74f;
            float forwardLimit = holdingFist ? 0.26f : 0.16f;
            var folded = FingerMask.None;
            extended = FingerMask.None;
            for (int finger = 0; finger < 4; finger++)
            {
                int knuckle = (int)HandPoint.IndexMcp + finger * 4;
                var root = PosePoint(knuckle);
                var firstJoint = PosePoint(knuckle + 1);
                var secondJoint = PosePoint(knuckle + 2);
                var tip = PosePoint(knuckle + 3);
                var length = Vector3.Distance(root, firstJoint) + Vector3.Distance(firstJoint, secondJoint) +
                    Vector3.Distance(secondJoint, tip);
                if (length <= 0.0001f)
                    continue;

                var reach = tip - root;
                var palmDirection = (root - wrist).normalized;
                bool curlsAtJoints = reach.magnitude <= straightnessLimit * length;
                bool foldsTowardPalm = Vector3.Dot(reach, palmDirection) <= forwardLimit * length;
                if (curlsAtJoints || foldsTowardPalm)
                    folded |= (FingerMask)(1 << finger);
                else if (reach.magnitude > 0.85f * length)
                    extended |= (FingerMask)(1 << finger);
            }

            return folded;
        }

        private float FingerExtension(HandPoint knuckle, float heightOverWidth)
        {
            var i = (int)knuckle;
            Vector3 first = ImageDelta(_points[i + 1], _points[i], heightOverWidth);
            Vector3 second = ImageDelta(_points[i + 2], _points[i + 1], heightOverWidth);
            Vector3 third = ImageDelta(_points[i + 3], _points[i + 2], heightOverWidth);
            float length = first.magnitude + second.magnitude + third.magnitude;
            return length > 0.0001f ? (first + second + third).magnitude / length : 0.75f;
        }

        // MediaPipe's relative Z uses roughly the same scale as normalized X.
        private static Vector3 ImageDelta(Vector3 a, Vector3 b, float heightOverWidth)
        {
            Vector3 delta = a - b;
            delta.y *= heightOverWidth;
            return delta;
        }
    }
}
