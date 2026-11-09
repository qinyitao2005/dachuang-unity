# 三工位 L/C 示教器：接入索引

更新：2026-11-09。当前普通入口已统一使用组员为第一台设计的 SELECT、POINT、TOUCHUP、SHIFT + FWD 流程。

## 正式操作步骤

- [Station1 循迹编程](../操作说明/Station1循迹编程.md)：安全点、探针循迹与评分。
- [Station2 套圈、Station3 画图编程](../操作说明/Station2和Station3示教器编程.md)：共同按键、套圈限制、画笔状态与保存。
- [2026-11-09 修改与 AI 参与说明](../操作说明/2026-11-09_三工位示教器接入_AI修改说明.md)：本次范围与验收边界。
- [验收索引](控制器编程验收.md)：区分本次检查与历史证据。

操作步骤只维护上述正式文件，本页说明模块分工与历史入口。

## 当前模块职责

| 模块 | 职责 |
|---|---|
| LabTrainingApp | 普通入口、每工位独立控制器实例、输入焦点、控制权互锁、相机和状态提示。 |
| LabStation1PendantView | 共用组员控制器外观和按钮命中区。保留原类名及资源引用，实际支持三台。 |
| LabStation1Programming | SELECT 命名/打开、自动保存、POINT/TOUCHUP、行选择、SHIFT、STEP、HOLD、ABORT、故障 RESET。 |
| LabPendantStationProfile | 工位坐标、程序默认名前缀、录点笔状态、首点及课堂规则。 |
| LabPendantProgramModel | 稳定点号、L/C 指令、圆弧经过点和终点、笔状态、数据校验和修订号。 |
| LabPendantMotionPath | 独立直线/三点圆弧几何、弧长采样和解析边界。 |
| LabPendantExecutor | 程序快照、选行运行、暂停/恢复、整条指令单步、FINE 到位与故障处理。 |
| LabPendantScreenView | LCD 内容布局、坐标、状态、程序名输入和动态软键。 |
| LabPendantProgramStorage | PendantPrograms/Station01、Station02、Station03 本机文件隔离。 |
| LabStation1JogButton | 按住、松开、移出按钮的点动事件；实际运动交给工位适配器。 |

三台新示教器指令均固定 20 mm/sec、FINE。不会改变工位运动上限、关节或碰撞参数；真实运动仍经过 LabRobotStation / Group0927 适配器。圈心、笔尖和探针使用各自真实工具位置。

控制器依赖位于 Assets/ControllerUI，沿用原资源 GUID。kongzhiqi 是保留的原控制器工程，不应把整个嵌套 Unity 工程重复复制进主 Assets。

## 历史录制与兼容接口

LabPendantController、LabPendantFilePanel、LabPendantJogButton 和旧演示模块仍保留，用于历史录制与诊断接口。它们的 **F1 直线、F2 圆弧、F4 调速、RESET 停止、DATA 文件菜单**属于旧流程，不能作为三台普通入口的操作说明。

Play L-C Recording.cmd、Robot Lab > Build L-C Recording Demo 和 --recording-showcase 继续播放约 3 分 40 秒的历史流程，直接进入 03 工位并用字幕标记旧按钮；本次未把该自动演示改为新的 SELECT/POINT/TOUCHUP 流程。

本次新流程应在普通主场景或 RobotLab.exe 中验收。旧 --pendant-smoke、--pendant-execution-smoke 只验证它们调用的历史接口与共享底层，不能单独证明新按钮流程通过。

这是虚拟实训控制器，不连接实体机器人控制柜或硬件安全回路。
