using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class ScreenMenuNavigator : MonoBehaviour
{
    [System.Serializable]
    public class MenuOption
    {
        public GameObject highlightObj; // 高亮图片
    }

    [Header("菜单选项列表")]
    public List<MenuOption> options;

    [Header("子菜单面板")]
    public GameObject menu_IO;      // 对应 5 I/O
    public GameObject menu_Setting; // 对应 6 设置

    [Header("按 Enter 打开的子菜单 (用于其他菜单)")]
    public GameObject menuOnEnter;

    [Header("按 F1 打开的子菜单 (用于 Menu_IO)")]
    public GameObject menuOnF1;

    [Header("按 F3 打开的子菜单 (用于 Bottom_Bar)")]
    public GameObject menuOnF3;

    [Header("按 Enter 打开的子菜单 (用于 Menu_Setting 的第5项)")]
    public GameObject menuOnSettingIndex4;

    private int currentIndex = 0;
    private bool isActive = false;

    void Update()
    {
        if (!isActive) return;

        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
        {
            currentIndex--;
            if (currentIndex < 0) currentIndex = options.Count - 1;
            UpdateHighlight();
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
        {
            currentIndex++;
            if (currentIndex >= options.Count) currentIndex = 0;
            UpdateHighlight();
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            ConfirmSelection();
        }
    }

    public void ActivateMenu()
    {
        isActive = true;
        gameObject.SetActive(true);
        currentIndex = 0;
        UpdateHighlight();
        Debug.Log(gameObject.name + " 菜单已激活！");
    }

    public void DeactivateMenu()
    {
        isActive = false;
        gameObject.SetActive(false);
    }

    void UpdateHighlight()
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].highlightObj != null)
                options[i].highlightObj.SetActive(i == currentIndex);
        }
    }

    void ConfirmSelection()
    {
        Debug.Log("【调试】在 " + gameObject.name + " 按下了Enter，当前选中的索引是：" + currentIndex);

        // 1. 专门处理 Menu1 的逻辑
        if (gameObject.name == "Menu1" || gameObject.name == "Menu 1")
        {
            if (currentIndex == 4) // 索引4是第5项 (5 I/O)
            {
                if (menu_IO != null)
                {
                    DeactivateMenu();
                    menu_IO.GetComponent<ScreenMenuNavigator>().ActivateMenu();
                    return;
                }
                else Debug.LogError("Menu1 里的 Menu IO 槽位没拖！");
            }
            else if (currentIndex == 5) // 索引5是第6项 (6 设置)
            {
                if (menu_Setting != null)
                {
                    DeactivateMenu();
                    menu_Setting.GetComponent<ScreenMenuNavigator>().ActivateMenu();
                    return;
                }
                else Debug.LogError("Menu1 里的 Menu Setting 槽位没拖！");
            }
        }

        //  2. 专门处理 Menu_Setting 的逻辑
        if (gameObject.name == "Menu_Setting" && currentIndex == 4)
        {
            if (menuOnSettingIndex4 != null)
            {
                DeactivateMenu();
                menuOnSettingIndex4.GetComponent<ScreenMenuNavigator>().ActivateMenu();
            }
            return;
        }

        // 3. 专门处理 Menu_Type 的逻辑
        if (gameObject.name == "Menu_Type" && currentIndex == 0)
        {
            Debug.Log("已选中预设程序，当前处于待执行状态。");
            return;
        }

        // 4. 其他菜单通用的按 Enter 打开逻辑
        if (menuOnEnter != null)
        {
            DeactivateMenu();
            menuOnEnter.GetComponent<ScreenMenuNavigator>().ActivateMenu();
        }
    }

    public void OnPressF1()
    {
        if (menuOnF1 != null)
        {
            DeactivateMenu();
            menuOnF1.GetComponent<ScreenMenuNavigator>().ActivateMenu();
        }
    }

    public void OnPressF3()
    {
        if (menuOnF3 != null)
        {
            DeactivateMenu();
            menuOnF3.GetComponent<ScreenMenuNavigator>().ActivateMenu();
        }
    }
}