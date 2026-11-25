using System.Text.Json;

namespace MyAntigravity;

public sealed record LocalSession(string Id, string Directory, string? Title, DateTimeOffset Updated,
    bool Archived = false, bool Pinned = false)
{
    public override string ToString() =>
        (Pinned ? "已置顶 · " : "") + (string.IsNullOrWhiteSpace(Title)
            ? $"会话 · {Updated.ToLocalTime():g} · {Id}"
            : $"{AgentText.Visible(Title)} · {Updated.ToLocalTime():g}");
}

public sealed record RemovedSession(string Id, string Directory);
public sealed record ProjectDirectory(string Project, string Directory);
public sealed record SessionWorkspace(string Session, string Directory, string Draft, ReadingBookmark? Bookmark);

public sealed class LocalSettings
{
    public string? Executable { get; set; }
    public bool Announce { get; set; } = true;
    public bool EnterToSend { get; set; }
    public bool AutoReadReply { get; set; }
    public bool Sandbox { get; set; }
    public string UpdateChannel { get; set; } = "beta";
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public bool RestoreSessionOnStartup { get; set; } = true;
    public string? LastProject { get; set; }
    public string? LastSession { get; set; }
    public SessionDefaults? NewSessionDefaults { get; set; }
    public AccountVerification? LastAccountVerification { get; set; }
    public List<string> Projects { get; set; } = [];
    public List<LocalSession> Sessions { get; set; } = [];
    public List<RemovedSession> RemovedSessions { get; set; } = [];
    public List<ProjectDirectory> AdditionalDirectories { get; set; } = [];
    public List<SessionWorkspace> SessionWorkspaces { get; set; } = [];

    public SessionWorkspace? FindWorkspace(string session, string directory) =>
        SessionWorkspaces.Find(item => item.Session == session && PathEquals(item.Directory, directory));

    public bool SaveDraft(string session, string directory, string draft) =>
        SaveWorkspace(session, directory, draft, FindWorkspace(session, directory)?.Bookmark);

    public bool SaveBookmark(string session, string directory, ReadingBookmark bookmark) =>
        SaveWorkspace(session, directory, FindWorkspace(session, directory)?.Draft ?? "", bookmark);

    private bool SaveWorkspace(string session, string directory, string draft, ReadingBookmark? bookmark)
    {
        if (IsSessionRemoved(session, directory))
            return false;
        int index = SessionWorkspaces.FindIndex(item => item.Session == session && PathEquals(item.Directory, directory));
        if (draft.Length == 0 && bookmark is null)
        {
            if (index < 0)
                return false;
            SessionWorkspaces.RemoveAt(index);
            return true;
        }
        if (index >= 0 && SessionWorkspaces[index] is { } previous)
        {
            if (previous.Draft == draft && previous.Bookmark == bookmark)
                return false;
            SessionWorkspaces[index] = previous with { Draft = draft, Bookmark = bookmark };
        }
        else
            SessionWorkspaces.Add(new SessionWorkspace(session, directory, draft, bookmark));
        return true;
    }

    public void RememberProject(string directory)
    {
        Projects.RemoveAll(path => PathEquals(path, directory));
        Projects.Insert(0, directory);
        if (Projects.Count > 30)
            Projects.RemoveAt(30);
    }

    public void RememberSession(string id, string directory, DateTimeOffset? updated = null)
    {
        if (IsSessionRemoved(id, directory))
            return;
        var saved = FindSession(id, directory);
        Sessions.RemoveAll(session => session.Id == id && PathEquals(session.Directory, directory));
        Sessions.Add(new LocalSession(id, directory, saved?.Title, updated ?? DateTimeOffset.UtcNow,
            saved?.Archived ?? false, saved?.Pinned ?? false));
    }

    public LocalSession? FindSession(string id, string directory) =>
        Sessions.Find(session => session.Id == id && PathEquals(session.Directory, directory));

    public bool RemoveProject(string directory) => Projects.RemoveAll(path => PathEquals(path, directory)) > 0;

    public IReadOnlyList<string> DirectoriesForProject(string project) => AdditionalDirectories
        .Where(item => PathEquals(item.Project, project)).Select(item => item.Directory).ToArray();

    public void SetProjectDirectories(string project, IReadOnlyList<string> directories)
    {
        var updated = new List<string>();
        foreach (string entry in directories)
        {
            var directory = ProjectPath(entry);
            if (!PathEquals(directory, project) && !updated.Any(existing => PathEquals(existing, directory)))
                updated.Add(directory);
        }
        AdditionalDirectories.RemoveAll(item => PathEquals(item.Project, project));
        AdditionalDirectories.AddRange(updated.Select(directory => new ProjectDirectory(project, directory)));
    }

