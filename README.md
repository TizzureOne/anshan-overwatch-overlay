# 安山守望

用于《守望先锋》教学的 Windows 桌面标注工具。拖放英雄头像、绘制战术路线，通过 OBS 输出透明标注。

当前版本：**0.1.2-beta（测试版）**。

## 下载

[版本下载](https://github.com/TizzureOne/anshan-overwatch-overlay/releases) · Windows 10/11 x64

同一个版本提供两种安装包，功能相同：

- **完整版**：自带所需运行组件，适合直接安装使用。
- **精简版（文件名含 Lite）**：省去运行组件，下载更小。需要 [.NET 8 Desktop Runtime 和 ASP.NET Core Runtime](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)，均选择 Windows x64；缺少时安装程序会列出所缺组件，并提供官方下载入口，补齐后再继续。

安装后从开始菜单启动。免安装版解压后双击 `AnshanOverlay.exe`。程序内置 54 位英雄头像（含血律），黑影位于“支援”分类。

## 使用

1. 在“场景设置”选择教学屏幕。
2. 在英雄库选择蓝方或红方，再拖放头像；右键可调整已放置头像的阵营和尺寸。
3. 用工具栏绘制线条、箭头、椭圆和矩形；用移动工具调整标注，用橡皮擦擦除局部。
4. 按 `Ctrl+Alt+E` 切换编辑与鼠标穿透，继续操作游戏或回放。

拖动窗口空白处可移动面板和工具栏，拖动边缘可调整尺寸。两者都支持折叠；关闭按钮隐藏窗口，电源图标或“退出程序”结束程序。

| 默认快捷键 | 操作 |
|---|---|
| Ctrl+Alt+E | 编辑 / 鼠标穿透 |
| Ctrl+Alt+P | 恢复面板与工具栏 |
| Ctrl+Alt+T | 画布置顶 |
| Ctrl+Alt+H | 显示 / 隐藏标注 |

工具按钮显示各自的快捷键，点击齿轮可自定义。

批量添加：开启英雄库的“批量选择”，点选英雄后按“添加”。可跨职责和搜索选择，添加后整组移动。

多选与框选：使用“移动”工具（`V`），`Shift+点击` 加选或取消；从空白处拖动，框住整个对象即可选中，`Shift+框选` 追加。拖动已选对象可整体移动，`Delete` 删除，`Ctrl+D` 复制，`Ctrl+Z` 撤销整次操作。

点击空白清除选择；`Esc` 依次取消当前操作、清除选择、进入鼠标穿透。

场景自动保存，也可在“场景设置”中手动保存和加载。数据位于 `%LOCALAPPDATA%\AnshanOverlay`，卸载后保留。

## OBS

1. 在“OBS / 帮助”复制地址。
2. 在 OBS 添加“浏览器”源，粘贴地址，宽高填写面板显示的画布尺寸。
3. 将标注源放在游戏或视频源上方，对齐位置、缩放和裁剪。

浏览器源输出英雄与标注。使用“显示器捕获”时，直接录制桌面上的标注即可。

## 开发

环境：Windows、.NET 8 SDK；制作安装包使用 [Inno Setup 6.7+](https://jrsoftware.org/isdl.php)。在仓库根目录运行：

```powershell
dotnet build src/AnshanOverlay/AnshanOverlay.csproj -c Release
./tools/test.ps1
./tools/publish.ps1 -CompilerPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
./tools/publish.ps1 -Lite -CompilerPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

`src/AnshanOverlay/` 包含应用、头像与 OBS 页面；`tools/` 包含素材生成、测试和打包脚本。安装包输出到 `dist/`，测试结果输出到 `artifacts/qa/`。

英雄素材及组件来源见 [第三方声明](THIRD-PARTY-NOTICES.md)。
