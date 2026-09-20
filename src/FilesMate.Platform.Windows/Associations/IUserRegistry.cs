namespace FilesMate.Platform.Windows.Associations;

public interface IUserRegistry
{
    public string? GetDefaultValue(string subKey);

    public string? GetValue(string subKey, string name);

    public void SetDefaultValue(string subKey, string value);

    public void SetValue(string subKey, string name, string value);

    public void DeleteDefaultValue(string subKey);

    public void DeleteValue(string subKey, string name);

    public void NotifyAssociationsChanged();
}