    public bool RemoveSession(string id, string directory)
    {
        if (IsSessionRemoved(id, directory))
            return false;
        Sessions.RemoveAll(session => session.Id == id && PathEquals(session.Directory, directory));
        SessionWorkspaces.RemoveAll(item => item.Session == id && PathEquals(item.Directory, directory));
        RemovedSessions.Add(new RemovedSession(id, directory));
        return true;
    }

    public bool IsSessionRemoved(string id, string directory) =>
        RemovedSessions.Any(session => session.Id == id && PathEquals(session.Directory, directory));

    public bool RenameSession(string id, string directory, string? title)
    {
        int index = Sessions.FindIndex(session => session.Id == id && PathEquals(session.Directory, directory));
        if (index < 0)
            return false;
        Sessions[index] = Sessions[index] with { Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim() };
        return true;
    }

    public bool SetSessionArchived(string id, string directory, bool archived)
    {
        int index = Sessions.FindIndex(session => session.Id == id && PathEquals(session.Directory, directory));
        if (index < 0)
            return false;
        Sessions[index] = Sessions[index] with { Archived = archived };
        return true;
    }

    public bool SetSessionPinned(string id, string directory, bool pinned)
    {
        int index = Sessions.FindIndex(session => session.Id == id && PathEquals(session.Directory, directory));
        if (index < 0 || Sessions[index].Archived || Sessions[index].Pinned == pinned)
            return false;
        Sessions[index] = Sessions[index] with { Pinned = pinned };
        return true;
    }

    public IReadOnlyList<LocalSession> ProjectSessions(string directory, IReadOnlyList<LocalSession> official,
        IReadOnlyDictionary<string, string?> activeTitles, bool archived = false)
    {
        var visible = new Dictionary<string, LocalSession>(StringComparer.Ordinal);
        var removed = RemovedSessions.Where(record => PathEquals(record.Directory, directory))
            .Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var record in official.Where(record => PathEquals(record.Directory, directory)))
            visible[record.Id] = record with
            {
                Title = activeTitles.TryGetValue(record.Id, out var title) ? title : record.Title
            };
        foreach (var record in Sessions.Where(record => PathEquals(record.Directory, directory)))
        {
            visible.TryGetValue(record.Id, out var remote);
            var title = activeTitles.TryGetValue(record.Id, out var liveTitle) ? liveTitle : remote?.Title;
            visible[record.Id] = record with
            {
                Title = record.Title ?? title,
                Updated = remote is not null && remote.Updated > record.Updated ? remote.Updated : record.Updated
            };
        }
        return visible.Values.Where(record => record.Archived == archived && !removed.Contains(record.Id))
            .OrderByDescending(record => !archived && record.Pinned).ThenByDescending(record => record.Updated)
            .ThenBy(record => record.Id, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<LocalSession> IndexedSessions() => Sessions
        .Where(record => Projects.Any(project => PathEquals(project, record.Directory)) &&
            !IsSessionRemoved(record.Id, record.Directory))
        .OrderByDescending(record => !record.Archived && record.Pinned)
        .ThenByDescending(record => record.Updated)
        .ThenBy(record => record.Directory, StringComparer.OrdinalIgnoreCase)
        .ThenBy(record => record.Id, StringComparer.Ordinal).ToArray();

    public (string Project, string? Session)? StartupWorkspace()
    {
        string? project = LastProject is { } previous
            ? Projects.FirstOrDefault(path => PathEquals(path, previous) && Directory.Exists(path)) : null;
        project ??= Projects.FirstOrDefault(path => Path.IsPathFullyQualified(path) && Directory.Exists(path));
        if (project is null)
            return null;
        string? session = null;
        if (RestoreSessionOnStartup)
        {
            var recent = Sessions.Where(item => PathEquals(item.Directory, project) && !item.Archived &&
                !IsSessionRemoved(item.Id, project));
            session = LastProject is { } last && PathEquals(last, project)
                ? recent.FirstOrDefault(item => item.Id == LastSession)?.Id
                : recent.OrderByDescending(item => item.Updated).FirstOrDefault()?.Id;
        }
        return (project, session);
    }

    public static string ProjectPath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("项目须使用完整目录路径。");
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"项目目录不存在：{fullPath}");
        return fullPath;
    }

    public static bool PathEquals(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

public sealed class SettingsFile(string directory)
{
    private readonly string path = Path.Combine(directory, "settings.json");
    private static readonly JsonSerializerOptions Format = new() { WriteIndented = true };

    public LocalSettings Load() => File.Exists(path)
        ? JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(path)) ?? new LocalSettings()
        : new LocalSettings();

    public void Save(LocalSettings settings)
    {
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Format));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
