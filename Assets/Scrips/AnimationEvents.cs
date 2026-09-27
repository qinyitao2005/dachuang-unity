using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimationEvents : MonoBehaviour
{
    public GameObject pointer;//指针模块
    public GameObject grabber;//抓手模块
    public GameObject platform;//多功能平台
    public GameObject smallWrist;//小手腕
    Animator animator;
    public RuntimeAnimatorController aToBController; // 1、2：a -> b
    public RuntimeAnimatorController bToAController; // 3、4：b -> a
    bool useAtoB = true;
    // 在 Animation 窗口里给最后一帧添加此事件
    private void Start()
    {
        animator = GetComponent<Animator>();
        animator.enabled = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (animator.enabled)
                return;

            animator.runtimeAnimatorController = useAtoB ? aToBController : bToAController;
            useAtoB = !useAtoB;
            animator.enabled = true;
            animator.speed = 1;
        }

        // 动画播完后禁用 animator，让 IK 恢复
        if (animator.enabled)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.normalizedTime >= 1f && !animator.IsInTransition(0))
            {
                animator.enabled = false;
            }
        }
    }


    public void PutdownPointer()
    {
        pointer.transform.SetParent(platform.transform, true);
    }
    public void PutdownGrabber()
    {
        grabber.transform.SetParent(platform.transform, true);
    }
    public void GetPointer()
    {
        pointer.transform.SetParent(smallWrist.transform, true);
    }
    public void GetGrabber()
    {
        grabber.transform.SetParent(smallWrist.transform, true);
    }

}
