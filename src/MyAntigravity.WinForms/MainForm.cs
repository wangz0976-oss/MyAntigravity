using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MyAntigravity;

internal sealed partial class MainForm : Form
{
    private readonly string dataDirectory;
    private readonly SettingsFile storage;
    private readonly LocalSettings settings;
    private readonly StringBuilder latestReply = new();
    private readonly Dictionary<(string ToolId, string Path, int Index), FileChange> changes = [];
    private readonly Dictionary<string, string> toolTitles = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim interactionQueue = new(1, 1);
    private AcpAgent? agent;
    private string? project;
    private string? sessionId;
    private string? contextUsage;
    private string? lastTurnUsage;
    private IReadOnlyList<SessionOption> options = [];
    private IReadOnlyList<PlanStep> plan = [];
    private event Action? PlanChanged;
    private bool busy;
    private bool prompting;
    private bool cancelRequested;
    private bool showOfficialHistory;
    private bool closing;

    public MainForm(string dataDirectory)
    {
        this.dataDirectory = dataDirectory;
        storage = new SettingsFile(dataDirectory);
        string? loadError = null;
        try { settings = storage.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            settings = new LocalSettings();
            loadError = $"无法读取设置。\n{error.Message}";
        }

        BuildUi();
        WireCommands();
        UpdateLiveSetting();
        ReloadProjects();
        UpdateCommands();
        workspaceSave.Tick += (_, _) => FlushWorkspace();

        Shown += async (_, _) =>
        {
            if (loadError is not null)
                ReportError(loadError);
            if (projects.Items.Count > 0)
                projects.Focus();
            else
                addProject.Focus();
            if (!closing && loadError is null)
                OfferCliSetupAtStartup();
            if (!closing)
                await CheckStartupUpdateAsync(new Updates(updateHttp, dataDirectory), Program.Version);
        };
    }

    private void WireCommands()
    {
        chooseProject.Click += async (_, _) => await ChooseProjectAsync();
        quickProject.Click += async (_, _) => await FindProjectAsync();
        addProject.Click += async (_, _) => await ChooseProjectAsync();
        openProject.Click += async (_, _) => await OpenSelectedProjectAsync();
        openProjectMenu.Click += async (_, _) => await OpenSelectedProjectAsync();
        projectExplorer.Click += async (_, _) => await ShowSelectedFolderAsync();
        copyProjectPath.Click += async (_, _) => await CopySelectedFolderAsync();
        projectChanges.Click += async (_, _) => await ShowProjectChangesAsync();
        newSession.Click += async (_, _) => await StartSessionAsync(null);
        restore.Click += async (_, _) => await RestoreSelectedAsync();
        findHistory.Click += async (_, _) => await FindSessionAsync();
        findAllHistory.Click += async (_, _) => await FindAllSessionsAsync();
        renameSession.Click += async (_, _) => await RenameSessionAsync();
        pinSession.Click += async (_, _) => await ToggleSessionPinAsync();
        commandPalette.Click += async (_, _) => await InsertOfficialCommandAsync();
        imageMenu.Click += async (_, _) => await ManageImagesAsync();
        imageButton.Click += async (_, _) => await ManageImagesAsync();
        textAttachmentsMenu.Click += async (_, _) => await ManageTextFilesAsync();
        textAttachmentsButton.Click += async (_, _) => await ManageTextFilesAsync();
        archiveSession.Click += async (_, _) => await ArchiveSelectedAsync();
        archivedHistory.Click += async (_, _) => await ShowArchivedAsync();
        removeSession.Click += async (_, _) => await RemoveSelectedHistoryAsync();
        removeProject.Click += async (_, _) => await RemoveSelectedProjectAsync();
        additionalDirectories.Click += async (_, _) => await EditAdditionalDirectoriesAsync();
        WireHistoryContextMenu();
        WireProjectContextMenu();
        WireMessageList();
        openSession.Click += async (_, _) => await RestoreSelectedAsync();
        refresh.Click += async (_, _) => await RunAsync(RefreshSessionsAsync);
        officialHistory.Click += async (_, _) => await ShowOfficialHistoryAsync();
        disconnect.Click += async (_, _) => await DisconnectNowAsync();
        optionsMenu.Click += async (_, _) => await SelectOptionAsync();
        showOptions.Click += async (_, _) => await SelectOptionAsync();
        browserLogin.Click += async (_, _) => await RunAsync(BrowserLoginAsync);
        terminalLogin.Click += async (_, _) => await RunAsync(TerminalLoginAsync);
        detect.Click += async (_, _) => await RunAsync(DetectCliAsync);
        settingsMenu.Click += async (_, _) => await RunAsync(EditSettingsAsync);
        sessionDefaultsMenu.Click += async (_, _) => await ManageSessionDefaultsAsync();
        updatesMenu.Click += async (_, _) => await RunAsync(ShowUpdatesAsync);
        extensionHub.Click += async (_, _) => await ShowExtensionHubAsync();
        send.Click += async (_, _) => await SendAsync();
        sendMessageMenu.Click += async (_, _) => await SendAsync();
        steer.Click += async (_, _) => await SendSteeringAsync();
        steerMenu.Click += async (_, _) => await SendSteeringAsync();
        cancel.Click += async (_, _) => await RequestCancellationAsync();
        showStatus.Click += async (_, _) => await ShowStatusAsync();
        logMenu.Click += (_, _) => ShowLog();
        readReplyMenu.Click += (_, _) => ReadLatestReply();
        showLog.Click += (_, _) => ShowLog();
        diffMenu.Click += (_, _) => ShowDiff();
        showDiff.Click += (_, _) => ShowDiff();
        projects.SelectedIndexChanged += (_, _) => UpdateCommands();
        sessions.SelectedIndexChanged += (_, _) => UpdateCommands();
        input.TextChanged += (_, _) =>
        {
            draft.Text = input.Text;
            if (draftOwner is { } owner)
                RememberDraft(owner, draft);
            UpdateComposerCommands();
        };

    }

