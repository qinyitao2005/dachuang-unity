using System;
using System.Collections.Generic;
using UnityEngine;

public class 机械臂轨迹重复控制器 : MonoBehaviour
{
    // =========================================================
    // 枚举
    // =========================================================

    public enum 本地轴
    {
        X,
        Y,
        Z
    }


    // =========================================================
    // 单个关节
    // =========================================================

    [Serializable]
    public class 关节配置
    {
        [Header("关节")]
        public string 名称;

        public Transform 关节;


        [Header("绕本地哪根轴旋转")]
        public 本地轴 旋转轴 = 本地轴.Y;

        [Tooltip("如果这个关节转动方向反了，就勾上")]
        public bool 反向 = false;


        [Header("是否参与 IK")]
        public bool 参与IK = true;


        [Header("关节权重")]
        [Range(0f, 2f)]
        public float 权重 = 1f;


        [Header("角度限制")]
        public bool 使用角度限制 = false;

        public float 最小角度 = -180f;

        public float 最大角度 = 180f;


        // 运行时
        [NonSerialized]
        public Quaternion 初始局部旋转;

        [NonSerialized]
        public float 当前角度;


        public void 缓存当前姿态()
        {
            if (关节 == null)
                return;

            初始局部旋转 = 关节.localRotation;
            当前角度 = 0f;
        }


        public Vector3 获取本地旋转轴()
        {
            Vector3 axis = Vector3.up;

            switch (旋转轴)
            {
                case 本地轴.X:
                    axis = Vector3.right;
                    break;

                case 本地轴.Y:
                    axis = Vector3.up;
                    break;

                case 本地轴.Z:
                    axis = Vector3.forward;
                    break;
            }

            if (反向)
                axis = -axis;

            return axis;
        }


        public Vector3 获取世界旋转轴()
        {
            if (关节 == null)
                return Vector3.up;

            return 关节
                .TransformDirection(
                    获取本地旋转轴()
                )
                .normalized;
        }


        public void 应用当前角度()
        {
            if (关节 == null)
                return;

            Quaternion delta =
                Quaternion.AngleAxis(
                    当前角度,
                    获取本地旋转轴()
                );

            关节.localRotation =
                初始局部旋转 * delta;
        }


        public void 增加角度(
            float 增量,
            float 本次最大增量)
        {
            if (关节 == null)
                return;

            if (!参与IK)
                return;

            增量 = Mathf.Clamp(
                增量,
                -本次最大增量,
                本次最大增量
            );

            float next =
                当前角度 + 增量;

            if (使用角度限制)
            {
                next = Mathf.Clamp(
                    next,
                    最小角度,
                    最大角度
                );
            }

            当前角度 = next;

            应用当前角度();
        }
    }


    // =========================================================
    // 核心引用
    // =========================================================

    [Header("核心对象")]

    [Tooltip("机械臂真正的末端，例如：等效针尖")]
    [SerializeField]
    private Transform 受控末端;


    [Tooltip("负责记录轨迹点的脚本")]
    [SerializeField]
    private 轨迹点记录控制器 轨迹记录控制器;


    [Tooltip("之前已经完成的轨迹速度设置控制器")]
    [SerializeField]
    private 轨迹速度设置控制器 速度设置控制器;


    [Tooltip(
        "可选。填写 XYZ 手动 Jog 控制器。" +
        "复现开始时会主动让它释放控制。"
    )]
    [SerializeField]
    private 机械臂XYZ手动移动控制器 XYZ手动移动控制器;


    // =========================================================
    // 公屏
    // =========================================================

    [Header("公屏流程")]

    [Tooltip("公屏出现这句话，就开始轨迹复现")]
    [SerializeField]
    private string 开始复现叫号 =
        "开始轨迹重复";


    [Tooltip("所有轨迹点复现完成后喊出")]
    [SerializeField]
    private string 复现完成叫号 =
        "轨迹重复完成";


    [Tooltip("轨迹无法开始时喊出")]
    [SerializeField]
    private string 复现失败叫号 =
        "轨迹重复失败";


