using System.Windows.Forms.Automation;

namespace MyAntigravity;

internal sealed partial class MainForm : Form
{
    private readonly ListBox projects = new() { AccessibleName = "项目列表", Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox sessions = new() { AccessibleName = "历史会话", Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ComposerTextBox input = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AccessibleName = "消息输入" };
    private readonly Label inputCaption = Caption("消息输入");
    private readonly Label status = new() { Text = "请选择本地项目，或从账户菜单登录。", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button send = new() { Text = "发送 (&S)", AutoSize = true, Enabled = false };
    private readonly Button cancel = new() { Text = "中止 (&X)", AutoSize = true, Enabled = false };
    private readonly Button steer = new() { Text = "追加指令 (&A)", AutoSize = true, Enabled = false };
    private readonly ToolStripMenuItem steerMenu = Item("追加指令 (&G)", Keys.Control | Keys.Shift | Keys.Enter);
    private readonly ToolStripMenuItem newSession = Item("新建会话 (&N)", Keys.Control | Keys.N);
    private readonly ToolStripMenuItem customAgentSession = Item("使用自定义代理新建会话 (&J)");
    private readonly ToolStripMenuItem orchestrationModels = Item("选择编排模型 (&O)");
    private readonly ToolStripMenuItem collaborationCommands = Item("团队与子代理命令 (&W)");
    private readonly ToolStripMenuItem restore = Item("恢复所选会话 (&H)", Keys.Control | Keys.H);
    private readonly ToolStripMenuItem findHistory = Item("查找会话 (&K)", Keys.Control | Keys.K);
    private readonly ToolStripMenuItem findAllHistory = Item("跨项目查找会话 (&G)");
    private readonly ToolStripMenuItem renameSession = Item("重命名本地会话 (&E)");
    private readonly ToolStripMenuItem pinSession = Item("置顶本地会话 (&T)");
    private readonly ToolStripMenuItem archiveSession = Item("移入本地归档 (&A)", Keys.Control | Keys.Shift | Keys.A);
    private readonly ToolStripMenuItem archivedHistory = Item("打开本地归档 (&B)", Keys.Control | Keys.Shift | Keys.H);
    private readonly ToolStripMenuItem removeSession = Item("从本机历史删除会话 (&D)");
    private readonly ToolStripMenuItem imageMenu = Item("图片附件 (&I)", Keys.Control | Keys.Shift | Keys.I);
    private readonly ToolStripMenuItem textAttachmentsMenu = Item("文件附件 (&J)", Keys.Control | Keys.Shift | Keys.J);
    private readonly ToolStripMenuItem refresh = Item("刷新历史会话 (&R)", Keys.Control | Keys.R);
    private readonly ToolStripMenuItem officialHistory = Item("查看官方历史 (&I)");
    private readonly ToolStripMenuItem disconnect = Item("断开并停止 ACP (&D)", Keys.Control | Keys.Shift | Keys.D);
    private readonly ToolStripMenuItem optionsMenu = Item("模型、权限模式与推理 (&M)", Keys.Control | Keys.M);
    private readonly ToolStripMenuItem browserLogin = Item("用浏览器登录 Google (&G)");
    private readonly ToolStripMenuItem terminalLogin = Item("用官方终端登录 (&T)");
    private readonly ToolStripMenuItem chooseProject = Item("添加本地项目 (&O)", Keys.Control | Keys.O);
    private readonly ToolStripMenuItem quickProject = Item("快速打开项目 (&Q)", Keys.Control | Keys.Shift | Keys.O);
    private readonly ToolStripMenuItem openProjectMenu = Item("打开所选项目 (&P)");
    private readonly ToolStripMenuItem projectExplorer = Item("在文件资源管理器打开 (&F)");
    private readonly ToolStripMenuItem copyProjectPath = Item("复制项目路径 (&C)");
    private readonly ToolStripMenuItem projectChanges = Item("查看 Git 项目改动 (&G)");
    private readonly ToolStripMenuItem projectWorktrees = Item("Git 工作树 (&T)");
    private readonly ToolStripMenuItem removeProject = Item("从项目列表移除 (&D)");
    private readonly ToolStripMenuItem additionalDirectories = Item("附加工作目录 (&W)", Keys.Control | Keys.Shift | Keys.W);
    private readonly ToolStripMenuItem commandPalette = Item("插入官方命令 (&P)", Keys.Control | Keys.Shift | Keys.P);
    private readonly ToolStripMenuItem commandFinder = Item("查找软件功能 (&F)", Keys.Control | Keys.Shift | Keys.K);
    private readonly ToolStripMenuItem queuedFollowUps = Item("查看排队消息 (&Q)");
    private readonly ToolStripMenuItem withdrawFollowUp = Item("撤回最后一条排队消息 (&W)");
    private readonly ToolStripMenuItem sendMessageMenu = Item("发送消息 (&S)");
    private readonly ToolStripMenuItem settingsMenu = Item("打开设置 (&S)", Keys.Control | Keys.Oemcomma);
    private readonly ToolStripMenuItem sessionDefaultsMenu = Item("新会话默认 (&N)");
    private readonly ToolStripMenuItem updatesMenu = Item("检查更新 (&U)");
    private readonly ToolStripMenuItem extensionHub = Item("规则、技能、MCP 与插件 (&E)", Keys.Control | Keys.F9);
    private readonly ToolStripMenuItem detect = Item("检测 CLI 版本 (&V)");
    private readonly ToolStripMenuItem logMenu = Item("任务日志 (&L)", Keys.Control | Keys.L);
    private readonly ToolStripMenuItem readReplyMenu = Item("朗读最新回复 (&E)", Keys.Control | Keys.Shift | Keys.R);
    private readonly ToolStripMenuItem diffMenu = Item("文件差异 (&F)", Keys.Control | Keys.D);
    private readonly Button openProject = new() { Text = "打开所选项目 (&P)", AutoSize = true };
    private readonly Button openSession = new() { Text = "恢复所选会话 (&H)", AutoSize = true, Enabled = false };
    private readonly Button addProject = new() { Text = "添加项目 (&O)", AutoSize = true };
    private readonly Button showOptions = new() { Text = "模型与权限 (&M)", AutoSize = true, Enabled = false };
    private readonly Button imageButton = new() { Text = "图片附件 (&I)", AutoSize = true, Enabled = false };
    private readonly Button textAttachmentsButton = new() { Text = "文件附件 (&J)", AutoSize = true, Enabled = false };
    private readonly Button showStatus = new() { Text = "查看状态 (&V)", AutoSize = true };
    private readonly Button showLog = new() { Text = "任务日志 (&L)", AutoSize = true };
    private readonly Button showDiff = new() { Text = "文件差异 (&F)", AutoSize = true };
    private readonly Button openSettings = new() { Text = "设置 (&S)", AutoSize = true };
    private readonly ListBox recentActivity = new() { AccessibleName = "实时活动", Dock = DockStyle.Fill,
        IntegralHeight = false, HorizontalScrollbar = true };
    private readonly Label context = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Text = "当前项目：未选择" };

    private void BuildUi()
    {
        Text = $"MyAntigravity {Program.Version}";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1070, 710);
        MinimumSize = new Size(690, 510);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        var menu = new MenuStrip { Dock = DockStyle.Top, TabIndex = 0, AccessibleName = "主菜单" };
        var projectMenu = Item("项目 (&P)");
        menu.MenuActivate += (_, _) =>
        {
            if (!menu.Items.OfType<ToolStripMenuItem>().Any(item => item.Selected))
                projectMenu.Select();
        };
        projectWorktrees.DropDownItems.AddRange([
            Item("查看和打开工作树 (&O)", action: BrowseWorktreesAsync),
            Item("新建工作树 (&N)", action: CreateWorktreeAsync)]);
        projectMenu.DropDownItems.AddRange([chooseProject, quickProject, openProjectMenu, new ToolStripSeparator(),
            projectChanges, projectWorktrees, additionalDirectories, projectExplorer, copyProjectPath, removeProject,
            new ToolStripSeparator(), Item("退出 (&X)", action: () => { Close(); return Task.CompletedTask; })]);
        var sessionMenu = Item("会话 (&C)");
        customAgentSession.Click += async (_, _) => await StartCustomAgentAsync();
        orchestrationModels.Click += async (_, _) => await SelectOrchestratorAsync();
        collaborationCommands.Click += async (_, _) => await InsertOfficialCommandAsync(collaborationOnly: true);
        sessionMenu.DropDownItems.AddRange([newSession, customAgentSession, restore, optionsMenu, orchestrationModels,
            collaborationCommands, findHistory, findAllHistory,
            new ToolStripSeparator(), renameSession, pinSession, archiveSession, archivedHistory, removeSession,
            new ToolStripSeparator(), refresh, officialHistory]);
        var messageMenu = Item("消息 (&M)");
        var attachments = Item("添加与管理附件 (&A)");
        attachments.DropDownItems.AddRange([imageMenu, textAttachmentsMenu]);
        messageMenu.DropDownItems.AddRange([
            Item("转到消息列表 (&J)", action: () => { messages.Focus(); return Task.CompletedTask; }),
            openMessage, speakMessage, copyMessage, findMessage, readReplyMenu, readConversation, exportText,
            new ToolStripSeparator(), sendMessageMenu, queuedFollowUps, withdrawFollowUp, steerMenu, attachments, commandPalette]);
        var taskMenu = Item("任务 (&T)");
        var cancelMenu = Item("中止任务 (&X)", Keys.Control | Keys.Shift | Keys.X);
        cancelMenu.Click += async (_, _) => await RequestCancellationAsync();
        cancel.EnabledChanged += (_, _) => cancelMenu.Enabled = cancel.Enabled;
        cancelMenu.Enabled = false;
        taskMenu.DropDownItems.AddRange([Item("任务活动 (&A)", action: ShowActivityAsync),
            Item("本轮结果 (&R)", action: ShowTurnResultsAsync),
            Item("任务计划 (&P)", Keys.Control | Keys.Shift | Keys.L, ShowPlanAsync),
            logMenu, diffMenu, Item("完整状态 (&I)", Keys.Control | Keys.I, ShowStatusAsync),
            new ToolStripSeparator(), cancelMenu, disconnect]);
        var accountMenu = Item("账户 (&A)");
        accountMenu.DropDownItems.AddRange([Item("账户与连接 (&S)", Keys.Control | Keys.Shift | Keys.S, ShowAccountAsync),
            new ToolStripSeparator(), browserLogin, terminalLogin, detect,
            Item("官方 CLI 安装指南 (&I)", action: () =>
            {
                ShowCliGuide("可通过 Google 官方 PowerShell 命令安装 Windows CLI。");
                return Task.CompletedTask;
            })]);
        var preferencesMenu = Item("设置 (&S)");
        preferencesMenu.DropDownItems.AddRange([settingsMenu, sessionDefaultsMenu, extensionHub, updatesMenu]);
        var help = Item("帮助 (&H)");
        commandFinder.Click += async (_, _) => await FindApplicationCommandAsync();
        help.DropDownItems.Add(commandFinder);
        help.DropDownItems.Add(Item("查看说明 (&M)", action: ShowUserGuideAsync));
        help.DropDownItems.Add(Item("查看更新日志 (&U)", action: ShowCurrentChangelogAsync));
        help.DropDownItems.Add(Item("键盘操作 (&K)", action: () =>
        {
            Dialogs.Text(this, "键盘操作", """
                按 Alt 进入主菜单，左右选分类，下键打开，上下选功能，回车执行，Esc 返回。
                “项目”管理目录，“会话”管理历史和模型，“消息”管阅读、发送和附件。
                “任务”查看计划、日志和文件差异，也能中止任务。“账户”管登录和额度。
                Tab / Shift+Tab 移动焦点。F6 / Shift+F6 在项目、历史、消息、实时活动、输入和状态间切换。

                消息列表用上下逐条选，左右跳到上一轮或下一轮提问。
                回车读所选消息的全文，Esc 回到原位置。Ctrl+C 复制全文，Ctrl+F 查找当前会话的消息。
                从“消息”菜单可以朗读所选消息或最新回复。全文窗口可跳转到代码段和链接。
                从“任务”菜单查看本轮结果和活动。从“项目”菜单查看 Git 项目改动、打开或新建工作树。
                “会话”菜单可以置顶本地会话。“设置”里的扩展入口可以查阅 Antigravity 规则。
                “会话”菜单能跨项目查找历史。“消息”菜单可将整段对话或所选消息预览后保存为文本。

                常用快捷键
                Ctrl+O：输入项目目录，按回车打开。也可以选择“浏览文件夹”。
                Ctrl+Shift+O：在已添加的项目中直接选、打开或移除。Ctrl+F 筛选。
                Ctrl+Shift+W：管理当前项目的附加工作目录。
                Ctrl+N：新建会话。Ctrl+R：刷新历史。Ctrl+H：恢复所选会话。
                Ctrl+K：查找当前项目的会话。会话列表中按 F2 编辑本地名称。
                Ctrl+Shift+K：搜索软件功能，按回车直接执行。也可从“帮助”菜单打开。
                Ctrl+逗号：打开分类设置窗口。
                会话列表中按 Shift+F10 打开操作菜单。Ctrl+Shift+A 归档所选会话，Ctrl+Shift+H 打开本地归档。
                项目、会话或归档列表中按 Delete 打开移除确认。项目文件和官方会话均保留。
                默认 Ctrl+Enter 发送，Enter 换行。可在常规设置中交换。Ctrl+Shift+X 中止任务。
                生成时按发送键排队。从“消息 → 查看排队消息”查看全文或撤回所选项。
                Ctrl+Shift+Enter：把输入框里的文字立即追加给正在运行的任务。
                Ctrl+Shift+I：图片附件。在附件窗口按 Ctrl+O 添加，回车查看，Delete 从草稿移除。
                Ctrl+Shift+J：文档、文本与代码附件。Ctrl+O 添加，回车读原文，Delete 从草稿移除。
                Ctrl+Shift+P：搜索并填入官方命令，确认后再发送。
                Ctrl+F9：从列表打开技能、MCP 服务或已安装插件。
                Ctrl+Shift+R：朗读最新回复，在审批窗口朗读操作详情。
                Ctrl+M：选择模型、权限模式和推理等级，按回车一次应用。Ctrl+Shift+D：断开 ACP。
                Ctrl+Shift+S：账户与连接。Alt+R 重新核验，Alt+U 刷新额度，Alt+T 在官方终端查询。
                Ctrl+L：任务日志。Ctrl+Shift+L：任务计划。Ctrl+I：完整状态。
                Ctrl+D：文件差异。Ctrl+Page Up / Down 切换文件，F7 / F8 切换差异段。
                审批先聚焦操作详情。Tab 到按钮，按回车或空格确认该按钮。Esc 拒绝，Alt+Y 允许本次。
                """);
            return Task.CompletedTask;
        }));
        menu.Items.AddRange([projectMenu, sessionMenu, messageMenu, taskMenu, accountMenu, preferencesMenu, help]);

        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 3, ColumnCount = 2, TabIndex = 1
        };
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.Controls.Add(context, 0, 0);
        area.SetColumnSpan(context, 2);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, TabIndex = 0 };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        projects.TabIndex = 0;
        sessions.TabIndex = 2;
        left.Controls.Add(Caption("项目列表"), 0, 0);
        left.Controls.Add(projects, 0, 1);
        left.Controls.Add(Buttons(1, addProject, openProject), 0, 2);
        left.Controls.Add(Caption("历史会话"), 0, 3);
        left.Controls.Add(sessions, 0, 4);
        left.Controls.Add(Buttons(3, openSession), 0, 5);
        area.Controls.Add(left, 0, 1);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, TabIndex = 1 };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 57));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 23));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        messages.TabIndex = 0;
        recentActivity.TabIndex = 1;
        input.TabIndex = 2;
        right.Controls.Add(Caption("消息列表"), 0, 0);
        right.Controls.Add(messages, 0, 1);
        right.Controls.Add(Caption("实时活动"), 0, 2);
        right.Controls.Add(recentActivity, 0, 3);
        right.Controls.Add(inputCaption, 0, 4);
        right.Controls.Add(input, 0, 5);
        right.Controls.Add(Buttons(3, send, steer, cancel, showOptions, imageButton, textAttachmentsButton), 0, 6);
        area.Controls.Add(right, 1, 1);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, TabIndex = 2
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        status.AutoSize = true;
        status.Margin = new Padding(3, 4, 3, 4);
        footer.Controls.Add(status, 0, 0);
        var footerButtons = Buttons(0, openSettings, showStatus, showLog, showDiff);
        footerButtons.WrapContents = false;
        footer.Controls.Add(footerButtons, 0, 1);
        area.Controls.Add(footer, 0, 2);
        area.SetColumnSpan(footer, 2);

        Controls.Add(area);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    private static ToolStripMenuItem Item(string text, Keys keys = Keys.None, Func<Task>? action = null)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeys = keys };
        if (action is not null)
            item.Click += async (_, _) => await action();
        return item;
    }

    internal static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 5, 3, 4) };

    internal static FlowLayoutPanel Buttons(int tabIndex, params Button[] buttons)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, TabIndex = tabIndex, WrapContents = true };
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].TabIndex = i;
            panel.Controls.Add(buttons[i]);
        }
        return panel;
    }

    internal static RichTextBox ReadableText(string name) => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = name, WordWrap = true,
        DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical, MaxLength = int.MaxValue
    };

    protected override bool ProcessCmdKey(ref Message message, Keys keys)
    {
        if (keys is Keys.F6 or (Keys.F6 | Keys.Shift))
        {
            Control[] targets = [projects, sessions, messages, recentActivity, input, showStatus];
            int current = Array.FindIndex(targets, control => control.ContainsFocus);
            int next = current < 0
                ? (keys == Keys.F6 ? 0 : targets.Length - 1)
                : (current + (keys == Keys.F6 ? 1 : targets.Length - 1)) % targets.Length;
            targets[next].Focus();
            return true;
        }
        if (input.Focused && !input.IsComposing && keys == (settings.EnterToSend ? Keys.Enter : Keys.Control | Keys.Enter))
        {
            _ = SendAsync();
            return true;
        }
        if (input.Focused && !input.IsComposing && settings.EnterToSend && keys == (Keys.Control | Keys.Enter))
        {
            input.SelectedText = Environment.NewLine;
            return true;
        }
        if (keys == (Keys.Control | Keys.Shift | Keys.Enter) && steerMenu.Enabled && !input.IsComposing)
        {
            _ = SendSteeringAsync();
            return true;
        }
        if (keys == (Keys.Control | Keys.F9) && extensionHub.Enabled)
        {
            _ = ShowExtensionHubAsync();
            return true;
        }
        if (keys == Keys.Enter && projects.Focused)
        {
            _ = OpenSelectedProjectAsync();
            return true;
        }
        if (keys == Keys.Enter && sessions.Focused)
        {
            _ = RestoreSelectedAsync();
            return true;
        }
        if (keys == Keys.Enter && recentActivity.Focused)
        {
            if (recentActivity.SelectedItem is ActivityRecord entry)
                Dialogs.Text(this, entry.Summary, entry.Detail);
            return true;
        }
        if (messages.Focused)
        {
            switch (keys)
            {
                case Keys.Enter:
                    OpenSelectedMessage();
                    return true;
                case Keys.Control | Keys.C:
                    CopySelectedMessage();
                    return true;
                case Keys.Control | Keys.F:
                    FindMessage();
                    return true;
                case Keys.Left:
                case Keys.Right:
                    MoveToUserMessage(keys == Keys.Left ? -1 : 1);
                    return true;
            }
        }
        if (keys == Keys.F2 && sessions.Focused && renameSession.Enabled)
        {
            _ = RenameSessionAsync();
            return true;
        }
        if (keys == Keys.Delete && projects.Focused && removeProject.Enabled)
        {
            _ = RemoveSelectedProjectAsync();
            return true;
        }
        if (keys == Keys.Delete && sessions.Focused && removeSession.Enabled)
        {
            _ = RemoveSelectedHistoryAsync();
            return true;
        }
        return base.ProcessCmdKey(ref message, keys);
    }

    private void UpdateLiveSetting()
    {
        status.LiveSetting = settings.Announce ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off;
    }
}
