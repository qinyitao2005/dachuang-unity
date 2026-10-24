using UnityEngine;
using UnityEngine.EventSystems;

namespace RoboticsLab
{
    /// <summary>Pointer lifetime only; robot motion stays with the existing station adapter.</summary>
    public sealed class LabPendantJogButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public LabPendantController controller;
        public Vector3 localDirection;
        public void OnPointerDown(PointerEventData data){if(controller)controller.SetJog(localDirection);}
        public void OnPointerUp(PointerEventData data){if(controller)controller.SetJog(Vector3.zero);}
        public void OnPointerExit(PointerEventData data){if(controller)controller.SetJog(Vector3.zero);}
        void OnDisable(){if(controller)controller.SetJog(Vector3.zero);}
    }
}
