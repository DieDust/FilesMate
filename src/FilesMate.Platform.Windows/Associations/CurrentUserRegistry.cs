using System.Runtime.Versioning;

using FilesMate.Platform.Windows.Interop;

using Microsoft.Win32;

namespace FilesMate.Platform.Windows.Associations;

[SupportedOSPlatform("windows")]
public sealed class CurrentUserRegistry : IUserRegistry
{
    public string? GetDefaultValue(string subKey) => GetValue(subKey, string.Empty);

    public string? GetValue(string subKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        return key?.GetValue(string.IsNullOrEmpty(name) ? null : name) as string;
    }

    public void SetDefaultValue(string subKey, string value) => SetValue(subKey, string.Empty, value);

    public void SetValue(string subKey, string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKey);
        ArgumentNullException.ThrowIfNull(value);
        using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
        key.SetValue(string.IsNullOrEmpty(name) ? null : name, value, RegistryValueKind.String);
    }

    public void DeleteDefaultValue(string subKey) => DeleteValue(subKey, string.Empty);

    public void DeleteValue(string subKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(string.IsNullOrEmpty(name) ? string.Empty : name, throwOnMissingValue: false);
    }

    public void NotifyAssociationsChanged() =>
        Shell32.SHChangeNotify(Shell32.ShcneAssocChanged, Shell32.ShcnfIdList, 0, 0);
}