    // =========================================================
    // IK
    // =========================================================

    [Header("IK 关节：从根到末端")]

    [SerializeField]
    private 关节配置[] 关节;


    [Header("IK 参数")]

    [Tooltip("每帧执行几次 DLS 小步")]
    [Range(1, 20)]
    [SerializeField]
    private int 每帧求解子步 = 4;


    [Tooltip("单个关节最大旋转速度：度/秒")]
    [Min(1f)]
    [SerializeField]
    private float 最大关节速度 = 35f;


    [Tooltip("DLS 阻尼")]
    [Min(0.0001f)]
    [SerializeField]
    private float 阻尼 = 0.1f;


    [Tooltip(
        "完整 Quaternion 姿态的权重。" +
        "机械臂只有 5 个自由度，建议先从 0.3~0.8 开始。"
    )]
    [Range(0f, 3f)]
    [SerializeField]
    private float 姿态约束权重 = 0.5f;


    // =========================================================
    // 到点判定
    // =========================================================

    [Header("轨迹点到达判定")]

    [Tooltip("真实末端距离记录点小于此值，认为位置到达")]
    [Min(0.0001f)]
    [SerializeField]
    private float 位置到点容差 = 0.006f;


    [Tooltip("真实末端与记录姿态夹角小于此值，认为姿态到达")]
    [Min(0.1f)]
    [SerializeField]
    private float 姿态到点容差 = 3f;


    // =========================================================
    // 防止虚拟目标跑太快
    // =========================================================

    [Header("防止轨迹目标跑飞")]

    [Tooltip(
        "真实机械臂明显跟不上内部目标时，" +
        "暂停轨迹时间推进。建议开启。"
    )]
    [SerializeField]
    private bool 限制目标领先 = true;


    [Tooltip("内部轨迹目标最多领先真实针尖多少 Unity 单位")]
    [Min(0.001f)]
    [SerializeField]
    private float 最大领先距离 = 0.03f;


    // =========================================================
    // 调试
    // =========================================================

    [Header("调试")]

    [SerializeField]
    private bool 打印日志 = true;

    [SerializeField]
    private bool 绘制调试图形 = true;


    [Header("运行时状态")]

    [SerializeField]
    private bool 正在复现 = false;

    [SerializeField]
    private int 当前点索引 = -1;

    [SerializeField]
    private float 当前使用速度 = 0f;

    [SerializeField]
    private Vector3 当前目标位置;

    [SerializeField]
    private Quaternion 当前目标旋转;

    [SerializeField]
    private bool 正在等待当前点收敛 = false;


    // =========================================================
    // 轨迹运行时副本
    // =========================================================

    private readonly List<Pose> 复现轨迹 =
        new List<Pose>();


    // 当前段
    private Vector3 段起点位置;

    private Quaternion 段起点旋转;

    private Vector3 段终点位置;

    private Quaternion 段终点旋转;

    private float 段总距离;

    private float 段已推进距离;


    // IK
    private bool 目标有效 = false;


    // 公屏
    private bool 已订阅公屏 = false;


    // =========================================================
    // 对外状态
    // =========================================================

    public bool IsPlaying
    {
        get { return 正在复现; }
    }

    public int CurrentPointIndex
    {
        get { return 当前点索引; }
    }


    // =========================================================
    // Unity
    // =========================================================

    private void OnEnable()
    {
        尝试订阅公屏();
    }


    private void Start()
    {
        尝试订阅公屏();
    }


    private void Update()
    {
        if (!已订阅公屏)
        {
            尝试订阅公屏();
        }

        if (!正在复现)
            return;

        if (!检查核心引用())
        {
            复现失败(
                "复现过程中核心引用失效。"
            );

            return;
        }

        更新当前速度();

        // 菜单速度为零表示暂停；加速后继续当前轨迹段。
        if (当前使用速度 <= 0f) return;

        // ----------------------------------
        // 1. 推进轨迹上的“虚拟末端目标”
        // ----------------------------------

        推进轨迹目标();

        // ----------------------------------
        // 2. IK 追踪虚拟末端目标
        // ----------------------------------

        求解到目标();
    }


