using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyAntigravity;

if (args is ["--fixture"] or ["--fixture-no-images"])
{
    await Fixture.RunAsync(args[0] == "--fixture");
    return;
}
if (args is ["--fixture-account", var fixtureState])
{
    await Fixture.RunAsync(true, fixtureState);
    return;
}
if (args is ["--fixture-rich"])
{
    await Fixture.RunAsync(true, rich: true);
    return;
}
if (args is ["--fixture-steering"])
{
    await Fixture.RunAsync(true, steering: true);
    return;
}
if (args is ["--fixture-streaming"])
{
    await Fixture.RunAsync(true, streaming: true);
    return;
}

string scratch = Path.Combine(Directory.GetCurrentDirectory(), ".work", "test-data");
Directory.CreateDirectory(scratch);
if (args is ["--live", var executable])
{
    await LiveAsync(Path.GetFullPath(executable));
    return;
}
if (args is ["--live-permissions", var permissionExecutable])
{
    Environment.ExitCode = await LivePermissions.RunAsync(Path.GetFullPath(permissionExecutable), scratch);
    return;
}

int testCount = 0;
await Case("项目权限偏好隔离、保存和升级兼容", () =>
{
    var first = Directory.CreateDirectory(Path.Combine(scratch, "permission-project", "first")).FullName;
    var second = Directory.CreateDirectory(Path.Combine(scratch, "permission-project", "second")).FullName;
    var store = new SettingsFile(Path.Combine(scratch, "permission-project", "data"));
    var original = new LocalSettings { NewSessionDefaults = new("default", "model-a", "high") };
    original.RememberProject(first);
    original.RememberProject(second);
    original.SetProjectPermission(first + Path.DirectorySeparatorChar, "accept-edits");
    store.Save(original);
    var reloaded = store.Load();
    Check(reloaded.DefaultsForProject(first) == new SessionDefaults("accept-edits", "model-a", "high") &&
        reloaded.DefaultsForProject(second) == original.NewSessionDefaults, "项目覆盖权限，其他项目沿用旧版全局偏好");
    Check(reloaded.DefaultsForProject(first).Changes().Any(change => change.Category == "mode" && change.Value == "accept-edits"),
        "新会话提交所保存的官方 ACP 权限值");
    reloaded.SetProjectPermission(second, "auto");
    Check(reloaded.DefaultsForProject(second).Mode is null && !SessionDefaults.CanRememberMode("auto"),
        "已保存的 Auto 不自动应用到新会话");
    reloaded.SetProjectPermission(second, null);
    reloaded.NewSessionDefaults = new("auto", "model-a");
    Check(reloaded.DefaultsForProject(second) == new SessionDefaults(null, "model-a"),
        "旧版全局 Auto 不自动应用，模型偏好保留");
    reloaded.NewSessionDefaults = original.NewSessionDefaults;
    Check(reloaded.RemoveProject(first) && reloaded.PermissionForProject(first) is null &&
        reloaded.DefaultsForProject(first).Mode == "default", "移除项目会取消其权限偏好");
    Check(reloaded.BackgroundApprovalNotifications && !reloaded.BackgroundCompletionNotifications,
        "升级后默认提醒审批，由用户决定是否提醒完成");
    reloaded.BackgroundApprovalNotifications = false;
    reloaded.BackgroundCompletionNotifications = true;
    store.Save(reloaded);
    var notifications = store.Load();
    Check(!notifications.BackgroundApprovalNotifications && notifications.BackgroundCompletionNotifications, "分别保存两种提醒的设置");
    return Task.CompletedTask;
});
await Case("排队追问按顺序发送、撤回及中止恢复", () =>
{
    var queue = new FollowUpQueue();
    Check(!queue.Enqueue("   ") && queue.TakeNext() is null, "空消息无需排队");
    Check(queue.Enqueue("第一条") && queue.Enqueue("第二条") && queue.Enqueue("撤回这条"), "可记录多条追问");
    Check(queue.WithdrawLatest() == "撤回这条" && queue.TakeNext() == "第一条", "撤回和出队保持原有顺序");
    Check(queue.PendingTexts.SequenceEqual(["第二条"]), "保留待发追问可写回本机草稿");
    Check(queue.Drain().SequenceEqual(["第二条"]) && queue.Count == 0, "任务未完成时可退回仍然排队的内容");
    for (int i = 0; i < FollowUpQueue.Capacity; i++)
        Check(queue.Enqueue(i.ToString()), "可排队至容量");
    Check(!queue.Enqueue("超出") && queue.Drain().Last() == (FollowUpQueue.Capacity - 1).ToString(), "达到容量时保留已排队内容");
    Check(queue.Enqueue("两条相同") && queue.Enqueue("两条相同") && queue.Enqueue("最后一条"), "可以排队相同的文字");
    var entries = queue.Entries.ToArray();
    Check(queue.Withdraw(entries[1]) && !queue.Withdraw(entries[1]) && ReferenceEquals(entries[0], queue.Entries.First()),
        "列表只撤回选中的一条，快照过期时不会撤回另一条同文消息");
    Check(queue.Drain().SequenceEqual(["两条相同", "最后一条"]), "部分撤回仍保持剩余消息顺序");
    return Task.CompletedTask;
});
await Case("跨项目会话索引和 UTF-8 正文导出", async () =>
{
    var first = Path.Combine(scratch, "索引甲");
    var second = Path.Combine(scratch, "索引乙");
    var removed = Path.Combine(scratch, "已经移除");
    var settings = new LocalSettings();
    settings.RememberProject(first);
    settings.RememberProject(second);
    settings.RememberSession("same", first, DateTimeOffset.UnixEpoch);
    settings.RememberSession("same", second, DateTimeOffset.UnixEpoch.AddDays(1));
    settings.RememberSession("deleted", first, DateTimeOffset.UnixEpoch.AddDays(2));
    settings.RememberSession("hidden", removed, DateTimeOffset.UnixEpoch.AddDays(3));
    settings.RemoveSession("deleted", first);
    settings.SetSessionPinned("same", first, true);
    settings.SetSessionArchived("same", second, true);
    var indexed = settings.IndexedSessions();
    Check(indexed.Count == 2 && indexed[0].Directory == first && indexed[0].Pinned &&
        indexed[1].Directory == second && indexed[1].Archived, "归档可查，同 ID 不跨项目合并，移除记录隐藏");

    var transcript = new ConversationTranscript();
    transcript.Add(MessageSpeaker.User, "我的问题");
    var reply = transcript.AppendChunk(MessageSpeaker.Assistant, "代码 👋\n", "reply", out _);
    transcript.AppendChunk(MessageSpeaker.Assistant, "第二行", "reply", out _);
    var full = ConversationExport.Format(transcript.Messages);
    var one = ConversationExport.Format([reply]);
    transcript.AppendChunk(MessageSpeaker.Assistant, "生成中的后续", "reply", out _);
    var area = Directory.CreateDirectory(Path.Combine(scratch, "导出文本")).FullName;
    var target = Path.Combine(area, "对话.txt");
    File.WriteAllText(target, "旧文本");
    await ConversationExport.SaveAsync(target, full);
    Check(full.ReplaceLineEndings("\n") == "你：\n我的问题\n\nAgent：\n代码 👋\n第二行" &&
        one.ReplaceLineEndings("\n") == "Agent：\n代码 👋\n第二行" &&
        File.ReadAllText(target) == full, "保存预览时已选内容的快照，保留完整正文");
    Check(File.ReadAllBytes(target).AsSpan(0, 3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }) &&
        Directory.EnumerateFiles(area).Single() == target, "生成 UTF-8 文本并清理临时文件");
});

await Case("更新日志转换为读屏纯文本", () =>
{
    var notes = """
        ## 更新内容
        > **CLI 支持**和 *键盘操作*
        - 用 `agy.exe` 连接
        - [官方安装指南](https://example.com/install) &amp; ~~旧入口~~
        ~~~text
        # 原样保留命令内的标点
        ~~~
        """;
    var expected = """
        更新内容
        CLI 支持和 键盘操作
        • 用 agy.exe 连接
        • 官方安装指南 & 旧入口

        # 原样保留命令内的标点
        """;
    Check(UpdateNotes.PlainText(notes).ReplaceLineEndings("\n") == expected.ReplaceLineEndings("\n"),
        "去除标题、引用、强调、链接和代码围栏的 Markdown 标记，保留内容");
    var changelog = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "CHANGELOG.md"));
    string beta15 = UpdateNotes.ForVersion(changelog, "0.1.0-beta.15");
    Check(beta15.Contains("会话 → 跨项目查找会话") && beta15.Contains("任务 → 本轮结果") &&
        !beta15.Contains("启动时检测 Windows CLI") && !beta15.Contains("新增启动更新提醒"),
        "日志显示当前安装版本的具体入口，并隔离其他版本和未发布改动");
    Check(UpdateNotes.ForVersion(changelog, "0.1.0-beta.1") == "", "按完整版本匹配日志");
    string beta18 = UpdateNotes.ForVersion(changelog, "0.1.0-beta.18");
    Check(beta18.Contains("Ctrl+G") && beta18.Contains("Alt+F") && beta18.Contains("Alt+A") &&
        !beta18.Contains("跨项目查找会话"), "新版更新窗显示目标版本的操作入口和修复");
    string beta19 = UpdateNotes.ForVersion(changelog, "0.1.0-beta.19");
    Check(beta19.Contains("创建桌面快捷方式") && beta19.Contains("检测 CLI 版本"), "安装修复有独立的目标版本日志");
    return Task.CompletedTask;
});

await Case("CLI 路径拒绝桌面版并恢复自动检测", () =>
{
    var folder = Directory.CreateDirectory(Path.Combine(scratch, "CLI 路径检测")).FullName;
    var desktop = Path.Combine(folder, "Antigravity.exe");
    var cli = Path.Combine(folder, "agy.exe");
    File.WriteAllText(desktop, "");
    File.WriteAllText(cli, "");
    try
    {
        Cli.ValidatePath(desktop);
        throw new InvalidOperationException("桌面版路径被接受。");
    }
    catch (FileNotFoundException error)
    {
        Check(error.Message.Contains("桌面版") && error.Message.Contains("agy.exe"), "解释两种程序的区别");
    }
    foreach (string name in new[] { "Antigravity-x64 (1).exe", "Antigravity (1).dmg" })
    {
        try
        {
            Cli.ValidatePath(Path.Combine(folder, name));
            throw new InvalidOperationException("桌面版安装包被接受。");
        }
        catch (FileNotFoundException error)
        {
            Check(error.Message.Contains(name) && error.Message.Contains("Windows CLI"), "指出误选的桌面版安装包");
        }
    }
    Check(Cli.InstallCommand == "irm https://antigravity.google/cli/install.ps1 | iex", "复制 Google 官方 PowerShell 命令");
    if (OperatingSystem.IsWindows())
    {
        var setup = Cli.WindowsInstallStartInfo(folder);
        Check(Path.IsPathFullyQualified(setup.FileName) && Path.GetFileName(setup.FileName) == "powershell.exe" &&
            setup.WorkingDirectory == folder && setup.CreateNoWindow && setup.RedirectStandardOutput &&
            setup.ArgumentList.Take(4).SequenceEqual(["-NoLogo", "-NoProfile", "-NonInteractive", "-Command"]) &&
            setup.ArgumentList[^1].EndsWith(Cli.InstallCommand, StringComparison.Ordinal),
            "自动安装使用系统 PowerShell 和同一条官方命令");
    }
    Check(Cli.ValidatePath($"\"{cli}\"") == cli && Cli.Find(cli) == cli, "带引号的官方路径可识别");
    string? automaticallyDetected = Cli.Find(null);
    if (automaticallyDetected is null)
        Throws<FileNotFoundException>(() => Cli.Find(desktop));
    else
        Check(Cli.Find(desktop) == automaticallyDetected, "已安装时绕过旧版误填路径");
    return Task.CompletedTask;
});

