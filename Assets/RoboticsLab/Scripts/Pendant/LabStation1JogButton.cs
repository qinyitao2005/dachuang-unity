using UnityEngine;
using UnityEngine.EventSystems;

namespace RoboticsLab
{
    public sealed class LabStation1JogButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public LabStation1Programming programming;
        public Vector3 direction;
        public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left && programming) programming.SetJog(direction); }
        public void OnPointerUp(PointerEventData data) { if (programming) programming.SetJog(Vector3.zero); }
        public void OnPointerExit(PointerEventData data) { if (programming) programming.SetJog(Vector3.zero); }
        void OnDisable() { if (programming) programming.SetJog(Vector3.zero); }
    }
}
