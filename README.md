# 开心桌宠（KaixinPet）

Windows 11 的「桌宠 × 番茄钟」小工具。设计文档见 [docs/开心桌宠-设计文档.md](docs/开心桌宠-设计文档.md)，美术计划见 [docs/美术素材手绘任务清单.md](docs/美术素材手绘任务清单.md)。

当前进度：**M0 技术验证**（分层窗口、帧渲染、透明穿透、沙漏/时间标签构图、性能采样）。

## M0 已实现

- Win32 分层窗口（320×280 DIP），`UpdateLayeredWindow` 逐像素 alpha 合成
- 透明区域点击穿透（alpha 阈值 + `WM_NCHITTEST`），沙漏区域保留悬停热区
- 2x 母版帧按当前 DPI 缩放，预乘 alpha 合成；待机呼吸 10fps + 随机眨眼叠加
- 左键拖拽移动，按目标显示器工作区钳制；`WM_DPICHANGED` 重建缩放缓存与 DIB
- 沙漏道具：沙量随倒计时进度裁剪上层/下层沙
- 独立 WPF 时间标签：56×22 DIP、点击穿透、不抢焦点、跟随沙漏，顶部空间不足时翻到下方
- 右键菜单：计时演示（25 分钟 / 1 分钟）、结束演示、始终置顶、打开日志目录、退出
- 日志与 CPU / 内存 / 帧率采样；24fps 基准模式
- 占位美术生成脚本（水彩风格占位猫、沙漏分层、接触阴影）

尚未包含（后续阶段）：托盘、单实例、真实番茄钟服务与轮次、行为状态机、毛线球物理、音效、设置窗口、正式水彩素材、安装器。

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

# 正常启动（猫出现在主屏工作区右下角）
dotnet run --project src/KaixinPet.App

# 启动后自动开始 25 分钟计时演示（用于验证沙漏 + 时间标签构图）
dotnet run --project src/KaixinPet.App -- --demo 25

# 24fps 基准：运行 60 秒后自动退出并写入结果
dotnet run --project src/KaixinPet.App -- --benchmark 60
```

也可以直接运行 `src/KaixinPet.App/bin/Debug/net10.0-windows/KaixinPet.exe`。

## 操作方式（M0）

| 操作 | 行为 |
| --- | --- |
| 左键拖动猫 | 移动桌宠；拖动期间沙漏与时间标签隐藏，松手恢复 |
| 右键猫 / 沙漏 | 打开菜单：计时演示、结束演示、置顶、日志目录、退出 |
| 点击猫旁透明区 | 事件穿透到下方窗口 |
| 右键菜单「打开日志目录」 | 查看 `%LocalAppData%\KaixinPet\logs\app-*.log` |

时间标签只读且点击穿透，不拦截桌面操作。

## 验证清单（M0 验收）

1. 启动 ≤ 1.2 秒内出现猫；任务栏无按钮，Alt+Tab 不出现桌宠窗口。
2. 观察 10 秒：待机呼吸循环、随机眨眼，脚底不滑动。
3. 右键 → 开始计时演示：沙漏出现在猫右侧地面，时间标签出现在沙漏正上方并每秒更新；标签不遮挡猫脸。
4. 拖动猫：沙漏与标签隐藏；松手后恢复，位置钳制在显示器工作区内。
5. 点击猫旁边透明区域：点击落到下方窗口（桌宠不接收）；点击猫身体可拖动。
6. 日志中每 10 秒输出 `stats: cpu=…%, mem=…MB, commits/s=…`。
7. `--benchmark 60` 结束后日志出现 `BENCHMARK RESULT`；M0 目标为 24fps 下平均帧率 ≥ 23、CPU ≤ 4%（参考机 4 核）、峰值内存 ≤ 130MB。
8. DPI：在 125% / 150% / 200% 缩放下重复 1–5；也可在双屏不同 DPI 间拖动验证。

## 目录结构（当前）

```
src/
  KaixinPet.Core/     路径、日志、进程指标
  KaixinPet.Pet/      Win32 分层窗口、帧渲染、动画时钟、场景布局
  KaixinPet.App/      WPF 宿主、时间标签、计时演示、基准模式
assets/
  characters/kaixin/  character.json + 占位待机/眨眼图集
  props/              props.json + 沙漏分层、接触阴影
tools/
  generate-placeholder-art.ps1   生成占位 PNG（正式美术到位后可删除）
docs/                 设计文档、美术任务清单与生图提示词
Reference/            角色照片、风格参考与待机视频
```

## 占位美术

当前猫、沙漏、阴影均由脚本生成的占位 PNG，仅用于验证尺寸、锚点、帧率与构图；正式水彩素材按美术任务清单生产后，替换 `assets/characters/kaixin/` 与 `assets/props/` 中的文件并更新 JSON 即可，渲染管线不需要改动。

重新生成占位素材：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/generate-placeholder-art.ps1
```

## 已知限制

- 输入法候选窗、部分应用的置顶浮层可能遮挡桌宠（系统 UI 层级高于普通置顶窗口）；移开输入法即可。
- 托盘、单实例、音效、设置窗口、正式美术均为后续阶段内容，当前版本没有。
- 右键菜单暂用系统原生菜单，M1 会替换为 Fluent 风格自绘菜单。
- 数据仅写入 `%LocalAppData%\KaixinPet\`；M0 不写注册表，不联网。
