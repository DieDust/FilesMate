using FilesMate.App.Models;

namespace FilesMate.App.Services;

public interface IAppearanceSettingsService
{
    public AppearanceSettings Load();

    public Task SaveAsync(AppearanceSettings settings, CancellationToken cancellationToken = default);
}
