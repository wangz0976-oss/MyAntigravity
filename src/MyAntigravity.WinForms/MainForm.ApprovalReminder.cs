using System.Runtime.InteropServices;

namespace MyAntigravity;

internal sealed partial class MainForm
{
    private NotifyIcon? backgroundNotification;
    private System.Windows.Forms.Timer? notificationTimeout;

    private bool IsAppForeground()
    {
        var foreground = GetForegroundWindow();
        return foreground != IntPtr.Zero && Application.OpenForms.Cast<Form>()
            .Any(form => form.IsHandleCreated && form.Handle == foreground);
    }

    private async Task WaitForApprovalForegroundAsync(CancellationToken cancellation)
    {
        if (IsAppForeground())
            return;
        if (settings.BackgroundApprovalNotifications)
            ShowBackgroundNotification("MyAntigravity 等待审批", "切回 MyAntigravity 用键盘查看和决定。", dismiss: false);
        try
        {
            while (!closing && !IsAppForeground())
                await Task.Delay(150, cancellation);
            cancellation.ThrowIfCancellationRequested();
        }
        finally
        {
            if (!closing && settings.BackgroundApprovalNotifications)
                HideBackgroundNotification();
        }
    }

    private void ShowBackgroundNotification(string title, string text, bool dismiss)
    {
        try
        {
            backgroundNotification ??= new NotifyIcon { Icon = SystemIcons.Information, Text = "MyAntigravity" };
            notificationTimeout?.Stop();
            backgroundNotification.Visible = true;
            backgroundNotification.ShowBalloonTip(10000, title, text, ToolTipIcon.Info);
            if (!dismiss)
                return;
            if (notificationTimeout is null)
            {
                notificationTimeout = new System.Windows.Forms.Timer { Interval = 15000 };
                notificationTimeout.Tick += (_, _) => HideBackgroundNotification();
            }
            notificationTimeout.Start();
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            Log(AgentText.Visible(error.Message), "Windows 通知发送失败", TaskActivityKind.Failure);
        }
    }

    private void HideBackgroundNotification()
    {
        notificationTimeout?.Stop();
        if (backgroundNotification is not null)
            backgroundNotification.Visible = false;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();
}
