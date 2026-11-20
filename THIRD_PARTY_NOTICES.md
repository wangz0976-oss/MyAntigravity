# 第三方声明

MyAntigravity 通过公开的 Agent Client Protocol 与 Google Antigravity CLI 通信。官方 CLI 由 Google 独立提供和许可，通过用户安装或项目内的 `local\agy` 单独获取。MyAntigravity 的安装包不包含该 CLI。

争渡读屏集成调用本机已安装的官方 ZDSRAPI 动态库。争渡读屏及其动态库由原厂独立提供，安装包不会分发该动态库。

发布成品随附 Microsoft .NET 和 Windows Desktop Runtime。运行时原始许可证及第三方声明见分发目录中的 `licenses`。安装程序由 Inno Setup 6 生成，Inno Setup 编译器与 MyAntigravity 源码分开分发。

安装向导使用 Inno Setup 的官方简体中文翻译，维护者为 Zhenghan Yang（Kira）。翻译来自[上游提交 c34976f](https://github.com/jrsoftware/issrc/blob/c34976f7d107e94d4fce8ac29f72ca33762293d7/Files/Languages/ChineseSimplified.isl)，原始许可见 [Inno Setup License](packaging/Inno-Setup-LICENSE.txt)。

## 参考项目

以下仓库用于调研产品设计、Windows 无障碍、进程通信、会话和发布流程。本项目独立实现 ACP 客户端，未复制这些仓库的源代码或二进制资源。

| 项目 | 调研提交 | 根 LICENSE 的版权声明 |
| --- | --- | --- |
| [MyCodex](https://github.com/jintonglu6688/MyCodex/tree/9ffffa8) | `9ffffa8` | 2026 jintonglu6688，MIT |
| [MyCodex-auth-host](https://github.com/jintonglu6688/MyCodex-auth-host/tree/e120f4d) | `e120f4d` | 2025 Jason Young，MIT |
| [MyCodex-relay](https://github.com/jintonglu6688/MyCodex-relay/tree/3b9ea9a) | `3b9ea9a` | 2026 jintonglu6688，MIT |
| [MyCodex-android](https://github.com/jintonglu6688/MyCodex-android/tree/4ec7767) | `4ec7767` | 2026 jintonglu6688，MIT |
