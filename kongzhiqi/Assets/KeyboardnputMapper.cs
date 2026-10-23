using UnityEngine;
using UnityEngine.UI;

public class PhysicalButton : MonoBehaviour
{
    [Header("高亮图片（红色图）")]
    public GameObject highlightObj; 

    // 显示高亮
    public void ShowHighlight()
    {
        if (highlightObj != null) highlightObj.SetActive(true);
    }

    // 隐藏高亮
    public void HideHighlight()
    {
        if (highlightObj != null) highlightObj.SetActive(false);
    }
}