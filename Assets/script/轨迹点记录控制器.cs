using System;
using System.Collections.Generic;
using UnityEngine;

public class 轨迹点记录控制器 : MonoBehaviour
{
    // =========================================================
    // 单个轨迹点
    // =========================================================

    [Serializable]
    public class 轨迹点
    {
        [Header("编号")]
        public int 编号;

        [Header("世界坐标位置")]
        public Vector3 位置;

        [Header("世界坐标旋转")]
        public Quaternion 旋转;


        public 轨迹点(
            int 编号,
            Vector3 位置,
            Quaternion 旋转)
        {
            this.编号 = 编号;
            this.位置 = 位置;
            this.旋转 = 旋转;
        }
    }


    // =========================================================
    // 核心引用
    // =========================================================

    [Header("记录对象")]

    [Tooltip("要记录哪个机械臂部件的位置和姿态。建议填写：等效针尖")]
    [SerializeField]
    private Transform 记录末端;


    // =========================================================
    // 公屏
    // =========================================================

    [Header("公屏设置")]

    [Tooltip("只有公屏当前是这句话时，才允许记录轨迹点")]
    [SerializeField]
    private string 我的输入叫号 =
        "输入给机械臂XYZ手动移动";


    [Tooltip("玩家结束记录以后向公屏发送")]
    [SerializeField]
    private string 完成命令 =
        "轨迹点记录完成";


    // =========================================================
    // 已记录轨迹点
    // =========================================================

    [Header("已记录轨迹点")]

    [SerializeField]
    private List<轨迹点> 轨迹点列表 =
        new List<轨迹点>();


    // =========================================================
    // 记录设置
    // =========================================================

    [Header("记录设置")]

    [Tooltip("当前是否允许记录轨迹点")]
    [SerializeField]
    private bool 正在记录 = true;


    [Tooltip("是否阻止完全相同的位置被连续记录两次")]
    [SerializeField]
    private bool 防止重复记录 = true;


    [Tooltip("两个点的位置距离小于这个值时，认为位置基本相同")]
    [Min(0f)]
    [SerializeField]
    private float 重复位置容差 = 0.0005f;


    [Tooltip("两个点的旋转差小于这个角度时，认为姿态基本相同")]
    [Min(0f)]
    [SerializeField]
    private float 重复旋转容差 = 0.5f;


    // =========================================================
    // 调试
    // =========================================================

    [Header("调试")]

    [SerializeField]
    private bool 打印日志 = true;


    [SerializeField]
    private bool 绘制轨迹点 = true;


    [Min(0.001f)]
    [SerializeField]
    private float Gizmo大小 = 0.015f;


    // =========================================================
    // 给以后自动轨迹播放读取
    // =========================================================

    public IReadOnlyList<轨迹点> CurrentPoints
    {
        get
        {
            return 轨迹点列表;
        }
    }


    public int PointCount
    {
        get
        {
            return 轨迹点列表.Count;
        }
    }


    public bool IsRecording
    {
        get
        {
            return 正在记录;
        }
    }


    // =========================================================
    // Unity Update
    // =========================================================

    private void Update()
    {
        // 已经结束记录
        if (!正在记录)
            return;


        // 只有当前输入确实归机械臂 Jog 才响应
        if (!当前有输入权())
            return;


        if (CentralInputReceiver.Instance == null)
            return;


        // =====================================================
        // E / 手柄 RB
        // 记录当前轨迹点
        // =====================================================

        if (CentralInputReceiver.Instance.NextPressedThisFrame)
        {
            记录当前点();
        }


        // =====================================================
        // Q / 手柄 LB
        // 结束轨迹点记录
        // =====================================================

        if (CentralInputReceiver.Instance.PreviousPressedThisFrame)
        {
            结束记录();
        }
    }


    // =========================================================
    // 检查输入权
    // =========================================================

    private bool 当前有输入权()
    {
        if (公屏叫号.Instance == null)
            return false;


        return 公屏叫号.Instance.当前是(
            我的输入叫号
        );
    }


    // =========================================================
    // 记录当前点
    // =========================================================

