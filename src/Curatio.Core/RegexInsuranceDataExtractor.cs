using System.Globalization;
using System.Text.RegularExpressions;

namespace Curatio.Core;

public sealed class RegexInsuranceDataExtractor(ExtractionRuleSet rules) : IInsuranceDataExtractor
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public InsuranceRecord Extract(string text, string path, long size, DateTime modifiedAt)
    {
        var record = new InsuranceRecord
        {
            ClaimNumber = Match("claimNumber", text),
            ClientFullName = Match("clientFullName", text),
            ClaimType = Match("claimType", text),
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

        var amountText = Match("insuredAmount", text)
            .Replace("руб.", "", StringComparison.OrdinalIgnoreCase)
            .Replace("руб", "", StringComparison.OrdinalIgnoreCase)
            .Replace("₽", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.Ordinal);
        if (decimal.TryParse(amountText, NumberStyles.Number, RussianCulture, out var amount)
            || decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
        {
            record.InsuredAmount = amount;
        }

        if (string.IsNullOrWhiteSpace(record.ClaimType))
            record.ClaimType = string.IsNullOrWhiteSpace(record.ExaminationForm)
                ? record.CareForm
                : record.ExaminationForm;

        record.Status = HasRequiredFields(record) ? DocumentStatus.Processed : DocumentStatus.NeedsReview;
        return record;
    }

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

    private static bool HasRequiredFields(InsuranceRecord record) =>
        !string.IsNullOrWhiteSpace(record.ClaimNumber)
        && record.EventDate.HasValue
        && !string.IsNullOrWhiteSpace(record.PolicyNumber)
        && (!string.IsNullOrWhiteSpace(record.ClientFullName)
            || !string.IsNullOrWhiteSpace(record.MedicalDocumentNumber));
}
