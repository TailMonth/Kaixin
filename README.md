# 开心桌宠（KaixinPet）

Windows 11 的「桌宠 × 番茄钟」小工具。设计文档见 [docs/开心桌宠-设计文档.md](docs/开心桌宠-设计文档.md)，美术计划见 [docs/美术素材手绘任务清单.md](docs/美术素材手绘任务清单.md)。

当前进度：**M1 骨架**（单实例、设置存储、系统托盘、窗口位置记忆、显示/隐藏/置顶闭环）。版本 `0.2.0-m1`。

## 已实现

**桌宠与计时演示（M0）**

- Win32 分层窗口（320×280 DIP），`UpdateLayeredWindow` 逐像素 alpha 合成
- 透明区域点击穿透（alpha 阈值 + `WM_NCHITTEST`），沙漏区域保留热区
- 2x 母版帧按当前 DPI 缩放；待机呼吸 10fps + 随机眨眼叠加
- 左键拖拽移动，按目标显示器工作区钳制；`WM_DPICHANGED` 重建缩放缓存与 DIB
- 沙漏道具：沙量随倒计时进度裁剪上层/下层沙
- 独立 WPF 时间标签：56×22 DIP、点击穿透、不抢焦点、跟随沙漏，顶部空间不足时翻到下方
- 日志与 CPU / 内存 / 帧率采样；24fps 基准模式

**托盘与外壳（M1）**

- 单实例：重复启动只唤起已有实例并退出
- 系统托盘：4 状态静态图标（空闲/专注/休息/暂停）、tooltip、双击/单击唤出、右键菜单
- 托盘菜单：显示/隐藏桌宠、开始/结束计时演示、始终置顶、重置位置、打开数据/日志目录、退出
- 窗口位置记忆：退出保存、启动恢复、越界自动回到底部；重置位置回到当前显示器右下角
- 设置存储：`settings.json` 原子写入、500ms 防抖、损坏备份恢复、未知字段保留、参数校验
- 便携模式：可执行文件旁存在 `portable.flag` 时数据写入 `.\data`
- 置顶状态持久化；托盘菜单与桌宠右键菜单状态同步
- 完全隐藏时停止逐帧渲染；计时继续，托盘 tooltip 是信息源
- 托盘双击恢复桌宠时，时间标签高亮 1 秒
- 计时演示中退出时二次确认；托盘图标随阶段切换（专注红点/空闲沙色）
- 应用图标嵌入 exe

尚未包含（后续阶段）：真实番茄钟服务与轮次、行为状态机、毛线球物理、音效、设置窗口、开机启动、全屏自动隐藏、正式水彩素材、安装器。

## 环境要求

- Windows 10/11 x64（目标 Windows 11 22H2+）
- 开发：.NET 10 SDK
- 运行：.NET 10 Desktop Runtime（Lite / 框架依赖模式）

本机 SDK 安装在用户目录（无需管理员）：

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" --version
```

如果 `dotnet` 不在 PATH 中，先在当前会话加入：

```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
```

## 构建与运行

```powershell
dotnet build KaixinPet.sln -c Debug

# 正常启动
dotnet run --project src/KaixinPet.App

# 启动后自动开始 25 分钟计时演示
dotnet run --project src/KaixinPet.App -- --demo 25

# 24fps 基准：运行 60 秒后自动退出并写入结果
dotnet run --project src/KaixinPet.App -- --benchmark 60
```

也可以直接运行 `src/KaixinPet.App/bin/Debug/net10.0-windows/KaixinPet.exe`。

## 操作方式（M1）

| 操作 | 行为 |
| --- | --- |
| 左键拖动猫 | 移动桌宠；拖动期间沙漏与时间标签隐藏，松手恢复并保存位置 |
| 右键猫 / 沙漏 | 桌宠菜单：计时演示、结束演示、置顶、重置位置、隐藏桌宠、日志目录、退出 |
| 点击猫旁透明区 | 事件穿透到下方窗口 |
| 托盘图标单击/双击 | 恢复并置前桌宠；计时中会高亮时间标签 1 秒 |
| 右键托盘图标 | 托盘菜单：显示/隐藏、计时演示、置顶、重置位置、数据/日志目录、退出 |
| 再次启动应用 | 不创建第二个进程，唤起已有桌宠 |
| 拖动后退出 | 下次启动恢复上次位置；显示器变化导致越界时自动回到底部右侧 |

时间标签只读且点击穿透，不拦截桌面操作。

## 数据与设置

```
%LocalAppData%\KaixinPet\
  settings.json          设置（首次启动自动生成）
  settings.bak.json      上一次设置的原子备份
  settings.corrupt-*.json 损坏设置的备份
  logs\app-YYYYMMDD.log  运行日志（1MB 轮转，保留 3 个）
  sounds\custom\         自定义音效（M4）
  cache\                 帧缓存（M5）
