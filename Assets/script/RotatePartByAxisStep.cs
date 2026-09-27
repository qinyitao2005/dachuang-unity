using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 单步骤：让某个部件绕指定轴旋转指定角度。
///
/// 接入 SequenceSignalController：
/// 1. 总控调用 StartSequence()
/// 2. 本脚本旋转 targetPart
/// 3. 完成后 onCompleted 回传 completeCommand
/// 4. 失败后 onFailed 回传 failedCommand
///
/// 注意：
/// - 不在 Start() 自动执行
/// - 不私自调用下一步
/// - 不重置部件姿态
/// - angleDegrees 可以填负数，表示反方向旋转
/// </summary>
public class RotatePartByAxisStep : MonoBehaviour
{
    public enum Axis
    {
        X,
        Y,
        Z,
        NegativeX,
        NegativeY,
        NegativeZ
    }

    public enum AxisSpace
    {
        TargetLocal,
        World,
        ReferenceLocal
    }

    [System.Serializable]
    public class RotateCommandEvent : UnityEvent<string>
    {
    }

    [Header("Sequence Signal Protocol")]
    [Tooltip("完成时回传给 SequenceSignalController 的信号，必须和总控 Expected Complete Command 完全一致")]
    public string completeCommand = "PART_ROTATED";

    [Tooltip("失败时回传给 SequenceSignalController 的信号")]
    public string failedCommand = "PART_ROTATE_FAILED";

    public RotateCommandEvent onCompleted;
    public RotateCommandEvent onFailed;

    [Header("旋转目标")]
    [Tooltip("要旋转的部件")]
    public Transform targetPart;

    [Header("旋转轴设置")]
    [Tooltip("TargetLocal = 绕部件自己的本地轴；World = 绕世界轴；ReferenceLocal = 绕参考物体的本地轴")]
    public AxisSpace axisSpace = AxisSpace.TargetLocal;

    [Tooltip("选择绕哪根轴转。Negative 表示反向轴。")]
    public Axis rotateAxis = Axis.Z;

    [Tooltip("当 Axis Space = ReferenceLocal 时使用。比如想沿某个接口/笔/目标物的本地轴转。")]
    public Transform referenceAxisTransform;

    [Header("旋转角度")]
    [Tooltip("总共旋转多少度。可以填负数。")]
    public float angleDegrees = 90f;

    [Tooltip("旋转速度，单位：度/秒")]
    public float angularSpeedDegPerSec = 45f;

    [Header("运行设置")]
    [Tooltip("是否使用不受 Time.timeScale 影响的时间。一般不勾。")]
    public bool useUnscaledTime = false;

    [Tooltip("完成时是否强制精确设置到最终角度，建议勾上。")]
    public bool snapExactlyAtEnd = true;

    [Tooltip("最多运行多久，超过则失败。0 或负数表示不启用超时。")]
    public float timeoutSeconds = 0f;

    [Header("Debug")]
    public bool verboseLog = true;
    public bool drawDebugGizmos = true;

    [Header("运行时显示")]
    public string state = "Idle";
    public float currentAngle = 0f;
    public float progress01 = 0f;
    public Vector3 runtimeAxisWorld = Vector3.forward;

    private bool isRunning = false;
    private bool hasFinished = false;
    private Coroutine runningCoroutine;

    private Quaternion initialLocalRotation;
    private Quaternion initialWorldRotation;

    private Vector3 cachedLocalAxis;
    private Vector3 cachedWorldAxis;

    // ============================================================
    // 总控统一启动入口：无参数
    // ============================================================

    public void StartSequence()
    {
        InternalStartSequence("");
    }

    // ============================================================
    // 总控统一启动入口：带字符串
    // 防止总控误勾 Pass Start Command 时也能接住
    // ============================================================

    public void StartSequence(string startCommand)
    {
        InternalStartSequence(startCommand);
    }

    [ContextMenu("Start Rotate")]
    public void StartRotate()
    {
        StartSequence();
    }