    private void OnDisable()
    {
        取消订阅公屏();

        正在复现 = false;
        目标有效 = false;
    }


    // =========================================================
    // 公屏
    // =========================================================

    private void 尝试订阅公屏()
    {
        if (已订阅公屏)
            return;

        if (公屏叫号.Instance == null)
            return;

        公屏叫号.Instance
            .叫号广播
            .AddListener(
                收到公屏叫号
            );

        已订阅公屏 = true;


        // 防止本脚本启用得比较晚，
        // 已经错过“开始轨迹重复”的广播。
        if (公屏叫号.Instance.当前是(
                开始复现叫号))
        {
            开始轨迹复现();
        }
    }


    private void 取消订阅公屏()
    {
        if (!已订阅公屏)
            return;

        if (公屏叫号.Instance != null)
        {
            公屏叫号.Instance
                .叫号广播
                .RemoveListener(
                    收到公屏叫号
                );
        }

        已订阅公屏 = false;
    }


    private void 收到公屏叫号(
        string command)
    {
        if (command != 开始复现叫号)
            return;

        开始轨迹复现();
    }


    // =========================================================
    // 开始复现
    // =========================================================

    public void 开始轨迹复现()
    {
        if (正在复现)
        {
            if (打印日志)
            {
                Debug.LogWarning(
                    "[轨迹重复] 当前已经正在播放轨迹。",
                    this
                );
            }

            return;
        }


        if (!检查核心引用())
        {
            复现失败(
                "缺少受控末端、轨迹记录器、速度控制器或关节配置。"
            );

            return;
        }


        更新当前速度();

        // 允许以零速度开始，Update 会保持暂停直到用户增加速度。


        if (轨迹记录控制器.PointCount <= 0)
        {
            复现失败(
                "当前没有任何轨迹点。"
            );

            return;
        }


        // ----------------------------------
        // 让手动 Jog 主动放手
        // ----------------------------------

        if (XYZ手动移动控制器 != null)
        {
            XYZ手动移动控制器
                .释放控制权();
        }


        // ----------------------------------
        // 将记录轨迹复制一份
        //
        // 播放过程中即使记录器发生变化，
        // 当前复现也不会受影响。
        // ----------------------------------

        复现轨迹.Clear();

        for (int i = 0;
             i < 轨迹记录控制器.PointCount;
             i++)
        {
            var point =
                轨迹记录控制器
                    .获取轨迹点(i);

            Pose pose =
                new Pose(
                    point.位置,
                    point.旋转
                );

            复现轨迹.Add(pose);
        }


        // ----------------------------------
        // 当前真实机械臂姿态
        // 就是这一套 IK 的新基准
        // ----------------------------------

        缓存当前关节姿态();


        当前目标位置 =
            受控末端.position;

        当前目标旋转 =
            受控末端.rotation;


        目标有效 = true;

        正在复现 = true;

        当前点索引 = 0;

        正在等待当前点收敛 = false;


        // 当前机械臂位置
        // → 第 0 个轨迹点
        设置当前轨迹段(
            当前目标位置,
            当前目标旋转,
            复现轨迹[0].position,
            复现轨迹[0].rotation
        );


        if (打印日志)
        {
            Debug.Log(
                "[轨迹重复] 开始播放。" +
                "轨迹点数量：" +
                复现轨迹.Count +
                "；速度：" +
                当前使用速度,
                this
            );
        }
    }


    // =========================================================
    // 设置一段轨迹
    // =========================================================

