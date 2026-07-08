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
        var replacedPaths = files
            .Select(Path.GetFullPath)
            .ToArray();

        var extractedRecords = new List<InsuranceRecord>();
        var logs = temporaryFiles
            .Select(path => new ProcessingLog(
                DateTime.Now,
                Path.GetFileName(path),
                "Временный файл Word пропущен."))
            .ToList();

        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[index];
            progress?.Report(new ProcessingProgress(index, files.Length, Path.GetFileName(path)));

            try
            {
                if (IsNonCaseDocument(path))
                {
                    logs.Add(new ProcessingLog(
                        DateTime.Now,
                        Path.GetFileName(path),
                        "Служебный документ пропущен: случаи и суммы берутся из заключений."));
                    continue;
                }

                var info = new FileInfo(path);
                var text = await textReader.ReadTextAsync(path, cancellationToken);
                extractedRecords.AddRange(extractor.ExtractRecords(text, path, info.Length, info.LastWriteTimeUtc));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logs.Add(new ProcessingLog(DateTime.Now, Path.GetFileName(path), SafeMessage(exception)));
                extractedRecords.Add(new InsuranceRecord
                {
                    SourceFileName = Path.GetFileName(path),
                    FullPath = Path.GetFullPath(path),
                    CaseKey = $"error|{Path.GetFullPath(path)}",
                    ProcessedAt = DateTime.UtcNow,
                    Status = DocumentStatus.Error,
                    ErrorMessage = SafeMessage(exception)
                });
            }
        }

        var records = MergeCaseRecords(extractedRecords);
        await repository.DeleteByPathsAsync(replacedPaths, cancellationToken);
        foreach (var record in records)
            await repository.SaveAsync(record, cancellationToken);

        progress?.Report(new ProcessingProgress(files.Length, files.Length, ""));
        return new ScanResult(
            records,
            logs,
            extractedRecords.Count - records.Count,
            replacedPaths);
    }

    private static IReadOnlyList<InsuranceRecord> MergeCaseRecords(IEnumerable<InsuranceRecord> records)
    {
        var merged = new List<InsuranceRecord>();
        var byCase = new Dictionary<string, InsuranceRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records.OrderByDescending(SourcePriority))
        {
            var key = MergeKey(record);
            if (!byCase.TryGetValue(key, out var existing))
            {
                byCase[key] = record;
                merged.Add(record);
                continue;
            }

            MergeInto(existing, record);
        }

        foreach (var record in merged)
            record.Status = HasRequiredFields(record) ? DocumentStatus.Processed : record.Status;

        return merged;
    }

    private static void MergeInto(InsuranceRecord target, InsuranceRecord source)
    {
        if (IsSpecificClaimNumber(source.ClaimNumber) && !IsSpecificClaimNumber(target.ClaimNumber))
            target.ClaimNumber = source.ClaimNumber;
        else
            Fill(ref target, source, static record => record.ClaimNumber, static (record, value) => record.ClaimNumber = value);

        Fill(ref target, source, static record => record.ClientFullName, static (record, value) => record.ClientFullName = value);
        Fill(ref target, source, static record => record.ClaimType, static (record, value) => record.ClaimType = value);
        Fill(ref target, source, static record => record.CheckType, static (record, value) => record.CheckType = value);
        Fill(ref target, source, static record => record.PolicyNumber, static (record, value) => record.PolicyNumber = value);
        Fill(ref target, source, static record => record.EventDescription, static (record, value) => record.EventDescription = value);
        Fill(ref target, source, static record => record.InsuranceOrganization, static (record, value) => record.InsuranceOrganization = value);
        Fill(ref target, source, static record => record.ExpertName, static (record, value) => record.ExpertName = value);
        Fill(ref target, source, static record => record.ExpertSpecialty, static (record, value) => record.ExpertSpecialty = value);
        Fill(ref target, source, static record => record.MedicalDocumentNumber, static (record, value) => record.MedicalDocumentNumber = value);
        Fill(ref target, source, static record => record.Gender, static (record, value) => record.Gender = value);
        Fill(ref target, source, static record => record.MedicalOrganization, static (record, value) => record.MedicalOrganization = value);
        Fill(ref target, source, static record => record.ExaminationForm, static (record, value) => record.ExaminationForm = value);
        Fill(ref target, source, static record => record.ExaminationPeriod, static (record, value) => record.ExaminationPeriod = value);
        Fill(ref target, source, static record => record.CareForm, static (record, value) => record.CareForm = value);
        Fill(ref target, source, static record => record.CareConditions, static (record, value) => record.CareConditions = value);
        Fill(ref target, source, static record => record.CareProfile, static (record, value) => record.CareProfile = value);
        Fill(ref target, source, static record => record.CarePeriod, static (record, value) => record.CarePeriod = value);
        Fill(ref target, source, static record => record.CaseOutcome, static (record, value) => record.CaseOutcome = value);
        Fill(ref target, source, static record => record.PrimaryDiagnosis, static (record, value) => record.PrimaryDiagnosis = value);
        Fill(ref target, source, static record => record.DiagnosisComplication, static (record, value) => record.DiagnosisComplication = value);
        Fill(ref target, source, static record => record.ComorbidDiagnosis, static (record, value) => record.ComorbidDiagnosis = value);
        Fill(ref target, source, static record => record.Operation, static (record, value) => record.Operation = value);
        Fill(ref target, source, static record => record.ClinicalStatisticalGroup, static (record, value) => record.ClinicalStatisticalGroup = value);
        Fill(ref target, source, static record => record.DefectCode, static (record, value) => record.DefectCode = value);
        Fill(ref target, source, static record => record.DefectDescription, static (record, value) => record.DefectDescription = value);
        Fill(ref target, source, static record => record.Recommendations, static (record, value) => record.Recommendations = value);

        target.EventDate ??= source.EventDate;
        target.BirthDate ??= source.BirthDate;
        target.InsuredAmount ??= source.InsuredAmount;
        target.FinancialSanctionsAmount ??= source.FinancialSanctionsAmount;
        target.PaymentReductionAmount ??= source.PaymentReductionAmount;
        target.PenaltyAmount ??= source.PenaltyAmount;
        if (string.IsNullOrWhiteSpace(target.CaseKey))
            target.CaseKey = source.CaseKey;
    }

    private static void Fill(
        ref InsuranceRecord target,
        InsuranceRecord source,
        Func<InsuranceRecord, string> get,
        Action<InsuranceRecord, string> set)
    {
        if (string.IsNullOrWhiteSpace(get(target)) && !string.IsNullOrWhiteSpace(get(source)))
            set(target, get(source));
    }

    private static string MergeKey(InsuranceRecord record)
    {
        if (record.Status == DocumentStatus.Error)
            return $"error|{record.FullPath}";

        if (!string.IsNullOrWhiteSpace(record.PolicyNumber) && !string.IsNullOrWhiteSpace(record.MedicalDocumentNumber))
        {
            return string.Join(
                "|",
                "case",
                record.PolicyNumber,
                record.MedicalDocumentNumber);
        }

        return !string.IsNullOrWhiteSpace(record.ClaimNumber)
            ? $"claim|{Normalize(record.ClaimNumber)}"
            : $"path|{record.FullPath}|{record.CaseKey}";
    }

    private static int SourcePriority(InsuranceRecord record)
    {
        var fileName = record.SourceFileName;
        if (fileName.Contains("Заключение", StringComparison.OrdinalIgnoreCase))
            return 30;
        if (fileName.Contains("ЭЗ", StringComparison.OrdinalIgnoreCase))
            return 20;
        return 10;
    }

    private static bool IsNonCaseDocument(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Contains("Реестр", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("Сопроводительное письмо", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpecificClaimNumber(string value) =>
        value.EndsWith("Э", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith("М", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string SafeMessage(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Нет доступа к файлу.",
        IOException => "Файл повреждён, заблокирован или недоступен.",
        _ => "Не удалось обработать документ."
    };

    private static bool HasRequiredFields(InsuranceRecord record) =>
        !string.IsNullOrWhiteSpace(record.ClaimNumber)
        && record.EventDate.HasValue
        && !string.IsNullOrWhiteSpace(record.PolicyNumber)
        && (!string.IsNullOrWhiteSpace(record.ClientFullName)
            || !string.IsNullOrWhiteSpace(record.MedicalDocumentNumber));
}
