using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;

public class 轨迹速度设置控制器 : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("整个速度输入界面的根物体，例如一个 Panel")]
    [SerializeField]
    private GameObject 速度设置界面;

    [Tooltip("输入速度的 TMP_InputField")]
    [SerializeField]
    private TMP_InputField 速度输入框;

    [Tooltip("显示说明和错误信息，可不填写")]
    [SerializeField]
    private TMP_Text 提示文字;


    [Header("公屏命令")]
    [Tooltip("收到这句话后打开速度设置")]
    [SerializeField]
    private string 启动命令 = "开始设置轨迹速度";

    [Tooltip("速度设置成功后回复这句话")]
    [SerializeField]
    private string 完成命令 = "轨迹速度设置完成";


    [Header("速度设置")]
    [Tooltip("允许输入的最小速度")]
    [Min(0.0001f)]
    [SerializeField]
    private float 最小速度 = 0.01f;

    [Tooltip("允许输入的最大速度")]
    [Min(0.0001f)]
    [SerializeField]
    private float 最大速度 = 2.0f;

    [Tooltip("还没手动设置之前使用的默认速度")]
    [Min(0.0001f)]
    [SerializeField]
    private float 默认速度 = 0.2f;


    // =========================
    // 给其他模块读取
    // =========================

    public float CurrentSpeed { get; private set; }

    public bool HasConfirmedSpeed { get; private set; }


    private bool 已连接公屏 = false;
    private bool 使用菜单调速;
    private bool 待回复速度完成;
    private int 菜单速度档位 = 2;

    public void 启用菜单调速()
    {
        使用菜单调速 = true;
        菜单速度档位 = 2;
        CurrentSpeed = 0.1f;
        HasConfirmedSpeed = true;
        StopAllCoroutines();
        if (速度设置界面 != null) 速度设置界面.SetActive(false);
    }

    public void 调整菜单速度(int direction)
    {
        if (!使用菜单调速) return;
        菜单速度档位 = Mathf.Clamp(菜单速度档位 + System.Math.Sign(direction), 0, 10);
        CurrentSpeed = 菜单速度档位 * 0.05f;
    }


    private void Awake()
    {
        CurrentSpeed = Mathf.Clamp(
            默认速度,
            最小速度,
            最大速度
        );

        HasConfirmedSpeed = false;

        if (速度设置界面 != null)
        {
            速度设置界面.SetActive(false);
        }
    }


    private void Start()
    {
        if (速度输入框 != null)
        {
            速度输入框.onSubmit.AddListener(
                提交速度
            );
        }

        尝试连接公屏();
    }


    private void Update()
    {
        // 如果场景初始化顺序导致 Start 时公屏还没出现，
        // 后面继续尝试连接。
        if (!已连接公屏)
        {
            尝试连接公屏();
        }
        // 延迟回复，避免在开始命令的广播中嵌套覆盖公屏状态。
        if (待回复速度完成 && 公屏叫号.Instance != null)
        {
            待回复速度完成 = false;
            公屏叫号.Instance.喊话(完成命令);
        }
    }


    private void OnDestroy()
    {
        if (速度输入框 != null)
        {
            速度输入框.onSubmit.RemoveListener(
                提交速度
            );
        }

        if (已连接公屏 &&
            公屏叫号.Instance != null)
        {
            公屏叫号.Instance.叫号广播.RemoveListener(
                接收公屏命令
            );
        }
    }


    // =========================
    // 公屏
    // =========================

    private void 尝试连接公屏()
    {
        if (已连接公屏)
            return;

        if (公屏叫号.Instance == null)
            return;

        公屏叫号.Instance.叫号广播.AddListener(
            接收公屏命令
        );

        已连接公屏 = true;

        Debug.Log(
            "[轨迹速度设置] 已连接公屏。",
            this
        );
    }


    private void 接收公屏命令(string 命令)
    {
        if (命令 == 启动命令)
        {
            打开速度设置();
        }
    }


    // =========================
    // 打开界面
    // =========================

    public void 打开速度设置()
    {
        if (使用菜单调速)
        {
            if (速度设置界面 != null) 速度设置界面.SetActive(false);
            待回复速度完成 = true;
            return;
        }
        if (速度设置界面 == null)
        {
            Debug.LogError(
                "[轨迹速度设置] 没有填写速度设置界面。",
                this
            );
            return;
        }

        if (速度输入框 == null)
        {
            Debug.LogError(
                "[轨迹速度设置] 没有填写速度输入框。",
                this
            );
            return;
        }

        速度设置界面.SetActive(true);

        速度输入框.text = "";

        if (提示文字 != null)
        {
            提示文字.text =
                "请输入轨迹速度\n" +
                "范围：" +
                最小速度 +
                " ~ " +
                最大速度;
        }

        StartCoroutine(
            下一帧聚焦输入框()
        );

        Debug.Log(
            "[轨迹速度设置] 等待玩家输入速度。",
            this
        );
    }


    private IEnumerator 下一帧聚焦输入框()
    {
        yield return null;

        速度输入框.Select();
        速度输入框.ActivateInputField();
    }


    // =========================
    // 提交速度
    // =========================

    private void 提交速度(string 输入内容)
    {
        if (使用菜单调速) return;
        if (!尝试解析速度(
            输入内容,
            out float 新速度))
        {
            显示错误(
                "请输入有效数字，例如 0.2"
            );
            return;
        }

        if (新速度 <= 0f)
        {
            显示错误(
                "速度必须大于 0"
            );
            return;
        }

        if (新速度 < 最小速度 ||
            新速度 > 最大速度)
        {
            显示错误(
                "速度必须在 " +
                最小速度 +
                " ~ " +
                最大速度 +
                " 之间"
            );
            return;
        }

        CurrentSpeed = 新速度;
        HasConfirmedSpeed = true;

        Debug.Log(
            "[轨迹速度设置] 设置完成，CurrentSpeed = "
            + CurrentSpeed,
            this
        );

        if (速度设置界面 != null)
        {
            速度设置界面.SetActive(false);
        }

        if (公屏叫号.Instance != null)
        {
            公屏叫号.Instance.喊话(
                完成命令
            );
        }
    }


    private bool 尝试解析速度(
        string 输入内容,
        out float 速度)
    {
        输入内容 = 输入内容.Trim();

        // 支持 0.2
        if (float.TryParse(
            输入内容,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out 速度))
        {
            return
                !float.IsNaN(速度) &&
                !float.IsInfinity(速度);
        }

        // 兼容本机区域格式
        if (float.TryParse(
            输入内容,
            NumberStyles.Float,
            CultureInfo.CurrentCulture,
            out 速度))
        {
            return
                !float.IsNaN(速度) &&
                !float.IsInfinity(速度);
        }

        速度 = 0f;
        return false;
    }


    private void 显示错误(
        string 错误内容)
    {
        Debug.LogWarning(
            "[轨迹速度设置] " + 错误内容,
            this
        );

        if (提示文字 != null)
        {
            提示文字.text = 错误内容;
        }

        StartCoroutine(
            下一帧聚焦输入框()
        );
    }


    private void OnValidate()
    {
        最小速度 =
            Mathf.Max(
                0.0001f,
                最小速度
            );

        if (最大速度 < 最小速度)
        {
            最大速度 = 最小速度;
        }

        默认速度 =
            Mathf.Clamp(
                默认速度,
                最小速度,
                最大速度
            );
    }
}