    private Task ShowOfficialHistoryAsync() => RunAsync(async () =>
    {
        if (project is null)
            return;
        if (!showOfficialHistory && MessageBox.Show(this,
            "官方 CLI 可能会在当前目录下列出其他项目的旧会话。请根据标题确认所属项目，再恢复会话。打开官方列表吗？",
            "查看官方历史", MessageBoxButtons.OKCancel, MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2) != DialogResult.OK)
            return;
        showOfficialHistory = true;
        await RefreshSessionsAsync();
    });

    private async Task RunAsync(Func<Task> operation)
    {
        if (busy || closing)
            return;
        busy = true;
        cancelRequested = false;
        UpdateCommands();
        try { await operation(); }
        catch (AcpException error) when (error.RequiresAuthentication)
        {
            RecordAccountVerification(connectedExecutable, true);
            ReportError("需要登录 Google。请从“账户”菜单选择“用浏览器登录 Google”。");
        }
        catch (CliSetupException error)
        {
            if (!closing)
                ShowCliGuide(error.Message);
        }
        catch (Exception error) when (error is IOException or AcpException or InvalidOperationException or
            TimeoutException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or ArgumentException or
            HttpRequestException or JsonException or FormatException or OperationCanceledException)
        {
            if (!closing)
                ReportError(error.Message);
        }
        finally
        {
            busy = false;
            prompting = false;
            UpdateCommands();
        }
    }

    private void UpdateCommands()
    {
        var idle = !busy && !closing;
        addProject.Enabled = chooseProject.Enabled = settingsMenu.Enabled = sessionDefaultsMenu.Enabled = updatesMenu.Enabled = extensionHub.Enabled =
            browserLogin.Enabled = terminalLogin.Enabled = detect.Enabled = idle;
        openProject.Enabled = openProjectMenu.Enabled = projectExplorer.Enabled = copyProjectPath.Enabled =
            removeProject.Enabled = idle && projects.SelectedItem is string;
        additionalDirectories.Enabled = projectWorktrees.Enabled = projectChanges.Enabled = idle && project is not null;
        quickProject.Enabled = idle && settings.Projects.Count > 0;
        newSession.Enabled = refresh.Enabled = officialHistory.Enabled = idle && project is not null;
        restore.Enabled = openSession.Enabled = idle && project is not null && sessions.SelectedItem is LocalSession;
        removeSession.Enabled = restore.Enabled;
        findHistory.Enabled = idle && sessions.Items.Count > 0;
        findAllHistory.Enabled = idle && settings.Projects.Count > 0;
        renameSession.Enabled = idle && project is not null && sessions.SelectedItem is LocalSession selected &&
            settings.FindSession(selected.Id, project) is not null;
        archiveSession.Enabled = renameSession.Enabled;
        pinSession.Enabled = renameSession.Enabled;
        pinSession.Text = sessions.SelectedItem is LocalSession { Pinned: true }
            ? "取消会话置顶 (&T)" : "置顶本地会话 (&T)";
        archivedHistory.Enabled = idle && project is not null;
        commandPalette.Enabled = idle && sessionId is not null && agent is { Connected: true };
        showOptions.Enabled = optionsMenu.Enabled = idle && sessionId is not null && options.Count > 0;
        UpdateComposerCommands();
        readReplyMenu.Enabled = !closing && latestReply.Length > 0;
        cancel.Enabled = prompting && !cancelRequested && agent is { Connected: true };
        disconnect.Enabled = agent is not null && !closing;
    }