await Case("安装后沿用旧进程 PATH 也能发现 CLI，PowerShell 保留中文", async () =>
{
    var folder = Directory.CreateDirectory(Path.Combine(scratch, "CLI 安装后检测", Guid.NewGuid().ToString("N"))).FullName;
    var defaultDir = Directory.CreateDirectory(Path.Combine(folder, "agy", "bin")).FullName;
    var customDir = Directory.CreateDirectory(Path.Combine(folder, "自定义安装位置")).FullName;
    string stalePath = Path.Combine(folder, "旧 PATH");
    string? userPath = null;
    string? RegisteredPath(EnvironmentVariableTarget scope) => scope == EnvironmentVariableTarget.User ? userPath : null;

    Check(Cli.FindInstalledIn(folder, stalePath, RegisteredPath) is null, "安装前没有 CLI");
    var customExe = Path.Combine(customDir, "agy.exe");
    File.WriteAllText(customExe, "");
    userPath = $"\"{customDir}\"";
    Check(Cli.FindInstalledIn(folder, stalePath, RegisteredPath) == customExe, "重读用户 PATH 找到新安装位置");
    var defaultExe = Path.Combine(defaultDir, "agy.exe");
    File.WriteAllText(defaultExe, "");
    Check(Cli.FindInstalledIn(folder, stalePath, _ => throw new InvalidOperationException()) == defaultExe,
        "默认安装目录独立于进程或注册表 PATH");

    if (OperatingSystem.IsWindows())
    {
        var powershell = Cli.WindowsInstallStartInfo(folder);
        string encodingSetup = powershell.ArgumentList[^1][..^Cli.InstallCommand.Length];
        powershell.ArgumentList[^1] = encodingSetup + "Write-Progress '进度记录'; Write-Host '中文进度'; " +
            "Write-Error '中文安装错误' -ErrorAction Continue; exit 0";
        var (output, error) = await Cli.ReadOutputAsync(powershell, "安装测试", CancellationToken.None,
            TimeSpan.FromSeconds(15), showError: true);
        Check(output == "中文进度" && error.Contains("中文安装错误", StringComparison.Ordinal) &&
            !error.Contains("CLIXML", StringComparison.Ordinal), "实际 PowerShell 中文双流及纯文本错误");
        powershell.ArgumentList[^1] = encodingSetup + "Write-Error '中文安装错误'";
        try
        {
            await Cli.ReadOutputAsync(powershell, "安装测试", CancellationToken.None, TimeSpan.FromSeconds(15), showError: true);
            throw new InvalidOperationException("PowerShell 错误退出未报告。");
        }
        catch (IOException failure)
        {
            Check(failure.Message.Contains("中文安装错误", StringComparison.Ordinal), "安装失败消息保留中文详情");
        }
    }
});

await Case("任务记录按会话、事件类型和正文关键词筛选", () =>
{
    var failed = new ActivityRecord("first", DateTimeOffset.UnixEpoch, "失败：编译",
        "执行 dotnet build\n未找到项目文件", TaskActivityKind.Failure);
    var asked = new ActivityRecord("second", DateTimeOffset.UnixEpoch, "等待工具审批",
        "需要运行 dotnet build", TaskActivityKind.Interaction);
    ActivityRecord[] entries = [failed, asked];
    Check(entries.Where(entry => entry.Matches(null, TaskActivityKind.Failure, ["编译", "DOTNET", "项目文件"]))
        .SequenceEqual([failed]), "标题和正文都可匹配，忽略大小写");
    Check(!failed.Matches("second", null, []) && asked.Matches("second", TaskActivityKind.Interaction, ["dotnet"]) &&
        !asked.Matches(null, TaskActivityKind.Failure, []) && !failed.Matches(null, null, ["dotnet", "无此关键词"]),
        "按会话和事件类型隔离，并要求匹配全部关键词");
    Check(failed.LogText.EndsWith(failed.Detail, StringComparison.Ordinal) && !failed.LogText.Contains(asked.Detail),
        "复制单条保留其完整多行正文");
    return Task.CompletedTask;
});

await Case("安装向导重用已装的 CLI，验证首次安装并保留失败原因", async () =>
{
    const string path = "test-agy.exe";
    string? available = path;
    int installs = 0;
    Task Install()
    {
        installs++;
        available = path;
        return Task.CompletedTask;
    }

    string? checkedPath = null;
    Task<string> Version(string executable)
    {
        checkedPath = executable;
        return Task.FromResult("1.5.14");
    }

    await Cli.EnsureInstalledAsync(() => available, Install, Version);
    Check(installs == 0 && checkedPath == path, "已有 CLI 只核验版本");
    available = checkedPath = null;
    await Cli.EnsureInstalledAsync(() => available, Install, Version);
    Check(installs == 1 && checkedPath == path, "安装后重新检测并核验版本");

    await ThrowsAsync<IOException>(() => Cli.EnsureInstalledAsync(() => null, () => Task.CompletedTask, Version));
    await ThrowsAsync<IOException>(() => Cli.EnsureInstalledAsync(() => available, Install,
        _ => Task.FromResult("版本未报告")));
    try
    {
        await Cli.EnsureInstalledAsync(() => null, () => Task.FromException(new IOException("示例安装失败")), Version);
        throw new InvalidOperationException("CLI 安装失败被忽略。");
    }
    catch (IOException failure)
    {
        Check(failure.Message == "示例安装失败", "把官方安装异常原样传到调用端");
    }
});

await Case("草稿与阅读位置跨重启并按项目会话隔离", () =>
{
    var data = Directory.CreateDirectory(Path.Combine(scratch, "工作状态")).FullName;
    var file = new SettingsFile(data);
    File.WriteAllText(Path.Combine(data, "settings.json"), """{"Announce":false,"Projects":[],"Sessions":[]}""");
    var settings = file.Load();
    Check(settings.SessionWorkspaces.Count == 0 && !settings.Announce, "兼容旧设置");
    string project = Path.Combine(scratch, "测试项目");
    string other = Path.Combine(scratch, "另一个项目");
    var history = new ConversationTranscript();
    history.Add(MessageSpeaker.User, "第一条");
    var current = history.Add(MessageSpeaker.Assistant, "第二条\r\n继续读这里");
    var bookmark = ReadingBookmark.From(current);
    Check(settings.SaveDraft("one", project, "未发送的中文草稿") && settings.SaveBookmark("one", project, bookmark) &&
        settings.SaveDraft("one", other, "另一个项目的草稿"), "草稿和选中消息分别存储");
    settings.RememberSession("one", project);
    file.Save(settings);
    settings = file.Load();
    Check(settings.FindWorkspace("one", project) is { Draft: "未发送的中文草稿", Bookmark: not null } &&
        settings.FindWorkspace("one", other)?.Draft == "另一个项目的草稿", "重新加载后仍按项目隔离");
    var replay = new ConversationTranscript();
    replay.Add(MessageSpeaker.Notice, "新插入的说明");
    replay.Add(MessageSpeaker.User, "第一条");
    replay.Add(MessageSpeaker.Assistant, "第二条\n继续读这里");
    Check(bookmark.Locate(replay.Messages) == 2, "用正文指纹重新找到消息，兼容 Windows 换行差异");
    settings.SaveDraft("one", project, "");
    Check(settings.FindWorkspace("one", project)?.Bookmark == bookmark, "清空已发草稿后保留阅读位置");
    Check(settings.RemoveSession("one", project) && settings.FindWorkspace("one", project) is null &&
        !settings.SaveDraft("one", project, "延迟到达的保存") && settings.FindWorkspace("one", other) is not null,
        "删除会话清理本机状态，延迟保存不复活记录");
    return Task.CompletedTask;
});

await Case("长消息的代码块、行号和链接定位", () =>
{
    string fence = new('\x60', 3);
    string text = $"说明 [文档](https://example.test/guide)\n{fence}python\n  x = 1\nhttps://example.test/code\n{fence}\n" +
        "其他 https://example.test/more. file:///C:/private\n~~~text\n尚未闭合";
    var outline = MessageOutline.Parse(text);
    Check(outline.CodeBlocks is [{ Line: 2, Language: "python" }, { Line: 7, Language: "text" }],
        "围栏类型、语言和块顺序");
    var first = outline.CodeBlocks[0];
    Check(first.Code == "  x = 1\nhttps://example.test/code\n" && text[first.Offset..first.End] == first.Code &&
        outline.CodeBlocks[1].Code == "尚未闭合", "复制保留缩进，支持流式尚未关闭的块");
    Check(outline.Links is [{ Line: 1, Address: "https://example.test/guide" },
        { Line: 6, Address: "https://example.test/more" }] &&
        text.AsSpan(outline.Links[1].Offset).StartsWith(outline.Links[1].Address), "位置准确，代码里的网址不当作链接");
    return Task.CompletedTask;
});

