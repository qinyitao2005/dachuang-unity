# 机械臂虚拟实训室

使用 **Unity 2022.3** 打开本仓库，当前验证版本为 **2022.3.61f1c1**。主场景：`Assets/RoboticsLab/Scenes/RoboticsTrainingRoom.unity`。

正常流程：领取并佩戴安全帽 → 右墙视频学习与问答 → 进入工位。

靠近右侧货架按 **E**，学员会走到最近的一顶安全帽前，双手取下、戴稳后松手。第三台画图工位的性能测试和防穿模验证见[复测说明](Documentation/Station3DrawingPerformance.md)。

- **01 循迹**：J 型手动循迹；圆、三角形、正方形和复合曲线自动循迹，按实际探针轨迹评分，不生成新笔迹。
- **02 套圈**：夹持圆环沿钢丝移动，保留钢丝端点、环缘与机械臂碰撞限制。
- **03 画图**：自由绘图和 L/C 示教编程，按每行保存的抬落笔状态生成笔迹。

## 操作说明与后续协作

各工位的使用步骤和 AI 修改说明统一放在 [操作说明](操作说明/README.md)。后面大家用 AI 做完功能后，请让 AI 把入口、操作步骤、修改内容、验证结果和未完成项写进这个文件夹，和代码一起提交。有问题直接联系本次提交者。

**三台示教器**共用第一台的 SELECT 新建/打开、POINT 选 L/C、TOUCHUP、选行运行和 SHIFT 使能流程，指令固定 20 mm/sec。每台保留独立程序；任务和运动约束仍按工位区分。

## 控制器 L/C 编程

进入工位后点击“示教器 / L・C 编程”。正式使用步骤见 [Station1 循迹编程](操作说明/Station1循迹编程.md) 和 [Station2 套圈、Station3 画图编程](操作说明/Station2和Station3示教器编程.md)。本次修改和检查边界见 [2026-11-09 三工位接入说明](操作说明/2026-11-09_三工位示教器接入_AI修改说明.md)。

**SHIFT + FWD** 从选中行运行/恢复，**HOLD** 暂停，**STEP** 切换单步，**FCTN → ABORT** 终止，**RESET** 复位已排除的故障。SELECT 管理程序，修改后自动保存。原 F1/F2 直录、RESET 停止、DATA 文件菜单只保留给历史录像与兼容接口。

03 用 **IO/P** 设置新录行的 UP/DOWN，抬笔只控制不留线，不会自动升高工具。02 使用真实圆环中心，没有纸面安全点或强制返回 P[1] 的要求；两台继续使用原 IK 与碰撞检查。

模块职责见[控制器接入索引](Documentation/控制器L-C编程接入.md)，当前与历史测试分开列在[验收索引](Documentation/控制器编程验收.md)。

## 比赛当天：90 秒循环展播

[比赛成片（MP4）](Promo/机械臂虚拟仿真实训平台_比赛循环版_90秒.mp4) 全长 1 分 30 秒，从 **00:18** 起可以看到新版白色学员入场、取帽和戴帽。

比赛电脑无需安装 Unity。把 [Promo 文件夹](Promo) 里的三个文件放在同一目录，双击「循环播放.html」，点击「开始展播」即可全屏循环播放。片段时间和素材来源见[播放与素材署名](Promo/播放与素材署名.txt)。

## 构建与慢速演示

在 Unity 中使用 `Robot Lab > Build Windows Demo` 构建正常 Windows 演示，再运行根目录 `Play Demo.cmd`。

`Robot Lab > Build L-C Recording Demo` 和 `Play L-C Recording.cmd` 保留约 3 分 40 秒的**历史按键流程**录制：直接进入 03 工位，以旧 F1/F2、RESET、DATA 接口展示 L/C。它不展示本次 SELECT / POINT / TOUCHUP 交互；验收新流程请使用普通主场景或 Windows 演示入口。

`Builds`、`Deliverables` 和 Unity 缓存不进入 Git；组员拉取源码后在本机构建。`Assets/ControllerUI` 已包含主场景所需的控制器依赖。`kongzhiqi` 为保留的原控制器工程，不需再次复制进主 Assets。
