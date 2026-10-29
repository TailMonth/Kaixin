# 开心桌宠（KaixinPet）

Windows 11 的「桌宠 × 番茄钟」小工具。设计文档见 [docs/开心桌宠-设计文档.md](docs/开心桌宠-设计文档.md)，美术计划见 [docs/美术素材手绘任务清单.md](docs/美术素材手绘任务清单.md)。

当前进度：**M4 音效与设置 + 水彩 UI 重构**（默认音效、自定义导入转码、音量/静音/呼噜、设置窗口、可改快捷键、开机启动；按 `documents/ux-design` 原型完成水彩主题、可视化设置与自绘菜单）。版本 `0.5.1-m4-ui`。

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
- 一体式计时面板：收起时是沙漏上方的极简时间标签（阶段圆点、暂停符号与灰显、每秒更新、点击穿透）；鼠标在猫或面板上停留约 350ms 展开为大时间 + 圆点的控制面板（结束、暂停/继续、跳过、打开设置目录），移出 1.5s 收回。时间标签与悬停展开可分别在设置中关闭
- 桌宠/托盘菜单：开始专注、开始休息、暂停/继续、跳过当前阶段、结束本次专注/休息；状态行显示阶段、剩余时间、第 n/轮
- 全局快捷键 `Ctrl+Alt+P`：空闲开始专注，运行中暂停，暂停中继续
- 系统事件：休眠自动暂停并在恢复时提示；全屏应用自动隐藏猫/沙漏/计时面板，计时继续，退出全屏恢复
- 专注安静模式：专注期间不自动玩耍，2 分钟无交互入睡；点击短暂醒来；专注中手动玩球需确认
- 托盘气泡通知：专注完成、休息开始/结束、休眠暂停；可关闭
- 计时中退出二次确认；托盘图标随专注/休息/暂停切换

**音效与设置（M4）**

- 13 个事件音效（摸猫、拎起、落地、玩耍、拍球、唤醒、呼噜、番茄阶段/暂停/跳过），默认音量与冷却按设计表；默认音频为项目自制合成占位音
- `KaixinPet.Audio`：NAudio 延迟初始化、效果通道最多 2 个并发、事件冷却、呼噜循环 300ms 淡入淡出；无音频设备时静默并重试，不阻止计时
- 自定义音效：WAV/MP3/M4A/FLAC（明确拒绝 OGG），0.2–30 秒、≤30MB；后台转 44.1kHz/16-bit/单声道、去首尾静音（-45dB）、峰值归一化 -1dBFS，缓存到 `sounds/custom`，失败回退默认
- 设置窗口（常规/番茄钟/音效/行为/关于）：宠物名字、置顶、全屏隐藏、开机启动、重置位置、番茄参数与自动流转、显示/提醒、音量/静音/呼噜、逐事件试听/更换/还原、行为参数、热键修改、重置全部
- 全局快捷键可在设置中修改并即时重新注册，冲突时就地提示；开机启动写入当前用户 Run 项，`--startup` 延迟 3 秒初始化
- 桌宠/托盘菜单增加“设置…”与“静音”勾选项；计时面板设置按钮打开设置窗口；专注时用户交互音效按 50% 播放
- 设置校验：时长/轮次/音量范围、非法输入就地提示；设置即时应用（时长下一阶段生效）；关闭设置窗口不退出应用

**水彩 UI（2026-10-28 原型）**

- 统一 `Themes/Watercolor.xaml` 主题：暖纸、猫黑、鼠尾草绿、耳粉、沙金 token；静态纸纹 `assets/ui/paper-tile.png`；卡片、按钮、导航、开关、水彩滑杆共用同一套模板
- 设置窗口 840×620（最小 680×500），自定义纸色标题栏与五页图标导航；时长/轮次/行为/音量全部改为滑杆 + 只读值签 + 上下限；番茄节奏摘要、轮次点、带完整标签的开关、位置示意与关于头像
- 桌宠右键改为 WPF 非模态纸色菜单：状态行、计时分组、勾选状态、禁用状态与全部原命令；Esc/外部点击关闭，不抢当前应用焦点；托盘菜单保持原生
- 番茄钟收起态 84×30、展开态 216×136：阶段文字与圆点、大号时间、细进度条、结束/暂停/跳过/设置四个线性图标；保留收起点击穿透与悬停展开
- 图标统一为 24×24 SVG viewBox 路径并按整体缩放，修复逐图标 `Stretch=Uniform` 造成的错位；系统减少动效时取消浮层淡入淡出

尚未包含（后续阶段）：深浅主题切换、宠物大小设置（M5）、锁屏专项处理、正式水彩素材（M5）、安装器（M6）。

## 环境要求

- Windows 10/11 x64（目标 Windows 11 22H2+）
- 开发：.NET 10 SDK
- 运行：.NET 10 Desktop Runtime（Lite / 框架依赖模式）
- 首次构建会从 NuGet 还原 NAudio 2.2.1（音频播放与 Media Foundation 解码）

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

