using System.Collections.Generic;
using UnityEngine;

namespace EngineeringDemo
{
    [DisallowMultipleComponent]
    public sealed class HandDemoVisuals : MonoBehaviour
    {
        [SerializeField] private Material _lineMaterial;
        [SerializeField] private HandVisualSettings _settings;

        private static readonly int[][] FingerChains =
        {
            new[] { 0, 1, 2, 3, 4 },
            new[] { 0, 5, 6, 7, 8 },
            new[] { 0, 9, 10, 11, 12 },
            new[] { 0, 13, 14, 15, 16 },
            new[] { 0, 17, 18, 19, 20 }
        };

        private sealed class HandVisual
        {
            internal readonly LineRenderer[] Lines = new LineRenderer[FingerChains.Length];
            internal readonly Transform[] Dots = new Transform[Hand.PointCount];
            internal readonly Renderer[] DotRenderers = new Renderer[Hand.PointCount];
            internal Color Color;
        }

        private readonly List<HandVisual> _hands = new();
        private MaterialPropertyBlock _visualColor;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");

        internal float RayTipWidth => _settings.RayTipWidth;

        private void Awake() => _visualColor = new MaterialPropertyBlock();

        // Keep one set of renderers per hand slot and reuse them as hands enter and leave the frame.
        internal void RenderHands(HandManager tracking, Camera camera, PistolTrigger trigger)
        {
            var hands = tracking.Hands;
            while (_hands.Count < hands.Count)
                CreateHandVisual();

            for (var i = 0; i < _hands.Count; i++)
            {
                var visible = i < hands.Count;
                var lines = _hands[i].Lines;
                var dots = _hands[i].Dots;
                for (int line = 0; line < lines.Length; line++)
                    lines[line].gameObject.SetActive(visible);
                if (!visible)
                {
                    for (int point = 0; point < dots.Length; point++)
                        dots[point].gameObject.SetActive(false);
                    continue;
                }

                var hand = hands[i];
                ApplyHandColor(_hands[i], GetHandColor(hand, trigger));
                for (var point = 0; point < Hand.PointCount; point++)
                {
                    var valid = tracking.TryGetWorldPoint(camera, hand, (HandPoint)point, out var position);
                    dots[point].gameObject.SetActive(valid);
                    dots[point].position = position;
                }
                for (var finger = 0; finger < FingerChains.Length; finger++)
                {
                    var chain = FingerChains[finger];
                    for (var joint = 0; joint < chain.Length; joint++)
                        lines[finger].SetPosition(joint, dots[chain[joint]].position);
                }
            }
        }

        private void CreateHandVisual()
        {
            var hand = new HandVisual { Color = _settings.NeutralColor };
            for (int i = 0; i < hand.Lines.Length; i++)
            {
                var line = CreateLine("Finger " + i, hand.Color, 0.003f);
                line.positionCount = FingerChains[i].Length;
                hand.Lines[i] = line;
            }
            for (int i = 0; i < hand.Dots.Length; i++)
            {
                var dot = CreateDot(((HandPoint)i).ToString(), hand.Color);
                hand.Dots[i] = dot;
                hand.DotRenderers[i] = dot.GetComponent<Renderer>();
            }
            _hands.Add(hand);
        }

        private Color GetHandColor(Hand hand, PistolTrigger trigger)
        {
            var color = hand.Gesture switch
            {
                HandGesture.OpenHand => _settings.OpenHandColor,
                HandGesture.Fist => _settings.FistColor,
                HandGesture.Pinch => _settings.PinchColor,
                HandGesture.Point => _settings.PointColor,
                HandGesture.Pistol => _settings.PistolColor,
                HandGesture.MiddleFinger => _settings.MiddleFingerColor,
                HandGesture.ThreeFingers => _settings.ThreeFingersColor,
                HandGesture.ThumbsUp => _settings.ThumbsUpColor,
                HandGesture.ThumbsDown => _settings.ThumbsDownColor,
                _ => _settings.NeutralColor
            };

            if (hand.Gesture == HandGesture.Pistol && ReferenceEquals(hand, trigger?.Hand))
            {
                if (trigger.State == PistolTrigger.TriggerState.Ready && _settings.UsePistolReadyColor)
                    color = _settings.PistolReadyColor;
                else if (trigger.State == PistolTrigger.TriggerState.Fired && _settings.UsePistolFiredColor)
                    color = _settings.PistolFiredColor;
            }

            if (hand.Side != HandSide.Left || _settings.LeftHandHue == 0f)
                return color;

            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            var shifted = Color.HSVToRGB(Mathf.Repeat(hue + _settings.LeftHandHue / 360f, 1f), saturation, value);
            shifted.a = color.a;
            return shifted;
        }

        private void ApplyHandColor(HandVisual hand, Color color)
        {
            if (hand.Color == color)
                return;

            var lines = hand.Lines;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                line.startColor = line.endColor = color;
                SetVisualColor(line, color);
            }
            var renderers = hand.DotRenderers;
            for (int i = 0; i < renderers.Length; i++)
                SetVisualColor(renderers[i], color);

            hand.Color = color;
        }

        internal void SetVisualColor(Renderer renderer, Color color)
        {
            _visualColor.SetColor(BaseColor, color);
            _visualColor.SetColor(LegacyColor, color);
            renderer.SetPropertyBlock(_visualColor);
        }

        internal LineRenderer CreateLine(string label, Color color, float width)
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

        internal Transform CreateDot(string label, Color color)
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
