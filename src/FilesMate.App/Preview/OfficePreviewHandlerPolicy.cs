namespace FilesMate.App.Preview;

internal enum OfficePreviewFamily { Unknown, Wps, MicrosoftOffice }
internal sealed record OfficePreviewHandlerCandidate(Guid ClassId, string DisplayName);

internal static class OfficePreviewHandlerPolicy
{
    internal static OfficePreviewFamily DetectApplication(string? progId, string? executable, string? friendlyName = null)
    {
        var id = progId ?? "";
        var name = (executable ?? "").Trim('"').Replace('\\', '/').Split('/').Last().ToLowerInvariant();
        if (Starts(id, "WPS.", "ET.", "WPP.", "Kingsoft.") || name is "wps.exe" or "et.exe" or "wpp.exe") return OfficePreviewFamily.Wps;
        if (Starts(id, "Word.", "Excel.", "PowerPoint.") || name is "winword.exe" or "excel.exe" or "powerpnt.exe") return OfficePreviewFamily.MicrosoftOffice;
        return DetectHandler(friendlyName ?? "");
    }

    internal static OfficePreviewFamily DetectHandler(string label)
    {
        if (label.Contains("WPS", StringComparison.OrdinalIgnoreCase) || label.Contains("Kingsoft", StringComparison.OrdinalIgnoreCase)) return OfficePreviewFamily.Wps;
        if (label.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)
            && new[] { "Word", "Excel", "PowerPoint", "Office" }.Any(n => label.Contains(n, StringComparison.OrdinalIgnoreCase))) return OfficePreviewFamily.MicrosoftOffice;
        return OfficePreviewFamily.Unknown;
    }

    internal static Guid[] Order(OfficePreviewFamily preferred, IEnumerable<OfficePreviewHandlerCandidate> candidates, Guid? progIdHandler, Guid? extensionHandler)
        => candidates.DistinctBy(c => c.ClassId)
            .OrderBy(c => preferred != OfficePreviewFamily.Unknown && DetectHandler(c.DisplayName) == preferred ? 0 : 1)
            .ThenBy(c => c.ClassId == progIdHandler ? 0 : c.ClassId == extensionHandler ? 1 : 2)
            .Take(3).Select(c => c.ClassId).ToArray();

    private static bool Starts(string value, params string[] prefixes) => prefixes.Any(p => value.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