    private void 设置当前轨迹段(
        Vector3 startPosition,
        Quaternion startRotation,
        Vector3 endPosition,
        Quaternion endRotation)
    {
        段起点位置 =
            startPosition;

        段起点旋转 =
            startRotation;

        段终点位置 =
            endPosition;

        段终点旋转 =
            endRotation;


        段总距离 =
            Vector3.Distance(
                段起点位置,
                段终点位置
            );

        段已推进距离 = 0f;

        正在等待当前点收敛 = false;


        // 每一段刚开始时，
        // 虚拟目标就是段起点。
        当前目标位置 =
            段起点位置;

        当前目标旋转 =
            段起点旋转;


        if (打印日志)
        {
            Debug.Log(
                "[轨迹重复] 开始前往轨迹点 " +
                当前点索引 +
                "；段长度：" +
                段总距离,
                this
            );
        }
    }


    // =========================================================
    // 推进目标
    // =========================================================

    private void 推进轨迹目标()
    {
        if (!目标有效)
            return;

        if (当前点索引 < 0 ||
            当前点索引 >= 复现轨迹.Count)
            return;


        // ----------------------------------
        // 虚拟目标已经走到记录点，
        // 等真实机械臂追上来。
        // ----------------------------------

        if (正在等待当前点收敛)
        {
            当前目标位置 =
                段终点位置;

            当前目标旋转 =
                段终点旋转;


            if (真实末端已经到达当前点())
            {
                当前轨迹点完成();
            }

            return;
        }


        // ----------------------------------
        // 防止内部目标一路狂奔，
        // 真机械臂还在后面追。
        // ----------------------------------

        if (限制目标领先)
        {
            float leadDistance =
                Vector3.Distance(
                    受控末端.position,
                    当前目标位置
                );

            if (leadDistance >
                最大领先距离)
            {
                return;
            }
        }


        if (当前使用速度 <= 0f)
            return;


        // ----------------------------------
        // 如果两点位置完全重合，
        // 位置无需移动，直接把旋转目标送到终点。
        //
        // 实际机械臂依然受到关节最大速度限制，
        // 所以不会瞬间跳姿态。
        // ----------------------------------

        if (段总距离 <= 0.000001f)
        {
            当前目标位置 =
                段终点位置;

            当前目标旋转 =
                段终点旋转;

            正在等待当前点收敛 =
                true;

            return;
        }


        // ----------------------------------
        // 根据 CurrentSpeed
        // 沿线段匀速前进
        // ----------------------------------

        段已推进距离 +=
            当前使用速度 *
            Time.deltaTime;


        float t =
            Mathf.Clamp01(
                段已推进距离 /
                段总距离
            );


        // 世界位置：直线插值
        当前目标位置 =
            Vector3.Lerp(
                段起点位置,
                段终点位置,
                t
            );


        // 世界旋转：Quaternion 球面插值
        当前目标旋转 =
            Quaternion.Slerp(
                段起点旋转,
                段终点旋转,
                t
            );


        // 虚拟目标已经走到这一点。
        if (t >= 1f)
        {
            当前目标位置 =
                段终点位置;

            当前目标旋转 =
                段终点旋转;

            正在等待当前点收敛 =
                true;
        }
    }


    // =========================================================
    // 当前点是否真正到达
    // =========================================================

    private bool 真实末端已经到达当前点()
    {
        float positionError =
            Vector3.Distance(
                受控末端.position,
                段终点位置
            );


        float rotationError =
            Quaternion.Angle(
                受控末端.rotation,
                段终点旋转
            );


        return
            positionError <=
                位置到点容差
            &&
            rotationError <=
                姿态到点容差;
    }


    // =========================================================
    // 当前轨迹点完成
    // =========================================================

    private void 当前轨迹点完成()
    {
        if (打印日志)
        {
            Debug.Log(
                "[轨迹重复] 到达轨迹点：" +
                当前点索引,
                this
            );
        }


        当前点索引++;


        // 全部完成
        if (当前点索引 >=
            复现轨迹.Count)
        {
            完成轨迹复现();
            return;
        }


        // ----------------------------------
        // 上一个记录点
        // → 下一个记录点
        // ----------------------------------

        Pose next =
            复现轨迹[
                当前点索引
            ];


        设置当前轨迹段(
            当前目标位置,
            当前目标旋转,
            next.position,
            next.rotation
        );
    }


