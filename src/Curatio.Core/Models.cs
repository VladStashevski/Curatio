using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Curatio.Core;

public enum DocumentStatus
{
    Unprocessed,
    Processed,
    NeedsReview,
    Error
}

public enum SendStatus
{
    NotSent,
    Pending,
    Sent,
    Failed
}

public sealed class InsuranceRecord : INotifyPropertyChanged
{
    private long _id;
    private string _claimNumber = "";
    private string _clientFullName = "";
    private DateTime? _eventDate;
    private string _claimType = "";
    private string _policyNumber = "";
    private decimal? _insuredAmount;
    private string _eventDescription = "";
    private string _insuranceOrganization = "";
    private string _expertName = "";
    private string _expertSpecialty = "";
    private string _medicalDocumentNumber = "";
    private string _gender = "";
    private DateTime? _birthDate;
    private string _medicalOrganization = "";
    private string _examinationForm = "";
    private string _examinationPeriod = "";
    private string _careForm = "";
    private string _careConditions = "";
    private string _careProfile = "";
    private string _carePeriod = "";
    private string _caseOutcome = "";
    private string _primaryDiagnosis = "";
    private string _diagnosisComplication = "";
    private string _comorbidDiagnosis = "";
    private string _operation = "";
    private string _clinicalStatisticalGroup = "";
    private string _recommendations = "";
    private DocumentStatus _status;
    private string? _errorMessage;

    public long Id { get => _id; set => SetField(ref _id, value); }
    public string ClaimNumber { get => _claimNumber; set => SetField(ref _claimNumber, value); }
    public string ClientFullName { get => _clientFullName; set => SetField(ref _clientFullName, value); }
    public DateTime? EventDate { get => _eventDate; set => SetField(ref _eventDate, value); }
    public string ClaimType { get => _claimType; set => SetField(ref _claimType, value); }
    public string PolicyNumber { get => _policyNumber; set => SetField(ref _policyNumber, value); }
    public decimal? InsuredAmount { get => _insuredAmount; set => SetField(ref _insuredAmount, value); }
    public string EventDescription { get => _eventDescription; set => SetField(ref _eventDescription, value); }
    public string InsuranceOrganization { get => _insuranceOrganization; set => SetField(ref _insuranceOrganization, value); }
    public string ExpertName { get => _expertName; set => SetField(ref _expertName, value); }
    public string ExpertSpecialty { get => _expertSpecialty; set => SetField(ref _expertSpecialty, value); }
    public string MedicalDocumentNumber { get => _medicalDocumentNumber; set => SetField(ref _medicalDocumentNumber, value); }
    public string Gender { get => _gender; set => SetField(ref _gender, value); }
    public DateTime? BirthDate { get => _birthDate; set => SetField(ref _birthDate, value); }
    public string MedicalOrganization { get => _medicalOrganization; set => SetField(ref _medicalOrganization, value); }
    public string ExaminationForm { get => _examinationForm; set => SetField(ref _examinationForm, value); }
    public string ExaminationPeriod { get => _examinationPeriod; set => SetField(ref _examinationPeriod, value); }
    public string CareForm { get => _careForm; set => SetField(ref _careForm, value); }
    public string CareConditions { get => _careConditions; set => SetField(ref _careConditions, value); }
    public string CareProfile { get => _careProfile; set => SetField(ref _careProfile, value); }
    public string CarePeriod { get => _carePeriod; set => SetField(ref _carePeriod, value); }
    public string CaseOutcome { get => _caseOutcome; set => SetField(ref _caseOutcome, value); }
    public string PrimaryDiagnosis { get => _primaryDiagnosis; set => SetField(ref _primaryDiagnosis, value); }
    public string DiagnosisComplication { get => _diagnosisComplication; set => SetField(ref _diagnosisComplication, value); }
    public string ComorbidDiagnosis { get => _comorbidDiagnosis; set => SetField(ref _comorbidDiagnosis, value); }
    public string Operation { get => _operation; set => SetField(ref _operation, value); }
    public string ClinicalStatisticalGroup { get => _clinicalStatisticalGroup; set => SetField(ref _clinicalStatisticalGroup, value); }
    public string Recommendations { get => _recommendations; set => SetField(ref _recommendations, value); }
    public string SourceFileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime FileModifiedAt { get; set; }
    public DateTime ProcessedAt { get; set; }
    public DocumentStatus Status { get => _status; set => SetField(ref _status, value); }
    public string? ErrorMessage { get => _errorMessage; set => SetField(ref _errorMessage, value); }
    public string? ExternalId { get; set; }
    public SendStatus SendStatus { get; set; }
    public DateTime? SentAt { get; set; }
    public string? ApiErrorMessage { get; set; }

    public string EventDateText
    {
        get => EventDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "";
        set => EventDate = DateTime.TryParse(value, new CultureInfo("ru-RU"), DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    public string InsuredAmountText
    {
        get => InsuredAmount?.ToString("0.##", CultureInfo.GetCultureInfo("ru-RU")) ?? "";
        set
        {
            var normalized = value.Replace(" ", "", StringComparison.Ordinal);
            InsuredAmount = decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.GetCultureInfo("ru-RU"), out var amount)
                || decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)
                ? amount
                : null;
        }
    }

    public string BirthDateText
    {
        get => BirthDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "";
        set => BirthDate = DateTime.TryParse(value, new CultureInfo("ru-RU"), DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ProcessingProgress(int Processed, int Total, string CurrentFile);

public sealed record ProcessingLog(DateTime Timestamp, string FileName, string Message);

public sealed record ScanResult(
    IReadOnlyList<InsuranceRecord> Records,
    IReadOnlyList<ProcessingLog> Logs,
    int SkippedDuplicates);

public sealed class ExtractionRuleSet
{
    public Dictionary<string, string[]> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ApiSendResult(bool Success, string? ExternalId = null, string? ErrorMessage = null);