await Case("Git 工作区只读查询及逐段差异", async () =>
{
    var repo = Directory.CreateDirectory(Path.Combine(scratch, "Git 审查 " + Guid.NewGuid().ToString("N"))).FullName;
    var path = Path.Combine(repo, "带 空格 [测试].txt");
    await Git("init", "-q");
    File.WriteAllText(path, "第一行\n中间行\n最后一行\n");
    await Git("add", "--", Path.GetFileName(path));
    await Git("-c", "user.name=MyAntigravity test", "-c", "user.email=test@example.invalid",
        "-c", "commit.gpgsign=false", "commit", "-qm", "fixture");
    File.WriteAllText(path, "第一行已暂存\n中间行\n最后一行\n");
    await Git("add", "--", Path.GetFileName(path));
    File.WriteAllText(path, "第一行已暂存\n中间行\n最后一行未暂存\n");
    File.WriteAllText(Path.Combine(repo, "新文件.txt"), "未跟踪内容\n");
    var review = (await GitReview.OpenAsync(repo))!;
    var entries = await review.ListAsync();
    Check(entries is [{ Scope: GitChangeScope.Staged }, { Scope: GitChangeScope.Worktree }, { Scope: GitChangeScope.Untracked }],
        "有双层改动的文件分别列出");
    var staged = GitReview.ReadSections(await review.ReadPatchAsync(entries[0]));
    var unstaged = GitReview.ReadSections(await review.ReadPatchAsync(entries[1]));
    var untracked = GitReview.ReadSections(await review.ReadPatchAsync(entries[2]));
    Check(staged.Count == 1 && staged[0].NewLine == 1 && staged[0].Added.SequenceEqual(["第一行已暂存"]),
        "带空格和中括号的路径按字面匹配");
    Check(unstaged.Count == 1 && unstaged[0].NewLine == 3 && unstaged[0].Added.SequenceEqual(["最后一行未暂存"]),
        "分开显示索引和工作区中的修改");
    Check(untracked.Count == 1 && untracked[0].NewLine == 1 && untracked[0].Added.SequenceEqual(["未跟踪内容"]) &&
        (await review.ListAsync()).SequenceEqual(entries), "未跟踪文件可审查，状态未被更改");

    var original = (await review.ListWorktreesAsync()).Single();
    var checkout = Path.Combine(scratch, "Git 工作树 " + Guid.NewGuid().ToString("N"));
    await ThrowsAsync<IOException>(() => review.CreateWorktreeAsync(repo, "work/existing-path"));
    await ThrowsAsync<IOException>(() => review.CreateWorktreeAsync(Path.Combine(repo, "子目录"), "work/nested"));
    await ThrowsAsync<ArgumentException>(() => review.CreateWorktreeAsync(checkout, "--force"));
    var created = await review.CreateWorktreeAsync(checkout, "work/fixture");
    var trees = await review.ListWorktreesAsync();
    Check(LocalSettings.PathEquals(checkout, created) && trees.Count == 2 && trees[0] == original &&
        trees[1].Branch == "work/fixture" && trees[1].Commit == original.Commit &&
        LocalSettings.PathEquals(trees[1].Path, checkout), "真实 Git 创建并列出新工作树");
    Check(File.ReadAllText(Path.Combine(checkout, Path.GetFileName(path))).ReplaceLineEndings("\n") == "第一行\n中间行\n最后一行\n" &&
        (await review.ListAsync()).SequenceEqual(entries), "新工作树从 HEAD 创建，原暂存和未暂存修改保留");

    Check(!(await review.InspectWorktreeAsync(repo, [])).CanRemove &&
        !(await review.InspectWorktreeAsync(checkout, [Path.Combine(checkout, "附加目录")])).CanRemove,
        "保护主工作树和当前项目附加目录");
    await Git("worktree", "lock", checkout);
    Check((await review.InspectWorktreeAsync(checkout, [])).BlockedReason?.Contains("锁定") == true,
        "Git 锁定工作树不能移除");
    await Git("worktree", "unlock", checkout);
    var approved = await review.InspectWorktreeAsync(checkout, []);
    Check(approved.CanRemove, $"可检查干净的附属工作树：{approved.BlockedReason ?? string.Join(", ", approved.Changes)}");
    var changedAfterConfirm = Path.Combine(checkout, "确认后才写入.txt");
    File.WriteAllText(changedAfterConfirm, "保留");
    Check((await review.InspectWorktreeAsync(checkout, [])).Changes is [{ Scope: GitChangeScope.Untracked }],
        "读取新增的未跟踪文件");
    await ThrowsAsync<IOException>(() => review.RemoveWorktreeAsync(approved, []));
    Check(File.Exists(changedAfterConfirm), "状态变更后拒绝上一次确认");
    File.Delete(changedAfterConfirm);
    var ignoreRules = Path.Combine(repo, ".git", "info", "exclude");
    File.AppendAllText(ignoreRules, "\nignored-cleanup-fixture.tmp\n");
    var ignoredFile = Path.Combine(checkout, "ignored-cleanup-fixture.tmp");
    File.WriteAllText(ignoredFile, "保留忽略文件");
    Check((await review.InspectWorktreeAsync(checkout, [])).IgnoredPaths is ["ignored-cleanup-fixture.tmp"],
        "被 Git 忽略的文件也阻止删除");
    await ThrowsAsync<IOException>(() => review.RemoveWorktreeAsync(approved, []));
    File.Delete(ignoredFile);
    await review.RemoveWorktreeAsync(approved, []);
    await Git("rev-parse", "--verify", "refs/heads/work/fixture");
    Check(!Directory.Exists(checkout) && (await review.ListWorktreesAsync()).Count == 1 &&
        (await review.ListAsync()).SequenceEqual(entries), "仅移除干净的附属目录，分支和原仓库改动保留");

    async Task Git(params string[] arguments)
    {
        using var process = Process.Start(Cli.StartInfo(OperatingSystem.IsWindows() ? "git.exe" : "git", repo, true,
            ["-c", $"core.hooksPath={Path.Combine(repo, "no-test-hooks")}", .. arguments]))!;
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Check(process.ExitCode == 0, await error);
    }
});

await Case("本轮工具结果合并 ACP 更新及差异", () =>
{
    var results = new TurnResults();
    Apply("""
        {"toolCallId":"first","kind":"execute","title":"运行测试","status":"in_progress",
         "locations":[{"path":"src/a.cs","line":3}],
         "content":[{"type":"content","content":{"type":"text","text":"测试开始"}}]}
        """);
    Apply("""
        {"toolCallId":"second","kind":"edit","title":"更新说明","status":"completed",
         "content":[{"type":"diff","path":"README.md","oldText":"before","newText":"after"},
          {"type":"content","content":{"type":"resource_link","name":"报告","uri":"https://example.com/report"}}]}
        """);
    var old = results.Snapshot();
    Apply("""{"toolCallId":"first","status":"completed","rawOutput":"测试成功"}""");
    Apply("""{"toolCallId":"first","rawOutput":null}""");
    var rows = results.Snapshot();
    Check(rows is [{ Id: "first", Title: "运行测试", Status: "completed" }, { Id: "second" }] &&
        rows[0].Details().Contains("测试开始") && rows[0].Details().Contains("测试成功") &&
        rows[0].Details().Contains("第 3 行") && old[0].Status == "in_progress", "补丁不清空未更新字段，浏览快照固定");
    Check(rows[1].Changes is [{ Path: "README.md" }] && rows[1].Details().Contains("https://example.com/report"),
        "同一项展示资源链接和结构化差异");
    Check(rows[0].ProjectLocations() is [{ Path: "src/a.cs", Line: 3 }] &&
        rows[1].ProjectLocations() is [{ Path: "README.md", Line: null }], "保留官方行号，差异文件也可定位");
    Apply("""
        {"toolCallId":"first","status":"failed","content":[
          {"type":"content","content":{"type":"text","text":"error: missing file"}},
          {"type":"content","content":{"type":"image","mimeType":"image/png","data":"iVBORw=="}},
          {"type":"content","content":{"type":"image","mimeType":"image/png","data":"invalid#"}},
          {"type":"content","content":{"type":"image","mimeType":"image/svg+xml","data":"PHN2Zy8+"}}
        ]}
        """);
    Apply("""{"toolCallId":"first","rawOutput":null}""");
    var failures = results.Snapshot().Where(result => result.IsFailure).ToArray();
    Check(failures is [{ Id: "first" }] && failures[0].Details().Contains("error: missing file") &&
        !failures[0].Details().Contains("更新说明"), "只筛失败项且其详情不混入其他工具");
    Check(failures[0].Images is [{ MimeType: "image/png", Length: 4 }] &&
        !failures[0].Details().Contains("iVBORw=="), "保留可预览的内嵌图片，正文省略二进制");
    Apply("""{"toolCallId":"second","content":[]}""");
    Check(results.Snapshot()[1].Changes.Count == 0, "显式的新内容取代旧内容");
    results.Clear();
    Check(results.Snapshot().Count == 0, "新轮次清除旧结果");
    return Task.CompletedTask;

    void Apply(string json)
    {
        using var document = JsonDocument.Parse(json);
        results.Apply(document.RootElement);
    }
});

await Case("项目文件只读预览和官方行号定位", async () =>
{
    var fixture = Directory.CreateDirectory(Path.Combine(scratch, "文件预览")).FullName;
    var primary = Directory.CreateDirectory(Path.Combine(fixture, "主目录")).FullName;
    var extra = Directory.CreateDirectory(Path.Combine(fixture, "附加目录")).FullName;
    var outside = Path.Combine(fixture, "项目外.txt");
    File.WriteAllText(outside, "项目外文件");
    var file = Path.Combine(primary, "代码.txt");
    File.WriteAllText(file, "一行\r\n二行 👋\r\n末行", Encoding.Unicode);
    string[] workspaces = [primary, extra];
    var row = await ProjectFilePreview.ReadAsync(new ToolLocation("代码.txt", 2), workspaces);
    Check(row.FullPath == file && row.ActualLine == 2 && row.Text.Substring(row.Start, row.Length) == "二行 👋",
        "相对路径以主项目为准，Unicode 和 CRLF 定位正确");
    var last = await ProjectFilePreview.ReadAsync(new ToolLocation(file, 50), workspaces);
    Check(last.RequestedLine == 50 && last.ActualLine == 3 && last.Text.Substring(last.Start, last.Length) == "末行",
        "文件缩短时落在实际最后一行");
    var otherFile = Path.Combine(extra, "附加文本.txt");
    File.WriteAllText(otherFile, "");
    Check((await ProjectFilePreview.ReadAsync(new ToolLocation(otherFile), workspaces)) is { ActualLine: 1, Length: 0 },
        "附加目录的绝对路径和空文件可读");
    await ThrowsAsync<IOException>(() => ProjectFilePreview.ReadAsync(new ToolLocation(outside), workspaces));
    await ThrowsAsync<IOException>(() => ProjectFilePreview.ReadAsync(new ToolLocation(Path.Combine("..", "项目外.txt")), workspaces));
    await ThrowsAsync<IOException>(() => ProjectFilePreview.ReadAsync(new ToolLocation("https://example.com/a.txt"), workspaces));
    var binary = Path.Combine(primary, "二进制.dat");
    File.WriteAllBytes(binary, [0, 1, 2]);
    await ThrowsAsync<IOException>(() => ProjectFilePreview.ReadAsync(new ToolLocation(binary), workspaces));
    using (var oversized = File.Create(Path.Combine(primary, "大文件.txt")))
        oversized.SetLength(ProjectFilePreview.MaxBytes + 1);
    await ThrowsAsync<IOException>(() => ProjectFilePreview.ReadAsync(new ToolLocation("大文件.txt"), workspaces));
});