    private Task ChooseProjectAsync() => RunAsync(async () =>
    {
        if (Dialogs.ProjectDirectory(this, project) is { } path)
            await SelectProjectAsync(path);
    });

    private Task OpenSelectedProjectAsync() => RunAsync(async () =>
    {
        if (projects.SelectedItem is string directory)
            await SelectProjectAsync(directory);
    });

    private async Task SelectProjectAsync(string directory)
    {
        var fullPath = LocalSettings.ProjectPath(directory);
        if (project is not null && LocalSettings.PathEquals(project, fullPath))
            return;

        await ResetAgentAsync();
        project = fullPath;
        showOfficialHistory = false;
        officialSessions = [];
        activeTitles.Clear();
        ClearConversation();
        activity.Clear();
        settings.RememberProject(fullPath);
        ReloadProjects();
        PopulateSessions();
        context.Text = $"当前项目：{project}";
        storage.Save(settings);
        SetStatus("项目已打开。发送消息时会创建会话。", speak: true);
        input.Focus();
    }

    private void ReloadProjects()
    {
        projects.BeginUpdate();
        try
        {
            projects.Items.Clear();
            projects.Items.AddRange(settings.Projects.Cast<object>().ToArray());
            if (project is not null)
                projects.SelectedItem = project;
            else if (projects.Items.Count > 0)
                projects.SelectedIndex = 0;
        }
        finally { projects.EndUpdate(); }
    }

    private async Task<AcpAgent> ConnectAsync()
    {
        var directory = WorkingDirectory();
        if (agent is { Connected: true } existing && LocalSettings.PathEquals(existing.WorkingDirectory, directory))
            return existing;

        await ResetAgentAsync();
        var executable = RequireCli();
        var current = new AcpAgent(AcpStartInfo(executable, directory), AskPermissionAsync, AskQuestionAsync);
        connectedExecutable = executable;
        current.SessionUpdated += (id, data) => Post(() =>
        {
            if (ReferenceEquals(agent, current))
                ApplyUpdate(id, data);
        });
        current.Disconnected += () => Post(() =>
        {
            if (!ReferenceEquals(agent, current))
                return;
            connectionReady = false;
            sessionId = null;
            SwitchDraft(null);
            options = [];
            availableCommands = [];
            pendingSessionMetadata = null;
            UpdateCommands();
            SetStatus("ACP 连接已断开。可重新打开历史会话或新建会话。");
        });

        agent = current;
        try
        {
            await current.InitializeAsync(Program.Version);
            if (ReferenceEquals(agent, current))
                connectionReady = true;
            UpdateCommands();
            return current;
        }
        catch
        {
            // Keep the executable available to classify authentication failures from initialization.
            await ResetAgentAsync();
            throw;
        }
    }

    private async Task ResetAgentAsync()
    {
        var previous = agent;
        agent = null;
        connectionReady = false;
        sessionId = null;
        SwitchDraft(null);
        options = [];
        availableCommands = [];
        pendingSessionMetadata = null;
        if (previous is not null)
            await previous.DisposeAsync();
        UpdateCommands();
    }

    private async Task DisconnectNowAsync()
    {
        try
        {
            await ResetAgentAsync();
            SetStatus("ACP 已断开。可从历史列表重新恢复会话。");
        }
        catch (IOException error) { ReportError(error.Message); }
    }

