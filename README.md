# 开心桌宠（KaixinPet）

Windows 11 的「桌宠 × 番茄钟」小工具。设计文档见 [docs/开心桌宠-设计文档.md](docs/开心桌宠-设计文档.md)，美术计划见 [docs/美术素材手绘任务清单.md](docs/美术素材手绘任务清单.md)。

当前进度：**M2 行为**（待机微动作、睡觉、玩毛线球、拖拽与重力下落）。版本 `0.3.0-m2`。

## 已实现

**桌宠与计时演示（M0）**

- Win32 分层窗口（320×280 DIP），`UpdateLayeredWindow` 逐像素 alpha 合成
- 透明区域点击穿透（alpha 阈值 + `WM_NCHITTEST`），沙漏区域保留热区
- 2x 母版帧按当前 DPI 缩放；独立 WPF 时间标签，点击穿透、跟随沙漏
- 沙漏沙量随倒计时进度裁剪；日志与 CPU/内存/帧率采样、24fps 基准模式

**托盘与外壳（M1）**

- 单实例、系统托盘（4 状态图标、tooltip、菜单、双击唤出、TaskbarCreated）
- 托盘/桌宠菜单：显示/隐藏、计时演示、置顶、重置位置、数据/日志目录、退出
- 窗口位置记忆（退出保存、启动恢复、越界回退、重置位置）；置顶持久化
- 设置存储（原子写、防抖、损坏备份、未知字段保留、参数校验）与便携模式

**行为与动作（M2）**

- 行为状态机：Idle / Sleep / Play / Drag / Fall；优先级与可打断/可恢复
- 待机微动作：眨眼、左右看、舔毛、伸懒腰、甩尾、耳朵抖动；每 500ms 按权重/冷却调度
- 单击摸猫：眯眼、爱心粒子；睡眠中单击先唤醒；区分点击与拖拽
- 睡觉：curl → breath 循环 → 随机耳朵/胡须抽动 → wake；Zzz 叠加与睡眠阴影；手动或待机超时入睡
- 玩毛线球：固定步长物理（重力 1600 DIP/s²、地面弹性 0.45、摩擦、窗口钳制）；notice → chase/bat → flop；猫随球横向移动、拍球给球脉冲；球静止 2s 或会话超时结束
- 拖拽：grab → hold → 松手下落 → land；按最近 100ms 速度计算初速度，重力落回工作区底部；拖拽/下落期间沙漏与时间标签隐藏
- 桌宠与托盘菜单新增「陪它玩毛线球」「让它睡觉 / 叫醒它」；托盘 tooltip 显示空闲/睡觉中/玩耍中
- 播放帧率随状态切换：待机 10fps，玩耍/拖拽/下落 24fps
- 行为参数可在 `assets/behaviors.json` 调整（权重、冷却、时长、自发玩耍概率）

尚未包含（后续阶段）：真实番茄钟服务与轮次、系统电源/全屏/锁屏事件、音效、设置窗口、开机启动、正式水彩素材、安装器。

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

## 操作方式（M2）

| 操作 | 行为 |
| --- | --- |
| 左键单击猫 | 摸猫：开心眯眼 + 爱心；睡眠时先醒来 |
| 左键拖动猫 | 拎起、跟随鼠标；松手后重力落回工作区底部并保存位置 |
| 右键猫 / 沙漏 | 桌宠菜单：玩毛线球、睡觉/叫醒、计时演示、置顶、隐藏、退出 |
| 托盘图标单击/双击 | 恢复并置前桌宠；计时中高亮时间标签 1 秒 |
| 右键托盘图标 | 托盘菜单：显示/隐藏、玩球、睡觉/叫醒、计时、置顶、重置、退出 |
| 点击猫旁透明区 | 事件穿透到下方窗口 |
| 再次启动应用 | 不创建第二个进程，唤起已有桌宠 |

时间标签只读且点击穿透，不拦截桌面操作。

## 行为参数与数据

```
%LocalAppData%\KaixinPet\
  settings.json          设置（行为参数、位置、置顶等）
  settings.bak.json      上一次设置的原子备份
  settings.corrupt-*.json 损坏设置的备份
  logs\app-YYYYMMDD.log  运行日志
```

