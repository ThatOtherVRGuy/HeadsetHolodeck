using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SpeechIntent
{
    /// <summary>Applies the shared transform grid after XRI updates a grabbed object.</summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class SnapWhileGrabbed : MonoBehaviour
    {
        XRGrabInteractable _grab;
        WorldManipulationController _settings;
        bool _selected;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
        }

        void OnEnable()
        {
            if (_grab == null)
                _grab = GetComponent<XRGrabInteractable>();
            _grab.selectEntered.AddListener(HandleSelectEntered);
            _grab.selectExited.AddListener(HandleSelectExited);
        }

        void OnDisable()
        {
            if (_grab == null)
                return;
            _grab.selectEntered.RemoveListener(HandleSelectEntered);
            _grab.selectExited.RemoveListener(HandleSelectExited);
            _selected = false;
        }

        void LateUpdate()
        {
            if (!_selected)
                return;
            if (_settings == null)
                _settings = FindFirstObjectByType<WorldManipulationController>(FindObjectsInactive.Include);
            if (_settings == null)
                return;

            transform.position = WorldManipulationController.SnapPosition(transform.position, _settings.PositionSnapMeters);
            transform.rotation = WorldManipulationController.SnapRotation(transform.rotation, _settings.AngleSnapDegrees);
        }

        void HandleSelectEntered(SelectEnterEventArgs _) => _selected = true;
        void HandleSelectExited(SelectExitEventArgs _) => _selected = false;
    }
}
