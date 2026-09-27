using System;
using System.Collections.Generic;
using UnityEngine;

public class 机械臂XYZ手动移动控制器 : MonoBehaviour
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
    // XYZ 单轴按键配置
    // =========================================================

    [Serializable]
    public class 轴移动按键
    {
        [Tooltip("沿该轴正方向移动")]
        public KeyCode 正方向键 = KeyCode.None;

        [Tooltip("沿该轴负方向移动")]
        public KeyCode 负方向键 = KeyCode.None;
    }


    // =========================================================
    // 单个机械臂关节配置
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


        // 运行时数据
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
            {
                axis = -axis;
            }

            return axis;
        }


        public Vector3 获取世界旋转轴()
        {
            if (关节 == null)
                return Vector3.up;

            return 关节
                .TransformDirection(获取本地旋转轴())
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

    [Tooltip("真正要被玩家拖动的机械臂末端。建议填写：等效针尖")]
    [SerializeField]
    private Transform 受控末端;


    [Tooltip("上一 Part 创建的轨迹速度设置控制器")]
    [SerializeField]
    private 轨迹速度设置控制器 速度设置控制器;


    // =========================================================
    // 输入归属
    // =========================================================

    [Header("公屏输入")]

    [Tooltip("只有公屏当前正好是这句话，才允许吃 XYZ 输入")]
    [SerializeField]
    private string 我的输入叫号 =
        "输入给机械臂XYZ手动移动";


    // =========================================================
    // XYZ 移动
    // =========================================================

    [Header("XYZ 自定义按键")]

    [SerializeField]
    private 轴移动按键 X轴按键 = new 轴移动按键
    {
        正方向键 = KeyCode.D,
        负方向键 = KeyCode.A
    };

    [SerializeField]
    private 轴移动按键 Y轴按键 = new 轴移动按键
    {
        正方向键 = KeyCode.E,
        负方向键 = KeyCode.Q
    };

    [SerializeField]
    private 轴移动按键 Z轴按键 = new 轴移动按键
    {
        正方向键 = KeyCode.W,
        负方向键 = KeyCode.S
    };


    [Header("XYZ Jog")]

    [Tooltip(
        "世界 XYZ 输入倍率。" +
        "正常保持 (1,1,1)。" +
        "某轴方向想反转就填 -1。"
    )]
    [SerializeField]
    private Vector3 世界轴倍率 =
        Vector3.one;


    [Header("防止目标跑飞")]

    [Tooltip(
        "机械臂跟不上时，暂停目标继续向前跑。强烈建议开启。"
    )]
    [SerializeField]
    private bool 限制目标领先 = true;


    [Tooltip(
        "代码内部目标最多领先真实末端多少 Unity 单位"
    )]
    [Min(0.001f)]
    [SerializeField]
    private float 最大领先距离 = 0.03f;


    // =========================================================
    // 姿态保持
    // =========================================================

    [Header("末端姿态保持")]

    [Tooltip(
        "Jog 时保持启动瞬间末端某根轴的世界方向。" +
        "对于笔/针尖建议开启。"
    )]
    [SerializeField]
    private bool 保持末端轴方向 = true;


    [Tooltip(
        "例如笔杆长轴如果是本地 Y，就选择 Y。"
    )]
    [SerializeField]
    private 本地轴 要保持的末端轴 =
        本地轴.Y;


    [Tooltip("如果选中的末端轴方向相反，可以勾上")]
    [SerializeField]
    private bool 末端轴反向 = false;


    [Tooltip(
        "末端姿态保持强度。第一版建议 2。"
    )]
    [Range(0f, 5f)]
    [SerializeField]
    private float 姿态约束权重 = 2f;


    // =========================================================
    // IK
    // =========================================================

    [Header("IK 关节：从根到末端")]

    [SerializeField]
    private 关节配置[] 关节;


    [Header("IK 参数")]

    [Tooltip("每帧做几次 DLS 小步")]
    [Range(1, 20)]
    [SerializeField]
    private int 每帧求解子步 = 4;


    [Tooltip("每个关节最大旋转速度：度/秒")]
    [Min(1f)]
    [SerializeField]
    private float 最大关节速度 = 35f;


    [Tooltip(
        "DLS 阻尼。一般 0.05 ~ 0.2。" +
        "太低容易抖，太高反应会变慢。"
    )]
    [Min(0.0001f)]
    [SerializeField]
    private float 阻尼 = 0.1f;


    [Tooltip("位置误差小于这个值后减少死磕")]
    [Min(0.0001f)]
    [SerializeField]
    private float 位置容差 = 0.006f;


    // =========================================================
    // 调试
    // =========================================================

    [Header("调试")]

    [SerializeField]
    private bool 打印日志 = true;

    [SerializeField]
    private bool 绘制调试图形 = true;


    [Header("运行时显示")]

    [SerializeField]
    private bool 当前有输入权 = false;

    [SerializeField]
    private Vector3 目标位置;

    [SerializeField]
    private float 当前使用速度;


    // =========================================================
    // 内部状态
    // =========================================================

    private bool 上一帧有输入权 = false;

    private bool 目标有效 = false;

    private Vector3 目标末端轴世界方向;


    // =========================================================
    // Unity
    // =========================================================

    private void Update()
    {
        更新输入权状态();

        if (!当前有输入权)
            return;

        if (!检查引用())
            return;

        更新目标位置();

        求解到目标();
    }


    // =========================================================
    // 输入权
    // =========================================================

    private void 更新输入权状态()
    {
        当前有输入权 =
            公屏叫号.Instance != null &&
            公屏叫号.Instance.当前是(
                我的输入叫号
            );


        // 刚刚获得控制权
        if (当前有输入权 &&
            !上一帧有输入权)
        {
            接管当前机械臂姿态();
        }


        // 刚刚失去控制权
        if (!当前有输入权 &&
            上一帧有输入权)
        {
            释放控制权();
        }


        上一帧有输入权 =
            当前有输入权;
    }


    // =========================================================
    // 接管
    // =========================================================

    public void 接管当前机械臂姿态()
    {
        if (!检查引用())
            return;


        // 非常重要：
        // 当前机械臂已经经过初始化，
        // 所以现在的姿态才是新的 IK 基准。
        缓存当前关节姿态();


        // 从真实末端当前位置开始，
        // 因此不会瞬移。
        目标位置 =
            受控末端.position;


        // 保存启动瞬间末端轴方向。
        Vector3 localAxis =
            获取本地轴向量(
                要保持的末端轴
            );

        if (末端轴反向)
        {
            localAxis = -localAxis;
        }


        目标末端轴世界方向 =
            受控末端
                .TransformDirection(localAxis)
                .normalized;


        目标有效 = true;


        if (打印日志)
        {
            Debug.Log(
                "[机械臂XYZ手动移动] " +
                "已接管当前姿态。" +
                "末端位置：" +
                受控末端.position,
                this
            );
        }
    }


    public void 释放控制权()
    {
        目标有效 = false;

        if (打印日志)
        {
            Debug.Log(
                "[机械臂XYZ手动移动] 已释放输入权。",
                this
            );
        }
    }


    private void 缓存当前关节姿态()
    {
        if (关节 == null)
            return;

        foreach (关节配置 joint in 关节)
        {
            if (joint == null)
                continue;

            joint.缓存当前姿态();
        }
    }


    // =========================================================
    // XYZ 自定义按键读取
    // =========================================================

    private Vector3 获取XYZ手动输入()
    {
        Vector3 input = Vector3.zero;


        // -------------------------
        // X 轴
        // -------------------------

        if (按键正在按下(X轴按键.正方向键))
        {
            input.x += 1f;
        }

        if (按键正在按下(X轴按键.负方向键))
        {
            input.x -= 1f;
        }


        // -------------------------
        // Y 轴
        // -------------------------

        if (按键正在按下(Y轴按键.正方向键))
        {
            input.y += 1f;
        }

        if (按键正在按下(Y轴按键.负方向键))
        {
            input.y -= 1f;
        }


        // -------------------------
        // Z 轴
        // -------------------------

        if (按键正在按下(Z轴按键.正方向键))
        {
            input.z += 1f;
        }

        if (按键正在按下(Z轴按键.负方向键))
        {
            input.z -= 1f;
        }


        return input;
    }


    private bool 按键正在按下(KeyCode key)
    {
        // Inspector 选 None 时，
        // 代表该方向暂时不绑定任何按键。
        if (key == KeyCode.None)
            return false;

        return Input.GetKey(key);
    }


    // =========================================================
    // 根据玩家输入移动内部目标
    // =========================================================

    private void 更新目标位置()
    {
        if (!目标有效)
            return;


        CurrentSpeed更新();


        if (当前使用速度 <= 0f)
            return;


        // 从 Inspector 自定义的六个按键读取 XYZ 输入
        Vector3 input =
            获取XYZ手动输入();


        // 允许某个世界轴整体反向
        input = Vector3.Scale(
            input,
            世界轴倍率
        );


        // 防止同时按 XYZ 时速度变快。
        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }


        if (input.sqrMagnitude <= 0.000001f)
            return;


        // 如果真实末端已经明显追不上目标，
        // 先不要继续把目标甩得更远。
        if (限制目标领先)
        {
            float leadDistance =
                Vector3.Distance(
                    受控末端.position,
                    目标位置
                );


            if (leadDistance >
                最大领先距离)
            {
                return;
            }
        }


        Vector3 本帧位移 =
            input *
            当前使用速度 *
            Time.deltaTime;


        目标位置 += 本帧位移;
    }


    private void CurrentSpeed更新()
    {
        if (速度设置控制器 == null)
        {
            当前使用速度 = 0f;
            return;
        }

        当前使用速度 =
            Mathf.Max(
                0f,
                速度设置控制器.CurrentSpeed
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
            float error =
                Vector3.Distance(
                    受控末端.position,
                    目标位置
                );


            if (!保持末端轴方向 &&
                error <= 位置容差)
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


        bool 使用姿态约束 =
            保持末端轴方向;


        int rows =
            使用姿态约束
                ? 6
                : 3;


        int cols =
            activeJoints.Count;


        double[,] J =
            new double[rows, cols];

        double[] error =
            new double[rows];


        // ---------------------------------
        // 位置误差
        // ---------------------------------

        Vector3 posError =
            目标位置 -
            受控末端.position;


        error[0] = posError.x;
        error[1] = posError.y;
        error[2] = posError.z;


        // ---------------------------------
        // 姿态轴误差
        // ---------------------------------

        if (使用姿态约束)
        {
            Vector3 localEndAxis =
                获取本地轴向量(
                    要保持的末端轴
                );


            if (末端轴反向)
            {
                localEndAxis =
                    -localEndAxis;
            }


            Vector3 currentAxis =
                受控末端
                    .TransformDirection(
                        localEndAxis
                    )
                    .normalized;


            Vector3 angularError =
                Vector3.Cross(
                    currentAxis,
                    目标末端轴世界方向
                );


            angularError *=
                姿态约束权重;


            error[3] =
                angularError.x;

            error[4] =
                angularError.y;

            error[5] =
                angularError.z;
        }


        // ---------------------------------
        // Jacobian
        // ---------------------------------

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


            // 旋转关节对末端线速度的影响
            Vector3 linear =
                Vector3.Cross(
                    axisWorld,
                    r
                );


            float weight =
                joint.权重;


            J[0, c] =
                linear.x * weight;

            J[1, c] =
                linear.y * weight;

            J[2, c] =
                linear.z * weight;


            if (使用姿态约束)
            {
                float angularWeight =
                    姿态约束权重;


                J[3, c] =
                    axisWorld.x *
                    angularWeight *
                    weight;

                J[4, c] =
                    axisWorld.y *
                    angularWeight *
                    weight;

                J[5, c] =
                    axisWorld.z *
                    angularWeight *
                    weight;
            }
        }


        // ---------------------------------
        // DLS
        // ---------------------------------

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
            new double[n, n + 1];


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
                    matrix[pivot, pivot]
                );


            for (int r =
                     pivot + 1;
                 r < n;
                 r++)
            {
                double value =
                    Math.Abs(
                        matrix[r, pivot]
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
                        matrix[pivot, c];

                    matrix[pivot, c] =
                        matrix[bestRow, c];

                    matrix[bestRow, c] =
                        temp;
                }
            }


            double div =
                matrix[pivot, pivot];


            for (int c = pivot;
                 c <= n;
                 c++)
            {
                matrix[pivot, c] /=
                    div;
            }


            for (int r = 0;
                 r < n;
                 r++)
            {
                if (r == pivot)
                    continue;


                double factor =
                    matrix[r, pivot];


                for (int c = pivot;
                     c <= n;
                     c++)
                {
                    matrix[r, c] -=
                        factor *
                        matrix[pivot, c];
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
                matrix[r, n];
        }


        return result;
    }


    // =========================================================
    // 工具
    // =========================================================

    private bool 检查引用()
    {
        return
            受控末端 != null &&
            速度设置控制器 != null &&
            关节 != null &&
            关节.Length > 0;
    }


    private static Vector3 获取本地轴向量(
        本地轴 axis)
    {
        switch (axis)
        {
            case 本地轴.X:
                return Vector3.right;

            case 本地轴.Y:
                return Vector3.up;

            case 本地轴.Z:
                return Vector3.forward;

            default:
                return Vector3.up;
        }
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
                目标位置,
                0.03f
            );


            if (受控末端 != null)
            {
                Gizmos.DrawLine(
                    受控末端.position,
                    目标位置
                );
            }
        }
    }
}