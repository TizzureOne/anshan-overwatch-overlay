# 安山守望

用于《守望先锋》教学的 Windows 桌面标注工具。拖放英雄头像、绘制战术路线，通过 OBS 输出透明标注。

## 下载

[下载安装包](https://github.com/TizzureOne/anshan-overwatch-overlay/releases/latest) · Windows 10/11 x64

安装后从开始菜单启动。程序内置 53 位英雄头像和所需运行时。

## 使用

1. 在“场景设置”选择教学屏幕。
2. 从英雄列表拖放头像，右键调整阵营和尺寸。
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
```

`src/AnshanOverlay/` 包含应用、头像与 OBS 页面；`tools/` 包含素材生成、测试和打包脚本。安装包输出到 `dist/`，测试结果输出到 `artifacts/qa/`。

英雄素材及组件来源见 [第三方声明](THIRD-PARTY-NOTICES.md)。
