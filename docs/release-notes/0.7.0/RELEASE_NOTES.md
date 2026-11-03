# 开心桌宠 0.7.0（M6）

发布日期：2026-11-03

## 下载

| 文件 | 大小 | 说明 |
| --- | --- | --- |
| `KaixinPet-0.7.0-Setup.exe` | 51.2 MB | 当前用户安装器，无需管理员 |
| `KaixinPet-0.7.0-win-x64-portable.zip` | 70.1 MB | 自包含便携包，解压即用 |
| `SHA256SUMS.txt` | — | 以上两个文件的 SHA256 |

## 安装

1. 运行 `KaixinPet-0.7.0-Setup.exe`，按向导安装到 `%LOCALAPPDATA%\Programs\KaixinPet`。
2. 从开始菜单启动“开心桌宠”。
3. 在“设置 → 应用 → 已安装的应用”或开始菜单的“卸载 开心桌宠”中卸载；卸载时会询问是否删除 `%LOCALAPPDATA%\KaixinPet` 中的设置、日志和自定义音效，静默卸载默认保留数据。

安装包未签名，SmartScreen 会提示；请选择“更多信息 → 仍要运行”，并按下方 SHA256 校验文件。

## 便携版

解压 `KaixinPet-0.7.0-win-x64-portable.zip` 后直接运行 `KaixinPet.exe`。包内含 `portable.flag`，设置与日志保存在同目录 `data/`，不会写入 `%LOCALAPPDATA%`。

## SHA256

```text
6aaace4d16fb6e5b0aeeaa2ab69a6d94aecd96000c8742a2158baf73b7466cbe  KaixinPet-0.7.0-Setup.exe
ea9981e5e7d743dfc92ab9d87e75abf86dd655730829d23edadfe503e32c6e3d  KaixinPet-0.7.0-win-x64-portable.zip
```

校验：

```powershell
Get-FileHash .\KaixinPet-0.7.0-Setup.exe -Algorithm SHA256
Get-FileHash .\KaixinPet-0.7.0-win-x64-portable.zip -Algorithm SHA256
```

## 验证记录

- 安装器静默安装/卸载：安装文件、开始菜单、卸载项创建成功；卸载后目录、快捷方式和注册表项清理完成，用户数据保留。
- 60 秒浸泡冒烟：`target=24fps actual_avg=24.0fps min=23 max=25 dropped_total=0 cpu_avg=0.29% cpu_max=2.43% private_peak=105.9MB`。
- 完整 8 小时浸泡：`powershell -File scripts\soak.ps1 -Hours 8`，完成后查看 `BENCHMARK RESULT`；本版本发布包已构建并通过安装/卸载与冒烟验证，8 小时结果作为最终验收记录另行执行。
