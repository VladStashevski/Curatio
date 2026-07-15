namespace Curatio.Core;

public interface IDocumentTextReader
{
    Task<string> ReadTextAsync(string path, CancellationToken cancellationToken);

    async Task<DocumentReadResult> ReadDocumentAsync(
        string path,
        CancellationToken cancellationToken) =>
        new(await ReadTextAsync(path, cancellationToken), "{}");
}

public interface IInsuranceDataExtractor
{
    InsuranceRecord Extract(string text, string path, long size, DateTime modifiedAt);
    IReadOnlyList<InsuranceRecord> ExtractRecords(string text, string path, long size, DateTime modifiedAt);
}

public interface IRecordRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<bool> IsImportedAsync(string path, long size, DateTime modifiedAt, CancellationToken cancellationToken);
    Task DeleteByPathsAsync(IEnumerable<string> paths, CancellationToken cancellationToken);
    Task ReplaceByPathsAsync(
        IEnumerable<string> paths,
        IEnumerable<InsuranceRecord> records,
        CancellationToken cancellationToken);
    Task<int> DeleteAllAsync(CancellationToken cancellationToken);
    Task SaveAsync(InsuranceRecord record, CancellationToken cancellationToken);
    Task UpdateAsync(InsuranceRecord record, CancellationToken cancellationToken);
    Task<IReadOnlyList<InsuranceRecord>> GetAllAsync(CancellationToken cancellationToken = default);
}

public static class DocumentTextMarkers
{
    public const string TableRowPrefix = "__CURATIO_TABLE_ROW__";
}

public sealed record DocumentReadResult(string Text, string StructureJson);

public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}

public interface IRecordExportService
{
    Task ExportXlsxAsync(IEnumerable<InsuranceRecord> records, string path, CancellationToken cancellationToken);
    Task ExportCsvAsync(IEnumerable<InsuranceRecord> records, string path, CancellationToken cancellationToken);
}

public interface IInsuranceApiSender
{
    Task<ApiSendResult> SendAsync(InsuranceRecord record, CancellationToken cancellationToken);
}

public interface IDocumentProcessingService
{
    Task<ScanResult> ScanAsync(
        string folder,
        bool recursive,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken);
}
