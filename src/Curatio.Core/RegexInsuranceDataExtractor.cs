using System.Globalization;
using System.Text.RegularExpressions;

namespace Curatio.Core;

public sealed class RegexInsuranceDataExtractor(ExtractionRuleSet rules) : IInsuranceDataExtractor
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private const string DefectCodeLabelPattern =
        @"(?:(?:код(?:ом)?(?:\(-ами\))?|коды?)\s+)?(?:нарушения\s*\(\s*)?дефект(?:а(?:\(-ов\))?|ов|\(-ов\))\)?\s*[:№\-–—]?\s*(?<code>\d+(?:\.\d+)+)";
    private const string DefectLabelWithoutCodePattern =
        @"(?:(?:код(?:ом)?(?:\(-ами\))?|коды?)\s+)?(?:нарушения\s*\(\s*)?дефект(?:а(?:\(-ов\))?|ов|\(-ов\))\)?\s*[:№\-–—]?\s*$";

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
        var defect = ExtractDefectDetails(text, path);
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
            DefectCode = defect.Code,
            DefectDescription = defect.Description,
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
        var rowDefectCode = NormalizeDefectCode(row.DefectCode);
        var rowDefectDescription = NormalizeDefectDescription(row.DefectDescription);
        record.DefectCode = rowDefectCode;
        record.DefectDescription = !string.IsNullOrWhiteSpace(rowDefectDescription)
            ? rowDefectDescription
            : SameDefectCode(baseRecord.DefectCode, rowDefectCode)
                ? baseRecord.DefectDescription
                : "";

        if (!string.IsNullOrWhiteSpace(record.DefectDescription))
        {
            record.EventDescription = record.DefectDescription;
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
        DefectCode = source.DefectCode,
        DefectDescription = source.DefectDescription,
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
            var defectCode = NormalizeDefectCode(row[3]);
            var defectDescription = NormalizeDefectDescription(row[4]);

            record.PaymentReductionAmount = paymentReduction;
            record.PenaltyAmount = penalty;
            record.FinancialSanctionsAmount = paymentReduction + (penalty ?? 0m);

            if (!string.IsNullOrWhiteSpace(defectCode))
            {
                if (!SameDefectCode(record.DefectCode, defectCode)
                    && string.IsNullOrWhiteSpace(defectDescription))
                {
                    record.DefectDescription = "";
                }

                record.DefectCode = defectCode;
            }

            if (!string.IsNullOrWhiteSpace(defectDescription))
                record.DefectDescription = defectDescription;
            if (string.IsNullOrWhiteSpace(record.EventDescription) && !string.IsNullOrWhiteSpace(record.DefectDescription))
                record.EventDescription = record.DefectDescription;
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
        var fileName = Path.GetFileName(path);
        if (fileName.Contains("ЭКМП", StringComparison.OrdinalIgnoreCase))
            return "Экспертная";
        if (fileName.Contains("МЭЭ", StringComparison.OrdinalIgnoreCase))
            return "Экономическая";

        var qualityIndex = FirstIndexOf(
            text,
            "экспертизы качества медицинской помощи",
            "экспертиза качества медицинской помощи");
        var economicIndex = FirstIndexOf(
            text,
            "медико-экономической экспертизы",
            "медико-экономическая экспертиза");

        if (qualityIndex >= 0 && (economicIndex < 0 || qualityIndex < economicIndex))
            return "Экспертная";
        if (economicIndex >= 0)
            return "Экономическая";

        return "";
    }

    private static int FirstIndexOf(string source, params string[] values) =>
        values
            .Select(value => source.IndexOf(value, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();

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

    private static DefectDetails ExtractDefectDetails(string text, string path)
    {
        var preferredCode = ExtractDefectCodeFromFileName(path);
        var paragraphLines = LogicalLines(text, tableRows: false).ToArray();
        var tableLines = LogicalLines(text, tableRows: true).ToArray();
        var scopes = new[]
        {
            BeforeConclusions(paragraphLines),
            paragraphLines,
            BeforeConclusions(tableLines),
            tableLines
        };

        var codeOnlyDetails = new DefectDetails("", "");
        foreach (var scope in scopes)
        {
            if (scope.Length == 0)
                continue;

            var details = ExtractDefectDetails(scope, preferredCode);
            if (!string.IsNullOrWhiteSpace(details.Description))
                return details;
            if (string.IsNullOrWhiteSpace(codeOnlyDetails.Code) && !string.IsNullOrWhiteSpace(details.Code))
                codeOnlyDetails = details;
        }

        if (!string.IsNullOrWhiteSpace(codeOnlyDetails.Code))
            return codeOnlyDetails;

        var allLines = paragraphLines
            .Concat(tableLines)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ContainsExplicitNoDefectStatement(allLines))
            return new DefectDetails("", "");

        return new DefectDetails(preferredCode, "");
    }

    private static DefectDetails ExtractDefectDetails(IReadOnlyList<string> lines, string preferredCode)
    {
        var candidates = new List<DefectDetails>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (IsNoDefectValue(line))
                continue;

            var matches = Regex.Matches(
                line,
                DefectCodeLabelPattern,
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(1));
            foreach (Match match in matches)
            {
                var code = NormalizeDefectCode(match.Groups["code"].Value);
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                candidates.Add(new DefectDetails(code, ExtractCandidateDescription(line, match)));
            }

            if (matches.Count > 0
                || index + 1 >= lines.Count
                || !Regex.IsMatch(
                    line,
                    DefectLabelWithoutCodePattern,
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(1))
                || !LooksLikeStandaloneDefectCode(lines[index + 1]))
            {
                continue;
            }

            var splitCode = NormalizeDefectCode(lines[index + 1]);
            var splitDescription = index > 0 && IsSubstantiveDescription(lines[index - 1])
                ? NormalizeDefectDescription(lines[index - 1])
                : "";
            candidates.Add(new DefectDetails(splitCode, splitDescription));
        }

        var eligible = string.IsNullOrWhiteSpace(preferredCode)
            ? candidates
            : candidates
                .Where(candidate => SameDefectCode(candidate.Code, preferredCode))
                .ToList();
        return eligible.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate.Description))
            ?? eligible.FirstOrDefault()
            ?? new DefectDetails("", "");
    }

    private static string[] BeforeConclusions(string[] lines)
    {
        var conclusionsIndex = Array.FindIndex(
            lines,
            line => Regex.IsMatch(
                line,
                @"^(?:(?:[IVXLCDM]+|\d+)\s*[.)]?\s*)?Выводы\b",
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(1)));
        return conclusionsIndex < 0 ? lines : lines.Take(conclusionsIndex).ToArray();
    }

    private static string ExtractCandidateDescription(string line, Match match)
    {
        var before = NormalizeDefectDescription(line[..match.Index]);
        var after = NormalizeDefectDescription(line[(match.Index + match.Length)..]);

        if (IsSubstantiveDescription(after) && !IsGenericConclusionDescription(after))
            return after;
        if (IsSubstantiveDescription(before))
            return before;
        return "";
    }

    private static bool IsSubstantiveDescription(string value)
    {
        var normalized = NormalizeDefectDescription(value);
        if (normalized.Length < 8
            || IsReferenceOnlyDescription(normalized)
            || IsNoDefectValue(normalized)
            || IsGenericConclusionDescription(normalized))
        {
            return false;
        }

        if (Regex.IsMatch(
                normalized,
                @"^(?:\d+(?:\.\d+)?\)\s*)?(?:сбор информации|диагноз|оказание медицинской помощи|преемственность|заключение)\b[^.]*:?$",
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(1)))
        {
            return false;
        }

        return normalized.Any(char.IsLetter);
    }

    private static bool IsGenericConclusionDescription(string value)
    {
        var normalized = NormalizeCell(value);
        return Regex.IsMatch(
                   normalized,
                   @"^(?:применить|установить|выявлено|выявлены|код|дефект|нарушение)\.?$",
                   RegexOptions.IgnoreCase,
                   TimeSpan.FromSeconds(1))
            || (normalized.StartsWith("Медицинская помощь оказана", StringComparison.OrdinalIgnoreCase)
                && (normalized.Contains("квалифицируем", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("выявлено", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool LooksLikeStandaloneDefectCode(string value) =>
        Regex.IsMatch(
            NormalizeCell(value),
            @"^(?:код\s*)?[:№\-–—]?\s*\d+(?:\.\d+)+\.?$",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

    private static bool ContainsExplicitNoDefectStatement(IEnumerable<string> lines) =>
        lines.Any(line => Regex.IsMatch(
            line,
            @"(?:дефект(?:ы|ов)?\b.{0,200}\bне\s+выявлен|нарушени(?:я|й)\b.{0,200}\bне\s+выявлен|дефект(?:ы|ов)?\s+отсутствуют|без\s+дефектов)",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1)));

    private static IEnumerable<string> LogicalLines(string text, bool tableRows) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(DocumentTextMarkers.TableRowPrefix, StringComparison.Ordinal) == tableRows)
            .Select(line => tableRows
                ? line[DocumentTextMarkers.TableRowPrefix.Length..].Replace('\t', ' ')
                : line)
            .Select(NormalizeCell)
            .Where(line => !string.IsNullOrWhiteSpace(line));

    private static string NormalizeDefectCode(string value)
    {
        if (IsNoDefectValue(value))
            return "";

        var match = Regex.Match(value, @"\d+(?:\.\d+)+", RegexOptions.None, TimeSpan.FromSeconds(1));
        return match.Success ? match.Value : "";
    }

    private static string NormalizeDefectDescription(string value)
    {
        var normalized = NormalizeCell(value);
        normalized = Regex.Replace(normalized, @"^\d+(?:\.\d+)?\)\s*[^:]+:\s*", "", RegexOptions.None, TimeSpan.FromSeconds(1));
        normalized = Regex.Replace(normalized, @"^[-–—:;.,\s]+", "", RegexOptions.None, TimeSpan.FromSeconds(1));
        normalized = Regex.Replace(normalized, @"[-–—:;.,\s]+$", "", RegexOptions.None, TimeSpan.FromSeconds(1));
        normalized = Regex.Replace(
            normalized,
            @"\s*(?:код(?:ом)?(?:\(-ами\))?\s+)?дефекта(?:\(-ов\))?\s*:?\s*\d+(?:\.\d+)+\.?$",
            "",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1)).Trim();

        return IsNoDefectValue(normalized) || IsReferenceOnlyDescription(normalized)
            ? ""
            : normalized;
    }

    private static bool SameDefectCode(string left, string right)
    {
        var normalizedLeft = NormalizeDefectCode(left);
        var normalizedRight = NormalizeDefectCode(right);
        return !string.IsNullOrWhiteSpace(normalizedLeft)
            && normalizedLeft.Equals(normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReferenceOnlyDescription(string value)
    {
        var normalized = NormalizeCell(value);
        return Regex.IsMatch(
                   normalized,
                   @"^(?:см\.?\s*)?(?:сноск\w*|примечани\w*|пояснени\w*)(?:\s*(?:№|N)?\s*[\d.,;()\s\-–—]+)?\.?$",
                   RegexOptions.IgnoreCase,
                   TimeSpan.FromSeconds(1))
            || Regex.IsMatch(
                normalized,
                @"^см\.?\s*(?:ниже|выше|в\s+приложени\w*)(?:\s*№?\s*\d+)?\.?$",
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(1));
    }

    private static string ExtractDefectCodeFromFileName(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var match = Regex.Match(
            fileName,
            @"(?:^|[_\s-])(?:ПД|ПБД|ВД|ВБД)[_\s-]*(?<code>\d+(?:\.\d+)+)\.?(?:[_\s-]|$)",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["code"].Value : "";
    }

    private static bool IsNoDefectValue(string value)
    {
        var normalized = NormalizeCell(value);
        return string.IsNullOrWhiteSpace(normalized)
            || Regex.IsMatch(normalized, @"^\d+$", RegexOptions.None, TimeSpan.FromSeconds(1))
            || normalized.Equals("Нет", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Не выявлено", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Замечаний нет", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Информация отсутствует", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("дефектов не выявлено", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(
                normalized,
                @"(?:дефект(?:ы|ов)?\b.{0,200}\bне\s+выявлен|дефект(?:ы|ов)?\s+отсутствуют|без\s+дефектов)",
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(1));
    }

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

    private sealed record DefectDetails(string Code, string Description);
}