```

- 便携模式：在 `KaixinPet.exe` 同目录放一个空的 `portable.flag`，数据改写到 `.\data\`。
- 托盘菜单「打开数据目录」「打开日志目录」可直接定位。
- 当前设置文件已包含番茄钟、音效、行为等字段（与设计文档附录 B 一致），其中计时参数、音效、行为会在后续阶段接入。
- 设置界面在 M4 提供；当前可通过编辑 `settings.json` 调整（关闭应用后修改，启动生效）。
- 不写注册表、不联网、不需要管理员权限。

## 验证清单（M1 验收）

1. 启动 ≤ 1.2 秒出现猫；任务栏无窗口按钮；托盘出现猫头图标；右键托盘显示菜单。
2. 托盘菜单「隐藏桌宠」→ 猫与时间标签消失，计时（若在运行）继续；「显示桌宠」恢复。
3. 拖动猫到其他位置 → 退出（托盘「退出」）→ 重启，猫回到上次位置。
4. 托盘「重置位置」→ 猫回到当前显示器右下角，设置文件中的 `lastPosition` 同步更新。
5. 托盘菜单「始终置顶」取消勾选 → 重启后仍为非置顶；勾选后恢复置顶。
6. 再次启动 exe → 不产生第二个进程，已有桌宠被唤起/置前。
7. 删除 `settings.json` → 启动生成默认设置；写入非法 JSON → 生成 `settings.corrupt-*.json`，使用默认设置并弹出托盘提示。
8. 在 exe 旁创建 `portable.flag` → 启动后数据写入 `.\data`；删除后恢复 `%LocalAppData%`。
9. 开始 1 分钟演示 → 托盘图标变为专注红点、菜单顶部显示「专注中」与剩余时间；完成后 2 秒标签/沙漏淡出，图标回到空闲。
10. 计时演示中托盘「退出」→ 弹出确认；退出后托盘图标移除，设置与位置已保存。
11. 透明穿透：点击猫旁透明区落到下方窗口；点击猫身体可拖动。
12. DPI：在 125% / 150% / 200% 缩放下重复 1–4，确认大小与位置合理，时间标签不遮挡猫脸。

## 目录结构（当前）

```
src/
  KaixinPet.Core/     路径、日志、进程指标、设置模型与存储
  KaixinPet.Pet/      Win32 分层窗口、帧渲染、动画时钟、场景布局
  KaixinPet.App/      WPF 宿主、时间标签、计时演示、托盘、单实例、基准模式
assets/
  characters/kaixin/  character.json + 占位待机/眨眼图集
  props/              props.json + 沙漏分层、接触阴影
  ui/                 app.ico + 4 个托盘状态图标
tools/
  generate-placeholder-art.ps1   生成占位 PNG
  generate-icons.ps1             生成 app/tray ICO（预览输出到 tools/icon-preview.png）
docs/                 设计文档、美术任务清单与生图提示词
Reference/            角色照片、风格参考与待机视频
```

## 占位美术与图标

猫、沙漏、阴影、图标均为脚本生成的占位资源，仅用于验证尺寸、锚点、帧率与构图；正式水彩素材到位后替换 `assets/` 对应文件并更新 JSON 即可，渲染管线无需改动。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/generate-placeholder-art.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/generate-icons.ps1
```

## 已知限制

- 输入法候选窗、部分应用的置顶浮层可能遮挡桌宠（系统 UI 层级高于普通置顶窗口）；移开输入法即可。
- 托盘和桌宠菜单暂用系统原生菜单；Fluent 自绘菜单与设置窗口在后续阶段实现。
- 当前计时器是演示计时，不是完整番茄钟（暂停/轮次/长休息/系统电源事件在 M3）。
- 开机启动、全屏自动隐藏、锁屏行为、音效、设置界面为后续阶段内容。
- 数据目录自动创建；卸载当前直接删除程序目录与 `%LocalAppData%\KaixinPet`（安装器在 M6）。