    [ContextMenu("记录当前点")]
    public void 记录当前点()
    {
        if (!正在记录)
        {
            if (打印日志)
            {
                Debug.LogWarning(
                    "[轨迹点记录] 当前已经结束记录，不能继续添加轨迹点。",
                    this
                );
            }

            return;
        }


        if (记录末端 == null)
        {
            Debug.LogError(
                "[轨迹点记录] 没有填写记录末端。",
                this
            );

            return;
        }


        Vector3 position =
            记录末端.position;


        Quaternion rotation =
            记录末端.rotation;


        // =====================================================
        // 检查是否和上一个点重复
        // =====================================================

        if (防止重复记录 &&
            轨迹点列表.Count > 0)
        {
            轨迹点 last =
                轨迹点列表[
                    轨迹点列表.Count - 1
                ];


            float positionDifference =
                Vector3.Distance(
                    last.位置,
                    position
                );


            float rotationDifference =
                Quaternion.Angle(
                    last.旋转,
                    rotation
                );


            if (positionDifference <= 重复位置容差 &&
                rotationDifference <= 重复旋转容差)
            {
                if (打印日志)
                {
                    Debug.LogWarning(
                        "[轨迹点记录] " +
                        "当前位置与上一个轨迹点基本相同，" +
                        "本次没有重复记录。",
                        this
                    );
                }

                return;
            }
        }


        // =====================================================
        // 创建轨迹点
        // =====================================================

        int newIndex =
            轨迹点列表.Count + 1;


        轨迹点 newPoint =
            new 轨迹点(
                newIndex,
                position,
                rotation
            );


        轨迹点列表.Add(
            newPoint
        );


        // =====================================================
        // 日志
        // =====================================================

        if (打印日志)
        {
            Debug.Log(
                "[轨迹点记录] 已记录点 " +
                newIndex +
                "\nPosition = " +
                position +
                "\nRotation = " +
                rotation.eulerAngles,
                this
            );
        }
    }


    // =========================================================
    // 结束记录
    // =========================================================

    [ContextMenu("结束记录")]
    public void 结束记录()
    {
        if (!正在记录)
            return;


        正在记录 = false;


        if (打印日志)
        {
            Debug.Log(
                "[轨迹点记录] ====================" +
                "\n轨迹点记录结束" +
                "\n共记录 " +
                轨迹点列表.Count +
                " 个轨迹点" +
                "\n====================",
                this
            );
        }


        // 向公屏回复
        if (公屏叫号.Instance != null &&
            !string.IsNullOrWhiteSpace(完成命令))
        {
            公屏叫号.Instance.喊话(
                完成命令
            );
        }
    }


    // =========================================================
    // 重新开始记录
    //
    // 不清除以前的点，只允许继续往后加
    // =========================================================

    [ContextMenu("继续记录")]
    public void 继续记录()
    {
        正在记录 = true;


        if (打印日志)
        {
            Debug.Log(
                "[轨迹点记录] 已重新开启记录。",
                this
            );
        }
    }


    // =========================================================
    // 开始一条全新的轨迹
    //
    // 清空旧点，再开始
    // =========================================================

    [ContextMenu("开始新的轨迹")]
    public void 开始新的轨迹()
    {
        轨迹点列表.Clear();

        正在记录 = true;


        if (打印日志)
        {
            Debug.Log(
                "[轨迹点记录] 已清空旧轨迹，开始新的记录。",
                this
            );
        }
    }


    // =========================================================
    // 删除最后一个轨迹点
    // =========================================================

    [ContextMenu("删除最后一个点")]
    public void 删除最后一个点()
    {
        if (轨迹点列表.Count <= 0)
        {
            if (打印日志)
            {
                Debug.LogWarning(
                    "[轨迹点记录] 当前没有轨迹点可以删除。",
                    this
                );
            }

            return;
        }


        int lastIndex =
            轨迹点列表.Count - 1;


        int removedNumber =
            轨迹点列表[lastIndex].编号;


        轨迹点列表.RemoveAt(
            lastIndex
        );


        if (打印日志)
        {
            Debug.Log(
                "[轨迹点记录] 已删除点 " +
                removedNumber,
                this
            );
        }
    }


    // =========================================================
    // 清空全部轨迹点
    // =========================================================

    [ContextMenu("清空全部轨迹点")]
    public void 清空全部轨迹点()
    {
        轨迹点列表.Clear();


        if (打印日志)
        {
            Debug.Log(
                "[轨迹点记录] 已清空全部轨迹点。",
                this
            );
        }
    }


    // =========================================================
    // 给以后自动播放脚本使用
    // =========================================================

    public 轨迹点 获取轨迹点(
        int index)
    {
        if (index < 0 ||
            index >= 轨迹点列表.Count)
        {
            return null;
        }


        return 轨迹点列表[index];
    }


    // =========================================================
    // Scene 调试显示
    // =========================================================

    private void OnDrawGizmos()
    {
        if (!绘制轨迹点)
            return;


        if (轨迹点列表 == null)
            return;


        for (int i = 0;
             i < 轨迹点列表.Count;
             i++)
        {
            轨迹点 point =
                轨迹点列表[i];


            if (point == null)
                continue;


            // 当前轨迹点
            Gizmos.DrawWireSphere(
                point.位置,
                Gizmo大小
            );


            // 前一个点 → 当前点
            if (i > 0)
            {
                轨迹点 previous =
                    轨迹点列表[i - 1];


                if (previous != null)
                {
                    Gizmos.DrawLine(
                        previous.位置,
                        point.位置
                    );
                }
            }


            // 显示轨迹点记录时的朝向
            Vector3 forward =
                point.旋转 *
                Vector3.forward;


            Gizmos.DrawLine(
                point.位置,
                point.位置 +
                forward *
                Gizmo大小 *
                3f
            );
        }
    }
}