    private async Task DetectCliAsync()
    {
        var executable = RequireCli();
        var detected = await Cli.VersionAsync(executable, WorkingDirectory());
        Dialogs.Text(this, "官方 CLI 检测结果", $"版本：{detected}\n位置：{executable}");
    }

    private string WorkingDirectory()
    {
        if (project is not null)
            return LocalSettings.ProjectPath(project);
        Directory.CreateDirectory(dataDirectory);
        return dataDirectory;
    }

    private async Task BrowserLoginAsync()
    {
        SetStatus("正在启动 Google 浏览器登录。请在浏览器完成授权后返回。");
        var current = await ConnectAsync();
        await current.AuthenticateGoogleAsync();
        RecordAccountVerification(connectedExecutable, false);
        SetStatus("Google 登录已完成。");
    }

    private async Task TerminalLoginAsync()
    {
        var executable = RequireCli();
        await ResetAgentAsync();
        using var login = Process.Start(Cli.StartInfo(executable, WorkingDirectory(), false, "acp", "--login"))
            ?? throw new IOException("无法启动官方登录终端。");
        SetStatus("请在官方终端按提示登录 Google。");
        await login.WaitForExitAsync();
        if (login.ExitCode != 0)
            throw new IOException($"官方登录程序退出，代码 {login.ExitCode}。");
        var verification = await VerifyAccountAsync(executable);
        RecordAccountVerification(verification);
        SetStatus(verification.RequiresLogin ? "官方 CLI 仍需要登录。" : "Google 登录已核验。");
    }

    private void PopulateSessions(string? selectId = null)
    {
        var selected = selectId ?? (sessions.SelectedItem as LocalSession)?.Id ?? sessionId;
        var current = project is null ? [] : settings.ProjectSessions(project,
            showOfficialHistory ? officialSessions : [], activeTitles);

        sessions.BeginUpdate();
        try
        {
            sessions.Items.Clear();
            foreach (var item in current)
            {
                sessions.Items.Add(item);
                if (item.Id == selected)
                    sessions.SelectedItem = item;
            }
            if (sessions.SelectedIndex < 0 && sessions.Items.Count > 0)
                sessions.SelectedIndex = 0;
        }
        finally { sessions.EndUpdate(); }
    }

    private IReadOnlyList<LocalSession> officialSessions = [];

    private async Task RefreshSessionsAsync()
    {
        if (project is null)
            return;
        if (showOfficialHistory)
        {
            var current = await ConnectAsync();
            if (!current.CanList)
                throw new InvalidOperationException("此 CLI 未声明官方历史列表能力。");
            officialSessions = await current.ListAsync();
        }
        PopulateSessions();
        SetStatus($"历史会话已刷新，共 {sessions.Items.Count} 条。");
    }

    private Task RestoreSelectedAsync() => sessions.SelectedItem is LocalSession selected
        ? StartSessionAsync(selected.Id) : Task.CompletedTask;

    private Task StartSessionAsync(string? requested) => RunAsync(() => StartSessionCoreAsync(requested));

