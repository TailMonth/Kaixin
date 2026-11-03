# 开心 0.7.1

正式发售版本。本版本起软件名统一为「开心」。

## 下载

| 文件 | 大小 | 说明 |
| --- | --- | --- |
| `Kaixin-0.7.1-Setup.exe` | 51.2 MB | 当前用户安装版，无需管理员 |
| `Kaixin-0.7.1-win-x64-portable.zip` | 70.1 MB | 自包含便携版，解压即用 |
| `SHA256SUMS.txt` | — | 两个安装包的 SHA256 |

## 安装

1. 运行 `Kaixin-0.7.1-Setup.exe`，安装到 `%LOCALAPPDATA%\Programs\KaixinPet`。
2. 从开始菜单启动「开心」。
3. 卸载前请先退出开心，然后使用开始菜单「卸载 开心」或“设置 → 应用 → 已安装的应用”。

安装包未签名，SmartScreen 会提示；请选择“更多信息 → 仍要运行”，并按下文 SHA256 校验。

## 便携版

解压 `Kaixin-0.7.1-win-x64-portable.zip` 后运行 `KaixinPet.exe`。包内含 `portable.flag`，设置与日志保存在同目录 `data/`，不会写入 `%LOCALAPPDATA%`。

## SHA256

```text
9ddefe35c96108d0db95e04172aa409b2c8c41bf0a598db4e793c297307b8a0b  Kaixin-0.7.1-Setup.exe
51a0fa380097c334a1e7a975efea74947c396bf43c3f8806b12d7d5f31cad126  Kaixin-0.7.1-win-x64-portable.zip
```

校验：

```powershell
Get-FileHash .\Kaixin-0.7.1-Setup.exe -Algorithm SHA256
Get-FileHash .\Kaixin-0.7.1-win-x64-portable.zip -Algorithm SHA256
```

## 验证记录

- 安装器：静默安装 exit 0；安装文件、开始菜单「开心」与卸载项创建成功；显示名称为「开心」、版本 0.7.1；卸载后目录、快捷方式和注册表项清理完成，用户数据保留。
- 60 秒浸泡冒烟：`target=24fps actual_avg=24.0fps min=23 max=25 dropped_total=0 cpu_avg=0.22% cpu_max=2.70% private_peak=97.3MB`。
- 完整 8 小时浸泡：`powershell -File scripts\soak.ps1 -Hours 8`；完成后查看 `BENCHMARK RESULT`。本版本已通过安装/卸载与冒烟验证，8 小时结果作为最终验收记录单独执行。

## 说明

- Windows 10/11 x64，self-contained，无需预装 .NET。
- 设置、日志和自定义音效保存在本机；应用不联网、不上传数据。
- 本版本起软件名统一为「开心」，安装包与关于页同步更新；上一版本的 v0.7.0 标签未创建 Release。
