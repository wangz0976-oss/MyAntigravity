using System.Text.Json;

namespace MyAntigravity;

public sealed record AccountVerification(string Executable, DateTimeOffset CheckedAt, bool RequiresLogin, string? Tier = null)
{
    public static async Task<AccountVerification> CheckAsync(AcpAgent agent, string executable, string version)
    {
        var timeout = TimeSpan.FromSeconds(12);
        try
        {
            await agent.InitializeAsync(version, timeout).ConfigureAwait(false);
            var session = await agent.NewSessionAsync(timeout).ConfigureAwait(false);
            if (session.Text("sessionId") is not { Length: > 0 })
                throw new IOException("官方 CLI 未确认测试会话。");
            return new AccountVerification(executable, DateTimeOffset.Now, false, ReadTier(session));
        }
        catch (AcpException error) when (error.RequiresAuthentication)
        {
            return new AccountVerification(executable, DateTimeOffset.Now, true);
        }
    }

    public static string? ReadTier(JsonElement session)
    {
        var name = session.Field("_meta").Field("antigravity").Field("tier").Text("name");
        return name is { Length: > 0 and <= 100 } ? AgentText.Visible(name) : null;
    }
}
