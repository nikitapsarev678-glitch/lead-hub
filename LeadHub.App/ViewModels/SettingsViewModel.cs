using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadHub.App.Services;
using Microsoft.Win32;

namespace LeadHub.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppState _state;

    [ObservableProperty] private string _singBoxPath = "";
    [ObservableProperty] private string _status = "";

    partial void OnSingBoxPathChanged(string value) => _state.Settings.Set("singbox_path", value);

    public SettingsViewModel(AppState state)
    {
        _state = state;
        _singBoxPath = state.Settings.Get("singbox_path") ?? "";
    }

    [RelayCommand]
    private void BrowseSingBox()
    {
        var dialog = new OpenFileDialog { Filter = "sing-box (sing-box.exe)|sing-box.exe|Все файлы (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        SingBoxPath = dialog.FileName;
        _state.Settings.Set("singbox_path", SingBoxPath);
        Status = "Путь к sing-box сохранён.";
    }

    [RelayCommand]
    private void OpenExportDir()
    {
        Directory.CreateDirectory(AppPaths.ExportDir);
        try { Process.Start(new ProcessStartInfo { FileName = AppPaths.ExportDir, UseShellExecute = true }); }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private void BackupDatabase()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "База LeadHub (*.db)|*.db",
            FileName = $"leadhub-backup-{DateTime.Now:yyyyMMdd-HHmm}.db",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.Copy(AppPaths.Database, dialog.FileName, overwrite: true);
            Status = "Резервная копия создана: " + dialog.FileName;
        }
        catch (Exception ex) { Status = "Ошибка копии: " + ex.Message; }
    }

    /// <summary>Импорт exclusions.json из скилла (9103 известных handles + телефоны).</summary>
    [RelayCommand]
    private void ImportExclusions()
    {
        var dialog = new OpenFileDialog { Filter = "exclusions.json (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var (handles, phones) = _state.Exclusions.ImportFromSkillJson(dialog.FileName);
            Status = $"Импортировано: {handles} handles, {phones} телефонов. Они больше не попадут в поиск.";
        }
        catch (Exception ex) { Status = "Ошибка импорта исключений: " + ex.Message; }
    }

    [RelayCommand]
    private void OpenDataDir()
    {
        try { Process.Start(new ProcessStartInfo { FileName = AppPaths.DataDir, UseShellExecute = true }); }
        catch (Exception ex) { Status = ex.Message; }
    }
}