# 优雅关闭正在运行的实例（脚本/测试用，会正常移除托盘图标）
dotnet run --project src/KaixinPet.App -- --quit

# 24fps 基准（自动开始专注，运行指定秒数后退出）
dotnet run --project src/KaixinPet.App -- --benchmark 60

# 番茄钟域测试
dotnet run --project tests/KaixinPet.Pomodoro.Tests

# 音效目录 / 导入转码测试
dotnet run --project tests/KaixinPet.Audio.Tests
```

也可以直接运行 `src/KaixinPet.App/bin/Debug/net10.0-windows/KaixinPet.exe`。

## 操作方式（M4）

| 操作 | 行为 |
| --- | --- |
| `Ctrl+Alt+P` | 空闲开始专注；运行中暂停；暂停中继续 |
| 左键单击猫 | 摸猫；睡眠时先醒来 |
| 左键拖动猫 | 拎起，松手后重力落回工作区底部 |
| 右键猫 / 沙漏 | 番茄钟命令、玩球/睡觉、置顶、重置、隐藏、设置、静音、退出 |
| 右键托盘图标 | 与桌宠菜单相同的计时命令、设置、静音与状态行 |
| 悬停猫 / 沙漏 | 约 350ms 展开计时面板：结束、暂停/继续、跳过、打开设置 |
| 托盘双击 | 恢复并置前桌宠，高亮计时面板 |
| 设置 → 番茄钟 → 快捷键 | 点击“更改”后按下组合键；Esc 取消；冲突时就地提示 |
| 设置 → 音效 → 逐事件 | 试听 / 更换自定义音频 / 还原默认 |
| 点击透明区 | 穿透到下方窗口 |

## 设置与数据

```
%LocalAppData%\KaixinPet\
  settings.json          设置（番茄钟参数、行为、位置、置顶等）
  settings.bak.json      上一次设置的原子备份
  settings.corrupt-*.json 损坏设置的备份
  logs\app-YYYYMMDD.log  运行日志
  sounds\custom\*.wav    自定义音效缓存（导入时生成）
```

- 设置入口：桌宠/托盘右键“设置…”、计时面板设置按钮；窗口关闭后应用继续运行。
- 番茄钟参数：`pomodoro.focusMinutes`（默认 25）、`shortBreakMinutes`（5）、`longBreakMinutes`（15）、`roundsPerLongBreak`（4）、`autoStartBreaks`（开）、`autoStartFocus`（关）。
- 界面相关：`pomodoro.showHourglassTime`、`showHoverControls`、`showNotifications`、`quietDuringFocus`、`hotkey`（默认 `Ctrl+Alt+P`，可在设置中修改）。
- 音效：`audio.muted`、`masterVolume`、`effectsVolume`、`ambientVolume`、`ambientPurrDuringFocus`、`customSounds`（事件 ID → `sounds/custom` 相对路径）。
- 行为参数：`behavior.sleepAfterIdleMinutes`、`playSessionMaxSeconds`、`spontaneousPlay`、`focusQuietAfterMinutes`。
- 开机启动：设置项写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\KaixinPet`，带 `--startup` 参数；默认关闭。
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

## 验证清单（M4 验收）

1. **A13 自定义音效**：设置 → 音效 → 摸猫 → 更换，选择 WAV/MP3 → 导入后立即试听；重启后仍显示“自定义”并生效；连续点击猫，冷却 1.5 秒内只播放一次。
2. **A14 无效音频**：选择损坏文件、OGG、过短/超长或 >30MB 文件 → 行内明确报错；原默认/自定义音效保留；应用不崩溃。
3. **A15 静音/隐藏**：设置或右键菜单勾选“静音”并隐藏桌宠 → 计时继续，托盘可恢复，期间无音效；取消静音后恢复。
4. **设置即时生效**：改名、置顶、全屏隐藏、音量/静音、呼噜开关、行为参数立即生效；番茄时长在下一阶段生效；非法数字/空名字就地报错且不写坏设置。
5. **快捷键**：设置 → 番茄钟 → 更改，按 `Ctrl+Alt+K` 等组合成功并立即生效；冲突时就地提示且保留旧键；“恢复默认”回到 `Ctrl+Alt+P`；重启后保持。
6. **开机启动**：勾选后注册表 Run 项出现带 `--startup` 的命令，取消后移除；启动时 `--startup` 延迟 3 秒初始化。
7. **音效事件**：摸猫/拎起/落地/玩球/拍球/唤醒、专注开始/完成、休息开始/完成、暂停、跳过均有事件；专注时交互音效减半；呼噜循环可开关且淡入淡出。
8. **设置窗口**：Esc 关闭、关闭不退出应用；声音列表可试听/更换/还原/全部还原；关于页可打开数据/日志/许可；“重置所有设置”恢复默认并清理自定义音效缓存。
9. **回归**：A6–A11、A21–A22 计时、沙漏、托盘、全屏与休眠流程保持通过。
10. 运行 `dotnet run --project tests/KaixinPet.Pomodoro.Tests` 与 `dotnet run --project tests/KaixinPet.Audio.Tests` 均输出 `ALL PASS`。
11. **水彩设置页**：五个页面无内容丢失；滑杆可用鼠标、点击轨道、方向键与 PageUp/PageDown；值签、节奏摘要、轮次点同步；静音时音量滑杆禁用、关闭自发玩耍时玩耍滑杆禁用且保留数值；非法名字/热键冲突就地提示。
12. **水彩右键菜单**：右键桌宠弹出；打开时前台应用不变；Esc 或点击外部关闭；勾选“始终置顶/静音”、禁用拖拽中的玩耍、命令与原生菜单一致。
13. **番茄钟浮层**：收起 84×30 仍点击穿透；悬停展开 216×136；阶段、时间和进度随计时更新；专注/休息/暂停配色正确，四个按钮可用。
14. **DPI 与图标**：100%/150% 缩放下文字不截断、卡片可滚动；所有 24×24 SVG viewBox 图标视觉尺寸与基线一致，无错位。

