using System.Windows;
using System.Windows.Controls;
using LeadHub.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LeadHub.App;

public partial class MainWindow
{
    private readonly Dictionary<string, FrameworkElement> _pages = new();

    public MainWindow()
    {
        InitializeComponent();
        NavList.SelectedIndex = 0;
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not ListBoxItem item || item.Tag is not string tag) return;
        if (IsLoaded) Navigate(tag);
    }

    /// <summary>Переход по тегу из кода страниц (например, «Прогон» после запуска парсинга).</summary>
    public void NavigateTo(string tag)
    {
        foreach (var item in NavList.Items.OfType<ListBoxItem>())
        {
            if ((string?)item.Tag != tag) continue;
            NavList.SelectedItem = item;
            return;
        }
        Navigate(tag);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Navigate(((ListBoxItem)NavList.SelectedItem).Tag.ToString()!);
    }

    private void Navigate(string tag)
    {
        if (!_pages.TryGetValue(tag, out var page))
        {
            page = tag switch
            {
                "vpn" => App.Services.GetRequiredService<VpnHubPage>(),
                "accounts" => App.Services.GetRequiredService<AccountsPage>(),
                "parse" => App.Services.GetRequiredService<ParsePage>(),
                "run" => App.Services.GetRequiredService<RunPage>(),
                "leads" => App.Services.GetRequiredService<LeadsPage>(),
                "stats" => App.Services.GetRequiredService<StatsPage>(),
                "settings" => App.Services.GetRequiredService<SettingsPage>(),
                _ => throw new ArgumentOutOfRangeException(nameof(tag)),
            };
            _pages[tag] = page;
        }
        if (page is IRefreshOnNavigate refreshable) refreshable.Refresh();
        PageHost.Content = page;
    }
}
