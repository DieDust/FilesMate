namespace FilesMate.App.Updates;

internal static class UpdateInstallPolicy
{
    internal static IReadOnlyList<string> Arguments(string installationDirectory) =>
    [
        "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-",
        // A different application may hold one of our files. Only FilesMate's
        // own shutdown paths may close processes; an external lock must fail the update.
        "/NOCLOSEAPPLICATIONS", "/NOFORCECLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS",
        "/FILESMATEUPDATE=1", "/DIR=" + Path.TrimEndingDirectorySeparator(installationDirectory),
    ];
}
