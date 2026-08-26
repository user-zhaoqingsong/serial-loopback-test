# 串口环回测试

[![Build](https://github.com/user-zhaoqingsong/serial-loopback-test/actions/workflows/build.yml/badge.svg)](https://github.com/user-zhaoqingsong/serial-loopback-test/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Windows 串口自动环回测试工具，支持双端口中继环回和单端口全双工自环。

运行环境：Windows 10/11，.NET Framework 4.8。

[下载最新 Windows EXE](https://github.com/user-zhaoqingsong/serial-loopback-test/releases/latest)

也可以使用 Node.js 16+ 直接下载、校验并启动：

```powershell
npx serial-loopback-test
```

## 功能

- 双端口环回：A 发送，B 校验并原样回传，A 再次校验。
- 单端口全双工自环：只打开端口 A，通过 TX/RX 自环验证收发。
- 20、40、60、80、100 字节固定帧长或逐帧随机长度。
- 递增、全 55、全 AA、55/AA 交替、自定义 HEX 或随机内容。
- 常用波特率选择，以及 300–3000000 范围内的自定义波特率。
- 错误和超时持续统计，不中断后续环回；支持实时耗时和日志查看。

## 接线

### 双端口环回

- A.TX → B.RX
- A.RX ← B.TX
- A.GND ↔ B.GND

### 单端口全双工自环

- UART/RS-232：端口 A 的 TX 与 RX 短接。
- RS-422 或四线制差分端口：A.TX+ → A.RX+，A.TX- → A.RX-。
- 根据设备要求连接参考 GND。端口 B 在此模式下不会打开。

## 使用方法

1. 打开 `dist/串口环回测试.exe`，选择“双端口环回”或“单端口全双工自环”。
2. 选择 COM 端口并设置波特率、最低回传超时、预设内容和帧长。默认通信格式为 8N1、无流控。
   波特率下拉框提供常用值，也可以直接输入 300–3000000 范围内的整数；最终是否支持由串口硬件及驱动决定。
3. 帧长可选择 20、40、60、80、100 字节；也可独立启用“内容随机”和“帧长随机”。
4. 点击“开始环回测试”。任一参与端口校验失败或本轮超时只会累计错误，程序仍继续下一轮；串口断开等不可恢复错误才会停止。
5. 查看当前模式对应的收发、正确/错误计数，以及最近和平均环回耗时。

预设内容支持递增字节、全 55、全 AA、55/AA 交替和自定义 HEX 循环。自定义 HEX 支持空格、连续字符、逗号、短横线和 `0x` 前缀。运行日志可随时清空。

程序会根据波特率和帧长度估算完整往返所需时间。在 9600/19200 等低波特率下，实际超时会自动增加，界面输入值只作为最低阈值；启动日志会显示本轮采用的实际超时时间。

## 构建

在 Windows PowerShell 中运行 `build.ps1`。脚本使用系统自带的 .NET Framework 4.x 编译器，执行核心逻辑测试后在 `dist` 目录生成单个 EXE。

GitHub Actions 会在每次推送和 Pull Request 时使用 Windows runner 执行相同的构建与测试，并上传 EXE 构建产物。

## 参与贡献

欢迎提交 Issue 和 Pull Request。提交代码前请运行 `build.ps1` 并确认全部测试通过。

## 许可证

本项目采用 [MIT License](LICENSE)。
