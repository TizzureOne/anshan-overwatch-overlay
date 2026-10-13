# 第三方素材与组件

安山守望是独立开发的教学辅助工具。

## 英雄图片与名称

《守望先锋》英雄图片、角色名称及相关商标归 Blizzard Entertainment 及相应权利人所有。
本工具内置官方公开英雄页的 54 位英雄头像，用于识别教学标记。
图片在构建时处理为 256 × 256 的圆形透明 PNG。素材权利与使用条件由原权利人保留。

- 国际服官方名单与头像入口：https://overwatch.blizzard.com/en-us/heroes/
- 国服官方页面：https://ow.blizzard.cn/heroes/
- 国服页面公开引用的名称清单：https://ld5.res.netease.com/pc/zt/20241115153332/static/heroConfigs.json
- 名单核对日期：2026-10-12；基础头像获取于 2026-09-26，血律头像获取于 2026-10-12。
- 每张图片的原始 URL、原图 SHA-256、成品 SHA-256 和原图尺寸记录在 `src/AnshanOverlay/Assets/heroes.json`。

## 运行时

程序使用 Microsoft .NET、WPF、Windows Forms（仅托盘和显示器信息）与 ASP.NET Core Kestrel。
自包含版本携带 .NET 8.0.31 运行时。对应许可证与第三方声明来自微软官方运行时分发包，随发布目录保留。

- .NET：https://github.com/dotnet/runtime
- WPF：https://github.com/dotnet/wpf
- ASP.NET Core：https://github.com/dotnet/aspnetcore

## 安装程序

Windows 安装程序由 Inno Setup 6.7.3 构建，版权归 Jordan Russell、Martijn Laan 及贡献者所有。
简体中文语言文件保留原作者注释，取自官方源码仓库固定标签：
https://github.com/jrsoftware/issrc/blob/is-6_7_3/Files/Languages/Unofficial/ChineseSimplified.isl

Inno Setup 项目及许可证：https://github.com/jrsoftware/issrc/blob/is-6_7_3/license.txt
