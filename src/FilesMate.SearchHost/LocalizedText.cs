using System;
using System.Windows.Markup;

namespace FilesMate.SearchHost;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocalizedText : MarkupExtension
{
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => FilesMate.App.Localization.StringTable.Get(Key);
}
