using System.Windows.Forms.Automation;

namespace MyAntigravity;

internal sealed partial class MainForm : Form
{
    private readonly ListBox projects = new() { AccessibleName = "项目列表", AccessibleDescription = "回车打开。Delete 从项目列表移除。Shift 加 F10 打开操作菜单。", Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox sessions = new() { AccessibleName = "历史会话", AccessibleDescription = "回车恢复，F2 重命名，Delete 删除本机历史记录。Shift 加 F10 打开操作菜单。", Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly RichTextBox chat = ReadableText("会话消息");
    private readonly TextBox input = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AccessibleName = "消息输入", AccessibleDescription = "Ctrl 加回车发送。回车换行。Ctrl 加 Shift 加 P 查找官方命令。" };
    private readonly Label status = new() { Text = "请选择本地项目，或从账户菜单登录。", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button send = new() { Text = "发送 (&S)", AutoSize = true, Enabled = false };
    private readonly Button cancel = new() { Text = "中止 (&X)", AutoSize = true, Enabled = false };
    private readonly Button steer = new() { Text = "追加指令 (&A)", AutoSize = true, Enabled = false };
    private readonly ToolStripMenuItem steerMenu = Item("追加指令 (&G)", Keys.Control | Keys.Shift | Keys.Enter);
    private readonly ToolStripMenuItem newSession = Item("新建会话 (&N)", Keys.Control | Keys.N);
    private readonly ToolStripMenuItem restore = Item("恢复所选会话 (&H)", Keys.Control | Keys.H);
    private readonly ToolStripMenuItem findHistory = Item("查找会话 (&K)", Keys.Control | Keys.K);
    private readonly ToolStripMenuItem renameSession = Item("重命名本地会话 (&E)");
    private readonly ToolStripMenuItem archiveSession = Item("移入本地归档 (&A)", Keys.Control | Keys.Shift | Keys.A);
    private readonly ToolStripMenuItem archivedHistory = Item("打开本地归档 (&B)", Keys.Control | Keys.Shift | Keys.H);
    private readonly ToolStripMenuItem removeSession = Item("从本机历史删除会话 (&D)");
    private readonly ToolStripMenuItem imageMenu = Item("图片附件 (&I)", Keys.Control | Keys.Shift | Keys.I);
    private readonly ToolStripMenuItem textAttachmentsMenu = Item("文本与代码附件 (&J)", Keys.Control | Keys.Shift | Keys.J);
    private readonly ToolStripMenuItem refresh = Item("刷新历史会话 (&R)", Keys.Control | Keys.R);
    private readonly ToolStripMenuItem disconnect = Item("断开并停止 ACP (&D)", Keys.Control | Keys.Shift | Keys.D);
    private readonly ToolStripMenuItem optionsMenu = Item("模型、权限模式与推理 (&M)", Keys.Control | Keys.M);
    private readonly ToolStripMenuItem browserLogin = Item("用浏览器登录 Google (&G)");
    private readonly ToolStripMenuItem terminalLogin = Item("用官方终端登录 (&T)");
    private readonly ToolStripMenuItem chooseProject = Item("添加本地项目 (&O)", Keys.Control | Keys.O);
    private readonly ToolStripMenuItem quickProject = Item("快速打开项目 (&Q)", Keys.Control | Keys.Shift | Keys.O);
    private readonly ToolStripMenuItem removeProject = Item("从项目列表移除 (&D)");
    private readonly ToolStripMenuItem additionalDirectories = Item("附加工作目录 (&W)", Keys.Control | Keys.Shift | Keys.W);
    private readonly ToolStripMenuItem commandPalette = Item("官方命令 (&P)", Keys.Control | Keys.Shift | Keys.P);
    private readonly ToolStripMenuItem settingsMenu = Item("设置 (&S)");
    private readonly ToolStripMenuItem updatesMenu = Item("检查更新 (&U)");
    private readonly ToolStripMenuItem extensionHub = Item("技能、MCP 与插件 (&E)", Keys.Control | Keys.F9);
    private readonly ToolStripMenuItem detect = Item("检测 CLI 版本 (&V)");
    private readonly ToolStripMenuItem logMenu = Item("任务日志 (&L)", Keys.Control | Keys.L);
    private readonly ToolStripMenuItem readReplyMenu = Item("朗读最新回复 (&E)", Keys.Control | Keys.Shift | Keys.R);
    private readonly ToolStripMenuItem diffMenu = Item("文件差异 (&F)", Keys.Control | Keys.D);
    private readonly Button openProject = new() { Text = "打开所选项目 (&P)", AutoSize = true };
    private readonly Button openSession = new() { Text = "恢复所选会话 (&H)", AutoSize = true, Enabled = false };
    private readonly Button addProject = new() { Text = "添加项目 (&O)", AutoSize = true };
    private readonly Button showOptions = new() { Text = "模型与权限 (&M)", AutoSize = true, Enabled = false };
    private readonly Button imageButton = new() { Text = "图片附件 (&I)", AutoSize = true, Enabled = false };
    private readonly Button textAttachmentsButton = new() { Text = "文本与代码附件 (&J)", AutoSize = true, Enabled = false };
    private readonly Button showStatus = new() { Text = "查看状态 (&V)", AutoSize = true, AccessibleDescription = "打开完整状态文本。也可按 Ctrl 加 I。" };
    private readonly Button showLog = new() { Text = "任务日志 (&L)", AutoSize = true };
    private readonly Button showDiff = new() { Text = "文件差异 (&F)", AutoSize = true };
    private readonly Label context = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Text = "当前项目：未选择" };

    private void BuildUi()
    {
        Text = $"MyAntigravity {Program.Version}";
        AccessibleDescription = "本地 Antigravity 客户端。F6 在项目、历史、会话、输入和状态之间切换。";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1070, 710);
        MinimumSize = new Size(690, 510);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        var menu = new MenuStrip { Dock = DockStyle.Top, TabIndex = 0 };
        var projectMenu = Item("项目 (&P)");
        projectMenu.DropDownItems.AddRange([chooseProject, quickProject, Item("打开所选项目 (&P)", action: OpenSelectedProjectAsync),
            additionalDirectories, removeProject,
            new ToolStripSeparator(), Item("退出 (&X)", action: () => { Close(); return Task.CompletedTask; })]);
        var sessionMenu = Item("会话 (&C)");
        var cancelMenu = Item("中止任务 (&X)", Keys.Control | Keys.Shift | Keys.X);
        cancelMenu.Click += async (_, _) => await RequestCancellationAsync();
        cancel.EnabledChanged += (_, _) => cancelMenu.Enabled = cancel.Enabled;
        cancelMenu.Enabled = false;
        sessionMenu.DropDownItems.AddRange([newSession, restore, findHistory, renameSession, archiveSession, archivedHistory, removeSession,
            refresh, optionsMenu, commandPalette, imageMenu, textAttachmentsMenu,
            new ToolStripSeparator(), steerMenu, cancelMenu, disconnect]);
        var accountMenu = Item("账户 (&A)");
        accountMenu.DropDownItems.AddRange([Item("账户与连接 (&S)", Keys.Control | Keys.Shift | Keys.S, ShowAccountAsync),
            new ToolStripSeparator(), browserLogin, terminalLogin, detect,
            Item("官方 CLI 安装指南 (&I)", action: () => { OpenLink(Cli.SetupGuide); return Task.CompletedTask; })]);
        var viewMenu = Item("查看 (&V)");
        viewMenu.DropDownItems.AddRange([readReplyMenu, Item("完整状态 (&I)", Keys.Control | Keys.I, ShowStatusAsync), logMenu,
            diffMenu, Item("任务计划 (&P)", Keys.Control | Keys.Shift | Keys.L, ShowPlanAsync)]);
        var toolsMenu = Item("工具 (&T)");
        toolsMenu.DropDownItems.AddRange([extensionHub,
            settingsMenu, updatesMenu, Item("官方插件列表 (&P)", action: ShowPluginsAsync)]);
        var help = Item("帮助 (&H)");
        help.DropDownItems.Add(Item("键盘操作 (&K)", action: () =>
        {
            Dialogs.Text(this, "键盘操作", """
                Tab / Shift+Tab：移动焦点。Alt：打开菜单。
                F6 / Shift+F6：在项目、会话历史、消息、输入和状态间切换。
                Ctrl+O：输入项目目录，按回车打开。也可以选择“浏览文件夹”。
                Ctrl+Shift+O：在已添加的项目中直接选、打开或移除。Ctrl+F 筛选。
                Ctrl+Shift+W：管理当前项目的附加工作目录。
                Ctrl+N：新建会话。Ctrl+R：刷新历史。Ctrl+H：恢复所选会话。
                Ctrl+K：查找当前项目的会话。会话列表中按 F2 编辑本地名称。
                会话列表中按 Shift+F10 打开操作菜单。Ctrl+Shift+A 归档所选会话，Ctrl+Shift+H 打开本地归档。
                项目、会话或归档列表中按 Delete 打开移除确认。项目文件和官方会话均保留。
                Ctrl+Enter：在输入框发送。Ctrl+Shift+X：中止任务。
                Ctrl+Shift+Enter：把输入框里的文字追加给正在运行的任务。
                Ctrl+Shift+I：图片附件。在附件窗口按 Ctrl+O 添加，回车查看，Delete 从草稿移除。
                Ctrl+Shift+J：文本与代码附件。Ctrl+O 添加，回车读原文，Delete 从草稿移除。
                Ctrl+Shift+P：搜索并填入官方命令，确认后再发送。
                Ctrl+F9：从列表打开技能、MCP 服务或已安装插件。
                Ctrl+Shift+R：朗读最新回复，在审批窗口朗读操作详情。
                Ctrl+M：查看模型、权限模式和推理等级。Ctrl+Shift+D：断开 ACP。
                Ctrl+Shift+S：账户与连接。Alt+R 重新核验，Alt+U 打开官方 CLI 并复制额度命令。粘贴后回车查询。
                Ctrl+L：任务日志。Ctrl+Shift+L：任务计划。Ctrl+I：完整状态。
                Ctrl+D：文件差异。Ctrl+Page Up / Down 切换文件，F7 / F8 切换差异段。
                审批先聚焦操作详情。Tab 浏览按钮，回车或 Esc 拒绝。Alt+Y 允许本次。
                """);
            return Task.CompletedTask;
        }));
        menu.Items.AddRange([projectMenu, sessionMenu, accountMenu, viewMenu, toolsMenu, help]);

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

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, TabIndex = 1 };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 78));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 22));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        chat.TabIndex = 0;
        input.TabIndex = 1;
        right.Controls.Add(Caption("会话消息"), 0, 0);
        right.Controls.Add(chat, 0, 1);
        right.Controls.Add(Caption("消息输入 · Ctrl+Enter 发送"), 0, 2);
        right.Controls.Add(input, 0, 3);
        right.Controls.Add(Buttons(2, send, steer, cancel, showOptions, imageButton, textAttachmentsButton), 0, 4);
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
        var footerButtons = Buttons(0, showStatus, showLog, showDiff);
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
            Control[] targets = [projects, sessions, chat, input, showStatus];
            int current = Array.FindIndex(targets, control => control.ContainsFocus);
            int next = current < 0
                ? (keys == Keys.F6 ? 0 : targets.Length - 1)
                : (current + (keys == Keys.F6 ? 1 : targets.Length - 1)) % targets.Length;
            targets[next].Focus();
            return true;
        }
        if (keys == (Keys.Control | Keys.Enter) && input.Focused)
        {
            _ = SendAsync();
            return true;
        }
        if (keys == (Keys.Control | Keys.Shift | Keys.Enter) && steerMenu.Enabled)
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
