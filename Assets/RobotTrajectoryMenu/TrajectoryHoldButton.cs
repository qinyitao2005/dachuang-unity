using UnityEngine;
using UnityEngine.EventSystems;

namespace RobotTeaching
{
    public class TrajectoryHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public RobotTrajectoryMenu menu;
        public int bindingIndex;
        public void OnPointerDown(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && menu != null)
                menu.SetPointerHeld(bindingIndex, true);
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) Release();
        }
        public void OnPointerExit(PointerEventData e) { Release(); }
        private void OnDisable() { Release(); }
        private void Release() { if (menu != null) menu.SetPointerHeld(bindingIndex, false); }
    }
}
