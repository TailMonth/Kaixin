# 开心桌宠（KaixinPet）

Windows 11 的「桌宠 × 番茄钟」小工具。设计文档见 [docs/开心桌宠-设计文档.md](docs/开心桌宠-设计文档.md)，美术计划见 [docs/美术素材手绘任务清单.md](docs/美术素材手绘任务清单.md)。

当前进度：**M3 番茄钟**（真实计时服务、沙漏阶段表现、轮次/长休息/自动流转、系统事件、悬停控制条）。版本 `0.4.0-m3`。

## 已实现

**桌宠与外壳（M0/M1）**

- Win32 分层窗口（320×280 DIP），`UpdateLayeredWindow` 逐像素 alpha 合成、透明穿透
- 单实例、系统托盘（4 状态图标、tooltip、菜单、双击唤出）、设置存储与便携模式
- 窗口位置记忆、置顶持久化、隐藏/显示、托盘退出

**行为（M2）**

- Idle / Sleep / Play / Drag / Fall；待机微动作、摸猫、睡觉、玩毛线球物理、拖拽与重力下落
- 玩耍中可结束；动作可打断并回到待机

**番茄钟（M3）**

- 纯域计时服务 `KaixinPet.Pomodoro`：Stopwatch 单调计时、暂停/继续、跳过、重置、轮次/长休息、自动流转、休眠暂停原因；带可复现的控制台测试
- 沙漏阶段表现：专注暖金 / 休息鼠尾草沙色、开始淡入、阶段切换翻面、暂停停沙、结束/重置淡出
- 时间标签：焦点/休息圆点、暂停符号与灰显、每秒更新；可在设置中关闭
- 桌宠/托盘菜单：开始专注、开始休息、暂停/继续、跳过当前阶段、结束本次专注/休息；状态行显示阶段、剩余时间、第 n/轮
- 全局快捷键 `Ctrl+Alt+P`：空闲开始专注，运行中暂停，暂停中继续
- 悬停控制条：鼠标停留约 350ms 出现（暂停/继续、跳过、设置目录），移出 1.5s 淡出；可关闭
- 系统事件：休眠自动暂停并在恢复时提示；全屏应用自动隐藏猫/沙漏/标签/控制条，计时继续，退出全屏恢复
- 专注安静模式：专注期间不自动玩耍，2 分钟无交互入睡；点击短暂醒来；专注中手动玩球需确认
- 托盘气泡通知：专注完成、休息开始/结束、休眠暂停；可关闭
- 计时中退出二次确认；托盘图标随专注/休息/暂停切换

尚未包含（后续阶段）：设置窗口与可改快捷键（M4）、音效导入/播放（M4）、锁屏专项处理、正式水彩素材（M5）、安装器（M6）。

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

# 启动后自动开始专注
dotnet run --project src/KaixinPet.App -- --start-focus

# 24fps 基准（自动开始专注，运行指定秒数后退出）
dotnet run --project src/KaixinPet.App -- --benchmark 60

# 番茄钟域测试
dotnet run --project tests/KaixinPet.Pomodoro.Tests
```

也可以直接运行 `src/KaixinPet.App/bin/Debug/net10.0-windows/KaixinPet.exe`。

## 操作方式（M3）

| 操作 | 行为 |
| --- | --- |
| `Ctrl+Alt+P` | 空闲开始专注；运行中暂停；暂停中继续 |
| 左键单击猫 | 摸猫；睡眠时先醒来 |
| 左键拖动猫 | 拎起，松手后重力落回工作区底部 |
| 右键猫 / 沙漏 | 番茄钟命令、玩球/睡觉、置顶、重置、隐藏、退出 |
| 右键托盘图标 | 与桌宠菜单相同的计时命令与状态行 |
| 悬停猫 / 沙漏 | 约 350ms 出现控制条：暂停/继续、跳过、打开设置目录 |
| 托盘双击 | 恢复并置前桌宠，高亮时间标签 |
| 点击透明区 | 穿透到下方窗口 |

## 设置与数据

```
%LocalAppData%\KaixinPet\
  settings.json          设置（番茄钟参数、行为、位置、置顶等）
  settings.bak.json      上一次设置的原子备份
  settings.corrupt-*.json 损坏设置的备份
  logs\app-YYYYMMDD.log  运行日志
