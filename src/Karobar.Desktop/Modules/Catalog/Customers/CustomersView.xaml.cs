using System.Windows.Controls;
using System.Windows.Input;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class CustomersView : UserControl
{
    public CustomersView()
    {
        InitializeComponent();
    }

    private void CustomersView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CustomersViewModel vm)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.N && vm.IsDirectoryMode && vm.CanCreate)
            {
                vm.OpenCreateModeCommand.Execute(null);
                CustomerFormNameTextBox?.Focus();
                e.Handled = true;
            }
            else if (key == Key.S && vm.IsFormMode)
            {
                vm.SaveCustomerCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (vm.IsReminderModalOpen)
            {
                vm.CloseReminderModalCommand.Execute(null);
                e.Handled = true;
            }
            else if (vm.IsCreditModalOpen)
            {
                vm.CloseCreditModalCommand.Execute(null);
                e.Handled = true;
            }
            else if (vm.IsPriceModalOpen)
            {
                vm.CloseWholesalePriceModalCommand.Execute(null);
                e.Handled = true;
            }
            else if (vm.IsImportModalOpen)
            {
                vm.CloseImportModalCommand.Execute(null);
                e.Handled = true;
            }
            else if (vm.IsFormMode || vm.IsDetailMode)
            {
                vm.BackToDirectoryCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
