using UnityEngine;

[RequireComponent(typeof(Renderer))]
[DefaultExecutionOrder(500)]
public class LightControl : MonoBehaviour
{
    [Header("引用")]
    public armProperty arm;   // 拖入机械臂脚本，读它的 armIsMoving

    [Header("颜色配置")]
    public Color movingColor = Color.green;   // armIsMoving = true
    public Color idleColor = Color.yellow;  // armIsMoving = false
    public Color faultColor = Color.red;     // 纸面碰撞警告，抬笔后恢复
    [Range(0f, 10f)] public float intensity = 2f;

    static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    Renderer stripRenderer;
    MaterialPropertyBlock mpb;
    bool lastMoving;
    bool lastFault;
    bool inited;
    RoboticsLab.LabRobotStation station;
    bool paperContactWarning;
    float lowestWarningZ;

    void Awake()
    {
        stripRenderer = GetComponent<Renderer>();
        mpb = new MaterialPropertyBlock();
        station = GetComponentInParent<RoboticsLab.LabRobotStation>();
    }

    void Start()
    {
        // 初始先刷一次
        ApplyByState();
    }

    public void RaisePaperContactWarning(float zMeters)
    {
        paperContactWarning = true;
        lowestWarningZ = zMeters;
    }

    void LateUpdate()
    {
        if (paperContactWarning && station)
        {
            float z = station.transform.InverseTransformPoint(station.ActualToolPosition).y - station.drawingHeight;
            // The existing warning starts at +0.5 mm. Require an actual lift
            // before clearing there; after contact, crossing Z=0 clears it.
            lowestWarningZ = Mathf.Min(lowestWarningZ, z);
            if (z > 0f && z > lowestWarningZ + .000001f)
                paperContactWarning = false;
        }

        // 只在状态变化时刷新，省性能
        bool moving = arm != null && arm.armIsMoving;
        if (!inited || moving != lastMoving || paperContactWarning != lastFault)
        {
            lastMoving = moving;
            lastFault = paperContactWarning;
            inited = true;
            ApplyByState();
        }
    }

    void ApplyByState()
    {
        Color c = paperContactWarning ? faultColor : (arm != null && arm.armIsMoving) ? movingColor : idleColor;
        ApplyColor(c);
    }

    void ApplyColor(Color c)
    {
        stripRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(EmissionColor, c * intensity);
        stripRenderer.SetPropertyBlock(mpb);
    }
}
