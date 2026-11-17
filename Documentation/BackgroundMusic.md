# 实训室背景音乐

使用用户提供的 `Robotics Training Grounds (96 BPM).wav`，完整时长 **182.464 秒**。原文件保留在用户下载目录；工程副本位于 `Assets/RoboticsLab/Resources/RoboticsLabAudio/RoboticsTrainingGrounds.wav`。

副本仅通过 FFmpeg 的 `-map 0:a:0 -c:a copy -map_metadata -1` 去掉嵌入的 MJPEG 封面和容器元数据，没有截断、变速或重新编码音频。两份文件解码到 PCM16 后的 SHA-256 完全一致：`99b62bb38d1221d38ff7e4611e81e194fbd938a2166e791eb336fd706a659f3c`。文件哈希、采样率与导入设置记录在音频旁的 `.provenance.json` 中。

## 接入方式

- `LabBackgroundMusic` 是独立组件，进入包含 `LabTrainingApp` 的场景时自动创建一个播放器，跨场景保持，避免叠播。不需要给三台机器分别挂脚本，也不修改它们的控制器。
- 默认音乐源音量为 **24%**，启动时约 2 秒淡入。全局音量仍由原有齿轮设置面板和 `AudioListener.volume` 控制，保存键 `RobotLab.Volume` 保持原样。
- 观看工位演示或墙屏课程（`app.DemoVideoVisible` / `app.panel == "lesson"`）时，音乐平滑降低到 **8.5%**；退出后恢复。播放和音量渐变使用 DSP / unscaled 时间，`Time.timeScale = 0` 时继续。
- 两个非空间 AudioSource 以 2 秒等功率交叉淡化衔接原曲尾部和开头，降低循环的突兀感。保留整段音频，交叉淡化仅在播放时重叠首尾。
- `LabBackgroundMusicImporter` 将指定资源导入为 **Vorbis 0.70 / Streaming / 保留 48 kHz 立体声**，避免运行时解压整首 PCM。35 MB WAV 是工程源素材，构建中使用压缩音频。

初次导入会自动应用设置。需要手动重导入时用菜单 `Robotics Lab > Audio > Configure Supplied Background Music`，或批处理入口 `RoboticsLab.Editor.LabBackgroundMusicImporter.ConfigureImport`。播放器自动安装，不需要改主场景。

## 验证方法

1. 在正常有音频设备的 Windows Player 启动后等待至少 3 秒。检查 `LabBackgroundMusic.Track` 时长约 182.464 秒，并对 `Sources` 的两个 AudioSource 调用 `GetOutputData` 采样，确认至少一个源 RMS 非零；同时监听实际声音。只检查 `isPlaying` 不足以证明有输出。
2. 把设置音量调到 0，应听不到音乐；恢复音量应继续播放而非重新起播。代码验证可短暂设置 `AudioListener.volume`，并采样混音输出；不要为自动检查覆盖 PlayerPrefs。不同 Unity 版本的输出采样位置可能在音量应用之前，遇到这种情况用系统回环录音核验实际静音。
3. 打开演示视频，确认 `IsDucked` 为 true、`CurrentGain` 接近 0.085，视频暂停游戏时音乐位置继续推进；关闭后恢复到 0.24。
4. 快速循环检查：记录 `CompletedLoops`，调用 `Seek(Track.length - .5f)`，约 0.65 秒后循环计数加一；再等至少 1 秒并采样输出，确认另一源继续有实际音频。Seek 同时安排两个源的正确偏移，避免仅改 `AudioSource.time` 破坏循环时间。
5. 在耳机或音箱上听一次完整的首尾衔接，确认没有停顿、爆音或突然变大。自动输出/循环检查不能代替这项听感检查。

2026-11-17 已在实际 Windows Player 执行 `room-refinish-smoke`：源和混音峰值非零，全局音量 0 时混音峰值为 0，实训暂停期间继续输出，视频状态降音量/关闭恢复及真实曲尾交叉淡化循环均通过，见 `Documentation/RoomRefinishSmoke.json`。这项自动检查不等于完整人工听感验收。
