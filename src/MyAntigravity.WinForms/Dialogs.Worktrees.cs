using System.Windows.Forms.Automation;

namespace MyAntigravity;

internal static partial class Dialogs
{
    internal static string? CreateWorktree(Form owner, GitReview repository)
    {
        using var form = Window("新建 Git 工作树", new Size(730, 365));
        form.ShowInTaskbar = true;
        string folderName = Path.GetFileName(repository.Root) + "-worktree";
        var path = new TextBox
        {
            AccessibleName = "新工作树的完整目录路径", Dock = DockStyle.Top, TabIndex = 0,
            Text = Path.GetDirectoryName(repository.Root) is { } parent ? Path.Combine(parent, folderName) : ""
        };
        var browse = new Button { Text = "浏览父目录 (&B)", AutoSize = true, TabIndex = 1 };
        var branch = new TextBox { AccessibleName = "新分支名", Dock = DockStyle.Top, TabIndex = 2 };
        var info = MainForm.Caption("从当前提交 HEAD 创建新分支。目录须位于现有工作树之外。");
        var validation = MainForm.Caption("");
        validation.LiveSetting = AutomationLiveSetting.Polite;
        var create = new Button { Text = "创建并打开 (&O)", AutoSize = true };
        var cancel = new Button { Text = "取消 (&C)", AutoSize = true, DialogResult = DialogResult.Cancel };
        string? created = null;
        bool creating = false;

        browse.Click += (_, _) =>
        {
            if (ProjectDirectory(form, null) is { } directory)
            {
                path.Text = Path.Combine(directory, folderName);
                path.SelectAll();
                path.Focus();
            }
        };
        create.Click += async (_, _) =>
        {
            creating = true;
            path.Enabled = browse.Enabled = branch.Enabled = create.Enabled = cancel.Enabled = false;
            validation.Text = "正在创建 Git 工作树。";
            try { created = await repository.CreateWorktreeAsync(path.Text.Trim().Trim('"'), branch.Text.Trim()); }
            catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException
                or TimeoutException or System.ComponentModel.Win32Exception)
            {
                validation.Text = AgentText.Visible(error.Message);
            }
            finally
            {
                creating = false;
                path.Enabled = browse.Enabled = branch.Enabled = create.Enabled = cancel.Enabled = true;
            }
            if (created is not null)
                form.DialogResult = DialogResult.OK;
            else
                path.Focus();
        };
        form.FormClosing += (_, e) => e.Cancel = creating;
        var entries = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true };
        entries.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        entries.Controls.Add(info);
        entries.Controls.Add(MainForm.Caption("新工作树的完整目录路径"));
        entries.Controls.Add(path);
        entries.Controls.Add(browse);
        entries.Controls.Add(MainForm.Caption("新分支名"));
        entries.Controls.Add(branch);
        entries.Controls.Add(validation);
        Vertical(form, entries, MainForm.Buttons(1, create, cancel));
        form.AcceptButton = create;
        form.CancelButton = cancel;
        form.ActiveControl = path;
        path.SelectAll();
        return form.ShowDialog(owner) == DialogResult.OK ? created : null;
    }
}