await Case("会话置顶及规则来源范围", () =>
{
    var folder = Directory.CreateDirectory(Path.Combine(scratch, "规则和置顶")).FullName;
    var repo = Directory.CreateDirectory(Path.Combine(folder, "repo")).FullName;
    var sub = Directory.CreateDirectory(Path.Combine(repo, "sub")).FullName;
    var nestedRules = Directory.CreateDirectory(Path.Combine(repo, ".agents", "rules", "nested")).FullName;
    var legacy = Directory.CreateDirectory(Path.Combine(sub, ".agent", "rules")).FullName;
    var profile = Directory.CreateDirectory(Path.Combine(folder, "profile")).FullName;
    var global = Directory.CreateDirectory(Path.Combine(profile, ".gemini", "config", "rules")).FullName;
    File.WriteAllText(Path.Combine(repo, ".git"), "gitdir: worktree");
    File.WriteAllText(Path.Combine(repo, "AGENTS.md"), "仓库级");
    File.WriteAllText(Path.Combine(repo, ".agents", "rules", "project.md"), "分项规则");
    File.WriteAllText(Path.Combine(nestedRules, "not-scanned.md"), "子规则");
    File.WriteAllText(Path.Combine(legacy, "legacy.md"), "旧规则");
    File.WriteAllText(Path.Combine(global, "global.md"), "全局");
    File.WriteAllText(Path.Combine(folder, "GEMINI.md"), "仓库边界之外");
    var rules = AntigravityRules.List([sub, repo], profile);
    Check(rules.Select(item => item.Name).ToHashSet().SetEquals(["AGENTS.md", "project.md", "legacy.md", "global.md"]) &&
        rules.Count == 4 && SkillCatalog.Preview(rules.Single(item => item.Name == "project.md").Path) == "分项规则",
        "扫描工作树 Git 边界、旧规则目录和全局，附加目录重叠时去重");

    var store = new SettingsFile(folder);
    var settings = new LocalSettings();
    settings.RememberSession("pin", repo, DateTimeOffset.UnixEpoch);
    settings.RememberSession("recent", repo, DateTimeOffset.UnixEpoch.AddDays(1));
    settings.RememberSession("pin", sub, DateTimeOffset.UnixEpoch);
    Check(settings.SetSessionPinned("pin", repo, true), "置顶本机记录");
    store.Save(settings);
    var loaded = store.Load();
    loaded.RememberSession("pin", repo, DateTimeOffset.UnixEpoch);
    var titles = new Dictionary<string, string?>();
    Check(loaded.ProjectSessions(repo, [], titles) is [{ Id: "pin", Pinned: true }, { Id: "recent" }] &&
        loaded.FindSession("pin", sub) is { Pinned: false }, "重启后仍优先显示，同 ID 的其他项目独立");
    loaded.SetSessionArchived("pin", repo, true);
    Check(loaded.ProjectSessions(repo, [], titles) is [{ Id: "recent" }], "归档不显示在当前列表");
    loaded.SetSessionArchived("pin", repo, false);
    Check(loaded.FindSession("pin", repo) is { Pinned: true } && loaded.SetSessionPinned("pin", repo, false) &&
        loaded.ProjectSessions(repo, [], titles) is [{ Id: "recent" }, { Id: "pin" }], "恢复归档保留置顶，取消后按时间");
    return Task.CompletedTask;
});

await Case("消息列表归并流式片段并保留原文", () =>
{
    var history = new ConversationTranscript();
    history.Add(MessageSpeaker.User, "第一轮");
    var running = history.AppendChunk(MessageSpeaker.Assistant, "第一行\n", null, out bool started);
    Check(started && ReferenceEquals(running,
        history.AppendChunk(MessageSpeaker.Assistant, "第二行", null, out started)) && !started,
        "缺少消息 ID 的相邻片段归到同一条");
    history.Add(MessageSpeaker.FollowUp, "请补充");
    history.AppendChunk(MessageSpeaker.Assistant, "代码\n" + new string('甲', 100), "reply-1", out started);
    Check(started && history.Messages.Count == 4 && running.Text == "第一行\n第二行" &&
        history.Messages[3].ToString().EndsWith('…') && history.Messages[3].Text.Length == 103,
        "追加指令划分消息，预览截断时保留全文");
    history.AppendChunk(MessageSpeaker.Assistant, "另一条回复", "reply-2", out started);
    Check(started && history.Messages.Count == 5, "同角色的新消息 ID 开启新列表项");
    history.EndChunks();
    history.AppendChunk(MessageSpeaker.Assistant, "下一轮", "reply-2", out started);
    Check(started && history.Messages.Count == 6, "结束流之后开启新列表项");
    history.Clear();
    Check(history.Messages.Count == 0 && history.Add(MessageSpeaker.Notice, "提示").Index == 0,
        "清空会话后重新编号");
    return Task.CompletedTask;
});

await Case("路径、设置和会话索引", () =>
{
    var project = Path.Combine(scratch, "空格 项目");
    Directory.CreateDirectory(project);
    Check(LocalSettings.ProjectPath(project + Path.DirectorySeparatorChar) == project, "规范化项目路径");
    Throws<ArgumentException>(() => LocalSettings.ProjectPath("relative"));
    Throws<DirectoryNotFoundException>(() => LocalSettings.ProjectPath(Path.Combine(scratch, "missing")));
    Check(LocalSettings.PathEquals(project, project + Path.DirectorySeparatorChar), "尾分隔符");
    var settings = new LocalSettings { Announce = false };
    settings.RememberProject(project);
    settings.RememberProject(project);
    settings.RememberSession("id-1", project);
    Check(settings.RenameSession("id-1", project, "  我的测试会话  "), "重命名本地会话");
    settings.RememberSession("id-1", project);
    settings.RememberSession("id-2", Path.Combine(scratch, "second"));
    var file = new SettingsFile(scratch);
    file.Save(settings);
    settings.UpdateChannel = "stable";
    settings.EnterToSend = true;
    settings.AutoReadReply = true;
    file.Save(settings);
    var restored = file.Load();
    Check(restored.Projects.SequenceEqual([project]) && restored.Sessions.Count == 2 &&
        restored.Sessions[0] is { Id: "id-1", Title: "我的测试会话" } && !restored.Announce &&
        restored.UpdateChannel == "stable" && restored.EnterToSend && restored.AutoReadReply,
        "往返、去重、保存与保留本地会话名称和阅读设置");
    LocalSession[] official =
    [
        new("id-1", project, "官方标题", DateTimeOffset.UtcNow),
        new("foreign", Path.GetPathRoot(project)!, "其他项目", DateTimeOffset.UtcNow)
    ];
    var titles = new Dictionary<string, string?> { ["id-1"] = "新标题" };
    var merged = restored.ProjectSessions(project, official, titles);
    Check(merged is [{ Id: "id-1", Title: "我的测试会话" }], "只显示当前项目，本地名称优先");
    Check(restored.RenameSession("id-1", project, "") &&
        restored.ProjectSessions(project, official, titles) is [{ Title: "新标题" }],
        "清空本地名称后显示实时官方标题");
    titles["id-1"] = null;
    Check(restored.ProjectSessions(project, official, titles) is [{ Title: null }],
        "官方明确清空标题后不再显示缓存旧标题");
    Check(restored.SetSessionArchived("id-1", project, true), "归档本地会话");
    restored.RememberSession("id-1", project);
    file.Save(restored);
    restored = file.Load();
    Check(restored.ProjectSessions(project, official, titles).Count == 0 &&
        restored.ProjectSessions(project, official, titles, archived: true) is [{ Id: "id-1", Archived: true }],
        "归档持久保存，官方列表也不复活已归档会话");
    Check(restored.SetSessionArchived("id-1", project, false) &&
        restored.ProjectSessions(project, official, titles) is [{ Id: "id-1", Archived: false }],
        "恢复归档后重现原会话");
    Check(!Directory.EnumerateFiles(scratch, ".settings-*.tmp").Any(), "设置临时文件已清理");
    Throws<FileNotFoundException>(() => Cli.ValidatePath(Path.Combine(project, "bad.exe")));
    return Task.CompletedTask;
});

await Case("官方模型额度按组读取并区分零额度与缺失", () =>
{
    var quotas = ModelQuota.Parse("""
        {"status":"SUCCESS","num_turns":0,"usage":{"total_tokens":0},"command":{"name":"usage","data":{"groups":[
          {"name":"Gemini","buckets":[
            {"window":"5h","remaining_fraction":0,"reset_time":"2026-11-19T08:00:00Z"},
            {"window":"weekly","remaining_fraction":0.75}]},
          {"name":"Other models","buckets":[
            {"window":"monthly","name":"Monthly limit","remaining_fraction":-1,"reset_time":"unknown"}]}
        ]}}}
        """);
    Check(quotas is
    [
        { Group: "Gemini", Window: "5 小时额度", RemainingFraction: 0, ResetAt: not null },
        { Group: "Gemini", Window: "每周额度", RemainingFraction: 0.75, ResetAt: null },
        { Group: "Other models", Window: "Monthly limit", RemainingFraction: null, ResetAt: null }
    ], "模型名、窗口、零额度和无效值");
    Check(quotas[0].ResetAt == new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.Zero) &&
        quotas[0].ToString().Contains("重置时间") && quotas[2].ToString().Contains("剩余比例未提供"),
        "日期与可读描述");
    Check(ModelQuota.Parse("""{"status":"SUCCESS","command":{"name":"usage","data":{"groups":[]}}}""").Count == 0,
        "官方返回空额度清单");
    Throws<IOException>(() => ModelQuota.Parse("""{"status":"SUCCESS","response":"unstructured"}"""));
    Check(Cli.SupportsQuotaQuery("1.5.13") && !Cli.SupportsQuotaQuery("1.4.12") &&
        !Cli.SupportsQuotaQuery("版本未报告"), "仅向受支持的官方 CLI 请求查询");
    return Task.CompletedTask;
});

