using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 继承四个接口，分别对应：鼠标进入、鼠标离开、鼠标按下、鼠标抬起
public class BtnEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("把红色的高亮图层拖到这里")]
    public GameObject redHighlight;

    private bool isHovering = false; // 鼠标是否悬停
    private bool isPressing = false; // 鼠标是否按下

    void Start()
    {
        // 游戏开始时，强制隐藏红色高亮，防止手动忘了关
        if (redHighlight != null)
        {
            redHighlight.SetActive(false);
        }
    }

    // 鼠标进入
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        UpdateHighlight();
    }

    // 鼠标离开
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        UpdateHighlight();
    }

    // 鼠标按下
    public void OnPointerDown(PointerEventData eventData)
    {
        isPressing = true;
        UpdateHighlight();
    }

    // 鼠标抬起
    public void OnPointerUp(PointerEventData eventData)
    {
        isPressing = false;
        UpdateHighlight();
    }

    // 统一刷新高亮状态
    void UpdateHighlight()
    {
        if (redHighlight != null)
        {
            // 只要鼠标在上面，或者正在按下，就显示红色
            redHighlight.SetActive(isHovering || isPressing);
        }
    }
}