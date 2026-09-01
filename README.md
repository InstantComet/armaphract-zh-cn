# ARMAPHRACT(暂定译名：装甲重骑) 0.6.3 简体中文补丁

## [ARMAPHRACT游戏官方发布页](https://aeoriii.itch.io/ironmirage)  

这是针对 ARMAPHRACT(暂定译名：装甲重骑) `0.6.3` 的简体中文本地化补丁，运行于 BepInEx IL2CPP 环境，并使用 HarmonyX 处理部分游戏 UI 文本。

本仓库只保存补丁内容，不包含游戏本体（本体请在上方链接下载）、Unity 资源、BepInEx 运行时依赖或自动生成缓存。

项目地址：[github.com/InstantComet/armaphract-zh-cn](https://github.com/InstantComet/armaphract-zh-cn)

## 当前版本

版本号见根目录的 [`VERSION`](VERSION)，变更记录见 [`CHANGELOG.md`](CHANGELOG.md)。当前发布标签为 `v0.8.14`。

源码工程位于 [`src/HarmonyXLocalization`](src/HarmonyXLocalization)，已编译插件位于 `BepInEx/plugins/`。

## 安装

准备一份合法的 ARMAPHRACT `0.6.3`,启动汉化补丁，选中 armaphract.exe 然后点击 开始安装 即可

## 从源码构建

构建需要 .NET 6 SDK，以及游戏目录中由 BepInEx 生成的引用程序集。工程默认把当前仓库根目录视为游戏目录：

```powershell
dotnet build .\src\HarmonyXLocalization\HarmonyXLocalization.csproj --configuration Release
```

如果仓库与游戏目录分开存放，用 `GameDir` 指定实际游戏目录：

```powershell
$gameDir = Read-Host '请输入 ARMAPHRACT 游戏目录的完整路径'
dotnet build .\src\HarmonyXLocalization\HarmonyXLocalization.csproj --configuration Release `
  "-p:GameDir=$gameDir"
```

构建输出位于 `src/HarmonyXLocalization/bin/Release/net6.0/`。将生成的 `Armaphract.HarmonyXLocalization.dll` 复制到游戏目录的 `BepInEx/plugins/` 后即可测试。

## 版本管理

使用语义化版本号：

- 修订版本：错别字、术语、标点或构建修正。
- 次版本：新增一批翻译或兼容性功能。
- 主版本：不兼容的安装方式、数据格式或运行时变更。

提交前运行：

```powershell
git diff --check
dotnet build .\src\HarmonyXLocalization\HarmonyXLocalization.csproj --configuration Release
```

## 版权说明

请先阅读 [`LICENSE`](LICENSE)。游戏原文和衍生翻译仍受原权利人权利约束；本仓库没有重新分发游戏本体。
