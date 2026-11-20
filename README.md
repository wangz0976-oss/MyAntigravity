# MyAntigravity

面向盲人及低视力开发者的 Windows 客户端。它调用 Google Antigravity CLI 的 ACP 接口，让你用标准 Windows 控件和键盘选择项目、读写会话、审批工具操作和检查文件修改。MyAntigravity 是免费、开源的独立项目，与 Google 和 OpenAI 无官方隶属关系。感谢 MyCodex 及其配套项目提供无障碍交互与发布流程方面的参考。来源见 [第三方声明](THIRD_PARTY_NOTICES.md)。

## 在 Windows 上开始

1. 打开 `dist\app\MyAntigravity.exe`。也可以运行 `dist\MyAntigravity-Setup.exe`，并在简体中文安装向导里选择自己的安装路径。当前产物是 Windows x64 版，自带 .NET 运行时。
2. 在“账户”菜单选择“检测 CLI 版本”。在本项目目录运行时，程序会识别 `local\agy\agy.exe`。其他电脑按照 [Google 官方安装指南](https://antigravity.google/docs/getting-started) 安装 CLI，或在“工具 → 设置”选取已有的 `agy.exe`。
3. 使用“账户 → 用浏览器登录 Google”，在 Google 页面完成授权后返回。已有官方 CLI 登录状态会自动复用，可以直接新建会话。需要终端授权时选“用官方终端登录”，它运行官方命令 `agy acp --login`。
4. 按 **Ctrl+O**，输入完整项目路径并按回车。也可以在对话框选“浏览文件夹”。按 **Ctrl+N** 新建会话。输入消息，用 **Ctrl+Enter** 发送。回车换行。**Ctrl+M** 打开当前会话的模型、权限模式和推理等级。权限模式有中文说明，模型名沿用官方原名。用方向键选配置项，按 Tab 到可选值，再按一次 Tab 查看说明。
5. 用 **F6** 与 **Shift+F6** 切换项目、历史、消息、输入、状态区。Tab 与 Shift+Tab 在控件之间移动。按 Alt 后用方向键浏览菜单。**Ctrl+L** 查看任务日志，**Ctrl+D** 查看 ACP 返回的文件差异，**Ctrl+I** 查看完整状态。**Ctrl+Shift+X** 请求中止。若 CLI 无响应，用 **Ctrl+Shift+D** 断开进程，再从历史恢复。
6. 按 **Ctrl+Shift+R** 朗读最新回复。在默认目录安装争渡时，程序会调用争渡语音接口；语音接口不可用时，快捷键会打开放有回复正文的可读窗口。按键可打断争渡接口的朗读。
7. 审批弹窗先把焦点放在详情。可用方向键阅读，或按 **Ctrl+Shift+R** 让争渡朗读详情。用 **Alt+Y** 允许本次，也可按 Tab 找到“允许本次”按钮后按空格。回车或 Esc 拒绝，**Ctrl+Shift+X** 中止任务。何时出现审批由官方 CLI 的权限配置及会话模式决定。
8. 按 **Ctrl+Shift+O** 搜索已添加的项目，或按 **Ctrl+K** 按名称、日期或 ID 查找当前项目的会话。会话列表中按 **F2** 可保存本地名称，清空后恢复默认显示。官方自动生成的标题只在当前运行期间显示。
9. 打开会话后按 **Ctrl+Shift+P** 搜索该会话收到的官方命令。方向键选命令，Tab 可读说明及参数，回车填入输入框。内容核对后按 **Ctrl+Enter** 发送。
10. 在历史会话列表按 **Shift+F10** 打开会话操作菜单，可恢复、重命名或移入本地归档。按 **Ctrl+Shift+A** 归档历史列表中选中的会话，按 **Ctrl+Shift+H** 查找本地归档并恢复。归档不会删除官方会话。
11. 在支持图片的会话按 **Ctrl+Shift+I** 打开图片附件。按 **Ctrl+O** 选图，列表用方向键选择，回车查看图片及文件名、尺寸、格式和大小，**Delete** 从草稿移除，**Esc** 返回。支持 PNG、JPEG、GIF、BMP 和 TIFF，最多 8 张，单张 8 MiB、合计 12 MiB。附件随 **Ctrl+Enter** 发送，原文件路径不会放入 ACP 请求。草稿在本次运行期间按会话保存。
12. 按 **Ctrl+Shift+S** 打开“账户与连接”。这里显示上次登录核验时间、官方返回的套餐（如有）、当前 ACP 连接及 CLI 版本。按 **Alt+R** 重新核验。核验会打开独立的空会话，不发送消息。使用官方 CLI 1.5.0 或更新版本时，按 **Alt+U** 刷新模型额度，窗口会显示官方返回的剩余比例和重置时间。按 **Alt+T** 也可打开官方终端并复制 `/usage`，在终端按 **Ctrl+V**、**回车**查询。**Ctrl+I** 还可以查看官方 ACP 报告的会话上下文与最近一轮 token 用量。
13. 按 **Ctrl+D** 打开文件差异审查。用 **Ctrl+Page Up / Down** 切换文件，**F7 / F8** 切换差异段，也可用方向键选择、回车进入差异段或阅读详情。审批包含差异时，可以按 **Alt+W** 打开同一个审查窗口，返回后再作决定。
14. 按 **Ctrl+Shift+L** 查看 ACP 提供的任务计划与进度。“工具 → 官方插件列表”显示官方 CLI 返回的插件名称、版本和启用状态。
15. 在“项目列表”“历史会话”或“本地归档”按 **Delete** 打开移除确认。主窗口也可用列表的 **Shift+F10** 菜单。移除项目条目会保留磁盘目录和会话，重新添加目录即可看到原历史。删除会话只清理 MyAntigravity 的本机记录和这条会话的草稿，官方会话仍保留。在快选窗口直接用方向键浏览，按 **Ctrl+F** 可选填筛选词。
16. 在支持嵌入内容的会话中按 **Ctrl+Shift+J** 打开文本与代码附件。按 **Ctrl+O** 选文件，回车阅读文本，**Delete** 从草稿移除。支持 UTF-8 和带 BOM 的 UTF-16，每条最多 8 个文件，单个最多 256 KiB，合计最多 1 MiB。按 **Ctrl+Enter** 发送后，官方 Agent 收到文件名和文本快照。快照随本次运行的会话草稿保存在内存。
17. 官方 Agent 发来提问表单时，用 **Tab** 依次听问题和字段，用方向键、空格或输入框填写。核对后按 **Alt+S** 提交，按 **Alt+D** 拒绝回答，按 **Esc** 取消这一表单，或按 **Ctrl+Shift+X** 中止当前任务。
18. 按 **Ctrl+Shift+W** 管理当前项目的附加工作目录。按 **Ctrl+O** 添加目录，**Delete** 从清单移除，**Alt+S** 保存。支持此能力的官方 CLI 在新建或恢复会话时将这些目录加入工作范围。项目列表的 **Shift+F10** 菜单还可以复制路径或在文件资源管理器打开项目。
19. 使用已验证的 Antigravity CLI 1.5.13 或更新版本时，在任务生成过程中输入补充文字，按 **Ctrl+Shift+Enter** 追加到当前任务。表单和审批处理完后可继续追加。服务器拒收时，补充文字会回到对应会话的草稿。
20. 按 **Ctrl+F9** 打开技能、MCP 服务和插件入口。技能窗口按回车阅读说明，当前会话提供技能命令时按 **Alt+I** 填入消息框。MCP 服务窗口显示全局和工作目录配置，用 **Alt+R** 刷新，用 **Alt+T** 确认后启用或停用所选全局配置。重新打开会话后应用。项目配置可查看，数量和状态以相应配置为准。

历史列表默认显示本程序为当前目录记录的会话。选中会话后按回车或 Ctrl+H 恢复，需要时可在“会话 → 查看 CLI 列出的历史”查看已存在的官方会话。CLI 1.5.12 在测试中会把部分旧会话标为当前目录，恢复前请确认对应项目。会话消息历史由 Google CLI 保管，客户端在磁盘保存设置、项目路径、会话标识、时间和手动设置的本地名称。日志与差异在客户端内存中供本次运行查看。

在源码主目录内运行或安装时，程序使用主目录下的 `data`。独立分发时使用程序旁的 `data`。Google 账户缓存由官方 CLI 按它自己的规则管理。

## 从源码构建

需要 Windows、.NET 10 SDK 和 Git。项目里已有 `local\dotnet\dotnet.exe` 时，可以用它运行下面的 `dotnet` 命令。打包使用 Inno Setup 6.7.4。CI 也使用同一套发布脚本。

```powershell
dotnet build MyAntigravity.sln -c Release
dotnet run --project tests/MyAntigravity.Tests -c Release --no-build
pwsh -NoProfile -File packaging/Build-Release.ps1 -Iscc 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

自动测试使用隔离的 ACP 子进程和 HTTP 夹具。另可运行真实集成测试，使用已有 Google 登录状态，在项目内的空目录创建一个官方测试会话并发送一条连通消息。

```powershell
dotnet run --project tests/MyAntigravity.Tests -c Release --no-build -- --live .\local\agy\agy.exe
```

## 更新与分发

发布脚本从 `Directory.Build.props` 读取版本，构建当前 `dist`，包括便携运行目录、Inno Setup 安装包和更新清单。应用根据 Stable 或 Beta 渠道检查 GitHub Releases，展示说明和下载进度，校验 SHA-256 后才打开安装程序。同目录覆盖安装会保留设置与会话索引。安装程序临时保留已安装的可执行文件，升级成功后清理，安装失败时尝试恢复。恢复遇到文件锁时，关掉应用后重新运行安装包即可继续。

获准发布时，把安装包和对应的 `update-stable.json` 或 `update-beta.json` 放入同一条 `v<版本>` GitHub Release，测试版标为 pre-release。GitCode 镜像可以通过打包脚本的 `-GitCodeDownloadUrl` 生成对应镜像清单，保持两处安装包的内容与哈希一致。应用内自动检查目前使用 GitHub 主通道。Windows 工作流只构建和生成 CI 产物，公开发布需另行授权。

## 当前范围

支持 Windows 本地项目、文本会话、图片附件以及 UTF-8 / UTF-16 文本与源代码附件。ACP 文件操作、命令、审批和任务提问由官方 CLI 管理，客户端负责显示内容和提交用户选择。状态使用 Windows 无障碍实时区域，读屏软件的实际播报可通过键盘实测。
