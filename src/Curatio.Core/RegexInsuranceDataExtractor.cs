using System.Globalization;
using System.Text.RegularExpressions;

namespace Curatio.Core;

public sealed class RegexInsuranceDataExtractor(ExtractionRuleSet rules) : IInsuranceDataExtractor
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public InsuranceRecord Extract(string text, string path, long size, DateTime modifiedAt) =>
        ExtractRecords(text, path, size, modifiedAt).FirstOrDefault()
        ?? CreateBaseRecord(text, path, size, modifiedAt);

    public IReadOnlyList<InsuranceRecord> ExtractRecords(string text, string path, long size, DateTime modifiedAt)
    {
        var baseRecord = CreateBaseRecord(text, path, size, modifiedAt);
        var summaryRows = ExtractSummaryTableRows(text).ToArray();
        if (summaryRows.Length > 0)
        {
            return summaryRows
                .Select(row => CreateSummaryRecord(baseRecord, row))
                .ToArray();
        }

        ApplySingleCaseSanctions(baseRecord, text);
        baseRecord.CaseKey = BuildCaseKey(baseRecord);
        baseRecord.Status = HasRequiredFields(baseRecord) ? DocumentStatus.Processed : DocumentStatus.NeedsReview;
        return [baseRecord];
    }

    private InsuranceRecord CreateBaseRecord(string text, string path, long size, DateTime modifiedAt)
    {
        var record = new InsuranceRecord
        {
            ClaimNumber = Match("claimNumber", text),
            ClientFullName = Match("clientFullName", text),
            ClaimType = Match("claimType", text),
            CheckType = DetectCheckType(text, path),
            PolicyNumber = Match("policyNumber", text),
            EventDescription = Match("eventDescription", text),
            InsuranceOrganization = Match("insuranceOrganization", text),
            ExpertName = Match("expertName", text),
            ExpertSpecialty = Match("expertSpecialty", text),
            MedicalDocumentNumber = Match("medicalDocumentNumber", text),
            Gender = Match("gender", text),
            MedicalOrganization = Match("medicalOrganization", text),
            ExaminationForm = Match("examinationForm", text),
            ExaminationPeriod = Match("examinationPeriod", text),
            CareForm = Match("careForm", text),
            CareConditions = Match("careConditions", text),
            CareProfile = Match("careProfile", text),
            CarePeriod = Match("carePeriod", text),
            CaseOutcome = Match("caseOutcome", text),
            PrimaryDiagnosis = Match("primaryDiagnosis", text),
            DiagnosisComplication = Match("diagnosisComplication", text),
            ComorbidDiagnosis = Match("comorbidDiagnosis", text),
            Operation = Match("operation", text),
            ClinicalStatisticalGroup = Match("clinicalStatisticalGroup", text),
            Recommendations = Match("recommendations", text),
            SourceFileName = Path.GetFileName(path),
            FullPath = Path.GetFullPath(path),
            FileSize = size,
            FileModifiedAt = modifiedAt,
            ProcessedAt = DateTime.UtcNow,
            SendStatus = SendStatus.NotSent
        };

        var dateText = Match("eventDate", text);
        if (DateTime.TryParse(dateText, RussianCulture, DateTimeStyles.None, out var eventDate))
            record.EventDate = eventDate;

        var birthDateText = Match("birthDate", text);
        if (DateTime.TryParse(birthDateText, RussianCulture, DateTimeStyles.None, out var birthDate))
            record.BirthDate = birthDate;

        if (TryParseAmount(Match("insuredAmount", text), out var amount))
            record.InsuredAmount = amount;

        if (string.IsNullOrWhiteSpace(record.ClaimType))
            record.ClaimType = string.IsNullOrWhiteSpace(record.ExaminationForm)
                ? record.CareForm
                : record.ExaminationForm;

        return record;
    }

    private InsuranceRecord CreateSummaryRecord(InsuranceRecord baseRecord, SummaryCaseRow row)
    {
        var record = CloneBase(baseRecord);
        record.CaseKey = BuildCaseKey(row.PolicyNumber, row.MedicalDocumentNumber, row.BirthDate, CarePeriod(row.StartDate, row.EndDate), row.RowNumber.ToString(CultureInfo.InvariantCulture));
        record.ClaimNumber = string.IsNullOrWhiteSpace(baseRecord.ClaimNumber)
            ? row.RowNumber.ToString(CultureInfo.InvariantCulture)
            : $"{baseRecord.ClaimNumber}/{row.RowNumber}";
        record.PolicyNumber = row.PolicyNumber;
        record.MedicalDocumentNumber = row.MedicalDocumentNumber;
        record.BirthDate = row.BirthDate;
        record.CareProfile = FirstNonEmpty(row.CareProfile, baseRecord.CareProfile);
        record.CarePeriod = CarePeriod(row.StartDate, row.EndDate);
        record.PrimaryDiagnosis = FirstNonEmpty(row.Diagnosis, baseRecord.PrimaryDiagnosis);
        record.InsuredAmount = row.AcceptedAmount;
        record.FinancialSanctionsAmount = row.FinancialSanctionsAmount;
        record.PaymentReductionAmount = row.PaymentReductionAmount;
        record.PenaltyAmount = row.PenaltyAmount;

        if (!string.IsNullOrWhiteSpace(row.DefectDescription)
            && !row.DefectDescription.Contains("не выявлено", StringComparison.OrdinalIgnoreCase)
            && !row.DefectDescription.Equals("Нет", StringComparison.OrdinalIgnoreCase))
        {
            record.EventDescription = row.DefectDescription;
        }

        record.Status = HasRequiredFields(record) ? DocumentStatus.Processed : DocumentStatus.NeedsReview;
        return record;
    }

    private static InsuranceRecord CloneBase(InsuranceRecord source) => new()
    {
        ClaimNumber = source.ClaimNumber,
        ClientFullName = source.ClientFullName,
        EventDate = source.EventDate,
        ClaimType = source.ClaimType,
        CheckType = source.CheckType,
        PolicyNumber = source.PolicyNumber,
        InsuredAmount = source.InsuredAmount,
        FinancialSanctionsAmount = source.FinancialSanctionsAmount,
        PaymentReductionAmount = source.PaymentReductionAmount,
        PenaltyAmount = source.PenaltyAmount,
        EventDescription = source.EventDescription,
        InsuranceOrganization = source.InsuranceOrganization,
        ExpertName = source.ExpertName,
        ExpertSpecialty = source.ExpertSpecialty,
        MedicalDocumentNumber = source.MedicalDocumentNumber,
        Gender = source.Gender,
        BirthDate = source.BirthDate,
        MedicalOrganization = source.MedicalOrganization,
        ExaminationForm = source.ExaminationForm,
        ExaminationPeriod = source.ExaminationPeriod,
        CareForm = source.CareForm,
        CareConditions = source.CareConditions,
        CareProfile = source.CareProfile,
        CarePeriod = source.CarePeriod,
        CaseOutcome = source.CaseOutcome,
        PrimaryDiagnosis = source.PrimaryDiagnosis,
        DiagnosisComplication = source.DiagnosisComplication,
        ComorbidDiagnosis = source.ComorbidDiagnosis,
        Operation = source.Operation,
        ClinicalStatisticalGroup = source.ClinicalStatisticalGroup,
        Recommendations = source.Recommendations,
        SourceFileName = source.SourceFileName,
        FullPath = source.FullPath,
        CaseKey = source.CaseKey,
        FileSize = source.FileSize,
        FileModifiedAt = source.FileModifiedAt,
        ProcessedAt = source.ProcessedAt,
        Status = source.Status,
        ErrorMessage = source.ErrorMessage,
        ExternalId = source.ExternalId,
        SendStatus = source.SendStatus,
        SentAt = source.SentAt,
        ApiErrorMessage = source.ApiErrorMessage
    };

    private static IEnumerable<SummaryCaseRow> ExtractSummaryTableRows(string text)
    {
        foreach (var row in TableRows(text))
        {
            if (row.Length < 16 || !IsInteger(row[0]))
                continue;

            if (!LooksLikePolicy(row[3]) || !TryParseDate(row[4], out var birthDate))
                continue;

            if (!TryParseDate(row[7], out var startDate)
                || !TryParseDate(row[8], out var endDate)
                || !TryParseAmount(row[9], out var acceptedAmount))
            {
                continue;
            }

            yield return new SummaryCaseRow(
                int.Parse(row[0], CultureInfo.InvariantCulture),
                row[2],
                row[3],
                birthDate,
                row[5],
                row[6],
                startDate,
                endDate,
                acceptedAmount,
                row[10],
                row[11],
                ParseAmountOrNull(row[13]),
                ParseAmountOrNull(row[14]),
                ParseAmountOrNull(row[15]));
        }
    }

    private static void ApplySingleCaseSanctions(InsuranceRecord record, string text)
    {
        foreach (var row in TableRows(text))
        {
            if (row.Length < 7 || !IsInteger(row[0]))
                continue;

            if (row[2].Length == 0 || !row[3].Contains('.', StringComparison.Ordinal))
                continue;

            if (!TryParseAmount(row[5], out var paymentReduction))
                continue;

            var penalty = ParseAmountOrNull(row[6]);
            record.PaymentReductionAmount = paymentReduction;
            record.PenaltyAmount = penalty;
            record.FinancialSanctionsAmount = paymentReduction + (penalty ?? 0m);
            if (string.IsNullOrWhiteSpace(record.EventDescription) && !string.IsNullOrWhiteSpace(row[4]))
                record.EventDescription = row[4];
            return;
        }
    }

    private static IEnumerable<string[]> TableRows(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(DocumentTextMarkers.TableRowPrefix, StringComparison.Ordinal))
            .Select(line => line.Split('\t').Skip(1).Select(NormalizeCell).ToArray());

    private string Match(string field, string text)
    {
        if (!rules.Fields.TryGetValue(field, out var patterns))
            return "";

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromSeconds(1));
            if (match.Success)
                return Regex.Replace(match.Groups["value"].Value.Trim(), @"\s+", " ");
        }

        return "";
    }

    private static string DetectCheckType(string text, string path)
    {
        var source = $"{Path.GetFileName(path)}\n{text}";
        if (source.Contains("МЭЭ", StringComparison.OrdinalIgnoreCase)
            || source.Contains("медико-экономической экспертизы", StringComparison.OrdinalIgnoreCase)
            || source.Contains("медико-экономическая экспертиза", StringComparison.OrdinalIgnoreCase))
        {
            return "Экономическая";
        }

        if (source.Contains("ЭКМП", StringComparison.OrdinalIgnoreCase)
            || source.Contains("экспертизы качества медицинской помощи", StringComparison.OrdinalIgnoreCase)
            || source.Contains("экспертиза качества медицинской помощи", StringComparison.OrdinalIgnoreCase))
        {
            return "Экспертная";
        }

        return "";
    }

    private static string BuildCaseKey(InsuranceRecord record) =>
        BuildCaseKey(
            record.PolicyNumber,
            record.MedicalDocumentNumber,
            record.BirthDate,
            record.CarePeriod,
            record.ClaimNumber);

    private static string BuildCaseKey(string policyNumber, string medicalDocumentNumber, DateTime? birthDate, string carePeriod, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(policyNumber) && !string.IsNullOrWhiteSpace(medicalDocumentNumber))
        {
            return string.Join(
                "|",
                "case",
                policyNumber.Trim(),
                medicalDocumentNumber.Trim(),
                birthDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "",
                NormalizeCell(carePeriod));
        }

        return string.IsNullOrWhiteSpace(fallback)
            ? Guid.NewGuid().ToString("N")
            : $"fallback|{NormalizeCell(fallback)}";
    }

    private static string CarePeriod(DateTime startDate, DateTime endDate) =>
        $"с {startDate:dd.MM.yyyy} по {endDate:dd.MM.yyyy}";

    private static string FirstNonEmpty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static bool LooksLikePolicy(string value) =>
        Regex.IsMatch(value, @"^\d{10,}$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static bool IsInteger(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool TryParseDate(string value, out DateTime date) =>
        DateTime.TryParse(value, RussianCulture, DateTimeStyles.None, out date)
        || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryParseAmount(string value, out decimal amount)
    {
        var normalized = Regex.Replace(
                value
                    .Replace("руб.", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("рублей", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("руб", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("₽", "", StringComparison.OrdinalIgnoreCase),
                @"\s+",
                "")
            .Trim();

        return decimal.TryParse(normalized, NumberStyles.Number, RussianCulture, out amount)
            || decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static decimal? ParseAmountOrNull(string value) =>
        TryParseAmount(value, out var amount) ? amount : null;

    private static string NormalizeCell(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ");

    private static bool HasRequiredFields(InsuranceRecord record) =>
        !string.IsNullOrWhiteSpace(record.ClaimNumber)
        && record.EventDate.HasValue
        && !string.IsNullOrWhiteSpace(record.PolicyNumber)
        && (!string.IsNullOrWhiteSpace(record.ClientFullName)
            || !string.IsNullOrWhiteSpace(record.MedicalDocumentNumber));

    private sealed record SummaryCaseRow(
        int RowNumber,
        string CareProfile,
        string PolicyNumber,
        DateTime BirthDate,
        string MedicalDocumentNumber,
        string Diagnosis,
        DateTime StartDate,
        DateTime EndDate,
        decimal AcceptedAmount,
        string DefectCode,
        string DefectDescription,
        decimal? FinancialSanctionsAmount,
        decimal? PaymentReductionAmount,
        decimal? PenaltyAmount);
}
