# 机械臂虚拟实训室

使用 **Unity 2022.3** 打开本仓库，当前验证版本为 **2022.3.61f1c1**。主场景：`Assets/RoboticsLab/Scenes/RoboticsTrainingRoom.unity`。

正常流程：领取并佩戴安全帽 → 右墙视频学习与问答 → 进入工位。

- **01 循迹**：J 型手动循迹；圆、三角形、正方形和复合曲线自动循迹，按实际探针轨迹评分，不生成新笔迹。
- **02 套圈**：夹持圆环沿钢丝移动，保留钢丝端点、环缘与机械臂碰撞限制。
- **03 画图**：自由绘图和 L/C 示教编程，按每行保存的抬落笔状态生成笔迹。

## 控制器 L/C 编程

在 02 或 03 工位打开“示教器 / L・C 编程”。F1 记录 L 终点，F2 依次记录 C 中间点和终点，数据出现在组员原控制器的 LCD 内。

**FWD** 运行/恢复，**HOLD** 暂停，**STEP** 单步执行一条完整指令，**RESET** 停止但保留程序，**DATA** 保存/加载或追加返回首点的 L 指令。C 使用经过中间点的三点圆弧；运动继续经过原工位的 IK 和碰撞适配器。

“抬笔”目前控制是否生成笔迹，不会自动升高末端。先抬笔再添加返回 P[1]，即可无痕回程。

详细步骤及模块职责见[控制器 L/C 编程说明](Documentation/控制器L-C编程接入.md)，验证记录见[验收说明](Documentation/控制器编程验收.md)。

## 构建与慢速演示

在 Unity 中使用 `Robot Lab > Build Windows Demo` 构建正常 Windows 演示，再运行根目录 `Play Demo.cmd`。只录制编程时，使用 `Robot Lab > Build L-C Recording Demo`，再运行 `Play L-C Recording.cmd`。后者会直接进入 03 工位，用字幕和按钮标记逐步展示完整 L/C 流程，约 3 分 40 秒。

`Builds`、`Deliverables` 和 Unity 缓存不进入 Git；组员拉取源码后在本机构建。`Assets/ControllerUI` 已包含主场景所需的控制器依赖。`kongzhiqi` 为保留的原控制器工程，不需再次复制进主 Assets。
