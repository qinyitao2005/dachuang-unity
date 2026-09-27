using UnityEngine;

public class TeachingMenuDisplayController : MonoBehaviour
{
    [Header("教学菜单")]
    [Tooltip("把你已经创建好的教学菜单 Canvas 整体拖进来")]
    [SerializeField]
    private GameObject teachingMenuCanvas;


    [Header("显示菜单")]
    [Tooltip("公屏听到这句话时，显示教学菜单")]
    [SerializeField]
    private string showCommand = "显示教学菜单";

    [Tooltip("菜单成功显示后，向公屏回复这句话")]
    [SerializeField]
    private string showCompletedCommand = "教学菜单已显示";


    [Header("关闭菜单")]
    [Tooltip("公屏听到这句话时，关闭教学菜单")]
    [SerializeField]
    private string hideCommand = "关闭教学菜单";

    [Tooltip("菜单成功关闭后，向公屏回复这句话")]
    [SerializeField]
    private string hideCompletedCommand = "教学菜单已关闭";


    [Header("开局状态")]
    [Tooltip("开局是否自动隐藏教学菜单")]
    [SerializeField]
    private bool hideOnStart = true;


    [Header("调试")]
    [SerializeField]
    private bool printLogs = true;


    private bool subscribed = false;


    // =========================================================
    // Unity 生命周期
    // =========================================================

    private void Start()
    {
        if (hideOnStart && teachingMenuCanvas != null)
        {
            teachingMenuCanvas.SetActive(false);
        }

        TrySubscribe();
    }


    private void Update()
    {
        // 如果开局时公屏还没初始化好，
        // 后续继续尝试连接。
        if (!subscribed)
        {
            TrySubscribe();
        }
    }


    private void OnDisable()
    {
        Unsubscribe();
    }


    // =========================================================
    // 连接公屏
    // =========================================================

    private void TrySubscribe()
    {
        if (subscribed)
            return;

        if (公屏叫号.Instance == null)
            return;

        公屏叫号.Instance.叫号广播.AddListener(ReceivePublicCommand);

        subscribed = true;

        if (printLogs)
        {
            Debug.Log(
                "[TeachingMenuDisplayController] 已连接公屏。",
                this
            );
        }
    }


    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (公屏叫号.Instance != null)
        {
            公屏叫号.Instance.叫号广播.RemoveListener(
                ReceivePublicCommand
            );
        }

        subscribed = false;
    }


    // =========================================================
    // 接收公屏命令
    // =========================================================

    private void ReceivePublicCommand(string command)
    {
        // -------------------------
        // 显示菜单
        // -------------------------

        if (command == showCommand)
        {
            ShowTeachingMenu();
            return;
        }


        // -------------------------
        // 关闭菜单
        // -------------------------

        if (command == hideCommand)
        {
            HideTeachingMenu();
            return;
        }
    }


    // =========================================================
    // 显示菜单
    // =========================================================

    public void ShowTeachingMenu()
    {
        if (teachingMenuCanvas == null)
        {
            Debug.LogError(
                "[TeachingMenuDisplayController] 没有指定教学菜单 Canvas！",
                this
            );

            return;
        }


        teachingMenuCanvas.SetActive(true);


        if (printLogs)
        {
            Debug.Log(
                "[TeachingMenuDisplayController] 教学菜单已显示。",
                this
            );
        }


        // 回复公屏
        if (公屏叫号.Instance != null &&
            !string.IsNullOrWhiteSpace(showCompletedCommand))
        {
            if (printLogs)
            {
                Debug.Log(
                    "[TeachingMenuDisplayController] 回复："
                    + showCompletedCommand,
                    this
                );
            }

            公屏叫号.Instance.喊话(showCompletedCommand);
        }
    }


    // =========================================================
    // 关闭菜单
    // =========================================================

    public void HideTeachingMenu()
    {
        if (teachingMenuCanvas == null)
        {
            Debug.LogError(
                "[TeachingMenuDisplayController] 没有指定教学菜单 Canvas！",
                this
            );

            return;
        }


        teachingMenuCanvas.SetActive(false);


        if (printLogs)
        {
            Debug.Log(
                "[TeachingMenuDisplayController] 教学菜单已关闭。",
                this
            );
        }


        // 回复公屏
        if (公屏叫号.Instance != null &&
            !string.IsNullOrWhiteSpace(hideCompletedCommand))
        {
            if (printLogs)
            {
                Debug.Log(
                    "[TeachingMenuDisplayController] 回复："
                    + hideCompletedCommand,
                    this
                );
            }

            公屏叫号.Instance.喊话(hideCompletedCommand);
        }
    }


    // =========================================================
    // 兼容其他脚本 / UnityEvent 直接传入命令
    // =========================================================

    public void ReceiveSignal(string command)
    {
        ReceivePublicCommand(command);
    }
}