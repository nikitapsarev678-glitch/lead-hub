using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadHub.App.Services;
using LeadHub.Core.Parser;
using LeadHub.Core.Store;
using Microsoft.Win32;

namespace LeadHub.App.ViewModels;

public partial class AccountsViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly SingBoxRuntime _vpn;

    public ObservableCollection<IgAccount> Accounts { get; } = new();

    [ObservableProperty] private string _importUsername = "";
    [ObservableProperty] private string _importCookies = "";
    [ObservableProperty] private string _exportPassword = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;

    public AccountsViewModel(AppState state, SingBoxRuntime vpn)
    {
        _state = state;
        _vpn = vpn;
        Reload();
    }

    public void Reload()
    {
        Accounts.Clear();
        foreach (var a in _state.Accounts.All()) Accounts.Add(a);
    }

    [RelayCommand]
    private void ImportCookiesFromText()
    {
        var session = CookieSession.Parse(ImportCookies);
        if (!session.IsUsable)
        {
            Status = "В cookies нет sessionid + csrftoken — скопируйте их из браузера, где выполнен вход в Instagram (F12 → Network → любой запрос → Cookie, или расширение «EditThisCookie» → экспорт JSON).";
            return;
        }
        var username = ImportUsername.Trim().TrimStart('@');
        if (username.Length == 0 && session.DsUserId is { } id)
            username = $"user_{id}";
        if (username.Length == 0)
        {
            Status = "Укажите ник аккаунта.";
            return;
        }
        var accountId = _state.SaveAccount(username, session);
        _state.Accounts.SetStatus(accountId, "active");
        _state.InvalidateSessions();
        Reload();
        ImportUsername = "";
        ImportCookies = "";
        Status = $"Аккаунт @{username} импортирован (sessionid на месте). Файлы cookies хранятся зашифрованными.";
    }

    [RelayCommand]
    private async Task CheckSession(IgAccount account)
    {
        IsBusy = true;
        Status = $"Проверяю сессию @{account.Username}…";
        try
        {
            var session = _state.LoadSession(account);
            if (session == null || !session.IsUsable)
            {
                _state.Accounts.SetStatus(account.Id, "needs_check");
                Status = $"@{account.Username}: cookies не читаются.";
                Reload();
                return;
            }
            var proxyPort = _vpn.IsRunning ? SingBoxRuntime.BaseProxyPort : 0;
            using var client = new IgWebClient(session, account.Username, proxyPort);
            var profile = await client.FetchProfileAsync(account.Username);
            if (profile != null)
            {
                _state.Accounts.SetStatus(account.Id, "active");
                _state.Accounts.MarkChecked(account.Id);
                Status = $"@{account.Username}: сессия жива ✅ (подписчиков у аккаунта: {profile.FollowerCount}).";
            }
            else
            {
                _state.Accounts.SetStatus(account.Id, "needs_check");
                Status = $"@{account.Username}: профиль не получен — возможно, сессия истекла.";
            }
        }
        catch (IgRateLimitException ex)
        {
            _state.Accounts.SetStatus(account.Id, "cooldown", DateTime.UtcNow.AddHours(2));
            Status = $"@{account.Username}: Instagram ответил ограничением ({ex.Message}) — кулдаун 2ч.";
        }
        catch (Exception ex)
        {
            Status = $"@{account.Username}: ошибка проверки — {ex.Message}";
        }
        finally
        {
            Reload();
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ExportAccount(IgAccount account)
    {
        if (ExportPassword.Length < 4)
        {
            Status = "Придумайте пароль (минимум 4 символа) для файла .lhaccount — без него файл не расшифровать.";
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "Аккаунт LeadHub (*.lhaccount)|*.lhaccount",
            FileName = $"{account.Username}.lhaccount",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var session = _state.LoadSession(account) ?? new CookieSession();
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                username = account.Username,
                ds_user_id = session.DsUserId,
                cookies = session.Cookies,
            });
            Crypto.WriteAccountPackage(dialog.FileName, payload, ExportPassword);
            Status = $"Экспортировано: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            Status = "Ошибка экспорта: " + ex.Message;
        }
    }

    [RelayCommand]
    private void ImportPackage()
    {
        var dialog = new OpenFileDialog { Filter = "Аккаунт LeadHub (*.lhaccount)|*.lhaccount" };
        if (dialog.ShowDialog() != true) return;
        if (ExportPassword.Length < 4)
        {
            Status = "Введите пароль файла в поле «Пароль для экспорта/импорта».";
            return;
        }
        try
        {
            var json = Crypto.ReadAccountPackage(dialog.FileName, ExportPassword);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var username = root.GetProperty("username").GetString()!;
            var session = System.Text.Json.JsonSerializer.Deserialize<CookieSession>(root.GetProperty("cookies").GetRawText()) ?? new CookieSession();
            _state.SaveAccount(username, session);
            _state.InvalidateSessions();
            Reload();
            Status = $"Аккаунт @{username} импортирован из файла.";
        }
        catch (Exception ex)
        {
            Status = "Не удалось прочитать файл (неверный пароль?): " + ex.Message;
        }
    }

    [RelayCommand]
    private void ClearCooldown(IgAccount account)
    {
        _state.Accounts.SetStatus(account.Id, "active");
        Reload();
        Status = $"Кулдаун @{account.Username} снят.";
    }

    [RelayCommand]
    private void DeleteAccount(IgAccount account)
    {
        _state.Accounts.Delete(account.Id);
        Reload();
        Status = $"Аккаунт @{account.Username} удалён.";
    }
}
