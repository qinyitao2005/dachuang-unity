using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class LightControl : MonoBehaviour
{
    [Header("引用")]
    public armProperty arm;   // 拖入机械臂脚本，读它的 armIsMoving

    [Header("颜色配置")]
    public Color movingColor = Color.green;   // armIsMoving = true
    public Color idleColor = Color.yellow;  // armIsMoving = false
    public Color faultColor = Color.red;     // 备用，故障用
    [Range(0f, 10f)] public float intensity = 2f;

    static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    Renderer stripRenderer;
    MaterialPropertyBlock mpb;
    bool lastMoving;
    bool inited;

    void Awake()
    {
        stripRenderer = GetComponent<Renderer>();
        mpb = new MaterialPropertyBlock();
    }

    void Start()
    {
        // 初始先刷一次
        ApplyByState();
    }

    void Update()
    {
        if (arm == null) return;

        // 只在状态变化时刷新，省性能
        if (!inited || arm.armIsMoving != lastMoving)
        {
            lastMoving = arm.armIsMoving;
            inited = true;
            ApplyByState();
        }
    }

    void ApplyByState()
    {
        Color c = (arm != null && arm.armIsMoving) ? movingColor : idleColor;
        ApplyColor(c);
    }

    void ApplyColor(Color c)
    {
        stripRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(EmissionColor, c * intensity);
        stripRenderer.SetPropertyBlock(mpb);
    }
}