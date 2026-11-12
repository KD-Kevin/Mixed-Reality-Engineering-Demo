using UnityEngine;

namespace EngineeringDemo
{
    [DisallowMultipleComponent]
    public sealed class FridgeHologram : MonoBehaviour
    {
        [SerializeField] private GameObject _modelPrefab;
        [SerializeField, Min(0.1f)] private float _modelHeight = 1.68f;
        [SerializeField, Min(0.1f)] private float _appearanceDistance = 2.5f;
        [SerializeField, Min(0.1f)] private float _inspectionDistance = 1.6f;
        [SerializeField, Range(0.1f, 0.9f)] private float _inspectionHeight = 0.54f;
        [SerializeField, Range(-60f, 60f)] private float _inspectionYaw = -22f;
        [SerializeField, Min(0.01f)] private float _transitionSeconds = 0.75f;

        private Transform _model;
        private Renderer[] _modelRenderers;
        private FridgeSection[] _sections;
        private FridgeSection _openSection;
        private FridgeSection _hoveredSection;
        private Quaternion _assembledRotation;
        private float _spreadTime;
        private bool _selected;
        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private Vector3 _startScale;
        private float _elapsed;
        private float _inspectionZoom = 1f;

        public bool IsSelected => IsVisible && _selected;
        public bool IsInspecting => IsSelected && CanRotate;
        internal bool IsVisible => _model != null && _model.gameObject.activeSelf;
        internal bool CanRotate => IsVisible && _elapsed >= _transitionSeconds;
        internal Quaternion InspectionRotation { get; set; }

        internal void ZoomInspection(float factor) => _inspectionZoom = Mathf.Clamp(_inspectionZoom * factor, 0.6f, 1.8f);

        internal string ExpandedSectionName => _openSection != null ? _openSection.name : null;

        internal readonly struct SectionHit
        {
            internal readonly FridgeSection Section;
            internal readonly FridgeSection.Part Part;
            internal readonly Vector3 Point;

            internal SectionHit(FridgeSection section, FridgeSection.Part part, Vector3 point)
            {
                Section = section;
                Part = part;
                Point = point;
            }
        }

        internal SectionHit FindSectionHit(Ray ray) => FindSectionHit(ray, out _);

        internal SectionHit FindSectionHit(Ray ray, out bool hitsModel)
        {
            hitsModel = false;
            if (!CanRotate)
                return default;

            if (_openSection != null)
            {
                var part = _openSection.FindPartHit(ray, out float distance);
                hitsModel = part != null;
                return hitsModel ? new SectionHit(_openSection, part, ray.GetPoint(distance)) : default;
            }

            var surface = FindClosestRenderer(ray, out float nearest);
            hitsModel = surface != null;
            if (!hitsModel)
                return default;

            // The nearest wall blocks selection of a door or cover behind it.
            foreach (var section in _sections)
            {
                var selector = section.FindSelector(surface);
                if (selector != null)
                    return new SectionHit(section, selector, ray.GetPoint(nearest));
            }

            return default;
        }

        internal bool IsUnderPointer(Camera camera, Vector2 screenPosition) =>
            CanRotate && FindClosestRenderer(camera.ScreenPointToRay(screenPosition), out _) != null;

        private Renderer FindClosestRenderer(Ray ray, out float nearest)
        {
            Renderer closest = null;
            nearest = float.PositiveInfinity;
            foreach (var renderer in _modelRenderers)
            {
                float distance = FridgeSection.HitDistance(renderer, ray);
                if (distance >= nearest)
                    continue;

                nearest = distance;
                closest = renderer;
            }

            return closest;
        }

        internal void ResetRotation()
        {
            InspectionRotation = _openSection != null ? _openSection.InspectionRotation :
                Quaternion.Euler(0f, _inspectionYaw, 0f);
        }

        internal void SetSectionHover(SectionHit hit)
        {
            if (_hoveredSection != hit.Section)
                _hoveredSection?.SetHover(null, _openSection != null);

            _hoveredSection = hit.Section;
            _hoveredSection?.SetHover(hit.Part, _openSection != null);
        }

        internal bool TryActivateSection(SectionHit hit, string source)
        {
            if (!CanRotate || hit.Section == null)
                return false;

            if (_openSection != null)
            {
                if (hit.Section != _openSection || !_openSection.IsAccessPanel(hit.Part))
                    return false;

                TryCollapseSection();
                Debug.Log($"[FridgeDemo] {hit.Section.name} closed by {source}.", this);
                return true;
            }

            SetSectionHover(default);
            _openSection = hit.Section;
            _assembledRotation = InspectionRotation;
            ResetRotation();
            _spreadTime = 0f;
            foreach (var section in _sections)
                section.Show(section == _openSection, false);

            Debug.Log($"[FridgeDemo] {hit.Section.name} opened by {source}.", this);
            return true;
        }

