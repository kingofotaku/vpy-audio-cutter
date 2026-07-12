# VpyAudioCutter 1.0.2

进一步修正选项区在不同 Windows DPI、字体渲染和控件模式下出现的细微视觉错位，并加入正式应用图标。

## 修复

- 帧率、过渡方式和音轨选择器改用同一个原生控件实现，消除不同下拉框模式造成的 1 像素高度差。
- 帧率仍可手动输入；过渡方式和音轨保持只读选择，不改变原有操作方式。
- “解析脚本”“分析媒体”和“工具...”按钮按系统计算出的下拉框高度对齐。
- UI 测试对帧率、过渡方式及其标签使用零容差位置检查。
- 增加多尺寸橙色剪刀与音频波形图标，用于窗口标题栏和 EXE 文件。

本版本不改变 VPY/AVS Trim 解析、CLT 格式、音频提取或切割逻辑。

## 下载

- `self-contained`：无需安装 .NET，体积较大。
- `framework-dependent`：体积较小，需要 .NET 8 Desktop Runtime。

## 外部工具

Release 不包含 ffmpeg、eac3to 或 BeSplit。程序会自动搜索 MeGUI 工具目录、程序目录和 PATH，也可手动选择。

这些工具分别遵循各自的许可协议。

## 致谢

本工具的 CLT、Audio Cutter 与 BeSplit split/join 工作流受到开源项目 [MeGUI](https://github.com/Kurtnoise-zeus/megui) 的启发。感谢 MeGUI 项目及其贡献者提供的长期维护和源码参考。

本项目是独立第三方工具，不是 MeGUI 官方组件。