await Case("官方配置结构、文件差异与混淆字符", () =>
{
    using var data = JsonDocument.Parse("""
        {"configOptions":[
          {"id":"model","category":"model","type":"select","name":"Model","currentValue":"m2",
           "options":[{"group":"x","name":"Test","options":[{"value":"m2","name":"Gemini 4 Neon (High)"}]}]},
          {"id":"effort","category":"thought_level","type":"boolean","currentValue":true}
         ],"modes":{"currentModeId":"default","availableModes":[{"id":"default","name":"Default"}]}}
        """);
    var config = SessionOption.Parse(data.RootElement);
    Check(config.Count == 3 && config[0].Choices[0].Id == "m2" && config[1].Current == "true" &&
        config[1].IsBoolean && config[2].Method == "session/set_mode", "动态配置及标准模式");
    Check(config[0].Label == "模型" && config[0].Choices[0].Name == "Gemini 4 Neon (High)",
        "模型分类汉化且保留官方模型名");
    Check(config[2].Label == "权限模式" && config[2].Choices[0].Name == "默认（按规则询问）" &&
        config[2].Current == "default", "旧版权限模式也汉化并保留值");

    using var modern = JsonDocument.Parse("""
        {"configOptions":[
          {"id":"mode","name":"Mode","currentValue":"accept-edits","options":[
            {"value":"accept-edits","name":"Accept edits","description":"Original help"},
            {"value":"plan","name":"Plan"},
            {"value":"new-mode","name":"New mode","description":"New help"}]},
          {"id":"thought_level","category":"thought_level","name":"Thinking Level","currentValue":"high",
           "options":[{"value":"low","name":"Low"},{"value":"high","name":"High"},{"value":"xhigh","name":"Extra"}]}
        ],"modes":{"currentModeId":"default","availableModes":[{"id":"default","name":"Default"}]}}
        """);
    var localized = SessionOption.Parse(modern.RootElement);
    Check(localized.Count == 2 && localized[0] is { Id: "mode", Label: "权限模式", Category: "mode", Current: "accept-edits" },
        "以配置方式提供的权限模式优先且不重复显示");
    Check(localized[0].Choices[0] is { Id: "accept-edits", Name: "允许工作区文件编辑" } &&
        localized[0].Choices[0].Description?.Contains("CLI 的权限规则") == true &&
        localized[0].Choices[2] == new Choice("new-mode", "New mode", "New help"),
        "已知权限模式汉化，未来选项保留原文");
    Check(localized[1] is { Current: "high", Label: "推理等级" } &&
        localized[1].Choices.Select(choice => choice.Name).SequenceEqual(["低", "高", "超高"]) &&
        localized[1].Choices.Select(choice => choice.Id).SequenceEqual(["low", "high", "xhigh"]),
        "推理等级汉化且维持官方值");
    using var update = JsonDocument.Parse("""
        {"title":"文件\u202e操作","kind":"edit","status":"pending","rawOutput":{"exitCode":0},"content":[
          {"type":"diff","path":"A B.cs","oldText":"前","newText":"后"},
          {"type":"content","content":{"type":"text","text":"完成"}}]}
        """);
    var diff = AgentText.Changes(update.RootElement).Single();
    Check(diff.Path == "A B.cs" && diff.Before == "前" && diff.After == "后", "文件差异解析");
    var detail = AgentText.ToolDetails(update.RootElement);
    Check(detail.Contains("\\u202E") && detail.Contains("exitCode") && detail.Contains("完成"), "内容展开和转义");
    Check(detail.Contains("类型：编辑") && detail.Contains("状态：等待执行") &&
        new PermissionOffer("test", "Allow", "allow_once").ToString() == "仅本次允许", "审批详情及决定采用中文");
    return Task.CompletedTask;
});

await Case("真实子进程上的 JSON-RPC 模拟服务与会话回放", async () =>
{
    await using var client = StartFixture((_, _) => Task.FromResult<string?>(null));
    await client.InitializeAsync("0.1.0");
    Check(client.CanList && client.CanReplay, "能力协商");
    await client.AuthenticateGoogleAsync();
    var listed = await client.ListAsync();
    Check(listed.Count == 2 && listed[0].Id == "second" && listed[1].Id == "first", "分页及目录过滤");
    var advertised = new List<AcpCommand>();
    client.SessionUpdated += (_, update) =>
    {
        if (Value(update, "sessionUpdate") == "available_commands_update")
            advertised.AddRange(AcpCommand.Parse(update));
    };
    var session = await client.NewSessionAsync();
    Check(advertised is [{ Name: "context", InputHint: "Filter by section" }],
        "收到新建回复之前的动态命令及参数提示，忽略重复和非法名称");
    Check(advertised[0].InsertBefore("我的草稿") == ("/context 我的草稿", 9) &&
        advertised[0].InsertBefore("") == ("/context ", 9) &&
        advertised[0].InsertBefore("  原样保留") == ("/context  原样保留", 8), "填入命令时完整保留草稿");
    using var cleared = JsonDocument.Parse("""{"availableCommands":[]}""");
    Check(AcpCommand.Parse(cleared.RootElement).Count == 0, "空更新清除命令目录");
    string id = Value(session, "sessionId");
    var options = SessionOption.Parse(session);
    var model = options.Single(option => option.Category == "model");
    Check(model.Choices[0].Id == "dynamic-model" && model.Choices[0].Name == "Dynamic model", "模型来自 ACP 并保留原名");
    var response = await client.SetOptionAsync(id, model, model.Choices[0]);
    Check(SessionOption.Parse(response)[0].Current == "dynamic-model", "模型切换");
    var mode = options.Single(option => option.Category == "mode");
    var localizedMode = mode.Choices.Single(choice => choice.Name == "允许工作区文件编辑");
    var updatedMode = SessionOption.Parse(await client.SetOptionAsync(id, mode, localizedMode));
    Check(updatedMode.Single(option => option.Category == "mode").Current == "accept-edits", "中文权限模式发送原始协议值");
    var updates = new List<string>();
    client.SessionUpdated += (_, value) => updates.Add(Value(value, "sessionUpdate"));
    await client.RestoreAsync(id);
    Check(updates.SequenceEqual(["user_message_chunk", "agent_message_chunk"]), "加载期间接收会话回放");
    await client.CloseAsync(id);
});

await Case("ACP 统一提交会话设置，按模型刷新后验证推理等级", async () =>
{
    await using var client = StartFixture((_, _) => Task.FromResult<string?>(null), streaming: true);
    await client.InitializeAsync("0.1.0");
    var opened = await client.NewSessionAsync();
    string id = Value(opened, "sessionId");
    var options = SessionOption.Parse(opened);
    var changed = await client.ApplySettingsAsync(id, options,
        [new("model", "second-model", "模型"), new("mode", "accept-edits", "权限模式"), new("thought_level", "high", "推理等级")]);
    Check(changed.Applied.Count == 3 && changed.Problems.Count == 0 &&
        changed.Options.Select(option => option.Current).SequenceEqual(["second-model", "accept-edits", "high"]),
        "与子进程逐项交换真实 ACP 值，三项只确认一次");
    var limited = await client.ApplySettingsAsync(id, changed.Options,
        [new("model", "plain-model", "模型"), new("thought_level", "low", "推理等级")]);
    Check(limited.Applied.Count == 1 && limited.Problems.Single().Contains("重新选择") &&
        limited.Options.All(option => option.Category != "thought_level"), "模型变更后跳过不存在的推理选项，保留确认结果");
    int attempted = 0;
    var stopped = await SessionSettings.ApplyAsync(options,
        [new("model", "second-model", "模型"), new("mode", "accept-edits", "权限模式")], (_, _) =>
        {
            attempted++;
            return Task.FromException<IReadOnlyList<SessionOption>>(new IOException("连接中断"));
        });
    Check(attempted == 1 && stopped.Applied.Count == 0 && stopped.Problems[0].Contains("连接中断") &&
        ReferenceEquals(stopped.Options, options), "子请求失败后保留最后确认状态及原因");
});

await Case("跨重启记住新会话模式、模型和推理，缺少官方选项时安全回退", async () =>
{
    var folder = Directory.CreateDirectory(Path.Combine(scratch, "新会话默认-" + Guid.NewGuid().ToString("N"))).FullName;
    var store = new SettingsFile(folder);
    Check(store.Load().NewSessionDefaults is null, "首次启动沿用官方值");
    var preferred = new SessionDefaults("auto", "second-model", "high");
    store.Save(new LocalSettings { NewSessionDefaults = preferred });
    var restored = new SettingsFile(folder).Load().NewSessionDefaults!;
    Check(restored == preferred, "跨重启恢复独立的默认偏好");

    await using var client = StartFixture((_, _) => Task.FromResult<string?>(null), streaming: true);
    await client.InitializeAsync("0.1.0");
    for (int round = 0; round < 2; round++)
    {
        var opened = await client.NewSessionAsync();
        var options = SessionOption.Parse(opened);
        Check(SessionDefaults.ConfirmedValue(options, "mode") == "default", "CLI 每次都从官方默认开始");
        var applied = await client.ApplySettingsAsync(Value(opened, "sessionId"), options, restored.Changes());
        Check(applied.Problems.Count == 0 && applied.Applied.Count == 3 &&
            SessionDefaults.ConfirmedValue(applied.Options, "mode") == "auto" &&
            SessionDefaults.ConfirmedValue(applied.Options, "model") == "second-model" &&
            SessionDefaults.ConfirmedValue(applied.Options, "thought_level") == "high", "新会话复用三个官方值");
    }
    SessionOption[] restricted =
    [
        new("m", "模型", null, "model", "other", [new("other", "Other", null)], "session/set_config_option"),
        new("preset", "权限", null, "mode", "default", [new("default", "默认", null)], "session/set_mode"),
        new("effort", "推理", null, "thought_level", "low", [new("low", "低", null), new("high", "高", null)], "session/set_config_option")
    ];
    var unchanged = await SessionSettings.ApplyAsync(restricted, restored.Changes(), (_, _) =>
        throw new InvalidOperationException("不应提交未通告的模式或者不同模型的推理等级。"));
    Check(unchanged.Applied.Count == 0 && unchanged.Problems.Count == 3 &&
        unchanged.Problems.Any(problem => problem.Contains("当前模型")), "不可用的值不发给 CLI，可独立清除两组偏好");
    Check((preferred with { Mode = null }).Changes().All(change => change.Category != "mode") &&
        (preferred with { Model = null, Reasoning = null }).Changes() is [{ Category: "mode" }], "分别清除权限或模型组");
});

await Case("ACP 正文、推理和工具交错，保留稳定的生成中条目", async () =>
{
    await using var client = StartFixture((_, _) => Task.FromResult<string?>(null), streaming: true);
    await client.InitializeAsync("0.1.0");
    string id = Value(await client.NewSessionAsync(), "sessionId");
    var transcript = new ConversationTranscript();
    transcript.Add(MessageSpeaker.User, "开始");
    transcript.BeginReply();
    var reply = transcript.LiveReply!;
    Check(reply.IsGenerating && reply.ToString() == "Agent（生成中）", "等待首段时即可定位到回复占位");
    var reasoning = new ActivityRecord(id, DateTimeOffset.UnixEpoch, "本轮推理记录", "", TaskActivityKind.Reasoning);
    int tools = 0;
    client.SessionUpdated += (_, data) =>
    {
        var type = Value(data, "sessionUpdate");
        if (type == "agent_message_chunk")
            transcript.AppendReply(Value(data.GetProperty("content"), "text"), Value(data, "messageId"));
        else if (type == "agent_thought_chunk")
            reasoning.AppendDetail(Value(data.GetProperty("content"), "text"));
        else if (type == "tool_call")
            tools++;
    };
    await client.PromptAsync(id, "开始").WaitAsync(TimeSpan.FromSeconds(6));
    Check(ReferenceEquals(reply, transcript.LiveReply) && reply.Text == "目前已完成。" && reasoning.Detail == "先核对再验证" &&
        tools == 1 && reasoning.Matches(id, TaskActivityKind.Reasoning, ["再验"]), "真实消息交错时正文和推理仍分别完整拼接");
    transcript.FinishReply("无正文");
    Check(!reply.IsGenerating && reply.Label == "Agent" && transcript.Messages.Count == 2, "原条目就地转为普通回复");
    transcript.BeginReply();
    var empty = transcript.FinishReply("任务已中止，尚无正文。");
    Check(empty is { Speaker: MessageSpeaker.Notice, IsGenerating: false }, "中止或纯工具轮结束后清除生成中状态");
});