```

- 番茄钟参数：`pomodoro.focusMinutes`（默认 25）、`shortBreakMinutes`（5）、`longBreakMinutes`（15）、`roundsPerLongBreak`（4）、`autoStartBreaks`（开）、`autoStartFocus`（关）。
- 界面相关：`pomodoro.showHourglassTime`、`showHoverControls`、`showNotifications`、`quietDuringFocus`；快捷键当前固定为 `Ctrl+Alt+P`（M4 可改）。
- 行为参数：`behavior.sleepAfterIdleMinutes`、`playSessionMaxSeconds`、`spontaneousPlay`、`focusQuietAfterMinutes`。
- 关闭应用后编辑 `settings.json`，重启生效；设置界面在 M4 提供。
- 便携模式：exe 旁放空的 `portable.flag`，数据写入 `.\data`。

### 快速验收参数

想快速跑完 A6–A9/A22，可临时把 `settings.json` 改为：

```json
"pomodoro": {
  "focusMinutes": 1,
  "shortBreakMinutes": 1,
  "longBreakMinutes": 1,
  "roundsPerLongBreak": 2,
  "autoStartBreaks": true,
  "autoStartFocus": false
}
```

重启后专注/休息各 1 分钟，完成 2 个专注进入长休息。验收后改回默认值并重启。

## 验证清单（M3 验收）

1. **A6 专注流程**：快捷键或菜单开始专注 → 猫做放沙漏动作、沙漏淡入、标签出现在上方并每秒更新；暂停后沙漏停沙、标签灰显带暂停符号；继续恢复正常。
2. **A7 长休息**：完成设定轮次（默认 4；快速验收设为 2）→ 第 N 个专注后进入长休息，周期计数归零。
3. **A8 自动流转**：自动休息开、自动专注关 → 专注完成自动进入休息；休息结束回空闲，不自动专注。
4. **A9 跳过**：专注中跳过 → 不增加完成数、不进入休息、回空闲；休息中跳过 → 回空闲且不自动专注。
5. **A10 休眠**：专注中让系统睡眠后唤醒 → 计时自动暂停，标签灰显，托盘提示；不会误记完成。
6. **A11 全屏**：播放全屏视频/游戏 → 猫/沙漏/标签/控制条隐藏，计时继续；退出全屏恢复。
7. **A21 极简 UI**：设置 `showHourglassTime=false` 与 `showHoverControls=false` 后重启 → 不显示标签/控制条，但沙漏沙量、托盘 tooltip/菜单与快捷键仍可查看和控制计时。
8. **A22 阶段表现**：专注与休息切换 → 沙漏翻面、沙色由暖金变鼠尾草绿，标签圆点同色变化；标签不遮挡猫脸。
9. 鼠标悬停猫/沙漏 350ms 出现控制条，移出 1.5s 淡出；暂停/继续与跳过按钮可用。
10. 专注中手动「陪它玩毛线球」弹出确认；专注 2 分钟无交互后猫入睡，点击唤醒。
11. 托盘图标在空闲/专注/休息/暂停间切换；tooltip 显示剩余时间；计时中退出有二次确认。
12. 运行 `dotnet run --project tests/KaixinPet.Pomodoro.Tests` 输出 `ALL PASS`。

## 目录结构（当前）

```
src/
  KaixinPet.Core/      路径、日志、进程指标、设置
  KaixinPet.Pomodoro/  相位/运行态、计时引擎、轮次/自动流转
  KaixinPet.Behavior/  行为状态机、调度、毛线球物理
  KaixinPet.Pet/       分层窗口、动画播放器、多图集渲染、拖拽/坠落
  KaixinPet.App/       WPF 宿主、标签、悬停条、托盘、系统事件、快捷键
assets/
  characters/kaixin/   角色图集（1x 占位）
  props/               沙漏、毛线球、阴影
  behaviors.json       行为权重/冷却
  ui/                  app.ico + 托盘图标
tests/
  KaixinPet.Pomodoro.Tests/  计时引擎控制台测试
tools/
  generate-placeholder-art.ps1
  generate-icons.ps1
docs/                 设计文档与美术计划
Reference/            角色照片与风格参考
```

## 占位美术

M2/M3 的猫帧使用 192×192 的 1x 占位画布以控制多动作内存；正式水彩素材按任务清单生产 2x 母版后替换 `assets/` 并更新 JSON 的 `masterScale`/`canvas` 即可。

## 已知限制

- 托盘与桌宠菜单仍为系统原生菜单；菜单打开时动画暂停，Fluent 非模态菜单在后续阶段。
- 锁屏专项处理（停止渲染）尚未加入；全屏隐藏与休眠暂停已实现。
- 悬停控制条的设置按钮当前打开数据目录；正式设置窗口在 M4。
- 占位动作/道具表现有限；正式水彩美术在 M5。
- M3 仍加载全部占位图集，空闲内存约 90–105MB；M5 优化缓存与正式素材。
- 开机启动、音效、设置界面为后续阶段内容。