    private void InternalStartSequence(string startCommand)
    {
        if (verboseLog)
        {
            Debug.Log(
                $"[RotatePartByAxisStep] StartSequence 被调用。Object = {name}, StartCommand = [{startCommand}]",
                this
            );
        }

        if (isRunning)
        {
            Debug.LogWarning($"[RotatePartByAxisStep] 重复启动被忽略。Object = {name}", this);
            return;
        }

        hasFinished = false;
        state = "Checking";

        if (!ValidateSettings())
        {
            Fail("参数检查失败");
            return;
        }

        CacheInitialPoseAndAxis();
        ResetRuntimeData();

        runningCoroutine = StartCoroutine(RotateRoutine());
    }

    [ContextMenu("Stop Rotate")]
    public void StopSequence()
    {
        StopRunningCoroutineOnly();

        isRunning = false;
        hasFinished = true;
        state = "Stopped";

        if (verboseLog)
        {
            Debug.Log($"[RotatePartByAxisStep] StopSequence 被调用。Object = {name}", this);
        }
    }

    [ContextMenu("Release Control")]
    public void ReleaseControl()
    {
        StopRunningCoroutineOnly();

        isRunning = false;
        hasFinished = true;
        state = "Released";

        if (verboseLog)
        {
            Debug.Log($"[RotatePartByAxisStep] 已释放控制权。Object = {name}", this);
        }
    }

    // ============================================================
    // 主协程
    // ============================================================