## 目录结构（当前）

```
src/
  KaixinPet.Core/      路径、日志、进程指标、设置
  KaixinPet.Pomodoro/  相位/运行态、计时引擎、轮次/自动流转
  KaixinPet.Behavior/  行为状态机、调度、毛线球物理
  KaixinPet.Pet/       分层窗口、动画播放器、多图集渲染、拖拽/坠落
  KaixinPet.Audio/     NAudio 音频服务、事件目录、导入/转码
  KaixinPet.App/       WPF 宿主、计时面板、水彩设置窗口/右键菜单、托盘、系统事件、快捷键
  KaixinPet.App/Themes/  Watercolor.xaml 统一水彩主题与控件模板
assets/
  characters/kaixin/   角色图集（1x 占位）
  props/               沙漏、毛线球、阴影
  sounds/default/      13 个默认占位音效（合成）
  sounds/sounds.json   音效事件、音量、冷却、循环定义
  behaviors.json       行为权重/冷却
  ui/                  app.ico、托盘图标、paper-tile.png 静态纸纹
licenses/audio.txt     默认音效与 NAudio 许可说明
tests/
  KaixinPet.Pomodoro.Tests/  计时引擎控制台测试
  KaixinPet.Audio.Tests/     事件目录与导入转码测试
tools/
  generate-placeholder-art.ps1
  generate-placeholder-sounds.ps1
  generate-ui-paper.ps1
  generate-icons.ps1
docs/                 设计文档与美术计划
Reference/            角色照片与风格参考
```

## 占位美术

M2/M3 的猫帧使用 192×192 的 1x 占位画布以控制多动作内存；正式水彩素材按任务清单生产 2x 母版后替换 `assets/` 并更新 JSON 的 `masterScale`/`canvas` 即可。

## 界面外观替换

- 颜色、字体、圆角、卡片、按钮、导航、开关、滑杆与图标样式集中在 `src/KaixinPet.App/Themes/Watercolor.xaml`；换主题优先改这里；
- 纸纹是静态可缓存的 `assets/ui/paper-tile.png`，由 `tools/generate-ui-paper.ps1` 生成，不随帧变化；
- 图标使用 24×24 SVG viewBox 的 Path 数据，统一按整体缩放，不再按各图形自身边界 `Stretch`；
- 设置窗口布局在 `Ui/SettingsWindow.xaml`，右键菜单在 `Ui/PetMenuWindow.xaml`，计时浮层在 `Ui/TimerPanelWindow.xaml`；
- code-behind 只更新命名元素与业务状态，不包含计时/音频/存储逻辑；大改布局时同步命名的布局元素即可。

正式美术完成后优先替换主题画刷、纸纹与图标 Path，不需要动 `PomodoroEngine` 或音频服务。

## 已知限制

- 默认音效是项目自制的合成占位音，不是真实猫叫；建议在正式美术阶段替换。
- 无音频设备（或设备被拔出）时静默运行并记录一次警告，下一次事件自动重试；不会阻止计时。
- 深浅主题切换、宠物大小设置尚未接入（M5）；当前固定浅色水彩主题，浮层/菜单在系统关闭客户端动画时取消淡入淡出。
- 托盘菜单仍为系统原生菜单；桌宠右键已改为 WPF 非模态浮层。
- 锁屏专项处理（停止渲染）尚未加入；全屏隐藏与休眠暂停已实现。
- 占位动作/道具表现有限；正式水彩美术在 M5。
- M4 仍加载全部占位图集，空闲内存约 90–140MB；M5 优化缓存与正式素材。
- 安装器与卸载清理在 M6；当前可用便携模式手动迁移数据。
