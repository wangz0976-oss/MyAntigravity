using System.Text;

namespace MyAntigravity;

public sealed record AgentSkill(string Name, string Source, string Description, string? FilePath, bool Enabled = true)
{
    public override string ToString() => $"{AgentText.Visible(Name)}，{AgentText.Visible(Source)}{(Enabled ? "" : "，插件已停用")}";
}

public static class SkillCatalog
{
    private const int PreviewLimit = 64 * 1024;
    private const int HeaderLimit = 8 * 1024;

    public static IReadOnlyList<AgentSkill> Read(IEnumerable<string> workspaces, string profile, IEnumerable<CliPlugin> plugins)
    {
        var skills = new List<AgentSkill>();
        var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var workspace in workspaces)
        {
            foreach (var directory in ProjectRoots(workspace))
                AddSkills(Path.Combine(directory, ".agents", "skills"), $"工作目录 {directory}");
        }
        if (!string.IsNullOrWhiteSpace(profile))
        {
            AddSkills(Path.Combine(profile, ".gemini", "antigravity-cli", "skills"), "本机 CLI");
            AddSkills(Path.Combine(profile, ".gemini", "skills"), "本机共享");
            AddSkills(Path.Combine(profile, ".gemini", "config", "skills"), "全局");
        }
        foreach (var plugin in plugins)
        {
            foreach (var skill in plugin.Skills.Where(name => !string.IsNullOrWhiteSpace(name)))
                skills.Add(new AgentSkill(skill, $"插件 {plugin.Name}", "", null, plugin.Enabled));
        }
        return skills.OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(skill => skill.Source, StringComparer.OrdinalIgnoreCase).ToArray();

        void AddSkills(string path, string source)
        {
            if (!visited.Add(path) || !Directory.Exists(path))
                return;
            try
            {
                foreach (var entry in Directory.EnumerateDirectories(path))
                {
                    var file = Path.Combine(entry, "SKILL.md");
                    if (!File.Exists(file))
                        continue;
                    var fallback = Path.GetFileName(entry);
                    try
                    {
                        var (name, description) = ReadMetadata(ReadPrefix(file, HeaderLimit, out _));
                        skills.Add(new AgentSkill(name ?? fallback, source, description ?? "", file));
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        skills.Add(new AgentSkill(fallback, source, "无法读取技能说明。", file));
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public static string Preview(string path)
    {
        var text = ReadPrefix(path, PreviewLimit, out bool limited);
        return AgentText.Visible(text) + (limited ? "\n\n已显示说明的前 64K 个字符。" : "");
    }

    private static IEnumerable<string> ProjectRoots(string path)
    {
        var workspace = new DirectoryInfo(path);
        var chain = new List<string>();
        for (DirectoryInfo? directory = workspace; directory is not null; directory = directory.Parent)
        {
            chain.Add(directory.FullName);
            string marker = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
                return chain;
        }
        return [workspace.FullName];
    }

    private static string ReadPrefix(string path, int size, out bool limited)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[size + 1];
        int length = reader.ReadBlock(buffer);
        limited = length > size;
        return new string(buffer, 0, Math.Min(length, size));
    }

    private static (string? Name, string? Description) ReadMetadata(string content)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
            return (null, null);
        string? name = null;
        string? description = null;
        for (int index = 1; index < lines.Length && lines[index].Trim() != "---"; index++)
        {
            var line = lines[index];
            if (line.StartsWith("name:", StringComparison.Ordinal))
                name = line[5..].Trim().Trim('"', '\'');
            if (line.StartsWith("description:", StringComparison.Ordinal))
            {
                description = line[12..].Trim().Trim('"', '\'');
                if (description.StartsWith('>') || description.StartsWith('|'))
                {
                    var parts = new List<string>();
                    while (index + 1 < lines.Length && lines[index + 1].StartsWith(' '))
                        parts.Add(lines[++index].Trim());
                    description = string.Join(" ", parts);
                }
            }
        }
        return (string.IsNullOrWhiteSpace(name) ? null : name, description);
    }
}
