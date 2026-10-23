using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class EnterKeySubmit : MonoBehaviour
{
    // 记录当前鼠标悬停的按钮
    private Button currentHoveredButton;

    void Update()
    {
        // 如果按下了 Enter 键，并且当前有选中的按钮
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (currentHoveredButton != null)
            {
                currentHoveredButton.onClick.Invoke(); // 手动触发按钮的点击事件
                currentHoveredButton = null; // 触发后清空，防止重复
            }
        }
    }

    // 鼠标进入按钮时调用
    public void OnPointerEnterButton(Button btn)
    {
        currentHoveredButton = btn;
        Debug.Log("选中了按钮：" + btn.name);
    }

    // 鼠标离开按钮时调用
    public void OnPointerExitButton(Button btn)
    {
        if (currentHoveredButton == btn)
        {
            currentHoveredButton = null;
        }
    }
}