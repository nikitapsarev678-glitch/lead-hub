using System.Windows.Controls;
using LeadHub.App.ViewModels;

namespace LeadHub.App.Views;

public partial class SettingsPage
{
    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
