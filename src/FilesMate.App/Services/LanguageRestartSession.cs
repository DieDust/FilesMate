using System.Text.Json;

namespace FilesMate.App.Services;

/// <summary>A one-use handoff; it does not change the user's normal session-restore preference.</summary>
public sealed record LanguageRestartSession(int ParentPid, long ParentStartedUtcTicks, WindowSession[] Windows)
{
    public static string FilePath(string profile, string token)
    {
        if (!Guid.TryParseExact(token, "N", out _)) throw new ArgumentException("Invalid restart token.", nameof(token));
        return Path.Combine(profile, $"language-restart-{token}.json");
    }

    public string Save(string profile)
    {
        Directory.CreateDirectory(profile);
        var token = Guid.NewGuid().ToString("N");
        using var stream = new FileStream(FilePath(profile, token), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, this);
        return token;
    }

    public static LanguageRestartSession Load(string profile, string token)
    {
        var path = FilePath(profile, token);
        var state = JsonSerializer.Deserialize<LanguageRestartSession>(File.ReadAllText(path));
        if (state is not { ParentPid: > 0, ParentStartedUtcTicks: > 0, Windows.Length: > 0 }
            || state.Windows.Any(w => w?.Tabs is null || w.Tabs.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidDataException("Invalid restart session.");
        return state;
    }
}
