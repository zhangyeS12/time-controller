# time-controller

time-controller 是一个本地优先的 Windows 应用使用时长统计与专注管理工具。

它会在本机记录前台应用的使用时长，帮助你了解每天的时间流向，并提供应用分类、忽略名单、专注规则、专注会话、报告分析、数据导出、备份恢复和时区设置等能力。

## 项目简介

time-controller 面向 Windows 桌面用户，使用 C#、.NET 8、WPF 和 SQLite 开发。所有统计数据默认只保存在本机，不需要登录账号，也不会上传到云端。

项目适合作为个人效率工具，也适合作为本地优先桌面应用的学习参考。

## 功能特点

- 应用使用时长统计
- 今日 / 最近 7 天 / 最近 30 天 / 历史累计统计
- 历史统计与报告分析
- 应用分类与忽略名单
- 专注规则与超时提醒
- 专注会话 / 番茄钟
- 托盘运行与开机自启
- 数据导出
- 备份与恢复
- 时间与时区设置
- 本地隐私保护

## 截图占位

截图后续可放在 `docs/screenshots/` 目录中。

- 仪表盘截图：`docs/screenshots/dashboard.png`
- 历史统计截图：`docs/screenshots/history.png`
- 报告分析截图：`docs/screenshots/report.png`
- 数据设置截图：`docs/screenshots/data-settings.png`

## 安装方式

### 方式一：下载 Release 安装包

进入 GitHub Releases 页面，下载最新版本安装包并运行。

安装包会把程序安装到：

```text
%ProgramFiles%\time-controller\
```

用户数据仍然保存在 LocalAppData，不会写入安装目录。

### 方式二：从源码运行

在项目根目录执行：

```powershell
dotnet restore AppTimeTracker\AppTimeTracker.csproj
dotnet build AppTimeTracker\AppTimeTracker.csproj
dotnet run --project AppTimeTracker\AppTimeTracker.csproj
```

### 方式三：自行发布

Framework-dependent 版本适合已经安装 .NET 8 Desktop Runtime 的用户：

```powershell
.\scripts\publish-framework-dependent.ps1
```

Self-contained 单文件版本适合没有安装 .NET 的用户，体积更大，但可直接运行：

```powershell
.\scripts\publish-self-contained.ps1
```

发布产物会输出到：

```text
release\framework-dependent\
release\self-contained\
```

也可以直接执行 dotnet publish：

```powershell
dotnet publish AppTimeTracker\AppTimeTracker.csproj -c Release -r win-x64 --self-contained false -o release/framework-dependent
dotnet publish AppTimeTracker\AppTimeTracker.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o release/self-contained
```

## 如何发布

1. 运行 self-contained 发布脚本：

```powershell
.\scripts\publish-self-contained.ps1
```

2. 使用 Inno Setup 打开：

```text
installer\time-controller.iss
```

3. 编译安装包。

安装包输出目录：

```text
installer\output\
```

## 开发环境

- Windows 10 / Windows 11
- .NET 8 SDK
- Visual Studio 2022 或支持 .NET 8 的编辑器
- WPF
- SQLite

## 数据保存位置

用户数据默认保存在本机 LocalAppData 目录，例如：

```text
%LOCALAPPDATA%\time-controller\
```

主要数据包括：

- SQLite 数据库：`app_usage.db`
- 日志：`logs\`
- 导出文件：`exports\`
- 备份文件：`backups\`

这些文件不应该提交到 GitHub。

## 备份保存位置

备份文件默认保存在：

```text
%LOCALAPPDATA%\time-controller\backups\
```

用户可以在数据设置页手动创建备份，也可以使用自动备份能力。恢复数据库前，程序会尽量先备份当前数据库，避免误操作导致数据丢失。

## 隐私说明

- 所有数据默认只保存在本地。
- 程序不上传云端。
- 程序不收集账号信息。
- 用户可以清空、导出、备份和恢复自己的数据。
- 应用使用记录、分类、专注规则、报告数据都由用户本机数据库保存。

更多说明见 [docs/privacy.md](docs/privacy.md)。

## 当前限制

- 目前仅支持 Windows。
- 应用识别基于前台窗口和进程名。
- 统计准确性会受到系统权限、进程访问权限和应用窗口行为影响。
- 报告分析为本地规则型分析，不包含云端 AI 分析。
- 安装包流程仍在完善中。

## 后续计划

- 制作正式安装包。
- 发布 GitHub Release。
- 优化更多图表展示。
- 继续提升统计准确性。
- 增加可选的 AI 周报分析。

更多计划见 [docs/roadmap.md](docs/roadmap.md)。
