using System.Windows;
using System.Windows.Controls;
using LeadHub.App.ViewModels;

namespace LeadHub.App.Views;

public partial class VpnHubPage : IRefreshOnNavigate
{
    public VpnHubPage(VpnHubViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private VpnHubViewModel Vm => (VpnHubViewModel)DataContext;

    public void Refresh() => Vm.Reload();

    private void SlotSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: SlotViewModel slot } && slot.AssignedNode != null)
        {
            Vm.AssignSlotCommand.Execute(slot);
        }
    }
}