    // =========================================================
    // IK
    // =========================================================

    private void 求解到目标()
    {
        if (!目标有效)
            return;

        if (关节 == null ||
            关节.Length == 0)
            return;


        int steps =
            Mathf.Max(
                1,
                每帧求解子步
            );


        float subDeltaTime =
            Time.deltaTime /
            steps;


        for (int i = 0;
             i < steps;
             i++)
        {
            float positionError =
                Vector3.Distance(
                    受控末端.position,
                    当前目标位置
                );


            float rotationError =
                Quaternion.Angle(
                    受控末端.rotation,
                    当前目标旋转
                );


            if (positionError <=
                    位置到点容差 &&
                rotationError <=
                    姿态到点容差)
            {
                break;
            }


            求解一次DLS(
                subDeltaTime
            );
        }
    }


    private void 求解一次DLS(
        float subDeltaTime)
    {
        List<关节配置> activeJoints =
            new List<关节配置>();


        foreach (关节配置 joint in 关节)
        {
            if (joint == null)
                continue;

            if (joint.关节 == null)
                continue;

            if (!joint.参与IK)
                continue;

            if (joint.权重 <= 0f)
                continue;

            activeJoints.Add(joint);
        }


        if (activeJoints.Count == 0)
            return;


        // Position XYZ
        // +
        // Rotation XYZ
        //
        // 共 6 行。
        int rows = 6;

        int cols =
            activeJoints.Count;


        double[,] J =
            new double[rows, cols];

        double[] error =
            new double[rows];


        // =====================================================
        // 位置误差
        // =====================================================

        Vector3 posError =
            当前目标位置 -
            受控末端.position;


        error[0] =
            posError.x;

        error[1] =
            posError.y;

        error[2] =
            posError.z;


        // =====================================================
        // 完整 Quaternion 姿态误差
        // =====================================================

        Vector3 angularError =
            计算世界旋转误差(
                受控末端.rotation,
                当前目标旋转
            );


        angularError *=
            姿态约束权重;


        error[3] =
            angularError.x;

        error[4] =
            angularError.y;

        error[5] =
            angularError.z;


        // =====================================================
        // Jacobian
        // =====================================================

        for (int c = 0;
             c < cols;
             c++)
        {
            关节配置 joint =
                activeJoints[c];


            Vector3 axisWorld =
                joint.获取世界旋转轴();


            Vector3 r =
                受控末端.position -
                joint.关节.position;


            // ---------------------------------
            // 旋转关节造成的末端线速度
            // ---------------------------------

            Vector3 linear =
                Vector3.Cross(
                    axisWorld,
                    r
                );


            float weight =
                joint.权重;


            J[0, c] =
                linear.x *
                weight;

            J[1, c] =
                linear.y *
                weight;

            J[2, c] =
                linear.z *
                weight;


            // ---------------------------------
            // 旋转关节造成的末端角速度
            // ---------------------------------

            J[3, c] =
                axisWorld.x *
                姿态约束权重 *
                weight;

            J[4, c] =
                axisWorld.y *
                姿态约束权重 *
                weight;

            J[5, c] =
                axisWorld.z *
                姿态约束权重 *
                weight;
        }


        // =====================================================
        // DLS
        // =====================================================

        double[] deltaRadians =
            求解阻尼最小二乘(
                J,
                error,
                阻尼
            );


        if (deltaRadians == null)
            return;


        float maxDelta =
            最大关节速度 *
            subDeltaTime;


        for (int i = 0;
             i < activeJoints.Count;
             i++)
        {
            float deltaDeg =
                (float)(
                    deltaRadians[i] *
                    Mathf.Rad2Deg
                );


            activeJoints[i]
                .增加角度(
                    deltaDeg,
                    maxDelta
                );
        }
    }


    // =========================================================
    // Quaternion → 世界角误差向量
    // =========================================================

