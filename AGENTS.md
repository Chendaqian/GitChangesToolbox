# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目

Git Changes Toolbox 是一个 Visual Studio 2022 VSIX 扩展，在「Git 更改」窗口顶部工具栏注入两个图标按钮：折叠/展开更改树、定位当前文件。

- C# 7.3，.NET Framework 4.7.2，WPF
- SDK 风格 csproj（`Microsoft.NET.Sdk`）：新增 `.cs` 文件自动包含，不必改 csproj
- 输出：`src\GitChangesToolbox\bin\<Configuration>\GitChangesToolbox.Probe.vsix`

## 构建与发布

使用 VS2022 自带的 MSBuild。SDK 风格项目首次构建需要还原：

```powershell
$ErrorActionPreference = 'Stop'
& 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe' src\GitChangesToolbox\GitChangesToolbox.csproj /restore /p:Configuration=Debug
```

发布：

```powershell
$ErrorActionPreference = 'Stop'
& .\scripts\Publish.ps1
```

产物复制到 `publish\GitChangesToolbox.vsix`。

没有测试项目。验证方式是安装 VSIX 后在真实 VS 里操作：

```powershell
$ErrorActionPreference = 'Stop'
& 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\VSIXInstaller.exe' /quiet <vsix路径>
```

安装或卸载前必须关闭所有 `devenv` 进程，安装完成后重新启动 VS。

## 架构

三个类分工：

- `GitChangesToolboxPackage`：`AsyncPackage`，用 `ProvideAutoLoad(SolutionExists)` 在打开解决方案时加载。
- `GitChangesToolbar`：`DispatcherTimer` 每 3 秒扫描一次 Git 窗口可视树，找到顶部工具栏 `buttonPanel` 后把两个按钮插到最前面；按钮用 `CrispImage` 加自定义扁平样式（`EnvironmentColors.ToolWindowButtonHoverActiveBrushKey`）。
- `GitChangesController`：所有 VS 内部对象的查找和反射访问。

VS 2022 17.14 实测的内部结构：

| 对象 | 类型或属性 |
| --- | --- |
| 窗口 DocView | `Microsoft.TeamFoundation.Git.Provider.GitWindow.GitToolWindow` |
| 更改列表控件 | `Microsoft.TeamFoundation.Controls.WPF.TeamExplorer.FileListView` |
| 列表内部控件 | `FileListView.InnerListViewControl`，类型 `IndentedItemListView` |
| 数据源 | `FileListView.ItemsSource`，扁平化的 `FastObservableCollection<GitPendingChangeNode>`，只含当前可见节点 |
| 节点属性 | `ItemPath`、`ItemText`、`IndentLevel`、`IsExpanded`、`Children`、`HasChildren` |

## 折叠与定位的实现约束

- 直接修改 `GitPendingChangeNode.IsExpanded` 只会更新箭头，列表不会重建；`Refresh()` 和重新绑定 `ItemsSource` 都无效。
- 唯一有效的路径是调用节点所属 ViewModel（`FileListView.DataContext`）的 `ExpanderCommand.Execute(node)`，由 ViewModel 自行重建列表。
- 折叠：对 `IsExpanded == true` 的节点执行 `ExpanderCommand`；展开：对 `IsExpanded == false` 且有子节点的节点执行。
- 定位：先用 `ExpanderCommand` 展开祖先节点，`PumpDispatcher()` 后再设 `IsSelected`、`InnerListViewControl.SelectedItem`，最后调用 `ScrollIntoView`。
- 节点集合取自 `ItemsSource` 的可见节点并递归 `Children`，去重后按 `IndentLevel` 升序执行。
- 调试折叠逻辑时查看 `%TEMP%\GitChangesToolbox\collapse-diagnostic.txt`，`GitChangesController` 会把每轮节点数和执行结果追加进去。

## 清单与版本

- 维护 `src\GitChangesToolbox\source.extension.vsixmanifest`。`extension.vsixmanifest` 是打包版本，不参与构建，不要改它来影响产物。
- 改版本要同步三处：清单的 `Identity Version`、`Properties\AssemblyInfo.cs` 的 `AssemblyVersion` 和 `AssemblyFileVersion`。VSIXInstaller 不接受同版本覆盖，调试迭代时要递增版本号。
- `Identity Id` 是 `GitChangesToolbox.Probe`，改成别的值会被 VS 识别为另一个扩展，导致新旧版本同时加载。

## 构建环境依赖

- VS 程序集用 `HintPath` 直接指向 NuGet 全局缓存（`$(UserProfile)\.nuget\packages\microsoft.visualstudio.*`）和 VS 安装目录的 `PublicAssemblies`；`Microsoft.VSSDK.BuildTools` 走 `PackageReference`，因此构建前必须 restore。
- 换机器或换 VS 版本时，需要修改 csproj 顶部的 `VSRoot`、`NuGetRoot` 以及各引用的具体版本号。
- `Microsoft.VsSDK.targets` 必须在 `Sdk.targets` 之后导入，否则 `$(TargetPath)` 尚未定义，`CreatePkgDef` 会报 VSSDK1032。csproj 用显式 `<Import Project="Sdk.props" .../>` 加 `<Import Project="Sdk.targets" .../>` 来保证这个顺序。
- `Package.vsct` 不参与编译。这套离线构建下 VSCT 的 `mergeCto` 不生效，菜单无法注册，因此扩展不提供菜单命令，完全依赖自动加载和运行时按钮注入。