using System.Windows.Controls;
using System.Windows.Input;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class SuppliersView : UserControl
{
    public SuppliersView()
    {
        InitializeComponent();
    }

    private void SuppliersView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SuppliersViewModel vm)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.N && vm.IsDirectoryMode && vm.CanCreate)
            {
                vm.OpenCreateModeCommand.Execute(null);
                SupplierFormNameTextBox?.Focus();
                e.Handled = true;
            }
            else if (key == Key.S && vm.IsFormMode)
            {
                vm.SaveSupplierCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (vm.IsCreditModalOpen)
            {
                vm.CloseCreditModalCommand.Execute(null);
                e.Handled = true;
            }
            else if (vm.IsImportModalOpen)
            {
                vm.CloseImportModalCommand.Execute(null);
                e.Handled = true;
            }
            else if (vm.IsFormMode)
            {
                vm.BackToDirectoryCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
