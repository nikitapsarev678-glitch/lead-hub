using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LeadHub.App.ViewModels;

namespace LeadHub.App.Views;

public partial class ParsePage : IRefreshOnNavigate
{
    public ParsePage(ParseViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RunStarted += () => (Window.GetWindow(this) as MainWindow)?.NavigateTo("run");
        Loaded += (_, _) =>
        {
            NicheGroupFilter.Items.Add("(все группы)");
            foreach (var group in vm.Niches.Select(n => n.Group).Distinct().OrderBy(g => g))
                NicheGroupFilter.Items.Add(group);
            NicheGroupFilter.SelectedIndex = 0;
        };
    }

    private ParseViewModel Vm => (ParseViewModel)DataContext;

    public void Refresh() => Vm.Reload();

    private void NicheGroupFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (NichesList.ItemsSource == null && Vm.Niches.Count > 0) NichesList.ItemsSource = Vm.Niches;
        if (CitiesList.ItemsSource == null && Vm.Cities.Count > 0)
        {
            var view = (ListCollectionView)CollectionViewSource.GetDefaultView(Vm.Cities);
            view.GroupDescriptions.Add(new PropertyGroupDescription("TierLabel"));
            CitiesList.ItemsSource = view;
        }
        var view2 = (ListCollectionView)CollectionViewSource.GetDefaultView(Vm.Niches);
        if (view2.GroupDescriptions.Count == 0)
            view2.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
        var selected = NicheGroupFilter.SelectedItem as string;
        view2.Filter = selected is null or "(все группы)" ? null : o => o is NicheVm { Group: var g } && g == selected;
    }
}
