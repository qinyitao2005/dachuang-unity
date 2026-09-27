using UnityEngine;
using UnityEngine.EventSystems;

public class HoldableButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public bool IsHeld { get; private set; }
    public armProperty arm;

    public void OnPointerDown(PointerEventData eventData)
    {
        IsHeld = true;
        arm.armIsMoving = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        IsHeld = false;
        arm.armIsMoving = false;
    }

    private void OnDisable()
    {
        IsHeld = false;
        arm.armIsMoving = false;
    }
}
