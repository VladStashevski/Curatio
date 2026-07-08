using System.Collections.ObjectModel;
using System.Globalization;
using Curatio.Core;

namespace Curatio.Desktop;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IDocumentProcessingService _processingService;
    private readonly IRecordRepository _repository;
    private readonly IRecordExportService _exportService;
    private readonly ISettingsStore _settings;
    private readonly List<InsuranceRecord> _allRecords = [];
    private CancellationTokenSource? _scanCancellation;
    private string _selectedFolder = "";
    private InsuranceRecord? _selectedRecord;
    private bool _isBusy;
    private bool _recursive = true;
    private double _uiScale = 1;
    private string _progressText = "Готово к работе";
    private string _notification = "";

    public MainWindowViewModel(
        IDocumentProcessingService processingService,
        IRecordRepository repository,
        IRecordExportService exportService,
        ISettingsStore settings)
    {
        _processingService = processingService;
        _repository = repository;
        _exportService = exportService;
        _settings = settings;
        ChooseFolderCommand = new AsyncCommand(() => RunSafeAsync(ChooseFolderAsync));
        ScanCommand = new AsyncCommand(ScanAsync, () => !IsBusy && Directory.Exists(SelectedFolder));
        ExportXlsxCommand = new AsyncCommand(() => RunSafeAsync(() => ExportAsync("xlsx")), CanExport);
        ExportCsvCommand = new AsyncCommand(() => RunSafeAsync(() => ExportAsync("csv")), CanExport);
        ClearRecordsCommand = new AsyncCommand(ClearRecordsAsync, CanClearRecords);
        ResetViewCommand = new RelayCommand(ResetView);

        TableColumns =
        [
            CreateColumn("claim", "Заключение"),
            CreateColumn("date", "Дата заключения"),
            CreateColumn("policy", "Полис"),
            CreateColumn("medicalDocument", "Меддокумент"),
            CreateColumn("expert", "Эксперт"),
            CreateColumn("expertSpecialty", "Специальность"),
            CreateColumn("careForm", "Форма помощи"),
            CreateColumn("carePeriod", "Период помощи"),
            CreateColumn("diagnosis", "Основной диагноз"),
            CreateColumn("diagnosisComplication", "Осложнение", false),
            CreateColumn("comorbidDiagnosis", "Сопутствующие диагнозы", false),
            CreateColumn("operation", "Операция", false),
            CreateColumn("clinicalStatisticalGroup", "КСГ", false),
            CreateColumn("description", "Выводы"),
            CreateColumn("file", "Файл"),
            CreateColumn("status", "Статус"),
            CreateColumn("client", "Застрахованный", false),
            CreateColumn("gender", "Пол", false),
            CreateColumn("birthDate", "Дата рождения", false),
            CreateColumn("insuranceOrganization", "Страховая организация", false),
            CreateColumn("medicalOrganization", "Медицинская организация", false),
            CreateColumn("checkType", "Вид проверки"),
            CreateColumn("amount", "Сумма случая"),
            CreateColumn("financialSanctions", "Финансовые санкции", false),
            CreateColumn("paymentReduction", "Неоплата/уменьшение", false),
            CreateColumn("penalty", "Штраф", false),
            CreateColumn("examinationForm", "Форма экспертизы", false),
            CreateColumn("examinationPeriod", "Срок экспертизы", false),
            CreateColumn("careConditions", "Условия помощи", false),
            CreateColumn("careProfile", "Профиль помощи", false),
            CreateColumn("outcome", "Исход", false),
            CreateColumn("recommendations", "Рекомендации", false)
        ];
    }

    public ObservableCollection<InsuranceRecord> Records { get; } = [];
    public ObservableCollection<ProcessingLog> Logs { get; } = [];
    public ObservableCollection<TableColumnPreference> TableColumns { get; }

    public Func<Task<string?>>? PickFolderAsync { get; set; }
    public Func<string, Task<string?>>? PickExportPathAsync { get; set; }
    public Func<int, Task<bool>>? ConfirmClearRecordsAsync { get; set; }

    public AsyncCommand ChooseFolderCommand { get; }
    public AsyncCommand ScanCommand { get; }
    public AsyncCommand ExportXlsxCommand { get; }
    public AsyncCommand ExportCsvCommand { get; }
    public AsyncCommand ClearRecordsCommand { get; }
    public RelayCommand ResetViewCommand { get; }

    public string SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetField(ref _selectedFolder, value))
                ScanCommand.RaiseCanExecuteChanged();
        }
    }

    public bool Recursive { get => _recursive; set => SetField(ref _recursive, value); }

    public double UiScale
    {
        get => _uiScale;
        set
        {
            if (!SetField(ref _uiScale, value))
                return;

            OnPropertyChanged(nameof(UiScaleText));
            _ = SaveViewSettingAsync("uiScale", value.ToString(CultureInfo.InvariantCulture));
        }
    }

    public string UiScaleText => $"{UiScale * 100:0}%";

    public InsuranceRecord? SelectedRecord
    {
        get => _selectedRecord;
        set => SetField(ref _selectedRecord, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
                return;
            ScanCommand.RaiseCanExecuteChanged();
            ExportXlsxCommand.RaiseCanExecuteChanged();
            ExportCsvCommand.RaiseCanExecuteChanged();
            ClearRecordsCommand.RaiseCanExecuteChanged();
        }
    }

    public string ProgressText { get => _progressText; private set => SetField(ref _progressText, value); }
    public string Notification { get => _notification; private set => SetField(ref _notification, value); }

    public async Task LoadAsync()
    {
        try
        {
            SelectedFolder = await _settings.GetAsync("lastFolder") ?? "";
            var scale = await _settings.GetAsync("uiScale");
            if (double.TryParse(scale, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedScale)
                && parsedScale is >= 0.8 and <= 1.25)
            {
                _uiScale = parsedScale;
                OnPropertyChanged(nameof(UiScale));
                OnPropertyChanged(nameof(UiScaleText));
            }

            var viewVersion = await _settings.GetAsync("viewVersion");
            if (viewVersion != "4")
            {
                ApplyDefaultColumnVisibility();
                await _settings.SetAsync("viewVersion", "4");
            }

            foreach (var column in TableColumns)
            {
                var visible = await _settings.GetAsync($"column.{column.Key}.visible");
                if (bool.TryParse(visible, out var parsedVisible))
                    column.SetWithoutNotification(parsedVisible);
            }

            _allRecords.Clear();
            _allRecords.AddRange(await _repository.GetAllAsync());
            RefreshRecords();
        }
        catch (Exception)
        {
            Notification = "Не удалось загрузить локальную историю.";
            Logs.Insert(0, new ProcessingLog(DateTime.Now, "", "Ошибка чтения локальной базы."));
        }
    }

    private async Task ChooseFolderAsync()
    {
        var path = PickFolderAsync is null ? null : await PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            SelectedFolder = path;
            await _settings.SetAsync("lastFolder", path);
        }
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        Notification = "";
        _scanCancellation = new CancellationTokenSource();
        var progress = new Progress<ProcessingProgress>(item =>
        {
            ProgressText = item.Total == 0
                ? "DOCX-файлы не найдены"
                : $"Обработано {item.Processed} из {item.Total}: {item.CurrentFile}";
        });

        try
        {
            var result = await _processingService.ScanAsync(
                SelectedFolder,
                Recursive,
                progress,
                _scanCancellation.Token);
            var refreshedPaths = result.Records
                .Select(record => record.FullPath)
                .Concat(result.ReplacedPaths)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _allRecords.RemoveAll(record => refreshedPaths.Contains(record.FullPath));
            _allRecords.InsertRange(0, result.Records);
            foreach (var log in result.Logs)
                Logs.Insert(0, log);
            RefreshRecords();
            Notification = $"Добавлено: {result.Records.Count}. Повторно не импортировано: {result.SkippedDuplicates}.";
        }
        catch (OperationCanceledException)
        {
            Notification = "Сканирование отменено.";
        }
        catch (Exception)
        {
            Notification = "Не удалось просканировать папку. Подробности записаны в журнал.";
            Logs.Insert(0, new ProcessingLog(DateTime.Now, "", "Ошибка доступа к папке или локальной базе."));
        }
        finally
        {
            IsBusy = false;
            _scanCancellation.Dispose();
            _scanCancellation = null;
        }
    }

    public async Task SaveRecordAsync(InsuranceRecord record)
    {
        if (record.Id <= 0)
            return;

        try
        {
            record.Status = DocumentStatus.Processed;
            record.ErrorMessage = null;
            await _repository.UpdateAsync(record, CancellationToken.None);
            Notification = $"Сохранено: {record.ClaimNumber}.";
            ExportXlsxCommand.RaiseCanExecuteChanged();
            ExportCsvCommand.RaiseCanExecuteChanged();
        }
        catch (Exception)
        {
            Notification = "Не удалось сохранить изменения.";
            Logs.Insert(0, new ProcessingLog(DateTime.Now, record.SourceFileName, "Ошибка сохранения записи."));
        }
    }

    private bool CanExport() => !IsBusy && _allRecords.Any(record => record.Status == DocumentStatus.Processed);

    private bool CanClearRecords() => !IsBusy && _allRecords.Count > 0;

    private async Task ExportAsync(string extension)
    {
        if (PickExportPathAsync is null)
            return;

        var path = await PickExportPathAsync(extension);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var records = _allRecords.Where(record => record.Status == DocumentStatus.Processed).ToArray();
        if (extension == "xlsx")
            await _exportService.ExportXlsxAsync(records, path, CancellationToken.None);
        else
            await _exportService.ExportCsvAsync(records, path, CancellationToken.None);

        Notification = $"Экспортировано записей: {records.Length}.";
    }

    private async Task ClearRecordsAsync()
    {
        var recordCount = _allRecords.Count;
        if (recordCount == 0)
        {
            Notification = "Нет извлечённых записей для очистки.";
            return;
        }

        if (ConfirmClearRecordsAsync is not null && !await ConfirmClearRecordsAsync(recordCount))
            return;

        IsBusy = true;
        ProgressText = "Очищаю извлечённые записи...";

        try
        {
            var deleted = await _repository.DeleteAllAsync(CancellationToken.None);
            _allRecords.Clear();
            SelectedRecord = null;
            RefreshRecords();
            Notification = deleted == 0
                ? "Нет извлечённых записей для очистки."
                : $"Очищено извлечённых записей: {deleted}.";
        }
        catch (Exception)
        {
            Notification = "Не удалось очистить извлечённые записи.";
            Logs.Insert(0, new ProcessingLog(DateTime.Now, "", "Ошибка очистки локальной базы."));
        }
        finally
        {
            ProgressText = "Готово к работе";
            IsBusy = false;
        }
    }

    private void RefreshRecords()
    {
        Records.Clear();
        foreach (var record in _allRecords)
            Records.Add(record);

        ExportXlsxCommand.RaiseCanExecuteChanged();
        ExportCsvCommand.RaiseCanExecuteChanged();
        ClearRecordsCommand.RaiseCanExecuteChanged();
    }

    private TableColumnPreference CreateColumn(string key, string title, bool isVisible = true) =>
        new(key, title, isVisible, column =>
        {
            if (!TableColumns.Any(item => item.IsVisible))
                column.SetWithoutNotification(true);

            _ = SaveViewSettingAsync($"column.{column.Key}.visible", column.IsVisible.ToString());
        });

    private void ResetView()
    {
        UiScale = 1;
        ApplyDefaultColumnVisibility();
    }

    private void ApplyDefaultColumnVisibility()
    {
        foreach (var column in TableColumns)
        {
            var isVisible = column.Key is
                "claim" or "date" or "policy" or "medicalDocument" or "expert"
                or "expertSpecialty" or "careForm" or "carePeriod" or "diagnosis"
                or "description" or "checkType" or "amount" or "file" or "status";
            column.SetWithoutNotification(isVisible);
            _ = SaveViewSettingAsync($"column.{column.Key}.visible", isVisible.ToString());
        }
    }

    private async Task SaveViewSettingAsync(string key, string value)
    {
        try
        {
            await _settings.SetAsync(key, value);
        }
        catch
        {
            Notification = "Не удалось сохранить настройки вида.";
        }
    }

    private async Task RunSafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            Notification = "Операция не выполнена. Проверьте путь и доступное место.";
            Logs.Insert(0, new ProcessingLog(DateTime.Now, "", "Ошибка локальной операции."));
        }
    }
}
