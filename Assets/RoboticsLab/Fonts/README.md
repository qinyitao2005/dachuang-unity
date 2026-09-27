# 项目内置中文字体

`NotoSansSC-Regular.ttf` 来自 Google Fonts 官方下载清单中的 **static/NotoSansSC-Regular.ttf**，未修改或转换。它是静态 TrueType 字体，400 字重，包含 30,898 个字形；没有可变字体 `fvar` 表，也不是 TTC 字体集合。

- 官方字体页面：https://fonts.google.com/noto/specimen/Noto+Sans+SC
- 本次官方清单：https://fonts.google.com/download/list?family=Noto%20Sans%20SC
- 精确下载地址、SHA-256 和检查结果：`FontSource.json`
- 许可：SIL Open Font License 1.1，完整文本见同目录 `OFL.txt`

下载时已检查 RoboticsLab C# 源文件中的 283 个非 ASCII 字符，未发现缺字。实际 Unity 导入与构建画面应由工程验证流程核对。

编辑器内的资产路径为：

```csharp
AssetDatabase.LoadAssetAtPath<Font>("Assets/RoboticsLab/Fonts/NotoSansSC-Regular.ttf")
```

运行时代码应使用组件序列化的 `Font` 引用；不要依赖 `AssetDatabase` 或操作系统字体名称。此目录不是 `Resources` 目录，因此不能直接按这个路径使用 `Resources.Load`。

字体文件可随本项目按 OFL 一起分发。完整许可另外放在 `Assets/StreamingAssets/ThirdPartyNotices/NotoSansSC-OFL.txt`，以便构建产物包含可阅读的许可文本。该字体未取自 Windows 字体目录。
