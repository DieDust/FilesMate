using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FilesMate.Search;

namespace FilesMate.SearchHost;

internal sealed class WindowsApplicationCatalog(Func<string> managerPath) : IApplicationCatalog
{
    private readonly object _gate = new();
    private IReadOnlyList<ApplicationEntry>? _snapshot;
    private Task<IReadOnlyList<ApplicationEntry>>? _refresh;
    private long _checked;

    public Task<IReadOnlyList<ApplicationEntry>> GetAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_refresh is null && (_snapshot is null || Environment.TickCount64 - _checked > 60000))
            {
                var completion = new TaskCompletionSource<IReadOnlyList<ApplicationEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
                _refresh = completion.Task;
                var worker = new Thread(() =>
                {
                    IReadOnlyList<ApplicationEntry> entries;
                    try { entries = ReadApplications(managerPath()); }
                    catch (Exception error) { Trace.WriteLine(error); entries = _snapshot ?? []; }
                    lock (_gate) { _snapshot = entries; _checked = Environment.TickCount64; _refresh = null; }
                    completion.TrySetResult(entries);
                }) { IsBackground = true, Name = "FilesMate application catalog" };
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
            }
            return _snapshot is not null ? Task.FromResult(_snapshot) : _refresh!.WaitAsync(token);
        }
    }

    internal static IReadOnlyList<ApplicationEntry> ReadApplications(string manager)
    {
        var entries = new List<ApplicationEntry>();
        // Shell.Application is a process-wide COM server and can hand different
        // threads the same RCW. Do not manually release these wrappers: the
        // launcher may be using that RCW concurrently. Normal RCW finalization
        // releases them after this refresh finishes.
        object? shell = null, folder = null, items = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            items = ((dynamic)folder!).Items();
            var count = Math.Min((int)((dynamic)items).Count, 5000);
            for (var i = 0; i < count; i++)
            {
                object? item = null;
                try
                {
                    item = ((dynamic)items).Item(i);
                    string name = ((dynamic)item!).Name;
                    string id = ((dynamic)item).Path;
                    string? target = ((dynamic)item).ExtendedProperty("System.Link.TargetParsingPath") as string;
                    string arguments = ((dynamic)item).ExtendedProperty("System.Link.Arguments") as string ?? "";
                    if (!string.IsNullOrWhiteSpace(id))
                        entries.Add(new(id, name, "shell:AppsFolder\\" + id,
                            !string.IsNullOrWhiteSpace(target) && Path.IsPathFullyQualified(target) ? target : null, arguments));
                }
                catch (COMException error) { Trace.WriteLine(error.Message); }
            }
        }
        catch (Exception error) { Trace.WriteLine("Application catalog: " + error.Message); }

        // Explicit launch entries also cover portable apps placed on the desktop
        // and installations whose Start menu items are not yet in AppsFolder.
        foreach (var root in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms,
            Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory }
            .Select(Environment.GetFolderPath).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
                foreach (var link in Directory.EnumerateFiles(root, "*.lnk", options).Take(5000))
                    if (ApplicationShortcut.ReadStoredTarget(link) is { } target)
                        entries.Add(new(link, Path.GetFileNameWithoutExtension(link), link, target.Target, target.Arguments));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Trace.WriteLine(error.Message); }
        }
        if (File.Exists(manager))
        {
            entries.RemoveAll(entry => entry.Name.Equals("FilesMate", StringComparison.OrdinalIgnoreCase));
            entries.Insert(0, new("FilesMate", "FilesMate", manager, manager, "--activate", IsFilesMate: true));
        }
        return ApplicationCatalog.Normalize(entries);
    }
}
