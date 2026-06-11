namespace Curatio.Core;

public sealed class DocumentProcessingService(
    IDocumentTextReader textReader,
    IInsuranceDataExtractor extractor,
    IRecordRepository repository) : IDocumentProcessingService
{
    public async Task<ScanResult> ScanAsync(
        string folder,
        bool recursive,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Папка не найдена: {folder}");

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var candidates = Directory.EnumerateFiles(folder, "*.docx", option).ToArray();
        var temporaryFiles = candidates
            .Where(path => Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
            .ToArray();
        var files = candidates.Except(temporaryFiles, StringComparer.OrdinalIgnoreCase).ToArray();

        var records = new List<InsuranceRecord>();
        var logs = temporaryFiles
            .Select(path => new ProcessingLog(
                DateTime.Now,
                Path.GetFileName(path),
                "Временный файл Word пропущен."))
            .ToList();
        var duplicates = 0;

        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[index];
            progress?.Report(new ProcessingProgress(index, files.Length, Path.GetFileName(path)));

            try
            {
                var info = new FileInfo(path);
                if (await repository.IsImportedAsync(path, info.Length, info.LastWriteTimeUtc, cancellationToken))
                {
                    duplicates++;
                    continue;
                }

                var text = await textReader.ReadTextAsync(path, cancellationToken);
                var record = extractor.Extract(text, path, info.Length, info.LastWriteTimeUtc);
                await repository.SaveAsync(record, cancellationToken);
                records.Add(record);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logs.Add(new ProcessingLog(DateTime.Now, Path.GetFileName(path), SafeMessage(exception)));
                records.Add(new InsuranceRecord
                {
                    SourceFileName = Path.GetFileName(path),
                    FullPath = Path.GetFullPath(path),
                    ProcessedAt = DateTime.UtcNow,
                    Status = DocumentStatus.Error,
                    ErrorMessage = SafeMessage(exception)
                });
            }
        }

        progress?.Report(new ProcessingProgress(files.Length, files.Length, ""));
        return new ScanResult(records, logs, duplicates);
    }

    private static string SafeMessage(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Нет доступа к файлу.",
        IOException => "Файл повреждён, заблокирован или недоступен.",
        _ => "Не удалось обработать документ."
    };
}
