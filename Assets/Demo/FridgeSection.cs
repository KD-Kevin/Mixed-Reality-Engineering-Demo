using System;
using UnityEngine;

namespace EngineeringDemo
{
    public sealed class FridgeSection : MonoBehaviour
    {
        internal static readonly Color HoverColor = new Color(1f, 0.46f, 0.08f, 0.85f);
        private static readonly Color ExpandedColor = new Color(0.12f, 0.65f, 1f, 0.68f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");

        [SerializeField] private GameObject _closed;
        [SerializeField] private Renderer[] _selectors = Array.Empty<Renderer>();
        [SerializeField] private Vector3 _inspectionEuler = new Vector3(13f, -14f, 0f);
        [SerializeField] private GameObject _exploded;
        [SerializeField] private Transform _accessPanel;
        [SerializeField] private Part[] _parts;

        private Part _section;
        private Part _hovered;
        private MaterialPropertyBlock _color;

        internal Quaternion InspectionRotation => Quaternion.Euler(_inspectionEuler);

        [Serializable]
        internal sealed class Part
        {
            [SerializeField] private Transform _root;
            [SerializeField] private string _referenceId;
            [SerializeField] private Vector3 _installedPosition;
            [SerializeField] private Vector3 _installedEuler;

            private Renderer[] _renderers;
            private Vector3 _explodedPosition;
            private Quaternion _explodedRotation;
            private Quaternion _installedRotation;

            internal Part(Transform root, Renderer[] renderers)
            {
                _root = root;
                _renderers = renderers;
            }

            internal string Name => _root.name;
            internal Transform Root => _root;
            internal string ReferenceId => _referenceId;

            internal void Initialize()
            {
                _renderers = _root.GetComponentsInChildren<Renderer>(true);
                _explodedPosition = _root.localPosition;
                _explodedRotation = _root.localRotation;
                _installedRotation = Quaternion.Euler(_installedEuler);
            }

            internal void Spread(float blend)
            {
                _root.localPosition = Vector3.Lerp(_installedPosition, _explodedPosition, blend);
                _root.localRotation = Quaternion.Slerp(_installedRotation, _explodedRotation, blend);
            }

            internal float HitDistance(Ray ray)
            {
                float nearest = float.PositiveInfinity;
                foreach (var renderer in _renderers)
                    nearest = Mathf.Min(nearest, FridgeSection.HitDistance(renderer, ray));
                return nearest;
            }

            internal void Colorize(MaterialPropertyBlock properties, Color? tint)
            {
                if (tint is Color color)
                {
                    properties.SetColor(BaseColor, color);
                    properties.SetColor(LegacyColor, color);
                }

                foreach (var renderer in _renderers)
                    renderer.SetPropertyBlock(tint.HasValue ? properties : null);
            }
        }

        internal void Initialize()
        {
            _color = new MaterialPropertyBlock();
            _section = new Part(_closed.transform, _selectors);
            foreach (var part in _parts)
                part.Initialize();
            Show(false, true);
        }

        internal void Show(bool expanded, bool assembled)
        {
            SetHover(null, _exploded.activeSelf);
            _closed.SetActive(assembled);
            _exploded.SetActive(expanded);
            if (!expanded)
                return;

            foreach (var part in _parts)
                part.Colorize(_color, ExpandedColor);
            Spread(0f);
        }

        internal void Spread(float blend)
        {
            foreach (var part in _parts)
                part.Spread(blend);
        }

        internal Part FindSelector(Renderer renderer) =>
            Array.IndexOf(_selectors, renderer) >= 0 ? _section : null;

        internal bool IsAccessPanel(Part part) => _accessPanel != null && part.Root == _accessPanel;

        internal Part FindPartHit(Ray ray, out float nearest)
        {
            nearest = float.PositiveInfinity;

            Part hit = null;
            foreach (var part in _parts)
            {
                float distance = part.HitDistance(ray);
                if (distance >= nearest)
                    continue;

                nearest = distance;
                hit = part;
            }
            return hit;
        }

        internal void SetHover(Part part, bool expanded)
        {
            if (_hovered == part)
                return;

            _hovered?.Colorize(_color, expanded ? ExpandedColor : null);
            _hovered = part;
            _hovered?.Colorize(_color, HoverColor);
        }

        internal static float HitDistance(Renderer renderer, Ray ray)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return float.PositiveInfinity;

            var transform = renderer.transform;
            var localRay = new Ray(transform.InverseTransformPoint(ray.origin), transform.InverseTransformVector(ray.direction));
            if (!renderer.localBounds.IntersectRay(localRay, out float distance))
                return float.PositiveInfinity;

            var worldHit = transform.TransformPoint(localRay.GetPoint(distance));
            return Vector3.Dot(worldHit - ray.origin, ray.direction);
        }
    }
}
