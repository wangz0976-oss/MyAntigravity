using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyAntigravity;

public sealed partial record ReleaseVersion(int Major, int Minor, int Patch, string? Prerelease)
    : IComparable<ReleaseVersion>
{
    [GeneratedRegex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$")]
    private static partial Regex Syntax();

    public static ReleaseVersion Parse(string text)
    {
        var match = Syntax().Match(text);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var major) ||
            !int.TryParse(match.Groups[2].Value, out var minor) || !int.TryParse(match.Groups[3].Value, out var patch))
            throw new FormatException("版本号格式无效。");

        var pre = match.Groups[4].Success ? match.Groups[4].Value : null;
        if (pre is not null && pre.Split('.').Any(part => part.Length > 1 && part[0] == '0' && Numeric(part)))
            throw new FormatException("预发布版本号包含前导零。");
        return new ReleaseVersion(major, minor, patch, pre);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
            return 1;
        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0 || Prerelease is null || other.Prerelease is null)
            return core != 0 ? core : (Prerelease is null).CompareTo(other.Prerelease is null);

        var first = Prerelease.Split('.');
        var second = other.Prerelease.Split('.');
        for (int index = 0; index < Math.Min(first.Length, second.Length); index++)
        {
            var a = first[index];
            var b = second[index];
            var numA = Numeric(a);
            var numB = Numeric(b);
            int comparison = numA && numB && a.Length != b.Length ? a.Length.CompareTo(b.Length)
                : numA != numB ? numB.CompareTo(numA) : string.CompareOrdinal(a, b);
            if (comparison != 0)
                return comparison;
        }
        return first.Length.CompareTo(second.Length);
    }

    private static bool Numeric(string text) => text.All(char.IsAsciiDigit);

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (Prerelease is null ? "" : $"-{Prerelease}");
}

public sealed record UpdateManifest(string Version, string Channel, Uri DownloadUrl, string Sha256, string Notes)
{
    public static UpdateManifest Parse(string json, string expectedChannel)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var version = root.Text("version") ?? "";
        var parsed = ReleaseVersion.Parse(version);
        var channel = root.Text("channel") ?? "";
        var sha = root.Text("sha256") ?? "";
        if (expectedChannel is not ("stable" or "beta") || channel != expectedChannel ||
            (channel == "stable") != (parsed.Prerelease is null) || sha.Length != 64 || !sha.All(Uri.IsHexDigit) ||
            !Uri.TryCreate(root.Text("downloadUrl"), UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length != 0 ||
            !url.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("更新清单的渠道、下载地址或 SHA-256 无效。");
        return new UpdateManifest(version, channel, url, sha, root.Text("notes") ?? "");
    }
}

public sealed class Updates(HttpClient http, string dataDirectory)
{
    private const string Project = "wangz0976-oss/MyAntigravity";

    public async Task<UpdateManifest?> CheckAsync(string channel, string current, CancellationToken cancellation)
    {
        if (channel is not ("stable" or "beta"))
            throw new ArgumentException("无效的更新渠道。", nameof(channel));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Project}/releases?per_page=100");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("MyAntigravity", "0.1"));
        using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("GitHub 更新通道尚未公开。");
        response.EnsureSuccessStatusCode();
        using var releases = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
        var installed = ReleaseVersion.Parse(current);
        var candidates = new List<(ReleaseVersion Version, Uri Manifest, string Installer)>();
        foreach (var release in releases.RootElement.Items())
        {
            var tag = release.Text("tag_name") ?? "";
            if (release.Flag("draft") || release.Flag("prerelease") != (channel == "beta") ||
                !tag.StartsWith('v'))
                continue;

            ReleaseVersion version;
            try { version = ReleaseVersion.Parse(tag[1..]); }
            catch (FormatException) { continue; }

            var assets = release.Field("assets").Items().ToArray();
            var manifest = assets.FirstOrDefault(asset => asset.Text("name") == $"update-{channel}.json");
            var installer = assets.FirstOrDefault(asset => asset.Text("name") == "MyAntigravity-Setup.exe");
            const string basePath = "https://github.com/" + Project + "/releases/download/";
            var prefix = basePath + tag + "/";
            if (version.CompareTo(installed) > 0 &&
                manifest.Text("browser_download_url") == prefix + $"update-{channel}.json" &&
                installer.Text("browser_download_url") == prefix + "MyAntigravity-Setup.exe")
                candidates.Add((version, new Uri(prefix + $"update-{channel}.json"),
                    prefix + "MyAntigravity-Setup.exe"));
        }

        var best = candidates.OrderByDescending(item => item.Version).FirstOrDefault();
        if (best.Version is null)
            return null;

        var verified = UpdateManifest.Parse(await http.GetStringAsync(best.Manifest, cancellation).ConfigureAwait(false), channel);
        if (verified.Version != best.Version.ToString() || verified.DownloadUrl.AbsoluteUri != best.Installer)
            throw new FormatException("更新清单与 GitHub Release 的安装包信息不一致。");
        return verified;
    }

    public async Task<string> DownloadAsync(UpdateManifest manifest, IProgress<int?> progress, CancellationToken cancellation)
    {
        var directory = Path.Combine(dataDirectory, "updates");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "MyAntigravity-Setup.exe");
        var temporary = Path.Combine(directory, $".download-{Guid.NewGuid():N}.tmp");
        try
        {
            using var response = await http.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead,
                cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var size = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[64 * 1024];
                long written = 0;
                int? last = -1;
                while (await source.ReadAsync(buffer, cancellation).ConfigureAwait(false) is int read && read > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                    written += read;
                    int? current = size > 0 ? (int)Math.Min(100, written * 100d / size.Value) : null;
                    if (current != last)
                    {
                        progress.Report(current);
                        last = current;
                    }
                }
            }

            if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("SHA-256 校验失败，安装包已丢弃。");
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