    private Vector3 计算世界旋转误差(
        Quaternion current,
        Quaternion target)
    {
        Quaternion delta =
            target *
            Quaternion.Inverse(
                current
            );


        // Quaternion q 和 -q
        // 表示完全相同的旋转。
        //
        // 强制走较短旋转路径。
        if (delta.w < 0f)
        {
            delta =
                new Quaternion(
                    -delta.x,
                    -delta.y,
                    -delta.z,
                    -delta.w
                );
        }


        delta.ToAngleAxis(
            out float angleDeg,
            out Vector3 axis
        );


        if (float.IsNaN(axis.x) ||
            float.IsNaN(axis.y) ||
            float.IsNaN(axis.z))
        {
            return Vector3.zero;
        }


        if (axis.sqrMagnitude <
            0.000001f)
        {
            return Vector3.zero;
        }


        if (angleDeg > 180f)
        {
            angleDeg -= 360f;
        }


        float angleRad =
            angleDeg *
            Mathf.Deg2Rad;


        return
            axis.normalized *
            angleRad;
    }


    // =========================================================
    // DLS
    //
    // delta =
    // J^T *
    // (J J^T + lambda² I)^-1 *
    // error
    // =========================================================

    private double[] 求解阻尼最小二乘(
        double[,] J,
        double[] error,
        float lambda)
    {
        int rows =
            J.GetLength(0);

        int cols =
            J.GetLength(1);


        double[,] A =
            new double[rows, rows];


        for (int r = 0;
             r < rows;
             r++)
        {
            for (int s = 0;
                 s < rows;
                 s++)
            {
                double sum = 0.0;


                for (int c = 0;
                     c < cols;
                     c++)
                {
                    sum +=
                        J[r, c] *
                        J[s, c];
                }


                A[r, s] = sum;
            }
        }


        double dampingValue =
            Math.Max(
                0.000001,
                lambda * lambda
            );


        for (int i = 0;
             i < rows;
             i++)
        {
            A[i, i] +=
                dampingValue;
        }


        double[] y =
            解线性方程组(
                A,
                error
            );


        if (y == null)
            return null;


        double[] delta =
            new double[cols];


        for (int c = 0;
             c < cols;
             c++)
        {
            double sum = 0.0;


            for (int r = 0;
                 r < rows;
                 r++)
            {
                sum +=
                    J[r, c] *
                    y[r];
            }


            delta[c] = sum;
        }


        return delta;
    }


    // =========================================================
    // 高斯消元
    // =========================================================

    private double[] 解线性方程组(
        double[,] A,
        double[] b)
    {
        int n =
            b.Length;


        double[,] matrix =
            new double[
                n,
                n + 1
            ];


        for (int r = 0;
             r < n;
             r++)
        {
            for (int c = 0;
                 c < n;
                 c++)
            {
                matrix[r, c] =
                    A[r, c];
            }


            matrix[r, n] =
                b[r];
        }


        for (int pivot = 0;
             pivot < n;
             pivot++)
        {
            int bestRow =
                pivot;

            double bestValue =
                Math.Abs(
                    matrix[
                        pivot,
                        pivot
                    ]
                );


            for (int r =
                     pivot + 1;
                 r < n;
                 r++)
            {
                double value =
                    Math.Abs(
                        matrix[
                            r,
                            pivot
                        ]
                    );


                if (value > bestValue)
                {
                    bestValue =
                        value;

                    bestRow =
                        r;
                }
            }


            if (bestValue < 1e-9)
            {
                return null;
            }


            if (bestRow != pivot)
            {
                for (int c = pivot;
                     c <= n;
                     c++)
                {
                    double temp =
                        matrix[
                            pivot,
                            c
                        ];

                    matrix[
                        pivot,
                        c
                    ] =
                        matrix[
                            bestRow,
                            c
                        ];

                    matrix[
                        bestRow,
                        c
                    ] =
                        temp;
                }
            }


            double div =
                matrix[
                    pivot,
                    pivot
                ];


            for (int c = pivot;
                 c <= n;
                 c++)
            {
                matrix[
                    pivot,
                    c
                ] /= div;
            }


            for (int r = 0;
                 r < n;
                 r++)
            {
                if (r == pivot)
                    continue;


                double factor =
                    matrix[
                        r,
                        pivot
                    ];


                for (int c = pivot;
                     c <= n;
                     c++)
                {
                    matrix[
                        r,
                        c
                    ] -=
                        factor *
                        matrix[
                            pivot,
                            c
                        ];
                }
            }
        }


        double[] result =
            new double[n];


        for (int r = 0;
             r < n;
             r++)
        {
            result[r] =
                matrix[
                    r,
                    n
                ];
        }


        return result;
    }