- 行为参数：`behavior.sleepAfterIdleMinutes`（默认 15 分钟）、`behavior.playSessionMaxSeconds`（默认 90）、`behavior.spontaneousPlay`。
- 编辑 `settings.json` 后重启生效；设置界面在 M4 提供。
- `assets/behaviors.json`：待机动作权重/冷却、眨眼间隔、睡觉抽动间隔、玩耍时长与自发概率；缺失时使用内置默认值。
- 便携模式：exe 旁放空的 `portable.flag`，数据写入 `.\data`。

## 验证清单（M2 验收）

1. 单击猫：开心眯眼 + 爱心，猫不移动；拖动超过阈值才进入拖拽。
2. 拖动猫到高处松手：猫进入下落姿态，最后落到工作区底部并播放落地动作；沙漏/时间标签在拖拽与下落期间隐藏。
3. 观察待机 1–3 分钟：呼吸/眨眼，并至少出现一次左右看、舔毛、伸懒腰、甩尾或耳朵抖动。
4. 托盘菜单「让它睡觉」：猫蜷起、闭眼、出现 Zzz 与睡眠阴影；「叫醒它」或单击猫后醒来回到坐姿。
5. 托盘/桌宠菜单「陪它玩毛线球」：毛线球出现并弹跳，猫跟随球横向移动、抬爪拍球；球静止约 2 秒或会话超时后猫趴下回待机。
6. 玩球/拖拽期间帧率约 24fps；待机约 10fps；日志每 10 秒输出 cpu/内存/帧率。
7. 玩球中拖拽猫：玩耍立即结束，进入拖拽/下落，落地后回到待机（可恢复）。
8. 睡觉中拖拽：先被拎起，落地后不会卡在睡觉或拖拽状态。
9. 隐藏桌宠再显示：行为与计时继续，托盘 tooltip 正确。
10. 置顶/位置/单实例/设置损坏/便携模式按 M1 清单复验。

## 目录结构（当前）

```
src/
  KaixinPet.Core/      路径、日志、进程指标、设置模型与存储
  KaixinPet.Behavior/  行为状态机、调度器、毛线球物理、behaviors.json 模型
  KaixinPet.Pet/       Win32 分层窗口、动画播放器、多图集渲染、场景、拖拽/坠落
  KaixinPet.App/       WPF 宿主、时间标签、计时演示、托盘、单实例、基准模式
assets/
  characters/kaixin/   character.json + 待机/微动作/睡觉/玩耍/拖拽/表情图集（1x 占位）
  props/               props.json + 沙漏、毛线球、接触阴影
  behaviors.json       行为权重/冷却/时长
  ui/                  app.ico + 4 个托盘状态图标
tools/
  generate-placeholder-art.ps1   生成占位帧、毛线球与阴影
  generate-icons.ps1             生成 app/tray ICO
docs/                 设计文档、美术任务清单与生图提示词
Reference/            角色照片、风格参考与待机视频
```

## 占位美术

M2 的猫帧使用 **192×192 的 1x 占位画布**，以控制多动作图集的内存；正式水彩素材按任务清单生产 2x 母版（384×384）后，替换 `assets/characters/kaixin/` 与 `assets/props/` 并更新 JSON 中的 `masterScale`/`canvas` 即可，渲染管线无需改动。毛线球、阴影、图标同样为程序生成占位。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/generate-placeholder-art.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/generate-icons.ps1
```

## 已知限制

- 托盘与桌宠菜单仍是系统原生菜单；菜单打开时动画会暂停（原生模态菜单阻塞 UI 线程），Fluent 自绘非模态菜单在后续阶段实现。
- 占位动作的美术表现有限（舔毛、追球等动作较概括）；正式美术在 M5 替换。
- 当前计时器是演示计时，不是完整番茄钟（暂停/轮次/长休息/系统电源事件在 M3）。
- 本机输入法候选窗、部分应用置顶浮层可能遮挡桌宠。
- M2 加载全部占位图集，空闲内存约 73–95MB；M5 会加入按状态懒加载/LRU 与正式素材优化。
- 开机启动、全屏自动隐藏、音效、设置界面为后续阶段内容。
