using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
        viewModel.ConfirmClearRecordsAsync = ConfirmClearRecordsAsync;
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

    private async Task<bool> ConfirmClearRecordsAsync(int recordCount)
    {
        var dialog = new Window
        {
            Title = "Очистить извлечённые данные",
            Width = 420,
            Height = 210,
            MinWidth = 360,
            MinHeight = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var title = new TextBlock
        {
            Text = "Удалить все извлечённые записи?",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold
        };
        var message = new TextBlock
        {
            Text = $"Будет удалено записей: {recordCount}. Исходные DOCX-файлы и настройки вида останутся.",
            TextWrapping = TextWrapping.Wrap
        };
        var cancelButton = new Button
        {
            Content = "Отмена",
            Classes = { "secondary", "compact" }
        };
        var clearButton = new Button
        {
            Content = "Очистить",
            Classes = { "destructive", "compact" }
        };

        cancelButton.Click += (_, _) => dialog.Close(false);
        clearButton.Click += (_, _) => dialog.Close(true);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, clearButton }
        };
        var content = new Grid
        {
            Margin = new Avalonia.Thickness(20),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 12,
            Children =
            {
                title,
                message,
                buttons
            }
        };
        Grid.SetRow(message, 1);
        Grid.SetRow(buttons, 2);
        dialog.Content = content;

        var result = await dialog.ShowDialog<bool?>(this);
        return result == true;
    }
}