await Case("ACP 图片能力、纯图片消息、混合消息和大小限制", async () =>
{
    var bytes = new byte[] { 0x89, 0x50, 0x4e, 0x47 };
    var png = new PromptImage("image/png", bytes);
    bytes[0] = 0;
    await using var capable = StartFixture((_, _) => Task.FromResult<string?>(null));
    await capable.InitializeAsync("0.1.0");
    Check(capable.CanSendImages, "识别官方图片能力");
    var id = Value(await capable.NewSessionAsync(), "sessionId");
    Check(Value(await capable.PromptAsync(id, "", [png]), "stopReason") == "end_turn" &&
        Value(await capable.PromptAsync(id, "图片说明", [png]), "stopReason") == "end_turn",
        "合成图片以快照和正确的 MIME、Base64 与顺序发送");
    await ThrowsAsync<ArgumentException>(() => capable.PromptAsync(id, "", []));
    await ThrowsAsync<ArgumentException>(() => capable.PromptAsync(id, "", Enumerable.Repeat(png, 9).ToArray()));
    var large = new PromptImage("image/png", new byte[PromptImage.MaxFileBytes]);
    await ThrowsAsync<ArgumentException>(() => capable.PromptAsync(id, "", [large, large]));
    Throws<ArgumentException>(() => new PromptImage("text/plain", bytes));
    Throws<ArgumentException>(() => new PromptImage("image/png", []));

    await using var unsupported = StartFixture((_, _) => Task.FromResult<string?>(null), supportsImages: false);
    await unsupported.InitializeAsync("0.1.0");
    Check(!unsupported.CanSendImages, "未协商图片能力时禁用");
    await ThrowsAsync<InvalidOperationException>(() => unsupported.PromptAsync(id, "", [png]));
});

await Case("项目与会话启动恢复兼容旧数据，跳过归档与失效路径", () =>
{
    var first = Directory.CreateDirectory(Path.Combine(scratch, "启动项目甲")).FullName;
    var second = Directory.CreateDirectory(Path.Combine(scratch, "启动项目乙")).FullName;
    var missing = Path.Combine(scratch, "已移走项目");
    var preferences = new LocalSettings { Projects = [missing, first, second] };
    preferences.RememberSession("old", first, DateTimeOffset.UnixEpoch);
    preferences.RememberSession("new", first, DateTimeOffset.UnixEpoch.AddDays(1));
    preferences.RememberSession("other", second, DateTimeOffset.UnixEpoch.AddDays(2));
    Check(preferences.StartupWorkspace() == (first, "new"), "旧版记录回到最近有效项目和最新会话");
    preferences.LastProject = second;
    preferences.LastSession = "other";
    Check(preferences.StartupWorkspace() == (second, "other"), "优先恢复精确的项目和会话");
    preferences.SetSessionArchived("other", second, true);
    Check(preferences.StartupWorkspace() == (second, (string?)null), "跳过归档会话");
    preferences.RestoreSessionOnStartup = false;
    preferences.LastProject = missing;
    Check(preferences.StartupWorkspace() == (first, (string?)null), "关闭自动恢复后仍加载本机历史所属项目");
    return Task.CompletedTask;
});

await Case("单次允许、拒绝默认、取消在途审批", async () =>
{
    var approvalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var inviteCount = 0;
    await using var client = StartFixture(async (request, token) =>
    {
        Check(request.Options.Single(offer => offer.Kind == "allow_always").Id == "allow-all", "保留官方提供的记住决定选项");
        Check(request.Description.Contains("src/测试.cs") && request.Reject() == "deny", "可读的实际审批详情");
        int invite = Interlocked.Increment(ref inviteCount);
        if (invite == 1)
            return request.Reject();
        if (invite == 2)
            return request.Options.Single(option => option.Kind == "allow_once").Id;

        approvalStarted.TrySetResult();
        await Task.Delay(Timeout.Infinite, token);
        return "approve";
    });
    await client.InitializeAsync("0.1.0");
    var id = Value(await client.NewSessionAsync(), "sessionId");
    var first = await client.PromptAsync(id, "请拒绝").WaitAsync(TimeSpan.FromSeconds(6));
    Check(Value(first, "stopReason") == "end_turn", "拒绝已到达服务端");
    var allowed = await client.PromptAsync(id, "请允许").WaitAsync(TimeSpan.FromSeconds(6));
    Check(Value(allowed, "stopReason") == "end_turn", "只有单次允许选项到达服务端");

    var second = client.PromptAsync(id, "请中止");
    await approvalStarted.Task.WaitAsync(TimeSpan.FromSeconds(6));
    await client.CancelAsync(id);
    Check(Value(await second.WaitAsync(TimeSpan.FromSeconds(6)), "stopReason") == "cancelled", "取消通知与取消审批均已到达");

    using var invalid = JsonDocument.Parse("{}");
    Check(JsonSerializer.Serialize(PermissionRequest.Parse(invalid.RootElement).Answer("unknown")) ==
        """{"outcome":{"outcome":"cancelled"}}""", "陌生审批选项不放行");
});

await Case("异常退出时结束等待，下一次连接可恢复", async () =>
{
    await using (var client = StartFixture((_, _) => Task.FromResult<string?>(null)))
    {
        await client.InitializeAsync("0.1.0");
        var id = Value(await client.NewSessionAsync(), "sessionId");
        await ThrowsAsync<IOException>(() => client.PromptAsync(id, "exit").WaitAsync(TimeSpan.FromSeconds(6)));
        Check(!client.Connected, "退出后的连接状态");
    }

    await using var next = StartFixture((_, _) => Task.FromResult<string?>(null));
    await next.InitializeAsync("0.1.0");
    await next.RestoreAsync("first");
    Check(next.Connected, "重新建立连接");
});

await Case("Stable/Beta 更新、SHA-256 和失败恢复", async () =>
{
    Check(ReleaseVersion.Parse("0.1.0-beta.10").CompareTo(ReleaseVersion.Parse("0.1.0-beta.2")) > 0, "数字预发布排序");
    Check(ReleaseVersion.Parse("0.1.0").CompareTo(ReleaseVersion.Parse("0.1.0-beta.10")) > 0, "正式版本优先");
    Throws<FormatException>(() => ReleaseVersion.Parse("0.1.0-beta.01"));
    byte[] payload = Encoding.UTF8.GetBytes("test installer payload");
    string sha = Convert.ToHexString(SHA256.HashData(payload));
    const string root = "https://github.com/wangz0976-oss/MyAntigravity/releases/download/";
    const string installer = "MyAntigravity-Setup.exe";
    var beta = root + "v0.2.0-beta.1/";
    var stable = root + "v0.1.0/";
    string Manifest(string version, string channel, string url) => JsonSerializer.Serialize(new
    {
        version, channel, downloadUrl = url + installer, sha256 = sha, notes = "更新说明"
    });
    var releases = JsonSerializer.Serialize(new[]
    {
        new { tag_name = "v0.1.0", prerelease = false, assets = Assets(stable, "stable") },
        new { tag_name = "v0.2.0-beta.1", prerelease = true, assets = Assets(beta, "beta") }
    });
    using var http = new HttpClient(new FakeHttp(url =>
    {
        if (url.Host == "api.github.com")
            return new StringContent(releases);
        return url.AbsoluteUri switch
        {
            var value when value == beta + "update-beta.json" => new StringContent(Manifest("0.2.0-beta.1", "beta", beta)),
            var value when value == stable + "update-stable.json" => new StringContent(Manifest("0.1.0", "stable", stable)),
            _ => new ByteArrayContent(payload)
        };
    }));
    var updater = new Updates(http, scratch);
    var found = await updater.CheckAsync("beta", "0.1.0", CancellationToken.None);
    Check(found is { Version: "0.2.0-beta.1", Notes: "更新说明" }, "Beta 检查");
    var foundStable = await updater.CheckAsync("stable", "0.1.0-beta.1", CancellationToken.None);
    Check(foundStable is { Version: "0.1.0" }, "Stable 检查");
    const string feed = """
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry><link href="https://example.org/releases/tag/v9.0.0-beta.9" /></entry>
          <entry><link href="https://github.com/wangz0976-oss/MyAntigravity/releases/tag/v0.2.0-beta.2" /></entry>
          <entry><link href="https://github.com/wangz0976-oss/MyAntigravity/releases/tag/v0.2.0-beta.1" /></entry>
          <entry><link href="https://github.com/wangz0976-oss/MyAntigravity/releases/tag/v0.1.0" /></entry>
        </feed>
        """;
    using var limitedHttp = new HttpClient(new FakeHttp(url => url.AbsoluteUri switch
    {
        "https://github.com/wangz0976-oss/MyAntigravity/releases.atom" => new StringContent(feed),
        var value when value == beta + "update-beta.json" => new StringContent(Manifest("0.2.0-beta.1", "beta", beta)),
        var value when value == stable + "update-stable.json" => new StringContent(Manifest("0.1.0", "stable", stable)),
        _ => new StringContent("")
    }, url => url.Host == "api.github.com" ? HttpStatusCode.Forbidden :
        url.AbsolutePath.Contains("v0.2.0-beta.2", StringComparison.Ordinal) ? HttpStatusCode.NotFound : HttpStatusCode.OK));
    var fallback = new Updates(limitedHttp, scratch);
    Check(await fallback.CheckAsync("beta", "0.1.0", CancellationToken.None) == found,
        "GitHub API 限流时使用同一仓库的公开发布订阅，跳过没有更新清单的标签");
    Check(await fallback.CheckAsync("stable", "0.1.0", CancellationToken.None) is null, "订阅回退保留渠道和版本比较");
    const string assetsApi = "https://api.github.com/repos/wangz0976-oss/MyAntigravity/releases/assets/";
    var releaseWithIds = JsonSerializer.Serialize(new[]
    {
        new
        {
            tag_name = "v0.2.0-beta.1", prerelease = true, assets = new[]
            {
                new { id = 401, name = "update-beta.json", browser_download_url = beta + "update-beta.json" },
                new { id = 402, name = installer, browser_download_url = beta + installer }
            }
        }
    });
    var fetchedIds = new List<string>();
    using var limitedAssets = new HttpClient(new FakeHttp(url => url.AbsoluteUri switch
    {
        assetsApi + "401" => new StringContent(Manifest("0.2.0-beta.1", "beta", beta)),
        assetsApi + "402" => new ByteArrayContent(payload),
        _ => new StringContent(releaseWithIds)
    }, url => url.AbsoluteUri == beta + "update-beta.json" ? HttpStatusCode.Forbidden
        : url.AbsoluteUri == beta + installer ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK, request =>
    {
        if (request.RequestUri!.AbsoluteUri.StartsWith(assetsApi, StringComparison.Ordinal))
        {
            Check(request.Headers.Accept.Single().MediaType == "application/octet-stream" &&
                request.Headers.Authorization is null, "备用路径只请求公开资产内容");
            fetchedIds.Add(request.RequestUri.Segments[^1]);
        }
    }));
    var assetUpdate = new Updates(limitedAssets, scratch);
    var alternate = await assetUpdate.CheckAsync("beta", "0.1.0", CancellationToken.None);
    Check(alternate?.Version == "0.2.0-beta.1" && Updates.PublicReleasePage(alternate).AbsoluteUri.EndsWith("/tag/v0.2.0-beta.1"),
        "更新清单直链受限时可读取同一发布的公开资产，手动入口对准目标版本");
    Check(File.ReadAllBytes(await assetUpdate.DownloadAsync(alternate!, new Progress<int?>(), CancellationToken.None))
        .SequenceEqual(payload) && fetchedIds.SequenceEqual(["401", "402"]), "安装包直链受限时仍校验备用下载的 SHA-256");

    using var feedBlocked = new HttpClient(new FakeHttp(_ => new StringContent("blocked"), _ => HttpStatusCode.Forbidden));
    try
    {
        await new Updates(feedBlocked, scratch).CheckAsync("beta", "0.1.0", CancellationToken.None);
        throw new InvalidOperationException("访问受限的检查未报告失败。");
    }
    catch (HttpRequestException error)
    {
        Check(error.StatusCode == HttpStatusCode.Forbidden && error.Message.Contains("读取备用发布订阅") &&
            error.Message.Contains("Alt+O"), "排除两条查找路径后告知失败步骤和手动下载入口");
    }
    Check(await updater.CheckAsync("stable", "0.1.0", CancellationToken.None) is null, "防止降级");
    var path = await updater.DownloadAsync(found!, new Progress<int?>(), CancellationToken.None);
    Check(File.ReadAllBytes(path).SequenceEqual(payload), "校验后的下载落盘");

    await ThrowsAsync<IOException>(() => updater.DownloadAsync(found! with { Sha256 = new string('0', 64) },
        new Progress<int?>(), CancellationToken.None));
    Check(File.ReadAllBytes(path).SequenceEqual(payload) &&
        !Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(), "失败清理且保留已校验文件");
    Throws<FormatException>(() => UpdateManifest.Parse(Manifest("0.2.0-beta.1", "beta", "http://example.org/"), "beta"));
    Throws<FormatException>(() => UpdateManifest.Parse(Manifest("0.2.0-beta.1", "beta", beta), "stable"));

    static object[] Assets(string directory, string channel) =>
        new[] { $"update-{channel}.json", installer }.Select(name => (object)new
        {
            name, browser_download_url = directory + name
        }).ToArray();
});

