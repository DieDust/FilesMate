using System.Runtime.Versioning;

using FilesMate.Platform.Windows.Interop;

namespace FilesMate.Platform.Windows.Shell;

[SupportedOSPlatform("windows")]
public sealed class ShellContextMenuSession : IDisposable
{
    private nint _host;
    private nint _popup;
    private IContextMenu? _menu;
    private bool _disposed;

    internal ShellContextMenuSession(
        nint host,
        IContextMenu menu,
        nint popup,
        IReadOnlyList<ShellMenuItem> items)
    {
        _host = host;
        _menu = menu;
        _popup = popup;
        Items = items;
    }

    public IReadOnlyList<ShellMenuItem> Items { get; }

    public void Invoke(uint commandId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (commandId == 0 || _menu is null)
        {
            return;
        }

        ShellContextMenu.InvokeVerb(_menu, _host, commandId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ShellContextMenu.DropHandlers();
        if (_popup != 0)
        {
            _ = User32.DestroyMenu(_popup);
            _popup = 0;
        }

        ShellContextMenu.ReleaseCom(_menu);
        _menu = null;
        if (_host != 0)
        {
            _ = User32.DestroyWindow(_host);
            _host = 0;
        }
    }
}
