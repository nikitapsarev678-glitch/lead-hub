using System.Windows;
using System.Windows.Controls;
using LeadHub.App.ViewModels;

namespace LeadHub.App.Views;

public partial class AccountsPage : IRefreshOnNavigate
{
    public AccountsPage(AccountsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        ExportPasswordBox.PasswordChanged += (_, _) => vm.ExportPassword = ExportPasswordBox.Password;
    }

    public void Refresh() => ((AccountsViewModel)DataContext).Reload();
}