    private async Task StartSessionCoreAsync(string? requested, bool focusComposer = true)
    {
        if (project is null)
            return;
        if (requested is not null && settings.IsSessionRemoved(requested, project))
            return;
        if (requested is not null && sessionId == requested && agent is { Connected: true })
        {
            messages.Focus();
            SetStatus("该会话已打开。");
            return;
        }
        SetStatus(requested is null ? "正在创建会话。" : "正在恢复历史会话。");
        var directory = LocalSettings.ProjectPath(project);
        var current = await ConnectAsync();
        if (requested is not null && !current.CanRestore)
            throw new InvalidOperationException("当前 CLI 未声明会话恢复能力。");
        var directories = settings.DirectoriesForProject(project).Select(LocalSettings.ProjectPath).ToArray();
        if (directories.Length > 0 && !current.CanAddDirectories)
            throw new InvalidOperationException("当前官方 CLI 不支持附加工作目录。请调整项目目录或更新 CLI。");
        if (sessionId is { } old)
            await current.CloseAsync(old);

        ClearConversation();
        sessionId = requested;
        if (requested is null)
            pendingSessionMetadata = [];
        try
        {
            var session = requested is null ? await current.NewSessionAsync(additionalDirectories: directories)
                : await current.RestoreAsync(requested, directories);
            if (!ReferenceEquals(agent, current) || !current.Connected)
                return;
            sessionId = StringValue(session, "sessionId") ?? requested
                ?? throw new IOException("CLI 未返回新会话标识。");
            if (requested is null)
                RecordAccountVerification(connectedExecutable, false, AccountVerification.ReadTier(session));
            options = SessionOption.Parse(session);
            if (pendingSessionMetadata is { } metadata)
            {
                pendingSessionMetadata = null;
                foreach (var update in metadata.Where(update => update.SessionId == sessionId))
                    ApplyUpdate(update.SessionId, update.Data);
            }
        }
        catch (AcpException error) when (!error.RequiresAuthentication)
        {
            ClearConversation();
            throw new IOException($"CLI 无法打开会话。\n项目目录：{directory}\n{error.Message}", error);
        }
        catch
        {
            ClearConversation();
            throw;
        }
        finally { pendingSessionMetadata = null; }

        var modeStatus = requested is null ? await ApplyNewSessionDefaultsAsync(current, sessionId) : "";
        if (closing || sessionId is null || !ReferenceEquals(agent, current) || !current.Connected)
            return;
        SwitchDraft(sessionId);
        RememberActive();
        PopulateSessions(sessionId);
        transcript.EndChunks();
        RenderMessages();
        bool positioned = RestoreReadingPosition();
        SetStatus(requested is null ? "会话已创建。" + modeStatus
            : current.CanReplay ? positioned ? "历史会话已恢复，已定位到上次阅读位置。" : "历史会话已恢复。"
            : "会话已恢复，当前 CLI 不回放历史消息。");
        if (requested is not null && current.CanReplay && messages.Items.Count > 0)
            messages.Focus();
        else if (focusComposer)
            input.Focus();
    }

    private void ClearConversation()
    {
        sessionId = null;
        SwitchDraft(null);
        options = [];
        availableCommands = [];
        pendingSessionMetadata = null;
        ClearMessages();
        latestReply.Clear();
        changes.Clear();
        toolTitles.Clear();
        turnResults.Clear();
        contextUsage = lastTurnUsage = null;
        plan = [];
        PlanChanged?.Invoke();
    }

    private Task SendAsync()
    {
        if (project is null || string.IsNullOrWhiteSpace(draft.Text) && !draft.HasAttachments)
            return Task.CompletedTask;
        return RunAsync(async () =>
        {
            if (sessionId is null || agent is not { Connected: true })
            {
                input.ReadOnly = true;
                try { await StartSessionCoreAsync(null, focusComposer: false); }
                finally { input.ReadOnly = false; }
            }
            if (closing || draftOwner is not { } owner || sessionId is not { } activeSession ||
                agent is not { Connected: true } current)
                return;
            var sending = draft;
            var text = sending.Text;
            var images = sending.Images.ToArray();
            var files = sending.TextFiles.ToArray();
            if (images.Length > 0 && !current.CanSendImages)
                throw new InvalidOperationException("当前官方 CLI 不支持图片附件。请在图片附件窗口移除图片或更换 CLI。");
            if (files.Length > 0 && !current.CanSendTextFiles)
                throw new InvalidOperationException("当前官方 CLI 不支持文本附件。请在附件窗口移除文本附件或更换 CLI。");

            prompting = true;
            UpdateCommands();
            var pending = new SendingText(text);
            sending.Sending.Add(pending);
            sending.Text = "";
            sending.Images.Clear();
            sending.TextFiles.Clear();
            RefreshComposer();
            latestReply.Clear();
            readReplyMenu.Enabled = false;
            turnResults.Clear();
            var attachmentText = images.Length > 0
                ? "\n图片附件：" + string.Join("、", images.Select(image => image.Name)) : "";
            if (files.Length > 0)
                attachmentText += "\n文本与代码附件：" + string.Join("、", files.Select(file => file.Name));
            BeginStreamingReply(text + attachmentText);
            SetStatus("任务进行中。");
            try
            {
                var result = await current.PromptAsync(activeSession, text, images.Select(image => image.Payload).ToArray(),
                    files.Select(file => file.Payload).ToArray());
                sending.Sending.Remove(pending);
                RememberDraft(owner, sending);
                var reason = StringValue(result, "stopReason");
                FinishStreamingReply(reason == "cancelled" ? "任务已中止，尚无正文。" : "任务已结束，CLI 未返回正文。");
                if (closing || sessionId != activeSession || !ReferenceEquals(agent, current))
                    return;
                lastTurnUsage = SessionUsage.LastTurn(result);
                if (reason == "cancelled")
                    SetStatus("任务已中止。", speak: true);
                else if (reason == "end_turn")
                {
                    bool spoken = settings.AutoReadReply &&
                        ScreenReaderSpeech.TrySpeak(latestReply.ToString().Trim(), interrupt: false);
                    SetStatus("回复已完成。可在消息列表阅读全文。", speak: !spoken);
                }
                else
                    SetStatus($"Agent 本轮已结束。原因：{AgentText.Visible(reason ?? "CLI 未报告")}。");

                RememberActive();
                PopulateSessions();
            }
            catch
            {
                if (!closing)
                {
                    sending.Sending.Remove(pending);
                    RestoreFailedDraft(sending, owner, text, images, files);
                    RememberDraft(owner, sending);
                }
                throw;
            }
            finally
            {
                FinishStreamingReply(cancelRequested ? "任务已中止，尚无正文。" : "任务未完成，CLI 未返回正文。");
                transcript.EndChunks();
            }
        });
    }

