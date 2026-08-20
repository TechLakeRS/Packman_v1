using Packman.Services;
using Packman.ViewModels;
using System.Windows.Controls;

namespace Packman.Views;

/// <summary>
/// Remote Test as a screen of its own, reached from the rail. It carries no package —
/// the user picks one built earlier — which is what separates it from the same tool
/// opened inside the package wizard.
/// </summary>
public partial class RemoteTestView : UserControl
{
    public RemoteTestViewModel ViewModel { get; }

    public RemoteTestView()
    {
        ViewModel = new RemoteTestViewModel(AppServices.Settings);
        DataContext = ViewModel;
        InitializeComponent();
    }

    /// <summary>Picks up machines the wizard's Remote Test used. Called by the host when shown.</summary>
    public void Refresh() => ViewModel.RefreshRecentComputers();
}
