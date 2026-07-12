# VpyAudioCutter 1.0.1

修复选项区在高 DPI 或窗口宽度不足时自动换行造成的错位。

## 修复

- 第一行固定显示帧率、过渡方式和“解析脚本”。
- 第二行完整显示音轨下拉框、“分析媒体”和“工具...”。
- 音轨下拉框会随窗口宽度伸缩，不再把对应按钮单独挤到下一行。
- 帧率、过渡方式和“解析脚本”按垂直中心严格对齐。
- 将不准确的“CLT 样式”改为“过渡方式”；CLT 内部仍按兼容格式写入 `<Style>`。

## 下载

- `self-contained`：无需安装 .NET，体积较大。
- `framework-dependent`：体积较小，需要 .NET 8 Desktop Runtime。

## 外部工具

Release 不包含 ffmpeg、eac3to 或 BeSplit。程序会自动搜索 MeGUI 工具目录、程序目录和 PATH，也可手动选择。

这些工具分别遵循各自的许可协议。

## 致谢

本工具的 CLT、Audio Cutter 与 BeSplit split/join 工作流受到开源项目 [MeGUI](https://github.com/Kurtnoise-zeus/megui) 的启发。感谢 MeGUI 项目及其贡献者提供的长期维护和源码参考。

本项目是独立第三方工具，不是 MeGUI 官方组件。
