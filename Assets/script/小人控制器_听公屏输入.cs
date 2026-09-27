using UnityEngine;

public class 小人控制器_听公屏输入 : MonoBehaviour
{
    [Header("公屏输入频道")]
    [Tooltip("只有公屏当前叫号等于这句话时，小人才会吃输入。")]
    public string 我的输入叫号 = "输入给小人";

    [Header("控制对象")]
    [Tooltip("负责整体移动。建议拖入 player 下的 Sphere。")]
    public Transform 控制根节点;

    [Tooltip("负责原地改变视角。拖入 demonstrate Camera。")]
    public Transform 视角节点;

    [Header("移动参数")]
    public float 移动速度 = 3.5f;
    public float 加速倍率 = 2.5f;

    [Tooltip("勾选后，前后左右移动只参考水平朝向，不会因为抬头而向上飞。")]
    public bool 只按水平视角移动 = true;

    [Header("视角参数")]
    public float 鼠标视角灵敏度 = 0.12f;
    public float 手柄视角灵敏度 = 90f;
    public bool 反转Y轴 = false;

    [Header("俯仰角限制")]
    public float 最小俯仰角 = -85f;
    public float 最大俯仰角 = 85f;

    [Header("调试状态")]
    [SerializeField] private bool 当前有输入权;
    [SerializeField] private string 最后收到的公屏叫号 = "";
    [SerializeField] private Vector3 当前移动输入;
    [SerializeField] private Vector2 当前鼠标视角输入;
    [SerializeField] private Vector2 当前手柄视角输入;

    private float yaw;
    private float pitch;

    private void Awake()
    {
        if (控制根节点 == null)
            控制根节点 = transform;

        if (视角节点 == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();

            if (childCamera != null)
                视角节点 = childCamera.transform;
        }

        if (视角节点 == null)
        {
            Debug.LogError(
                "[小人控制器] 没有指定视角节点。请把 demonstrate Camera 拖入“视角节点”。",
                this
            );
            return;
        }

        Vector3 viewEuler = 视角节点.rotation.eulerAngles;

        yaw = 标准化角度(viewEuler.y);
        pitch = 标准化角度(viewEuler.x);
    }

    private void Start()
    {
        if (公屏叫号.Instance != null)
        {
            公屏叫号.Instance.叫号广播.AddListener(收到公屏叫号);
            刷新输入权();
        }
        else
        {
            Debug.LogWarning("[小人控制器] 场景里找不到公屏叫号。", this);
        }
    }

    private void OnDisable()
    {
        if (公屏叫号.Instance != null)
        {
            公屏叫号.Instance.叫号广播.RemoveListener(收到公屏叫号);
        }
    }

    private void Update()
    {
        if (CentralInputReceiver.Instance == null)
            return;

        if (控制根节点 == null || 视角节点 == null)
            return;

        刷新输入权();

        if (!当前有输入权)
            return;

        CentralInputReceiver input = CentralInputReceiver.Instance;

        当前移动输入 = input.Move3D;
        当前鼠标视角输入 = input.MouseLookDelta;
        当前手柄视角输入 = input.GamepadLookStick;

        处理视角(input);
        处理移动(input);
    }

    private void 收到公屏叫号(string 叫号内容)
    {
        最后收到的公屏叫号 = 叫号内容;
        刷新输入权();

        if (当前有输入权)
        {
            Debug.Log(
                "[小人控制器] 收到自己的叫号，开始接收输入：" + 我的输入叫号,
                this
            );
        }
        else
        {
            Debug.Log(
                "[小人控制器] 公屏叫号不是给我的，停止吃输入。当前叫号：" +
                叫号内容,
                this
            );
        }
    }

    private void 刷新输入权()
    {
        if (公屏叫号.Instance == null)
        {
            当前有输入权 = false;
            return;
        }

        当前有输入权 =
            公屏叫号.Instance.当前是(我的输入叫号);

        最后收到的公屏叫号 =
            公屏叫号.Instance.当前频道;
    }

    private void 处理移动(CentralInputReceiver input)
    {
        Vector3 moveInput = input.Move3D;

        if (moveInput.sqrMagnitude < 0.0001f)
            return;

        float finalSpeed = 移动速度;

        if (input.SprintHeld)
            finalSpeed *= 加速倍率;

        Vector3 forward;
        Vector3 right;

        if (只按水平视角移动)
        {
            Quaternion horizontalRotation =
                Quaternion.Euler(0f, yaw, 0f);

            forward = horizontalRotation * Vector3.forward;
            right = horizontalRotation * Vector3.right;
        }
        else
        {
            forward = 视角节点.forward;
            right = 视角节点.right;
        }

        Vector3 worldMove =
            right * moveInput.x +
            Vector3.up * moveInput.y +
            forward * moveInput.z;

        if (worldMove.sqrMagnitude > 1f)
            worldMove.Normalize();

        控制根节点.position +=
            worldMove * finalSpeed * Time.deltaTime;
    }

    private void 处理视角(CentralInputReceiver input)
    {
        Vector2 mouseDelta = input.MouseLookDelta;
        Vector2 stickLook = input.GamepadLookStick;

        float ySign = 反转Y轴 ? 1f : -1f;

        // 鼠标本身已经是每帧位移，不乘 Time.deltaTime
        yaw += mouseDelta.x * 鼠标视角灵敏度;
        pitch += mouseDelta.y * 鼠标视角灵敏度 * ySign;

        // 手柄摇杆是持续输入，需要乘 Time.deltaTime
        yaw +=
            stickLook.x *
            手柄视角灵敏度 *
            Time.deltaTime;

        pitch +=
            stickLook.y *
            手柄视角灵敏度 *
            Time.deltaTime *
            ySign;

        pitch = Mathf.Clamp(
            pitch,
            最小俯仰角,
            最大俯仰角
        );

        // 只旋转摄像机本身，不旋转它的父物体
        // 因此摄像机位置不会绕父节点画圆
        视角节点.rotation =
            Quaternion.Euler(pitch, yaw, 0f);
    }

    private float 标准化角度(float angle)
    {
        while (angle > 180f)
            angle -= 360f;

        while (angle < -180f)
            angle += 360f;

        return angle;
    }
}