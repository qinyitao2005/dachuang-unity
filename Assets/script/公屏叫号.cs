using UnityEngine;
using UnityEngine.Events;

[DefaultExecutionOrder(-900)]
public class 公屏叫号 : MonoBehaviour
{
    public static 公屏叫号 Instance { get; private set; }

    [Header("开局设置")]
    [Tooltip("开局时自动喊的话。比如：输入给相机。")]
    public string 开局叫号 = "输入给相机";

    [Tooltip("是否在 Start() 时自动喊一句开局叫号。")]
    public bool 开局自动叫号 = true;

    [Header("当前状态")]
    [SerializeField] private string 当前叫号 = "";

    [Header("调试")]
    public bool 打印日志 = true;

    [Header("公屏事件")]
    [Tooltip("每次叫号时都会广播当前叫号内容。")]
    public UnityEvent<string> 叫号广播 = new UnityEvent<string>();

    public string 当前频道
    {
        get { return 当前叫号; }
    }

    public bool 有当前叫号
    {
        get { return !string.IsNullOrEmpty(当前叫号); }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[公屏叫号] 场景里出现了多个公屏叫号器，已销毁重复对象。");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (开局自动叫号)
        {
            喊话(开局叫号);
        }
    }

    /// <summary>
    /// 核心入口：向公屏喊一句任意内容。
    /// </summary>
    public void 喊话(string 内容)
    {
        if (string.IsNullOrWhiteSpace(内容))
        {
            Debug.LogWarning("[公屏叫号] 收到空叫号，已忽略。");
            return;
        }

        当前叫号 = 内容.Trim();

        if (打印日志)
        {
            Debug.Log("[公屏叫号] " + 当前叫号);
        }

        叫号广播.Invoke(当前叫号);
    }

    /// <summary>
    /// 兼容你之前 SequenceSignalController 那种 ReceiveSignal(string) 写法。
    /// </summary>
    public void ReceiveSignal(string command)
    {
        喊话(command);
    }

    /// <summary>
    /// 判断当前公屏叫号是不是某一句。
    /// </summary>
    public bool 当前是(string 目标叫号)
    {
        return 当前叫号 == 目标叫号;
    }

    /// <summary>
    /// 判断当前公屏叫号不是某一句。
    /// </summary>
    public bool 当前不是(string 目标叫号)
    {
        return 当前叫号 != 目标叫号;
    }

    /// <summary>
    /// 清空当前叫号。
    /// </summary>
    public void 清空()
    {
        当前叫号 = "";

        if (打印日志)
        {
            Debug.Log("[公屏叫号] 当前叫号已清空");
        }

        叫号广播.Invoke(当前叫号);
    }
}