    private async Task RequestCancellationAsync()
    {
        if (!prompting || cancelRequested || sessionId is null || agent is null)
            return;
        cancelRequested = true;
        UpdateCommands();
        SetStatus("已请求中止。若 CLI 无响应，可按 Ctrl+Shift+D 断开。");
        try { await agent.CancelAsync(sessionId); }
        catch (Exception error) when (error is IOException or OperationCanceledException or InvalidOperationException)
        {
            ReportError(error.Message);
        }
    }

    private Task SelectOptionAsync() => RunAsync(async () =>
    {
        if (sessionId is not { } session || agent is not { Connected: true } current || options.Count == 0)
            return;
        if (Dialogs.SessionSettings(this, options) is not { } selected ||
            sessionId != session || !ReferenceEquals(agent, current))
            return;
        var result = await current.ApplySettingsAsync(session, options, selected.Changes);
        if (closing || sessionId != session || !ReferenceEquals(agent, current))
            return;
        options = result.Options;
        var problems = result.Problems.ToList();
        var remembered = RememberSessionDefaults(selected.Defaults, problems);
        if (problems.Count == 0)
        {
            if (result.Applied.Count != 0)
                SetStatus(AgentText.Visible($"已应用设置。{string.Join("、", result.Applied)}。{remembered}"));
            else if (remembered is not null)
                SetStatus(remembered);
            return;
        }

        var applied = result.Applied.Count == 0 ? "本次没有确认生效的改动。"
            : "已应用：\n" + string.Join("\n", result.Applied);
        var report = AgentText.Visible($"{applied}\n{remembered}\n\n请核对：\n{string.Join("\n", problems)}");
        Log(report, "会话设置未全部应用", TaskActivityKind.Failure);
        Dialogs.Text(this, "会话设置应用结果", report);
    });

    private void RememberActive()
    {
        if (sessionId is null || project is null)
            return;
        settings.RememberSession(sessionId, project);
        try { storage.Save(settings); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ReportError($"当前会话仍可使用。无法保存本地历史索引：{error.Message}");
        }
    }

    private Task<string?> AskPermissionAsync(PermissionRequest request, CancellationToken cancellation) =>
        AskDuringTurnAsync<string?>(request.SessionId, cancellation, null, () =>
        {
            Log(request.Description, "等待工具审批", TaskActivityKind.Interaction);
            SetStatus("Agent 请求工具审批。请查看详情。", record: false, speak: true);
            var selected = Dialogs.Permission(Form.ActiveForm ?? this, request, cancellation, RequestCancellationAsync);
            var outcome = cancellation.IsCancellationRequested ? null : request.Options.FirstOrDefault(item => item.Id == selected);
            string summary = "审批结果：" + (outcome?.ToString() ?? "已取消");
            Log($"{summary}\n\n{request.Description}", summary, TaskActivityKind.Interaction);
            if (!cancellation.IsCancellationRequested)
                SetStatus("审批已提交，等待 Agent 继续。", kind: TaskActivityKind.Interaction);
            return outcome?.Id;
        });

