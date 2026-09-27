using UnityEngine;

public class Part1启动桥 : MonoBehaviour
{
    [Header("教学主控")]
    public PublicCommandSequenceController 教学主控;

    private bool 已启动 = false;


    public void 启动Part1()
    {
        if (已启动)
        {
            // 操作阶段退出后，保留已经初始化的模型姿态，重新进入记录阶段。
            var menuAdapter = GetComponent<Part1MenuAdapter>();
            if (menuAdapter != null) menuAdapter.ResumeOperation();
            return;
        }

        if (教学主控 == null)
        {
            Debug.LogError("[Part1启动桥] 没有绑定教学主控！");
            return;
        }

        已启动 = true;

        Debug.Log("[Part1启动桥] Part1教学开始");

        教学主控.enabled = true;

        // 不再经过公屏
        // 直接启动整个教学流程
        教学主控.StartSequence();
    }
}