        internal bool TryCollapseSection()
        {
            if (_openSection == null)
                return false;

            SetSectionHover(default);
            _openSection = null;
            InspectionRotation = _assembledRotation;
            foreach (var section in _sections)
                section.Show(false, true);
            return true;
        }

        public bool TryShowTest(Camera camera)
        {
            if (camera == null || !EnsureModel())
                return false;

            _elapsed = _transitionSeconds;
            UpdateInspection(camera, 0f);
            return true;
        }

        public bool TryClose()
        {
            if (!IsVisible)
                return false;

            TryCollapseSection();
            SetSectionHover(default);
            _model.gameObject.SetActive(false);
            _selected = false;
            _inspectionZoom = 1f;
            InspectionRotation = Quaternion.Euler(0f, _inspectionYaw, 0f);
            return true;
        }

        public bool TrySelect(Camera camera, Rect imageViewport, Rect box)
        {
            if (IsSelected || camera == null || box.width <= 0f || box.height <= 0f || !EnsureModel())
                return false;

            var center = new Vector2(imageViewport.xMin + box.center.x * imageViewport.width,
                imageViewport.yMax - box.center.y * imageViewport.height);
            float depth = Mathf.Max(camera.nearClipPlane + 0.1f, _appearanceDistance);
            float viewportHeight = Mathf.Clamp(imageViewport.height * box.height, 0.05f, 0.85f);
            _startPosition = camera.ViewportToWorldPoint(new Vector3(center.x, center.y, depth));
            _startRotation = camera.transform.rotation;
            _startScale = ScaleToHeight(camera, center, viewportHeight, depth);

            _model.SetPositionAndRotation(_startPosition, _startRotation);
            _model.localScale = _startScale;
            _elapsed = 0f;
            _selected = true;
            _inspectionZoom = 1f;
            return true;
        }

        private bool EnsureModel()
        {
            if (_model == null && _modelPrefab != null)
            {
                _model = Instantiate(_modelPrefab, transform).transform;
                _sections = _model.GetComponentsInChildren<FridgeSection>(true);
                _modelRenderers = _model.GetComponentsInChildren<Renderer>(true);
                foreach (var section in _sections)
                    section.Initialize();
                InspectionRotation = Quaternion.Euler(0f, _inspectionYaw, 0f);
                _selected = false;
            }

            if (_model == null)
                return false;

            _model.gameObject.SetActive(true);
            return true;
        }

        public void UpdateInspection(Camera camera, float deltaTime)
        {
            if (!IsVisible || camera == null)
                return;

            _elapsed += deltaTime;
            float depth = Mathf.Max(camera.nearClipPlane + 0.1f, _inspectionDistance);
            var center = new Vector2(0.5f, 0.5f);
            var position = camera.ViewportToWorldPoint(new Vector3(center.x, center.y, depth));
            var rotation = camera.transform.rotation * InspectionRotation;
            float height = _inspectionHeight * (_openSection != null ? 1.15f : 1f);
            float layoutWidth = _openSection != null ? 1.58f : 1.15f;
            var left = camera.ViewportToWorldPoint(new Vector3(0.08f, 0.5f, depth));
            var right = camera.ViewportToWorldPoint(new Vector3(0.92f, 0.5f, depth));
            float fit = Mathf.Min(ScaleToHeight(camera, center, height, depth).x,
                Vector3.Distance(left, right) / layoutWidth);
            var scale = Vector3.one * (fit * _inspectionZoom);
            if (_openSection != null)
            {
                _spreadTime += deltaTime;
                _openSection.Spread(Mathf.SmoothStep(0f, 1f, _spreadTime / 0.3f));
            }
            if (_elapsed < _transitionSeconds)
            {
                float blend = Mathf.SmoothStep(0f, 1f, _elapsed / _transitionSeconds);
                position = Vector3.Lerp(_startPosition, position, blend);
                rotation = Quaternion.Slerp(_startRotation, rotation, blend);
                scale = Vector3.Lerp(_startScale, scale, blend);
            }

            _model.SetPositionAndRotation(position, rotation);
            _model.localScale = scale;
        }

        private Vector3 ScaleToHeight(Camera camera, Vector2 center, float viewportHeight, float depth)
        {
            float halfHeight = viewportHeight * 0.5f;
            var bottom = camera.ViewportToWorldPoint(new Vector3(center.x, center.y - halfHeight, depth));
            var top = camera.ViewportToWorldPoint(new Vector3(center.x, center.y + halfHeight, depth));
            return Vector3.one * (Vector3.Distance(bottom, top) / _modelHeight);
        }
    }
}
