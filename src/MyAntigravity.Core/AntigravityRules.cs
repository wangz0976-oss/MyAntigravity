namespace MyAntigravity;

public sealed record AntigravityRule(string Name, string Source, string Path)
{
    public override string ToString() => AgentText.Visible($"{Name} · {Source}");
}

public static class AntigravityRules
{
    public static IReadOnlyList<AntigravityRule> List(IEnumerable<string> workspaces, string profile)
    {
        var rules = new List<AntigravityRule>();
        var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string workspace in workspaces)
        {
            foreach (var directory in SkillCatalog.ProjectRoots(workspace))
            {
                var source = $"工作目录 {directory}";
                AddInstructions(directory, source);
                var agents = Path.Combine(directory, ".agents");
                AddInstructions(agents, source);
                AddFolder(Path.Combine(agents, "rules"), source);
                AddFolder(Path.Combine(directory, ".agent", "rules"), $"{source}（旧目录）");
            }
        }

        if (!string.IsNullOrWhiteSpace(profile))
        {
            var gemini = Path.Combine(profile, ".gemini");
            var config = Path.Combine(gemini, "config");
            AddInstructions(gemini, "全局");
            AddInstructions(config, "全局配置");
            AddFolder(Path.Combine(config, "rules"), "全局配置");
        }
        return rules;

        void AddInstructions(string directory, string source)
        {
            AddFile(Path.Combine(directory, "AGENTS.md"), source);
            AddFile(Path.Combine(directory, "GEMINI.md"), source);
        }

        void AddFolder(string directory, string source)
        {
            if (!Directory.Exists(directory))
                return;
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*.md")
                    .Order(StringComparer.OrdinalIgnoreCase))
                    AddFile(file, source);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }

        void AddFile(string file, string source)
        {
            if (File.Exists(file) && visited.Add(Path.GetFullPath(file)))
                rules.Add(new AntigravityRule(Path.GetFileName(file), source, file));
        }
    }
}
