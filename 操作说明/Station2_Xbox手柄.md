# Station2 Xbox 手柄操作

适用范围：Station2 钢丝套圈。示教器按键沿用 [Station1 手柄说明](Station1_Xbox手柄.md)；这里列出 Station2 的入口和套圈差异。Station3 已接入，见 [Station3 手柄说明](Station3_Xbox手柄.md)。

## 进入与退出

完成安全帽、教学和问答后，在游荡状态用左摇杆移动、右摇杆转视角，靠近 Station2 按 A 进入。示教器立即显示，左侧是套圈状态和“退出工位 [Esc]”；原右侧点动菜单不再显示。

在 `SELECT PROGRAM` 程序列表按 **Menu** 退出工位。在编辑页或 FCTN 等子页面按 Menu 先回到程序列表，再按一次 Menu 退出。键盘 Esc 和左侧退出按钮也有效。程序运行中先用 FCTN → 1 ABORT 停止，再切换页面。

## 手柄按键

| Xbox | Station2 示教器 |
|---|---|
| 左摇杆、RT/LT，按住 RB | 点动 USER X/Y/Z；松开 RB 停止 |
| A | F1 POINT；RB+A 录 L 终点或 C 经过点 |
| B | F2 CREATE（程序列表中） |
| X、Y | F4 CHOICE、F5 TOUCHUP；RB+Y 补 C 终点或更新点 |
| 十字键上/下、左/右 | 选项或程序行；FCTN / ENTER |
| Menu | 其他页面回 SELECT PROGRAM；在列表中退出工位 |
| View 短按 | PREV，松开时返回编辑页或取消删除确认 |
| View 在程序编辑页长按 5 秒 | 隐藏调试操作：清空当前程序的全部指令和点位，保留程序名并覆盖保存，不能撤销 |
| 右摇杆按下 | F3；在 SELECT PROGRAM 列表中进入删除整个程序确认 |
| 左摇杆按下 | 输入数字 1，可在 End 引用 P[1]，也可在 FCTN 选 1 ABORT |
| LB，按住 RB | FWD，执行或恢复程序；松开 RB 暂停 |

完整删除整个程序：在列表选中目标 → 右摇杆按下（F3 DELETE）→ X（F4 YES）确认，或 Y（F5 NO）取消。若已在列表，切勿先按 Menu，否则会退出工位。View 长按清空的是当前程序内容，不会删除程序文件。

## 一个套圈 L/C 例子

1. 进入后，在 SELECT PROGRAM 按 B（CREATE）→ A（F1 END），用默认的 `RING001` 等名称建程序；或上下选已有程序、十字键右打开。
2. 按住 RB，左摇杆与 RT/LT 把圆环移动到钢丝上的合法起点。松开 RB，按 A（POINT）选 L，十字键右确认并录入 `L P[1]`。
3. 移到下一段合法直线终点，RB+A 录入 `L P[2]`。若需要圆弧，移到经过点后按 A 选 C 并确认；再移到终点，RB+Y 补齐该 C。
4. 十字键上选第一行，保持 RB 后按 LB 运行。套圈运动仍受钢丝端点、圆环和机械臂碰撞限制；路径不合法时需 ABORT 后重录，不会自动绕障。

Station2 不要求 Station1 的离纸安全点或末行回 P[1]，也没有抬笔/落笔。运动指令固定 20 mm/sec，不能调速。HOLD、RESET、急停等专用操作仍需点击示教器；自定义程序名仍需键盘。更多几何和故障说明见 [Station2、Station3 示教器编程](Station2和Station3示教器编程.md)。

本次 AI 修改与验收边界见 [2026-11-13 Station2 修改说明](2026-11-13_Station2自动示教器与手柄导航_AI修改说明.md)。
