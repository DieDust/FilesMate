using Microsoft.UI.Xaml.Markup;

namespace FilesMate.App.Localization;

[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class LocalizedText : MarkupExtension
{
    public string Key { get; set; } = "";
    protected override object ProvideValue() => StringTable.Get(Key);
}
