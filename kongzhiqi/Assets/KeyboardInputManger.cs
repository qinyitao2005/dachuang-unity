using UnityEngine;

public class KeyboardInputManager : MonoBehaviour
{
    [Header("物理按键映射")]
    public PhysicalButton keyUp;
    public PhysicalButton keyDown;
    public PhysicalButton keyLeft;
    public PhysicalButton keyRight;
    public PhysicalButton keyEnter;
    public PhysicalButton keyMenu; 
    public PhysicalButton keyShiftLeft;
    public PhysicalButton keyShiftRight;
    public PhysicalButton keyF1;
    public PhysicalButton keyF3;
    public PhysicalButton keyFWD;     // 新增：FWD按键
    public PhysicalButton keyDeadman; // 新增：deadman按键

    [Header("菜单总控")]
    public ScreenMenuNavigator menu1;
    public ScreenMenuNavigator menuIO;      // 拖入 Menu_I/O
    public ScreenMenuNavigator menuType;    // 拖入 Menu_Type
    public ScreenMenuNavigator bottomBar;   // 拖入 Bottom_Bar
    public ScreenMenuNavigator menuUCS;     // 拖入 Menu_UCS

    void Update()
    {
        // 1. 基础按键高亮
        HandleKey(KeyCode.UpArrow, KeyCode.W, keyUp);
        HandleKey(KeyCode.DownArrow, KeyCode.S, keyDown);
        HandleKey(KeyCode.LeftArrow, KeyCode.A, keyLeft);
        HandleKey(KeyCode.RightArrow, KeyCode.D, keyRight);
        HandleKey(KeyCode.Return, KeyCode.KeypadEnter, keyEnter);
        HandleKey(KeyCode.Tab, KeyCode.Tab, keyMenu); // MENU键
        HandleKey(KeyCode.LeftShift, KeyCode.LeftShift, keyShiftLeft);
        HandleKey(KeyCode.RightShift, KeyCode.RightShift, keyShiftRight);
        HandleKey(KeyCode.F1, KeyCode.F1, keyF1);
        HandleKey(KeyCode.F3, KeyCode.F3, keyF3);
        
        // Fn键通常被系统拦截，这里用 LeftAlt 代替 Fn 键，你可以改成 KeyCode.Space
        HandleKey(KeyCode.LeftAlt, KeyCode.LeftAlt, keyFWD); 
        // 键盘 Ctrl 键关联 deadman 键
        HandleKey(KeyCode.LeftControl, KeyCode.LeftControl, keyDeadman); 

        // 2. MENU键(Tab)开关主菜单
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (menu1 != null)
            {
                if (menu1.gameObject.activeSelf) menu1.DeactivateMenu();
                else menu1.ActivateMenu();
            }
        }

        // 3. 按下 F1 键：如果在 I/O 菜单内，弹出 Menu_Type
        if (Input.GetKeyDown(KeyCode.F1))
        {
            if (menuIO != null && menuIO.gameObject.activeSelf)
            {
                menuIO.OnPressF1();
            }
        }

        // 4. 按下 F3 键：如果在 Bottom_Bar 内，弹出 Menu_UCS
        if (Input.GetKeyDown(KeyCode.F3))
        {
            if (bottomBar != null && bottomBar.gameObject.activeSelf)
            {
                bottomBar.OnPressF3();
            }
        }

        // 5. 组合键检测：Shift + FWD(Alt) + deadman(Ctrl) -> 执行程序
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            if (Input.GetKey(KeyCode.LeftAlt) && Input.GetKey(KeyCode.LeftControl))
            {
                ExecuteProgram();
            }
        }
    }

    void HandleKey(KeyCode key1, KeyCode key2, PhysicalButton btn)
    {
        if (btn == null) return;
        if (Input.GetKeyDown(key1) || Input.GetKeyDown(key2)) btn.ShowHighlight();
        if (Input.GetKeyUp(key1) || Input.GetKeyUp(key2)) btn.HideHighlight();
    }

    // 执行程序的预留接口
    void ExecuteProgram()
    {
        // 防止在 Update 里每帧打印，用标志位或者判断鼠标按下
        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.LeftControl))
        {
            Debug.Log("【组合键达成】正在执行选中的程序...（此处留出接口供后续对接）");
            // 【预留接口】：在这里写执行程序的代码
        }
    }
}