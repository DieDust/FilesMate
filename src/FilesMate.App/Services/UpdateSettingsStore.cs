using System.Text.Json;

namespace FilesMate.App.Services;

public sealed record UpdateSettings(bool AutomaticallyCheck = true, string? LastAttemptDay = null);

public sealed class UpdateSettingsStore(string path)
{
    public UpdateSettings Load()
    {
        try { return JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(path)) ?? new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public bool TryReserveDailyCheck(DateOnly today, bool manual = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var state = Load();
            var day = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (!manual && (!state.AutomaticallyCheck || state.LastAttemptDay == day)) return false;
            Save(state with { LastAttemptDay = day });
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public void SetAutomatic(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Save(Load() with { AutomaticallyCheck = enabled });
    }

    private void Save(UpdateSettings settings)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(settings)); File.Move(temporary, path, overwrite: true); }
        finally { File.Delete(temporary); }
    }
}