    // =========================================================
    // 关节接管
    // =========================================================

    private void 缓存当前关节姿态()
    {
        if (关节 == null)
            return;


        foreach (关节配置 joint
                 in 关节)
        {
            if (joint == null)
                continue;

            joint.缓存当前姿态();
        }
    }


    // =========================================================
    // 速度
    // =========================================================

    private void 更新当前速度()
    {
        if (速度设置控制器 == null)
        {
            当前使用速度 = 0f;
            return;
        }


        当前使用速度 =
            Mathf.Max(
                0f,
                速度设置控制器
                    .CurrentSpeed
            );
    }


    // =========================================================
    // 完成
    // =========================================================

    private void 完成轨迹复现()
    {
        正在复现 = false;

        目标有效 = false;

        正在等待当前点收敛 = false;


        if (打印日志)
        {
            Debug.Log(
                "[轨迹重复] 全部轨迹点复现完成。",
                this
            );
        }


        if (公屏叫号.Instance != null &&
            !string.IsNullOrWhiteSpace(
                复现完成叫号))
        {
            公屏叫号.Instance
                .喊话(
                    复现完成叫号
                );
        }
    }


    // =========================================================
    // 失败
    // =========================================================

    private void 复现失败(
        string reason)
    {
        正在复现 = false;

        目标有效 = false;

        正在等待当前点收敛 = false;


        Debug.LogError(
            "[轨迹重复] 失败：" +
            reason,
            this
        );


        if (公屏叫号.Instance != null &&
            !string.IsNullOrWhiteSpace(
                复现失败叫号))
        {
            公屏叫号.Instance
                .喊话(
                    复现失败叫号
                );
        }
    }


    // =========================================================
    // 外部手动停止
    // =========================================================

    public void 停止轨迹复现()
    {
        正在复现 = false;

        目标有效 = false;

        正在等待当前点收敛 = false;


        if (打印日志)
        {
            Debug.Log(
                "[轨迹重复] 已手动停止。",
                this
            );
        }
    }


    // =========================================================
    // 引用检查
    // =========================================================

    private bool 检查核心引用()
    {
        return
            受控末端 != null &&
            轨迹记录控制器 != null &&
            速度设置控制器 != null &&
            关节 != null &&
            关节.Length > 0;
    }


    // =========================================================
    // Gizmos
    // =========================================================

    private void OnDrawGizmos()
    {
        if (!绘制调试图形)
            return;


        if (受控末端 != null)
        {
            Gizmos.DrawWireSphere(
                受控末端.position,
                0.02f
            );
        }


        if (目标有效)
        {
            Gizmos.DrawWireSphere(
                当前目标位置,
                0.03f
            );


            if (受控末端 != null)
            {
                Gizmos.DrawLine(
                    受控末端.position,
                    当前目标位置
                );
            }
        }


        if (Application.isPlaying &&
            复现轨迹.Count > 0)
        {
            for (int i = 0;
                 i < 复现轨迹.Count;
                 i++)
            {
                Gizmos.DrawWireSphere(
                    复现轨迹[i].position,
                    0.015f
                );


                if (i > 0)
                {
                    Gizmos.DrawLine(
                        复现轨迹[
                            i - 1
                        ].position,
                        复现轨迹[i].position
                    );
                }
            }
        }
    }
}
