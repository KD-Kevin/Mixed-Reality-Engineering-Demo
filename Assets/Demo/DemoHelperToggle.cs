using UnityEngine;

namespace EngineeringDemo
{
    [RequireComponent(typeof(UnityEngine.UI.Button))]
    public sealed class DemoHelperToggle : MonoBehaviour
    {
        [SerializeField] private GameObject _helperPanel;
        [SerializeField] private UnityEngine.UI.Text _buttonLabel;
        [SerializeField] private bool _hideOnStart = true;

        private UnityEngine.UI.Button _button;

        private void Awake()
        {
            _button = GetComponent<UnityEngine.UI.Button>();
            SetHelperVisible(!_hideOnStart);
        }

        private void OnEnable() => _button.onClick.AddListener(ToggleHelper);
        private void OnDisable() => _button.onClick.RemoveListener(ToggleHelper);

        private void ToggleHelper()
        {
            if (_helperPanel != null)
                SetHelperVisible(!_helperPanel.activeSelf);
        }

        private void SetHelperVisible(bool visible)
        {
            if (_helperPanel != null)
                _helperPanel.SetActive(visible);

            if (_buttonLabel != null)
                _buttonLabel.text = visible ? "Hide Helper" : "Show Helper";
        }
    }
}
