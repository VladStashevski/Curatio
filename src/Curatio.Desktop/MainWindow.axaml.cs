using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Curatio.Core;
using System.ComponentModel;

namespace Curatio.Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.PickFolderAsync = PickFolderAsync;
        viewModel.PickExportPathAsync = PickExportPathAsync;
        Opened += async (_, _) =>
        {
            await viewModel.LoadAsync();
            ApplyColumnVisibility(viewModel);
            foreach (var column in viewModel.TableColumns)
                column.PropertyChanged += OnColumnPreferenceChanged;
        };
        Closed += (_, _) =>
        {
            foreach (var column in viewModel.TableColumns)
                column.PropertyChanged -= OnColumnPreferenceChanged;
        };
    }

    private void OnColumnPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableColumnPreference.IsVisible)
            && DataContext is MainWindowViewModel viewModel)
        {
            ApplyColumnVisibility(viewModel);
        }
    }

    private void ApplyColumnVisibility(MainWindowViewModel viewModel)
    {
        var visibility = viewModel.TableColumns.ToDictionary(column => column.Key, column => column.IsVisible);
        foreach (var column in ResultsGrid.Columns)
        {
            if (column.Tag is string key && visibility.TryGetValue(key, out var isVisible))
                column.IsVisible = isVisible;
        }
    }

    private async void OnRowEditEnded(object? sender, DataGridRowEditEndedEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit
            || e.Row.DataContext is not InsuranceRecord record
            || DataContext is not MainWindowViewModel viewModel)
            return;

        await viewModel.SaveRecordAsync(record);
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку с документами",
            AllowMultiple = false
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickExportPathAsync(string extension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = extension == "xlsx" ? "Сохранить Excel-файл" : "Сохранить CSV-файл",
            SuggestedFileName = $"curatio-export-{DateTime.Now:yyyyMMdd-HHmm}.{extension}",
            DefaultExtension = extension,
            FileTypeChoices =
            [
                new FilePickerFileType(extension == "xlsx" ? "Excel" : "CSV")
                {
                    Patterns = [$"*.{extension}"]
                }
            ]
        });
        return file?.TryGetLocalPath();
    }
}
