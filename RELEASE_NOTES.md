# VpyAudioCutter 1.0.0

首次公开发布。

## 功能

- 静态读取 VPY / AVS 中的 Trim 区间，不执行脚本。
- 生成 MeGUI 兼容的 CLT 文件。
- 支持直接音频和 TS、M2TS、MKV、MP4 等媒体容器。
- 支持多音轨选择。
- 优先使用 eac3to 抽取容器音轨，失败时回退到 ffmpeg。
- BeSplit 支持的格式复刻 MeGUI Audio Cutter 流程。
- 其他格式自动检查压缩音频包边界：可安全直通时不重新编码，否则在 PCM 域精确切割后编码回原格式。

## 下载

- `self-contained`：无需安装 .NET，体积较大。
- `framework-dependent`：体积较小，需要 .NET 8 Desktop Runtime。

## 外部工具

Release 不包含 ffmpeg、eac3to 或 BeSplit。程序会自动搜索 MeGUI 工具目录、程序目录和 PATH，也可手动选择。

这些工具分别遵循各自的许可协议。

## 致谢

本工具的 CLT、Audio Cutter 与 BeSplit split/join 工作流受到开源项目 [MeGUI](https://github.com/Kurtnoise-zeus/megui) 的启发。感谢 MeGUI 项目及其贡献者提供的长期维护和源码参考。

本项目是独立第三方工具，不是 MeGUI 官方组件。
