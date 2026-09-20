using FilesMate.App.Workspace;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Panes;

public sealed partial class PaneHost : UserControl
{
    public PaneHost()
    {
        InitializeComponent();
    }

    public PaneSession? Session { get; private set; }

    public UIElement? Body
    {
        get => BodyPresenter.Content as UIElement;
        set => BodyPresenter.Content = value;
    }

    public void Attach(PaneSession session, UIElement body)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(body);
        Session = session;
        Body = body;
    }
}