    private IEnumerator RotateRoutine()
    {
        isRunning = true;
        state = "Running";

        float totalAbsAngle = Mathf.Abs(angleDegrees);
        float sign = angleDegrees >= 0f ? 1f : -1f;

        float timer = 0f;
        float movedAbsAngle = 0f;

        if (totalAbsAngle <= 0.0001f)
        {
            ApplyAngle(0f);
            Complete("旋转角度为 0，直接完成");
            yield break;
        }

        if (verboseLog)
        {
            Debug.Log(
                $"[RotatePartByAxisStep] 开始旋转。" +
                $" targetPart = {targetPart.name}," +
                $" axisSpace = {axisSpace}," +
                $" rotateAxis = {rotateAxis}," +
                $" angleDegrees = {angleDegrees:F3}," +
                $" speed = {angularSpeedDegPerSec:F3}",
                this
            );
        }

        while (!hasFinished)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            timer += dt;

            if (timeoutSeconds > 0f && timer > timeoutSeconds)
            {
                Fail($"超时：{timeoutSeconds:F2} 秒内没有完成。当前角度 = {currentAngle:F3}");
                yield break;
            }

            float step = angularSpeedDegPerSec * dt;
            movedAbsAngle = Mathf.MoveTowards(movedAbsAngle, totalAbsAngle, step);

            currentAngle = movedAbsAngle * sign;
            progress01 = totalAbsAngle <= 0.0001f ? 1f : movedAbsAngle / totalAbsAngle;

            ApplyAngle(currentAngle);

            if (movedAbsAngle >= totalAbsAngle - 0.0001f)
            {
                if (snapExactlyAtEnd)
                {
                    currentAngle = angleDegrees;
                    progress01 = 1f;
                    ApplyAngle(currentAngle);
                }

                Complete($"旋转完成：{currentAngle:F3}°");
                yield break;
            }

            yield return null;
        }
    }

    // ============================================================
    // 姿态缓存与应用
    // ============================================================

    private void CacheInitialPoseAndAxis()
    {
        initialLocalRotation = targetPart.localRotation;
        initialWorldRotation = targetPart.rotation;

        cachedLocalAxis = AxisToVector(rotateAxis).normalized;
        cachedWorldAxis = GetAxisWorldAtStart().normalized;

        runtimeAxisWorld = cachedWorldAxis;
    }

    private void ApplyAngle(float angle)
    {
        if (targetPart == null)
            return;

        if (axisSpace == AxisSpace.TargetLocal)
        {
            // 绕目标物自己的本地轴转。
            // 使用 initialLocalRotation * delta，避免每帧 Rotate 累积误差。
            Quaternion delta = Quaternion.AngleAxis(angle, cachedLocalAxis);
            targetPart.localRotation = initialLocalRotation * delta;
        }
        else
        {
            // 绕世界轴 / 参考物体轴转。
            // 使用世界旋转叠加。
            Quaternion delta = Quaternion.AngleAxis(angle, cachedWorldAxis);
            targetPart.rotation = delta * initialWorldRotation;
        }
    }

    private Vector3 GetAxisWorldAtStart()
    {
        Vector3 localAxis = AxisToVector(rotateAxis).normalized;

        switch (axisSpace)
        {
            case AxisSpace.TargetLocal:
                if (targetPart != null)
                {
                    return targetPart.TransformDirection(localAxis).normalized;
                }
                return localAxis;

            case AxisSpace.World:
                return localAxis.normalized;

            case AxisSpace.ReferenceLocal:
                if (referenceAxisTransform != null)
                {
                    return referenceAxisTransform.TransformDirection(localAxis).normalized;
                }
                return localAxis;

            default:
                return Vector3.forward;
        }
    }

    private static Vector3 AxisToVector(Axis axis)
    {
        switch (axis)
        {
            case Axis.X:
                return Vector3.right;

            case Axis.Y:
                return Vector3.up;

            case Axis.Z:
                return Vector3.forward;

            case Axis.NegativeX:
                return Vector3.left;

            case Axis.NegativeY:
                return Vector3.down;

            case Axis.NegativeZ:
                return Vector3.back;

            default:
                return Vector3.forward;
        }
    }

    private void ResetRuntimeData()
    {
        currentAngle = 0f;
        progress01 = 0f;
        runtimeAxisWorld = cachedWorldAxis;
    }

    // ============================================================
    // Validate
    // ============================================================

    private bool ValidateSettings()
    {
        if (targetPart == null)
        {
            Debug.LogError("[RotatePartByAxisStep] targetPart 没填。", this);
            return false;
        }

        if (axisSpace == AxisSpace.ReferenceLocal && referenceAxisTransform == null)
        {
            Debug.LogError("[RotatePartByAxisStep] axisSpace = ReferenceLocal，但 referenceAxisTransform 没填。", this);
            return false;
        }

        if (angularSpeedDegPerSec <= 0f)
        {
            Debug.LogError("[RotatePartByAxisStep] angularSpeedDegPerSec 必须大于 0。", this);
            return false;
        }

        return true;
    }

    // ============================================================
    // Complete / Fail
    // ============================================================

    private void Complete(string reason)
    {
        if (hasFinished)
            return;

        hasFinished = true;
        isRunning = false;
        state = "Completed";
        runningCoroutine = null;

        Debug.Log(
            $"[RotatePartByAxisStep] 完成。Reason = {reason}, Command = [{completeCommand}], CurrentAngle = {currentAngle:F3}",
            this
        );

        onCompleted?.Invoke(completeCommand);
    }

    private void Fail(string reason)
    {
        if (hasFinished)
            return;

        hasFinished = true;
        isRunning = false;
        state = "Failed";

        StopRunningCoroutineOnly();

        Debug.LogWarning(
            $"[RotatePartByAxisStep] 失败。Reason = {reason}, Command = [{failedCommand}], CurrentAngle = {currentAngle:F3}",
            this
        );

        onFailed?.Invoke(failedCommand);
    }

    private void StopRunningCoroutineOnly()
    {
        if (runningCoroutine != null)
        {
            StopCoroutine(runningCoroutine);
            runningCoroutine = null;
        }
    }

    // ============================================================
    // Gizmos
    // ============================================================

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos)
            return;

        if (targetPart == null)
            return;

        Vector3 origin = targetPart.position;
        Vector3 axisWorld;

        if (Application.isPlaying && isRunning)
        {
            axisWorld = runtimeAxisWorld.normalized;
        }
        else
        {
            axisWorld = GetPreviewAxisWorld().normalized;
        }

        Gizmos.DrawLine(origin, origin + axisWorld * 0.35f);
        Gizmos.DrawWireSphere(origin + axisWorld * 0.35f, 0.02f);
    }

    private Vector3 GetPreviewAxisWorld()
    {
        Vector3 localAxis = AxisToVector(rotateAxis).normalized;

        switch (axisSpace)
        {
            case AxisSpace.TargetLocal:
                if (targetPart != null)
                {
                    return targetPart.TransformDirection(localAxis).normalized;
                }
                return localAxis;

            case AxisSpace.World:
                return localAxis.normalized;

            case AxisSpace.ReferenceLocal:
                if (referenceAxisTransform != null)
                {
                    return referenceAxisTransform.TransformDirection(localAxis).normalized;
                }
                return localAxis;

            default:
                return Vector3.forward;
        }
    }
}