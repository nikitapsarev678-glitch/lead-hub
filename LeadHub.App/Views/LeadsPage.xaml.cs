using System.Windows.Controls;
using LeadHub.App.ViewModels;
using LeadHub.Core.Parser;

namespace LeadHub.App.Views;

public partial class LeadsPage : IRefreshOnNavigate
{
    public LeadsPage(LeadsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    public void Refresh() => ((LeadsViewModel)DataContext).Reload();

    private void OutreachText_LostFocus(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: LeadRecord lead } && DataContext is LeadsViewModel vm)
            vm.SaveEditedTextCommand.Execute(lead);
    }
}