await Case("账户核验、需要登录及连接失败", async () =>
{
    await using var loggedIn = StartFixture((_, _) => Task.FromResult<string?>(null), account: "verified");
    var verified = await AccountVerification.CheckAsync(loggedIn, "test.exe", "0.1.0");
    Check(!verified.RequiresLogin && verified.Tier == "Test tier", "成功核验及官方套餐");
    await using var loggedOut = StartFixture((_, _) => Task.FromResult<string?>(null), account: "required");
    var required = await AccountVerification.CheckAsync(loggedOut, "test.exe", "0.1.0");
    Check(required.RequiresLogin && required.Tier is null, "明确认证错误识别");
    await using var failed = StartFixture((_, _) => Task.FromResult<string?>(null), account: "failed");
    await ThrowsAsync<AcpException>(() => AccountVerification.CheckAsync(failed, "test.exe", "0.1.0"));
    var legacy = Directory.CreateDirectory(Path.Combine(scratch, "账户核验")).FullName;
    File.WriteAllText(Path.Combine(legacy, "settings.json"), """{"Announce":false,"Projects":[],"Sessions":[]}""");
    var file = new SettingsFile(legacy);
    Check(file.Load().LastAccountVerification is null, "旧设置的默认状态");
    file.Save(new LocalSettings { LastAccountVerification = verified });
    Check(file.Load().LastAccountVerification == verified, "重启后保留核验时间和来源");
});

await Case("逐段差异、审批内差异、任务计划与会话用量", () =>
{
    var sections = DiffSections.Build(new FileChange("a", "上\r\n旧一\r\n中\r\n旧二\r\n下", "上\n新一\n中\n新二\n下"));
    Check(sections is [{ OldLine: 2, NewLine: 2 }, { OldLine: 4, NewLine: 4 }] &&
        sections[0].Removed.SequenceEqual(["旧一"]) && sections[1].Added.SequenceEqual(["新二"]), "逐段定位");
    Check(DiffSections.Build(new FileChange("added", null, "新增")) is
        [{ Removed.Length: 0, Added: ["新增"] }], "新增文件不虚构原文");
    Check(DiffSections.Build(new FileChange("removed", "删除", "")) is
        [{ Removed: ["删除"], Added.Length: 0 }], "删除末尾文本");
    var many = Enumerable.Range(0, 3200).Select(index => $"唯一行 {index}").ToArray();
    var edited = many.ToArray();
    edited[18] = edited[3100] = "替换";
    Check(DiffSections.Build(new FileChange("large", string.Join('\n', many), string.Join('\n', edited))) is
        [{ OldLine: 19 }, { OldLine: 3101 }], "大文件远距离改动可逐段跳转");
    using var data = JsonDocument.Parse("""
        {"toolCall":{"content":[{"type":"diff","path":"readme.txt","oldText":"旧","newText":"新"}]},
         "entries":[{"content":"核对","priority":"medium","status":"in_progress"}],
         "used":10,"size":200,"usage":{"inputTokens":12,"outputTokens":4,"thoughtTokens":0,"totalTokens":16}}
        """);
    Check(PermissionRequest.Parse(data.RootElement).Changes is [{ Path: "readme.txt" }], "审批带结构化差异");
    Check(PlanStep.Parse(data.RootElement).Single().ToString() == "进行中，中优先级。核对", "任务计划状态中文化");
    Check(SessionUsage.Context(data.RootElement)?.Contains("10 / 200") == true &&
        SessionUsage.LastTurn(data.RootElement)?.Contains("输入 12，输出 4，推理 0，合计 16") == true, "按原样展示官方用量字段");
    Check(SessionUsage.Context(default) is null && SessionUsage.LastTurn(default) is null, "缺失时不显示虚构用量");
    return Task.CompletedTask;
});

await Case("移除项目与删除本机会话记录", () =>
{
    var directory = Directory.CreateDirectory(Path.Combine(scratch, "清理列表测试")).FullName;
    var project = Directory.CreateDirectory(Path.Combine(directory, "项目一")).FullName;
    var other = Directory.CreateDirectory(Path.Combine(directory, "项目二")).FullName;
    var sentinel = Path.Combine(project, "保留.txt");
    File.WriteAllText(sentinel, "原文件");
    var settings = new LocalSettings();
    settings.RememberProject(project);
    settings.RememberProject(other);
    settings.RememberSession("local", project);
    settings.RememberSession("archive", project);
    settings.SetSessionArchived("archive", project, true);
    settings.RememberSession("local", other);

    Check(settings.RemoveProject(project) && !settings.RemoveProject(project) && settings.Projects.SequenceEqual([other]),
        "只移除项目列表指定条目");
    Check(File.ReadAllText(sentinel) == "原文件" && settings.FindSession("local", project) is not null,
        "项目文件和会话都保留");
    settings.RememberProject(project);
    Check(settings.ProjectSessions(project, [], new Dictionary<string, string?>()) is [{ Id: "local" }],
        "重新添加项目显示原有未归档历史");

    Check(settings.RemoveSession("local", project) && settings.RemoveSession("archive", project) &&
        settings.RemoveSession("official", project) && !settings.RemoveSession("local", project), "删除活动、归档和仅官方列出的记录");
    var file = new SettingsFile(directory);
    file.Save(settings);
    settings = file.Load();
    settings.RememberSession("local", project);
    var remote = new[] { "local", "archive", "official" }.Select(id =>
        new LocalSession(id, project, "官方会话", DateTimeOffset.Now)).ToArray();
    var titles = new Dictionary<string, string?>();
    Check(settings.ProjectSessions(project, remote, titles).Count == 0 &&
        settings.ProjectSessions(project, remote, titles, archived: true).Count == 0, "重新读取及刷新后不复活记录");
    Check(settings.ProjectSessions(other, [], titles) is [{ Id: "local" }] && File.Exists(sentinel),
        "同名会话标识在其他项目中保留");
    return Task.CompletedTask;
});

await Case("文本附件快照与附加工作目录", async () =>
{
    const string script = "标记 = '测试通过'\r\n";
    var directory = Directory.CreateDirectory(Path.Combine(scratch, "附加测试目录")).FullName;
    var textSource = Path.Combine(directory, "probe.py");
    File.WriteAllText(textSource, script, Encoding.Unicode);
    var snapshot = await PromptTextFile.LoadAsync(textSource, PromptTextFile.MaxPromptBytes, CancellationToken.None);
    File.WriteAllText(textSource, "磁盘原文件已变化");
    Check(snapshot.Text == script && snapshot.Name == "probe.py" && snapshot.Length == Encoding.UTF8.GetByteCount(script),
        "带 BOM 的文件转码成 ACP 文本快照");
    await ThrowsAsync<IOException>(() => PromptTextFile.LoadAsync(textSource, 1, CancellationToken.None));
    var binary = Path.Combine(directory, "binary.bin");
    File.WriteAllBytes(binary, [0xff, 0xd8, 0x00, 0x03]);
    await ThrowsAsync<IOException>(() => PromptTextFile.LoadAsync(binary, PromptTextFile.MaxPromptBytes, CancellationToken.None));
    Throws<ArgumentException>(() => new PromptTextFile(@"反斜杠\probe.py", script));
    Throws<ArgumentException>(() => new PromptTextFile("probe.py", "binary\0"));

    var file = new SettingsFile(directory);
    var settings = new LocalSettings();
    settings.SetProjectDirectories(scratch, [scratch, directory, directory + Path.DirectorySeparatorChar]);
    file.Save(settings);
    Check(file.Load().DirectoriesForProject(scratch).SequenceEqual([directory]) &&
        file.Load().DirectoriesForProject(directory).Count == 0, "附加列表按项目持久化和去重");

    await using var client = StartFixture((_, _) => Task.FromResult<string?>(null),
        question: (_, _) => Task.FromResult(ElicitationResponse.Cancelled));
    await client.InitializeAsync("0.1.0");
    Check(client.CanSendTextFiles && client.CanAddDirectories, "能力由官方协商");
    await client.NewSessionAsync(additionalDirectories: [directory]);
    await client.RestoreAsync("demo", [directory]);
    Check(Value(await client.PromptAsync("demo", "", [], [snapshot]), "stopReason") == "end_turn", "纯文本附件可发送");
    Check(Value(await client.PromptAsync("demo", "代码说明", [], [snapshot]), "stopReason") == "end_turn", "混合消息可发送");
    await using var older = StartFixture((_, _) => Task.FromResult<string?>(null));
    await older.InitializeAsync("0.1.0");
    await ThrowsAsync<InvalidOperationException>(() => older.PromptAsync("demo", "", [], [snapshot]));
    await ThrowsAsync<InvalidOperationException>(() => older.NewSessionAsync(additionalDirectories: [directory]));
});

