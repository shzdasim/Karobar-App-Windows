using System.Windows.Controls;
using Karobar.Desktop.Core.Navigation;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class DashboardView : UserControl, IPageKeyboardHandler
{
    public DashboardView()
    {
        InitializeComponent();
    }

    public bool HandleNewShortcut() => false;
    public bool HandleSaveShortcut() => false;
    public bool HandleSearchShortcut() => false;
    public bool HandleEscapeShortcut() => false;
    public bool HandleImportShortcut() => false;
    public bool HandleExportShortcut() => false;

    public bool HandleRefreshShortcut()
    {
        if (DataContext is DashboardViewModel vm && vm.RefreshDashboardCommand.CanExecute(null))
        {
            vm.RefreshDashboardCommand.Execute(null);
            return true;
        }
        return false;
    }
}
