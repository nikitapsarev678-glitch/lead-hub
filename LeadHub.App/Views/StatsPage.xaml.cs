using System.Windows.Controls;
using LeadHub.App.ViewModels;

namespace LeadHub.App.Views;

public partial class StatsPage : IRefreshOnNavigate
{
    public StatsPage(StatsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    public void Refresh() => ((StatsViewModel)DataContext).Reload();
}