await Case("ACP 提问提交、拒绝、取消与能力边界", async () =>
{
    var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    string outcome = "accept";
    int questions = 0;
    await using var client = StartFixture((_, _) => Task.FromResult<string?>(null), question: async (request, cancellation) =>
    {
        questions++;
        Check(request.SessionId == "demo" && request.AgentName == "测试 Agent" && request.Fields.Count == 4,
            "提问绑定会话并识别来源");
        if (outcome == "cancel")
        {
            received.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellation);
        }
        if (outcome == "decline")
            return new ElicitationResponse("decline");
        var count = request.Fields.Single(field => field.Key == "count");
        var extras = request.Fields.Single(field => field.Key == "extras");
        Check(count.TryAnswer("2", out var valid, out _) && !count.TryAnswer("0", out _, out _) &&
            !count.TryAnswer("2.2", out _, out _) &&
            !extras.TryAnswer(new[] { "mint", "lemon" }, out _, out _), "表单字段验证");
        return new ElicitationResponse("accept", new Dictionary<string, object>
            { ["choice"] = "tea", ["confirmed"] = false, ["count"] = valid! });
    });
    await client.InitializeAsync("0.1.0");
    await client.NewSessionAsync(additionalDirectories: [Path.Combine(scratch, "附加测试目录")]);
    await client.PromptAsync("demo", "question:accept");
    outcome = "decline";
    await client.PromptAsync("demo", "question:decline");
    outcome = "cancel";
    var turn = client.PromptAsync("demo", "question:cancel");
    await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await client.CancelAsync("demo");
    Check(Value(await turn.WaitAsync(TimeSpan.FromSeconds(5)), "stopReason") == "cancelled", "取消时表单解除等待");
    await client.PromptAsync("demo", "question:unsupported");
    Check(questions == 3, "未声明的 URL 模式不显示");
});

await Case("运行中追加、并发响应顺序与中止", async () =>
{
    await using var client = StartFixture((_, _) => Task.FromResult<string?>(null), steering: true);
    await client.InitializeAsync("0.1.0");
    Check(client.CanSteer, "确认官方适配器版本");
    await ThrowsAsync<InvalidOperationException>(() => client.SteerAsync("demo", "无任务时的追加"));
    var signal = NewSignals();
    client.SessionUpdated += (_, update) =>
    {
        if (Value(update, "sessionUpdate") is { } kind && signal.TryGetValue(kind, out var ready))
            ready.TrySetResult();
    };
    var turn = client.PromptAsync("demo", "开始任务");
    await Wait("fixture_started");
    await ThrowsAsync<InvalidOperationException>(() => client.SteerAsync("other", "不同会话"));
    var addition = client.SteerAsync("demo", "正常追加");
    await Wait("fixture_primary_finished");
    Check(!turn.IsCompleted, "主请求先返回时任务仍等待追加响应");
    await addition.WaitAsync(TimeSpan.FromSeconds(5));
    Check(SessionUsage.LastTurn(await turn.WaitAsync(TimeSpan.FromSeconds(5)))?.Contains("输出 42") == true, "采用最终用量");

    signal = NewSignals();
    turn = client.PromptAsync("demo", "开始任务");
    await Wait("fixture_started");
    await ThrowsAsync<AcpException>(() => client.SteerAsync("demo", "被拒绝的追加"));
    Check(!turn.IsCompleted, "追加失败不影响原任务");
    addition = client.SteerAsync("demo", "中止前追加");
    await Wait("fixture_followup_started");
    await ThrowsAsync<InvalidOperationException>(() => client.SteerAsync("demo", "重复并发追加"));
    await client.CancelAsync("demo");
    Check(Value(await turn.WaitAsync(TimeSpan.FromSeconds(5)), "stopReason") == "cancelled" &&
        Value(await addition.WaitAsync(TimeSpan.FromSeconds(5)), "stopReason") == "cancelled", "一次中止结束整个活动任务");

    static Dictionary<string, TaskCompletionSource> NewSignals() =>
        new[] { "fixture_started", "fixture_primary_finished", "fixture_followup_started" }
            .ToDictionary(name => name, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    Task Wait(string key) => signal[key].Task.WaitAsync(TimeSpan.FromSeconds(5));
});

await Case("技能来源与 MCP 配置的安全显示", () =>
{
    var catalogHome = Directory.CreateDirectory(Path.Combine(scratch, "扩展测试")).FullName;
    var root = Directory.CreateDirectory(Path.Combine(catalogHome, "project")).FullName;
    Directory.CreateDirectory(Path.Combine(root, ".git"));
    var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
    var profile = Directory.CreateDirectory(Path.Combine(catalogHome, "profile")).FullName;
    var localSkill = Directory.CreateDirectory(Path.Combine(root, ".agents", "skills", "verify-work")).FullName;
    var globalSkill = Directory.CreateDirectory(Path.Combine(profile, ".gemini", "config", "skills", "config-check")).FullName;
    File.WriteAllText(Path.Combine(localSkill, "SKILL.md"), "---\nname: verify-work\ndescription: 读取说明\n---\n正文");
    File.WriteAllText(Path.Combine(globalSkill, "SKILL.md"), "---\nname: config-check\ndescription: >\n  第一行\n  第二行\n---\n正文");
    var catalog = SkillCatalog.Read([nested], profile, [new CliPlugin("sample", "1.0", false) { Skills = ["bundled-note"] }]);
    Check(catalog.Count == 3 && catalog.Any(skill => skill.Name == "bundled-note" && !skill.Enabled) &&
        catalog.Any(skill => skill.Name == "config-check" && skill.Description == "第一行 第二行") &&
        SkillCatalog.Preview(catalog.Single(skill => skill.Name == "verify-work").FilePath!).Contains("正文"),
        "技能从工作区仓库根、本机全局和插件取得");
    File.WriteAllText(Path.Combine(root, ".agents", "mcp_config.json"), """
        {"mcpServers":{"local-docs":{"serverUrl":"https://example.invalid/private?token=secret-fixture","disabled":true}}}
        """);
    var workspace = McpServer.ReadWorkspace(root).Single();
    var global = McpServer.ParseCliList("NAME        TYPE   STATUS    COMMAND/URL\r\nsample      http   enabled   https://example.invalid/?token=secret-fixture\r\n").Single();
    Check(!workspace.IsGlobal && !workspace.CanChange && !workspace.Enabled && global.IsGlobal && global.CanChange && global.Enabled &&
        !workspace.ToString().Contains("secret-fixture") && !global.ToString().Contains("secret-fixture"),
        "项目声明和可编辑的 CLI 全局开关独立，列表只展示名称和状态");
    Check(McpServer.ParseCliList("No MCP servers configured.").Count == 0, "官方 CLI 空状态");
    Throws<IOException>(() => McpServer.ParseCliList("unexpected"));
    return Task.CompletedTask;
});

Console.WriteLine($"全部 {testCount} 组本地测试通过。");

AcpAgent StartFixture(Func<PermissionRequest, CancellationToken, Task<string?>> approve, bool supportsImages = true,
    string? account = null, Func<ElicitationRequest, CancellationToken, Task<ElicitationResponse>>? question = null,
    bool steering = false, bool streaming = false)
{
    string binary = Environment.ProcessPath ?? throw new InvalidOperationException("未找到测试进程路径。");
    var start = Cli.StartInfo(binary, scratch, redirect: true);
    if (Path.GetFileNameWithoutExtension(binary) == "dotnet")
        start.ArgumentList.Add(typeof(Fixture).Assembly.Location);
    if (account is null)
        start.ArgumentList.Add(streaming ? "--fixture-streaming" : steering ? "--fixture-steering" : question is not null ? "--fixture-rich"
            : supportsImages ? "--fixture" : "--fixture-no-images");
    else
    {
        start.ArgumentList.Add("--fixture-account");
        start.ArgumentList.Add(account);
    }
    return new AcpAgent(start, approve, question);
}

async Task LiveAsync(string binary)
{
    var isolated = Path.Combine(scratch, "官方 ACP 空目录");
    Directory.CreateDirectory(isolated);
    var version = await Cli.VersionAsync(binary, isolated);
    string? sessionId = null;
    var collected = new StringBuilder();
    await using var client = new AcpAgent(Cli.StartInfo(binary, isolated, true, "acp",
        "--disable-telemetry", "--log-file", "NUL"), (request, _) => Task.FromResult(request.Reject()));
    client.SessionUpdated += (_, value) =>
    {
        if (Value(value, "sessionUpdate") == "agent_message_chunk" && value.TryGetProperty("content", out var content))
            collected.Append(Value(content, "text"));
    };
    try
    {
        await client.InitializeAsync("0.1.0-beta.1");
        Check(client.CanReplay, "官方会话回放能力");
        var data = await client.NewSessionAsync();
        sessionId = Value(data, "sessionId");
        var options = SessionOption.Parse(data);
        var model = options.Single(option => option.Category == "model");
        var current = model.Choices.Single(choice => choice.Id == model.Current);
        var updated = SessionOption.Parse(await client.SetOptionAsync(sessionId, model, current));
        Check(updated.Any(option => option.Category == "model" && option.Choices.Count > 0), "官方动态模型配置");
        Console.WriteLine($"官方 CLI {version}，已协商 ACP、建会话并读取动态选项。");

        const string expected = "MYANTIGRAVITY_OK";
        var reply = await client.PromptAsync(sessionId,
            "这是连通测试。只回复 MYANTIGRAVITY_OK，不要调用工具、访问本地项目或执行命令。")
            .WaitAsync(TimeSpan.FromSeconds(75));
        Check(Value(reply, "stopReason") == "end_turn" && collected.ToString().Contains(expected), "收到真实流式回复");
        Console.WriteLine("官方 ACP 流式回复通过。");
        await client.CloseAsync(sessionId);
        collected.Clear();
        await client.RestoreAsync(sessionId);
        Check(collected.ToString().Contains(expected), "官方会话回放");
        Console.WriteLine("官方 ACP 历史回放通过。");
    }
    finally
    {
        if (sessionId is not null && client.Connected)
            await client.CloseAsync(sessionId);
    }
}

async Task Case(string name, Func<Task> test)
{
    await test();
    testCount++;
    Console.WriteLine($"通过 {name}");
}

static void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException($"测试失败：{name}");
}

static void Throws<T>(Action test) where T : Exception
{
    try { test(); }
    catch (T) { return; }
    throw new InvalidOperationException($"测试应返回 {typeof(T).Name}。");
}

static async Task ThrowsAsync<T>(Func<Task> test) where T : Exception
{
    try { await test(); }
    catch (T) { return; }
    throw new InvalidOperationException($"测试应返回 {typeof(T).Name}。");
}

static string Value(JsonElement element, string key) => element.TryGetProperty(key, out var field)
    ? field.GetString() ?? "" : "";

sealed class FakeHttp(Func<Uri, HttpContent> get, Func<Uri, HttpStatusCode>? status = null,
    Action<HttpRequestMessage>? observe = null) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        observe?.Invoke(request);
        return Task.FromResult(new HttpResponseMessage(status?.Invoke(request.RequestUri!) ?? HttpStatusCode.OK)
            { Content = get(request.RequestUri!), RequestMessage = request });
    }
}
