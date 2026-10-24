using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>A paced presentation of real pendant teaching and actual robot execution.</summary>
    public static class LabRecordingPendantFlow
    {
        public static IEnumerator Run(LabRecordingShowcase demo)
        {
            var app = demo.App;
            var station = app.ActiveStation;
            Require(station && station.IsDrawing, "录制流程需要第三台画图机械臂。");
            var controller = app.pendantController;
            Require(controller, "主场景缺少示教器。");
            demo.Caption("03 · 示教器编程与实际画图", "先复位、清理纸面，再从实际末端位置录入 L 直线与 C 圆弧。");
            app.ResetActiveExercise();
            yield return demo.Pause(4);
            controller.Open(station);
            yield return demo.Pause(4);
            controller.StopProgram();
            if (controller.Model.Instructions.Count > 0)
                yield return Press(demo, controller, "F5", "清空当前示教草稿", "F5 只清空当前控制器程序，已经保存的程序文件仍保留。");
            yield return Press(demo, controller, "F4", "F4 · 选择示教速度", "从默认 100 调到 50 mm/sec；新录入的指令使用这个速度。");

            demo.Caption("手动示教 · 纸面起点", "落笔贴到纸面，再抬笔保留当前位置；接下来所有点都取自机械臂实际 TCP。");
            station.SetPen(true);
            yield return demo.Pause(2);
            station.SetPen(false);
            Vector3 paperStart = station.exerciseCenter;
            paperStart.y = station.drawingHeight;
            yield return ToPoint(station, paperStart, .04f);
            yield return demo.Pause(3);
            yield return Press(demo, controller, "F1", "F1 · 记录起点 L P[1]", "首条直线保存抬笔状态；运行时机械臂先到达纸面起点。");
            Require(controller.Model.Instructions.Count == 1 && !controller.Model.Instructions[0].PenDown, "首个抬笔 L 点未成功记录。");

            Vector3 start = PointPosition(controller, 0);
            Vector3 lineEnd = start + Vector3.right * .020f;
            lineEnd.y = station.drawingHeight;
            demo.Caption("手动示教 · 20 mm 直线", "按 P 落笔，沿纸面移动；按钮记录实际位置，也记录当前的落笔状态。");
            station.SetPen(true);
            yield return demo.Pause(2);
            yield return ToPoint(station, lineEnd, .012f);
            yield return demo.Pause(3);
            yield return Press(demo, controller, "F1", "F1 · 记录 L P[2]", "第二行是落笔直线，屏幕同时显示点号、速度与 FINE 到点方式。");
            Require(controller.Model.Instructions.Count == 2 && controller.Model.Instructions[1].PenDown, "落笔直线未成功记录。");
            Vector3 arcStart = PointPosition(controller, 1);
            Vector3 via = arcStart + new Vector3(.010f, 0, .010f);
            Vector3 arcEnd = arcStart + Vector3.right * .020f;
            demo.Caption("C 圆弧 · 先示教中间点", "从上一行终点出发，先移动到圆弧经过的中间点。C 必须有中间点和终点。");
            yield return ToPoint(station, via, .012f);
            yield return demo.Pause(3);
            yield return Press(demo, controller, "F2", "F2 · 记录圆弧中间点 P[3]", "C 行此时显示 P[3] P[?]，表示还缺少终点，不能运行。");
            Require(controller.Model.HasPendingArc, "C 中间点未成功记录。");
            demo.Caption("C 圆弧 · 再示教终点", "移动到终点后再按一次 F2；同一条 C 行会填入终点，而不是增加第二条圆弧。");
            yield return ToPoint(station, arcEnd, .012f);
            yield return demo.Pause(3);
            yield return Press(demo, controller, "F2", "F2 · 完成 C P[3] P[4]", "运行时会沿三点确定的圆弧经过 P[3]，不会用两段直线代替。");
            Require(!controller.Model.HasPendingArc && controller.Model.Instructions.Count == 3, "完整 C 行未成功记录。");

            demo.Caption("抬笔 · 返回起点", "先抬笔，再用 DATA 页追加返回已记录的 P[1]，保留原点号，不重复建立新点。");
            station.SetPen(false);
            yield return demo.Pause(3);
            yield return Press(demo, controller, "DATA", "DATA · 打开程序文件页", "保存、加载与返回首点都在控制器本来的 LCD 内操作。");
            yield return PressFile(demo, controller, "ReturnFirstPoint", "追加 L P[1]", "程序最后增加抬笔回程，复用第一个点的三维坐标。");
            Require(controller.Model.Instructions.Count == 4 && controller.Model.Instructions[3].TargetPoint == controller.Model.Points[0].Number, "返回首点指令未成功追加。");
            string name = "Recording_" + DateTime.Now.ToString("HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 5);
            SetFilename(controller, name);
            demo.Caption("保存当前 L/C 程序", "程序名：" + name + "。点位、速度和每行抬落笔状态会一起保存。");
            yield return demo.Pause(4);
            yield return PressFile(demo, controller, "SaveProgram", "点击保存", "使用本次独有的程序名，不覆盖已有文件。");
            Require(Array.IndexOf(controller.ProgramNames(), name) >= 0, "示教程序未成功保存。");
            yield return PressFile(demo, controller, "CloseProgramFiles", "回到编程列表", "完整程序是 L 起点 → L 直线 → C 圆弧 → L 回 P[1]。");

            demo.Caption("清理示教痕迹 · 准备自动运行", "按 R 复位并清空纸面，已经录好的四条程序仍完整保留。");
            app.ResetActiveExercise();
            yield return demo.Pause(4);
            Require(controller.Model.Instructions.Count == 4, "工位复位意外删除了示教程序。");
            demo.Caption("FWD · 执行刚录入的程序", "实际机械臂先到 P[1]，然后自动完成直线与圆弧；当前行会高亮。");
            yield return demo.Pause(4);
            yield return PressButton(demo, ButtonFor(controller, "FWD"));
            yield return WaitForMotion(controller.Executor, .30f);
            yield return PressImmediate(demo, controller, "HOLD", "HOLD · 途中暂停", "保持暂停 4 秒。纸面位置不继续推进，手动移动暂时锁定。");
            Require(controller.Executor.IsPaused, "HOLD 未进入暂停状态。");
            yield return demo.Pause(4);
            yield return PressImmediate(demo, controller, "FWD", "FWD · 继续运行", "从暂停的位置继续当前指令，直到实际末端到达最后的 P[1]。");
            yield return WaitForComplete(controller.Executor, 35);
            demo.Caption("自动运行完成", "纸面上留下真正执行的直线和圆弧，最后抬笔返回 P[1]。");
            yield return demo.Pause(5);

            yield return Press(demo, controller, "RESET", "RESET · 停止并保留程序", "RESET 释放执行控制，不会删除已经示教的 L/C 行。");
            for (int i = 0; i < 4; i++)
            {
                string label = i == 2 ? "STEP · 整条圆弧单步" : "STEP · 第 " + (i + 1) + " 条指令";
                string detail = i == 2 ? "中间点和终点属于同一条 C。一次 STEP 执行整条圆弧后再暂停。" : "每按一次 STEP，只执行一条完整指令，到点后暂停等待下一次操作。";
                yield return Press(demo, controller, "STEP", label, detail);
                yield return WaitForStopped(controller.Executor, 25);
                Require(controller.Executor.CurrentInstructionIndex == i + 1, "STEP 执行的指令数量不正确。");
                Require(i == 3 ? controller.Executor.State == LabTrajectoryState.Completed : controller.Executor.IsPaused, "STEP 未在正确的完整指令边界暂停。");
                yield return demo.Pause(3);
            }

            yield return Press(demo, controller, "RESET", "文件保存与重新加载", "接下来清空当前内存草稿，再从 DATA 加载，验证保存内容能再次执行。");
            yield return Press(demo, controller, "DATA", "DATA · 程序文件", "已保存的程序仍列在 LCD 内。选中保存的名称后可以加载。");
            SetFilename(controller, name);
            yield return PressFile(demo, controller, "SaveProgram", "保存当前程序", "四条指令及其点号、速度、抬落笔状态已保存。");
            yield return PressFile(demo, controller, "CloseProgramFiles", "关闭文件页", "先清空当前草稿，以便直观看出加载恢复的结果。");
            yield return Press(demo, controller, "F5", "F5 · 清空当前列表", "当前 LCD 只剩 [End]；磁盘上的程序文件仍然保留。");
            Require(controller.Model.Instructions.Count == 0, "F5 未清空草稿。");
            yield return Press(demo, controller, "DATA", "DATA · 加载已存程序", "使用刚才的程序名恢复，加载后可继续运行。");
            SetFilename(controller, name);
            yield return demo.Pause(3);
            yield return PressFile(demo, controller, "LoadProgram", "点击加载", "恢复 L、C、原点号、点位坐标与逐行抬落笔状态。");
            Require(controller.Model.Instructions.Count == 4 && !controller.Model.HasPendingArc, "文件加载后程序不完整。");
            yield return PressFile(demo, controller, "CloseProgramFiles", "已恢复四条指令", "列表与保存前相同，执行器已绑定刚加载的程序。");
            app.ResetActiveExercise();
            yield return demo.Pause(3);
            yield return Press(demo, controller, "FWD", "执行加载后的程序", "重新绘制同一条直线与圆弧，确认加载后的数据实际参与机器人运动。");
            yield return WaitForComplete(controller.Executor, 35);
            yield return demo.Pause(5);

            demo.Caption("最后展示 · 急停与明确恢复", "重新运行时触发控制器红色急停，解除急停后也不会自行恢复运动。");
            app.ResetActiveExercise();
            yield return demo.Pause(3);
            yield return PressImmediate(demo, controller, "FWD", "开始急停演示", "机器人正在执行前往首点的直线。");
            yield return WaitForMotion(controller.Executor, .25f);
            yield return PressImmediate(demo, controller, "EmergencyStop", "红色急停 · 立即暂停", "保持 4 秒；机械臂停止，落笔状态解除，执行控制仍保留。");
            Require(station.EmergencyStopped && controller.Executor.IsPaused, "红色急停未暂停当前程序。");
            yield return demo.Pause(4);
            yield return PressImmediate(demo, controller, "EmergencyStop", "解除急停", "解除后仍保持 HOLD。等待 3 秒，确认不会自动续跑。");
            Require(!station.EmergencyStopped && controller.Executor.IsPaused, "解除急停后错误地自动恢复了程序。");
            yield return demo.Pause(3);
            yield return PressImmediate(demo, controller, "FWD", "FWD · 人工确认继续", "再次按 FWD 后才恢复运动，并完整完成这次 L/C 画图。");
            yield return WaitForComplete(controller.Executor, 35);
            demo.Caption("03 · 完整示教编程流程已演示", "录实际点 → L/C 编程 → 保存加载 → 运行/暂停 → 单步 → 急停恢复 → 完成画图。");
            yield return demo.Pause(7);
        }

        static Vector3 PointPosition(LabPendantController controller, int instruction)
        {
            var row = controller.Model.Instructions[instruction];
            var point = controller.Model.FindPoint(row.TargetPoint);
            Require(point != null, "示教点不存在。");
            return point.LocalPosition;
        }
        static IEnumerator ToPoint(LabRobotStation station, Vector3 localDestination, float feedMetres)
        {
            float until = Time.realtimeSinceStartup + 20;
            int settled = 0;
            while (Time.realtimeSinceStartup < until)
            {
                Require(station.IsOperating && !station.EmergencyStopped, "手动示教被退出或急停中断。");
                Vector3 target = station.transform.InverseTransformPoint(station.toolTarget.position);
                Vector3 delta = localDestination - target;
                Vector3 actual = station.transform.InverseTransformPoint(station.ActualToolPosition);
                if (delta.magnitude <= .00008f && Vector3.Distance(actual, localDestination) <= .0013f)
                {
                    station.MoveTool(Vector3.zero);
                    if (++settled >= 5) yield break;
                }
                else
                {
                    settled = 0;
                    float velocity = Mathf.Min(feedMetres, station.moveSpeed);
                    float input = Mathf.Min(velocity / station.moveSpeed, delta.magnitude / Mathf.Max(.000001f, station.moveSpeed * Time.deltaTime));
                    station.MoveTool(station.transform.TransformDirection(delta.normalized) * input);
                }
                yield return null;
            }
            station.MoveTool(Vector3.zero);
            throw new InvalidOperationException("实际 TCP 未能到达手动示教点：" + localDestination + "；" + station.DrawingBlockReason);
        }
        static IEnumerator WaitForMotion(LabPendantExecutor executor, float seconds)
        {
            Require(executor.State == LabTrajectoryState.Running, "程序没有开始运行：" + executor.StatusText);
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                Require(executor.State == LabTrajectoryState.Running, "程序在预期暂停前已停止：" + executor.StatusText);
                yield return null;
            }
        }
        static IEnumerator WaitForStopped(LabPendantExecutor executor, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (executor.State == LabTrajectoryState.Running && Time.realtimeSinceStartup < end) yield return null;
            Require(executor.State != LabTrajectoryState.Running && executor.State != LabTrajectoryState.Faulted, "L/C 执行未正常到点：" + executor.StatusText + " " + executor.LastError);
        }
        static IEnumerator WaitForComplete(LabPendantExecutor executor, float seconds)
        {
            yield return WaitForStopped(executor, seconds);
            Require(executor.State == LabTrajectoryState.Completed, "程序未完整执行：" + executor.StatusText);
        }
        static IEnumerator Press(LabRecordingShowcase demo, LabPendantController controller, string key, string title, string detail)
        {
            demo.Caption(title, detail); yield return demo.Pause(2);
            yield return PressButton(demo, ButtonFor(controller, key)); yield return demo.Pause(2);
        }
        static IEnumerator PressImmediate(LabRecordingShowcase demo, LabPendantController controller, string key, string title, string detail)
        {
            demo.Caption(title, detail); yield return PressButton(demo, ButtonFor(controller, key));
        }
        static IEnumerator PressFile(LabRecordingShowcase demo, LabPendantController controller, string name, string title, string detail)
        {
            demo.Caption(title, detail); yield return demo.Pause(2);
            Button found = null;
            foreach (var button in controller.GetComponentsInChildren<Button>(true))
                if (button.name == name && button.gameObject.activeInHierarchy) { found = button; break; }
            Require(found, "LCD 页面缺少按钮：" + name);
            yield return PressButton(demo, found); yield return demo.Pause(2);
        }
        static Button ButtonFor(LabPendantController controller, string key)
        {
            Button found;
            Require(controller.Buttons.TryGetValue(key, out found) && found, "控制器缺少按钮：" + key);
            return found;
        }
        static IEnumerator PressButton(LabRecordingShowcase demo, Button button)
        {
            Require(button && button.interactable && button.gameObject.activeInHierarchy, "按钮当前不可用。");
            Require(EventSystem.current, "主场景缺少 EventSystem。");
            Canvas.ForceUpdateCanvases();
            RectTransform rect = button.GetComponent<RectTransform>();
            Canvas canvas = button.GetComponentInParent<Canvas>();
            Camera camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
            demo.Highlight(screen);
            var pointer = new PointerEventData(EventSystem.current) { position = screen, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            GameObject top = hits.Count > 0 ? hits[0].gameObject : null;
            Require(top && (top == button.gameObject || top.transform.IsChildOf(button.transform)), "按钮被其他界面遮挡：" + button.name + "；" + (top ? top.name : "无射线结果"));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.22f);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            Require(ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler), "按钮点击未被处理：" + button.name);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            yield return null;
        }
        static void SetFilename(LabPendantController controller, string value)
        {
            InputField field = null;
            foreach (var item in controller.GetComponentsInChildren<InputField>(true))
                if (item.name == "ProgramName" && item.gameObject.activeInHierarchy) { field = item; break; }
            Require(field, "DATA 页缺少程序名输入框。");
            field.text = value; field.DeactivateInputField(); EventSystem.current.SetSelectedGameObject(null);
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
