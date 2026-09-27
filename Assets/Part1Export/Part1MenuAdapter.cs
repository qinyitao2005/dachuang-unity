using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using RobotTeaching;
using RoboticsLab;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Runtime-only wiring: does not save or rewrite scenes, prefabs or model transforms.
[DefaultExecutionOrder(-600)]
public sealed class Part1MenuAdapter : MonoBehaviour
{
    private const string JogCommand = "输入给机械臂XYZ手动移动";
    private LabTrainingApp app;
    private PublicCommandSequenceController main;
    private 机械臂XYZ手动移动控制器 jog;
    private 轨迹点记录控制器 recorder;
    private 机械臂轨迹重复控制器 replay;
    private 轨迹速度设置控制器 speed;
    private RobotTrajectoryMenu menu;
    private bool ready;
    private bool initialized;
    private bool exiting;
    private readonly bool[] held = new bool[6];
    private readonly UnityAction[] pressed = new UnityAction[6];
    private readonly UnityAction[] released = new UnityAction[6];

    public bool CanOperate => initialized && ready && app && app.Part1接管中 &&
        main && main.IsRunning && 公屏叫号.Instance != null &&
        公屏叫号.Instance.当前是(JogCommand) && recorder.IsRecording && !replay.IsPlaying;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++)
            InstallInScene(SceneManager.GetSceneAt(i));
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => InstallInScene(scene);

    private static void InstallInScene(Scene scene)
    {
        foreach (var candidate in Resources.FindObjectsOfTypeAll<LabTrainingApp>())
        {
            if (candidate.gameObject.scene != scene || !candidate.part1启动桥) continue;
            var bridge = candidate.part1启动桥;
            if (bridge.GetComponent<Part1MenuAdapter>()) continue;
            var adapter = bridge.gameObject.AddComponent<Part1MenuAdapter>();
            adapter.Connect(candidate, bridge);
        }
    }

    private void Connect(LabTrainingApp owner, Part1启动桥 bridge)
    {
        app = owner;
        main = bridge.教学主控;
        Transform scope = bridge.transform.parent;
        while (scope && !scope.GetComponentInChildren<机械臂XYZ手动移动控制器>(true))
            scope = scope.parent;
        if (scope)
        {
            jog = scope.GetComponentInChildren<机械臂XYZ手动移动控制器>(true);
            recorder = scope.GetComponentInChildren<轨迹点记录控制器>(true);
            replay = scope.GetComponentInChildren<机械臂轨迹重复控制器>(true);
            speed = scope.GetComponentInChildren<轨迹速度设置控制器>(true);
        }
        if (!main || !jog || !recorder || !replay || !speed)
        {
            Debug.LogError("[Part1菜单] 缺少主控、XYZ、记录、复现或速度组件，未接管原流程。", this);
            enabled = false;
            return;
        }

        var menus = Resources.FindObjectsOfTypeAll<RobotTrajectoryMenu>()
            .Where(item => item.gameObject.scene == app.gameObject.scene).ToArray();
        var local = menus.Where(item => item.transform.IsChildOf(scope.parent ? scope.parent : scope)).ToArray();
        if (local.Length == 1) menu = local[0];
        else if (menus.Length == 1) menu = menus[0];
        else if (menus.Length == 0)
        {
            var root = new GameObject("机械臂循迹菜单（运行时）");
            root.transform.SetParent(scope.parent ? scope.parent : scope, false);
            menu = root.AddComponent<RobotTrajectoryMenu>();
        }
        else
        {
            Debug.LogError("[Part1菜单] 场景有多个菜单，无法唯一确定 Part1 菜单；请只保留一份。", this);
            enabled = false;
            return;
        }
        if (menu.bindings == null || menu.bindings.Length != 9)
        {
            Debug.LogError("[Part1菜单] 需要原始的九个按钮配置。", this);
            enabled = false;
            return;
        }
        if (!menu.bindings[0].button) menu.Build();
        speed.启用菜单调速();
        menu.EnsureSpeedControls();
        menu.speedDecrease.onClick.AddListener(DecreaseSpeed);
        menu.speedIncrease.onClick.AddListener(IncreaseSpeed);
        menu.RefreshSpeed(speed.CurrentSpeed, false);
        // Keyboard remains owned by the existing Part1 scripts; labels use their actual keys.
        menu.readKeyboard = false;
        for (int i = 0; i < 6; i++)
        {
            int index = i;
            menu.bindings[i].continuous = true;
            pressed[i] = () => SetHeld(index, true);
            released[i] = () => SetHeld(index, false);
            menu.bindings[i].onPressed.AddListener(pressed[i]);
            menu.bindings[i].onReleased.AddListener(released[i]);
        }
        for (int i = 6; i < 9; i++) menu.bindings[i].continuous = false;
        menu.bindings[6].onPressed.AddListener(RecordPoint);
        menu.bindings[7].onPressed.AddListener(StartTrace);
        menu.bindings[8].onPressed.AddListener(ExitModule);
        initialized = true;
        RefreshKeys();
        menu.gameObject.SetActive(false);
        Debug.Log("[Part1菜单] 已自动连接；进入手动操作后显示，Esc 或退出返回漫步。", this);
    }

    private void Update()
    {
        if (!initialized || !menu || !app) return;
        if (!ready && app.Part1接管中 && main.IsRunning &&
            公屏叫号.Instance != null && 公屏叫号.Instance.当前是(JogCommand))
        {
            ready = true;
            recorder.开始新的轨迹();
        }
        bool visible = ready && app.Part1接管中;
        if (!visible)
        {
            ClearHeld();
            if (menu.gameObject.activeSelf) menu.gameObject.SetActive(false);
            return;
        }
        bool escape = false;
#if ENABLE_INPUT_SYSTEM
        escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        escape = Input.GetKeyDown(KeyCode.Escape);
#endif
        if (escape) { ExitModule(); return; }
        if (!menu.gameObject.activeSelf) menu.gameObject.SetActive(true);
        RefreshKeys();
        bool allow = CanOperate;
        menu.RefreshSpeed(speed.CurrentSpeed, visible);
        for (int i = 0; i < 8; i++)
            if (menu.bindings[i].button)
                menu.bindings[i].button.interactable = allow && (i != 7 || recorder.PointCount > 0);
        if (menu.bindings[8].button) menu.bindings[8].button.interactable = true;
        if (!allow) ClearHeld();
    }

    private void RefreshKeys()
    {
        for (int i = 0; i < 9; i++)
        {
            KeyCode key = i < 6 ? jog.获取方向按键(i) :
                i == 6 ? KeyCode.E : i == 7 ? KeyCode.Q : KeyCode.Escape;
            menu.bindings[i].key = key;
#if ENABLE_INPUT_SYSTEM
            Key mapped;
            menu.bindings[i].inputSystemKey = Enum.TryParse(key.ToString(), out mapped) ? mapped : Key.None;
#endif
        }
    }

    private void SetHeld(int index, bool value)
    {
        held[index] = value && CanOperate;
        jog.设置菜单方向(new Vector3(
            (held[0] ? 1 : 0) - (held[1] ? 1 : 0),
            (held[2] ? 1 : 0) - (held[3] ? 1 : 0),
            (held[4] ? 1 : 0) - (held[5] ? 1 : 0)));
    }

    private void ClearHeld()
    {
        Array.Clear(held, 0, held.Length);
        if (jog) jog.设置菜单方向(Vector3.zero);
    }

    public void RecordPoint()
    {
        if (CanOperate) recorder.记录当前点();
    }

    public void DecreaseSpeed() => ChangeSpeed(-1);
    public void IncreaseSpeed() => ChangeSpeed(1);
    private void ChangeSpeed(int direction)
    {
        if (!initialized || !ready || !app || !app.Part1接管中) return;
        speed.调整菜单速度(direction);
        menu.RefreshSpeed(speed.CurrentSpeed, true);
    }

    public void StartTrace()
    {
        if (!CanOperate || recorder.PointCount == 0) return;
        ClearHeld();
        recorder.结束记录();
    }

    public void ExitModule()
    {
        if (!initialized || !ready || !app.Part1接管中 || exiting) return;
        exiting = true;
        ClearHeld();
        main.StopSequence();
        replay.停止轨迹复现();
        if (公屏叫号.Instance != null) 公屏叫号.Instance.清空();
        jog.释放控制权();
        menu.gameObject.SetActive(false);
        app.ClosePanel();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        exiting = false;
    }

    public void ResumeOperation()
    {
        if (!initialized || !ready || !app.Part1接管中 || main.IsRunning) return;
        ClearHeld();
        main.enabled = true;
        if (main.StartSequenceFromCommand(JogCommand)) recorder.开始新的轨迹();
    }

    private void OnApplicationFocus(bool focus) { if (!focus) ClearHeld(); }
    private void OnApplicationPause(bool pause) { if (pause) ClearHeld(); }
    private void OnDisable() { ClearHeld(); }
    private void OnDestroy()
    {
        if (!initialized || !menu) return;
        for (int i = 0; i < 6; i++)
        {
            menu.bindings[i].onPressed.RemoveListener(pressed[i]);
            menu.bindings[i].onReleased.RemoveListener(released[i]);
        }
        menu.bindings[6].onPressed.RemoveListener(RecordPoint);
        menu.bindings[7].onPressed.RemoveListener(StartTrace);
        menu.bindings[8].onPressed.RemoveListener(ExitModule);
        if (menu.speedDecrease) menu.speedDecrease.onClick.RemoveListener(DecreaseSpeed);
        if (menu.speedIncrease) menu.speedIncrease.onClick.RemoveListener(IncreaseSpeed);
    }
}