    private Task<ElicitationResponse> AskQuestionAsync(ElicitationRequest request, CancellationToken cancellation) =>
        AskDuringTurnAsync(request.SessionId, cancellation, ElicitationResponse.Cancelled, () =>
        {
            Log(AgentText.Visible(request.Message), "等待回答 Agent 的问题", TaskActivityKind.Interaction);
            SetStatus("Agent 有问题需要你回答。", record: false, speak: true);
            var answer = Dialogs.Elicitation(Form.ActiveForm ?? this, request, cancellation, RequestCancellationAsync);
            if (!cancellation.IsCancellationRequested)
            {
                SetStatus(answer.Action switch
                {
                    "accept" => "回答已提交，等待 Agent 继续。",
                    "decline" => "已拒绝回答。",
                    _ => "已取消回答。"
                }, kind: TaskActivityKind.Interaction);
            }
            return answer;
        });

    private Task<T> AskDuringTurnAsync<T>(string requestingSession, CancellationToken cancellation, T fallback, Func<T> ask)
    {
        var response = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async () =>
        {
            bool held = false;
            try
            {
                await interactionQueue.WaitAsync(cancellation);
                held = true;
                if (closing || cancellation.IsCancellationRequested || sessionId != requestingSession)
                {
                    response.TrySetResult(fallback);
                    return;
                }

                interactionOpen = true;
                UpdateComposerCommands();
                try { response.TrySetResult(ask()); }
                finally
                {
                    interactionOpen = false;
                    UpdateComposerCommands();
                }
            }
            catch (OperationCanceledException) { response.TrySetResult(fallback); }
            catch (Exception error) { response.TrySetException(error); }
            finally
            {
                if (held)
                    interactionQueue.Release();
            }
        });
        return response.Task.WaitAsync(cancellation);
    }

    private void ApplyUpdate(string id, JsonElement data)
    {
        var kind = StringValue(data, "sessionUpdate");
        if (sessionId != id)
        {
            if (kind is "available_commands_update" or "session_info_update" or "usage_update" or "plan")
                pendingSessionMetadata?.Add((id, data));
            return;
        }

        switch (kind)
        {
            case "agent_message_chunk":
            case "user_message_chunk":
                var speaker = kind == "agent_message_chunk" ? MessageSpeaker.Assistant : MessageSpeaker.User;
                if (speaker == MessageSpeaker.User && prompting)
                    break;
                if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("content", out var message) ||
                    StringValue(message, "text") is not { Length: > 0 } text)
                    break;
                var visible = AgentText.Visible(text);
                if (speaker == MessageSpeaker.Assistant && transcript.LiveReply is not null)
                {
                    ReceiveReplyText(visible, StringValue(data, "messageId"));
                    break;
                }
                var entry = transcript.AppendChunk(speaker, visible, StringValue(data, "messageId"), out bool started);
                updatedMessages.Add(entry.Index);
                messageRefresh.Start();
                if (speaker == MessageSpeaker.Assistant)
                {
                    if (started && !prompting)
                        latestReply.Clear();
                    latestReply.Append(visible);
                    readReplyMenu.Enabled = latestReply.Length > 0;
                }
                break;

            case "agent_thought_chunk":
                if (transcript.LiveReply is not null && data.TryGetProperty("content", out var thought) &&
                    StringValue(thought, "text") is { Length: > 0 } reasoning)
                    ReceiveReasoning(AgentText.Visible(reasoning));
                break;

            case "config_option_update":
                options = SessionOption.Parse(data);
                UpdateCommands();
                break;

            case "current_mode_update":
                options = options.Select(option => option.Category == "mode"
                    ? option with { Current = StringValue(data, "currentModeId") } : option).ToArray();
                break;

            case "available_commands_update":
                availableCommands = AcpCommand.Parse(data);
                break;

            case "session_info_update":
                if (data.TryGetProperty("title", out var titleValue) &&
                    titleValue.ValueKind is JsonValueKind.String or JsonValueKind.Null)
                {
                    var updatedTitle = titleValue.GetString();
                    if (!activeTitles.TryGetValue(id, out var oldTitle) || oldTitle != updatedTitle)
                    {
                        activeTitles[id] = updatedTitle;
                        PopulateSessions();
                    }
                }
                break;
            case "tool_call":
            case "tool_call_update":
                if (prompting)
                    turnResults.Apply(data);
                var toolId = StringValue(data, "toolCallId");
                var title = StringValue(data, "title");
                if (toolId is not null && title is not null)
                    toolTitles[toolId] = title;
                if (title is null && toolId is not null)
                    toolTitles.TryGetValue(toolId, out title);

                var details = AgentText.ToolDetails(data);
                var toolStatus = StringValue(data, "status");
                var phase = toolStatus switch
                {
                    "completed" => "已完成",
                    "failed" => "失败",
                    "pending" => "等待执行",
                    _ => "正在执行"
                };
                string summary = $"{phase}：{AgentText.Visible(title ?? "工具操作")}";
                Log(title is null || StringValue(data, "title") is not null ? details : $"{AgentText.Visible(title)}\n{details}",
                    summary, toolStatus == "failed" ? TaskActivityKind.Failure : TaskActivityKind.General);
                int diffIndex = 0;
                foreach (var diff in AgentText.Changes(data))
                {
                    var key = (toolId ?? "", diff.Path, diffIndex++);
                    changes[key] = diff;
                }
                if (transcript.LiveReply is not null)
                    QueueReplyPhase(ReplyPhase.Tools);
                break;

            case "plan":
                plan = PlanStep.Parse(data);
                PlanChanged?.Invoke();
                if (plan.Count > 0)
                    Log("任务计划\n" + string.Join("\n", plan));
                break;
            case "usage_update":
                contextUsage = SessionUsage.Context(data);
                break;
        }
    }

    private void SetStatus(string text, bool record = true, bool speak = false, TaskActivityKind kind = TaskActivityKind.General)
    {
        if (record)
            Log(text, kind: kind);
        if (status.Text == text)
            return;
        status.AccessibleName = text;
        status.Text = text;
        if (speak && settings.Announce)
            ScreenReaderSpeech.TrySpeak(text, interrupt: false);
    }

    private void ReadLatestReply()
    {
        var response = latestReply.ToString().Trim();
        if (response.Length > 0 && !ScreenReaderSpeech.TrySpeak(response))
            Dialogs.ReadMessage(this, "最新回复", latestReply.ToString(),
                transcript.LiveReply is not null ? () => latestReply.ToString() : null);
    }


    private Task ShowStatusAsync()
    {
        string[] lines = sessionId is null ? [context.Text, status.Text]
            : [context.Text, status.Text,
                $"模型：{AgentText.Visible(SessionDefaults.CurrentName(options, "model") ?? "未报告")}",
                $"权限模式：{AgentText.Visible(SessionDefaults.CurrentName(options, "mode") ?? "未报告")}",
                $"推理等级：{AgentText.Visible(SessionDefaults.CurrentName(options, "thought_level") ?? "未报告")}",
                contextUsage ?? "上下文用量：官方 CLI 暂未报告。",
                lastTurnUsage ?? "最近一轮 token：官方 CLI 暂未报告。"];
        Dialogs.Text(this, "当前状态", string.Join("\n", lines));
        return Task.CompletedTask;
    }

    private void ShowDiff() => Dialogs.ReviewChanges(this, changes.Values);

    private void ReportError(string text)
    {
        SetStatus($"错误：{AgentText.Visible(text)}", kind: TaskActivityKind.Failure);
        Dialogs.Text(this, "MyAntigravity 错误", AgentText.Visible(text));
    }

    private void OpenLink(Uri uri)
    {
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception error) { ReportError(error.Message); }
    }

    private void Post(Action action)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(action);
        }
        catch (InvalidOperationException) { }
    }

    private static string? StringValue(JsonElement root, string key) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString() : null;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || closing)
            return;
        RememberReadingPosition();
        if (draftOwner is { } owner)
            RememberDraft(owner, draft);
        FlushWorkspace();
        closing = true;
        startupUpdate?.Cancel();
        workspaceSave.Dispose();
        messageRefresh.Dispose();
        if (agent is not null)
        {
            var current = agent;
            agent = null;
            current.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
