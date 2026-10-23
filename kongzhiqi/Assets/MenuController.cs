using UnityEngine;

public class MenuController : MonoBehaviour
{
    public GameObject menu1; 
    public GameObject ioMenu1; 
    public GameObject ioMenu2; 

    // Start 会在游戏运行的第一帧自动执行
    void Start()
    {
        // 强制初始化状态：只有主菜单亮着，其他的都关掉！
        menu1.SetActive(true);
        ioMenu1.SetActive(false);
        ioMenu2.SetActive(false);
    }

    public void OpenIOMenu()
    {
        ioMenu1.SetActive(true); 
        ioMenu2.SetActive(false); 
    }

    public void OpenRobotMenu()
    {
        ioMenu2.SetActive(true); 
    }
}