using System.Windows.Controls;
using LeadHub.App.ViewModels;

namespace LeadHub.App.Views;

public partial class RunPage
{
    public RunPage(RunViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
