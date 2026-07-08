using Curatio.Core;
using Microsoft.Data.Sqlite;

namespace Curatio.Infrastructure;

public sealed class SqliteRecordRepository(string databasePath) : IRecordRepository, ISettingsStore
{
    private const int CurrentExtractionVersion = 6;

    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS records (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                claim_number TEXT NOT NULL DEFAULT '',
                client_full_name TEXT NOT NULL DEFAULT '',
                event_date TEXT NULL,
                claim_type TEXT NOT NULL DEFAULT '',
                check_type TEXT NOT NULL DEFAULT '',
                policy_number TEXT NOT NULL DEFAULT '',
                insured_amount TEXT NULL,
                financial_sanctions_amount TEXT NULL,
                payment_reduction_amount TEXT NULL,
                penalty_amount TEXT NULL,
                event_description TEXT NOT NULL DEFAULT '',
                insurance_organization TEXT NOT NULL DEFAULT '',
                expert_name TEXT NOT NULL DEFAULT '',
                expert_specialty TEXT NOT NULL DEFAULT '',
                medical_document_number TEXT NOT NULL DEFAULT '',
                gender TEXT NOT NULL DEFAULT '',
                birth_date TEXT NULL,
                medical_organization TEXT NOT NULL DEFAULT '',
                examination_form TEXT NOT NULL DEFAULT '',
                examination_period TEXT NOT NULL DEFAULT '',
                care_form TEXT NOT NULL DEFAULT '',
                care_conditions TEXT NOT NULL DEFAULT '',
                care_profile TEXT NOT NULL DEFAULT '',
                care_period TEXT NOT NULL DEFAULT '',
                case_outcome TEXT NOT NULL DEFAULT '',
                primary_diagnosis TEXT NOT NULL DEFAULT '',
                diagnosis_complication TEXT NOT NULL DEFAULT '',
                comorbid_diagnosis TEXT NOT NULL DEFAULT '',
                operation TEXT NOT NULL DEFAULT '',
                clinical_statistical_group TEXT NOT NULL DEFAULT '',
                defect_code TEXT NOT NULL DEFAULT '',
                defect_description TEXT NOT NULL DEFAULT '',
                recommendations TEXT NOT NULL DEFAULT '',
                source_file_name TEXT NOT NULL,
                full_path TEXT NOT NULL,
                case_key TEXT NOT NULL DEFAULT '',
                file_size INTEGER NOT NULL,
                file_modified_at TEXT NOT NULL,
                processed_at TEXT NOT NULL,
                status INTEGER NOT NULL,
                error_message TEXT NULL,
                external_id TEXT NULL,
                send_status INTEGER NOT NULL DEFAULT 0,
                sent_at TEXT NULL,
                api_error_message TEXT NULL
                ,extraction_version INTEGER NOT NULL DEFAULT 0
            );
            DROP INDEX IF EXISTS ux_records_file_identity;
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureRecordColumnsAsync(connection, cancellationToken);
        await EnsureIndexesAsync(connection, cancellationToken);
    }

    public async Task<bool> IsImportedAsync(
        string path,
        long size,
        DateTime modifiedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1 FROM records
                WHERE full_path = $path
                  AND file_size = $size
                  AND file_modified_at = $modified
                  AND extraction_version >= $extractionVersion
            );
            """;
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$modified", modifiedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$extractionVersion", CurrentExtractionVersion);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task DeleteByPathsAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var normalizedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedPaths.Length == 0)
            return;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var path in normalizedPaths)
        {
            var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "DELETE FROM records WHERE full_path = $path;";
            command.Parameters.AddWithValue("$path", path);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var delete = connection.CreateCommand();
        delete.Transaction = (SqliteTransaction)transaction;
        delete.CommandText = "DELETE FROM records;";
        var deleted = await delete.ExecuteNonQueryAsync(cancellationToken);

        var resetIdentity = connection.CreateCommand();
        resetIdentity.Transaction = (SqliteTransaction)transaction;
        resetIdentity.CommandText = "DELETE FROM sqlite_sequence WHERE name = 'records';";
        await resetIdentity.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    public async Task SaveAsync(InsuranceRecord record, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO records (
                claim_number, client_full_name, event_date, claim_type, policy_number,
                check_type, insured_amount, financial_sanctions_amount, payment_reduction_amount,
                penalty_amount, event_description, insurance_organization, expert_name,
                expert_specialty, medical_document_number, gender, birth_date,
                medical_organization, examination_form, examination_period, care_form,
                care_conditions, care_profile, care_period, case_outcome, primary_diagnosis,
                diagnosis_complication, comorbid_diagnosis, operation,
                clinical_statistical_group, defect_code, defect_description, recommendations, source_file_name, full_path, case_key, file_size,
                file_modified_at, processed_at, status, error_message, external_id,
                send_status, sent_at, api_error_message, extraction_version
            ) VALUES (
                $claimNumber, $clientName, $eventDate, $claimType, $policyNumber,
                $checkType, $amount, $financialSanctionsAmount, $paymentReductionAmount,
                $penaltyAmount, $description, $insuranceOrganization, $expertName,
                $expertSpecialty, $medicalDocumentNumber, $gender, $birthDate,
                $medicalOrganization, $examinationForm, $examinationPeriod, $careForm,
                $careConditions, $careProfile, $carePeriod, $caseOutcome, $primaryDiagnosis,
                $diagnosisComplication, $comorbidDiagnosis, $operation,
                $clinicalStatisticalGroup, $defectCode, $defectDescription, $recommendations, $fileName, $path, $caseKey, $fileSize,
                $modified, $processed, $status, $error, $externalId,
                $sendStatus, $sentAt, $apiError, $extractionVersion
            )
            ON CONFLICT(full_path, file_size, file_modified_at, case_key) DO UPDATE SET
                claim_number = excluded.claim_number,
                client_full_name = excluded.client_full_name,
                event_date = excluded.event_date,
                claim_type = excluded.claim_type,
                check_type = excluded.check_type,
                policy_number = excluded.policy_number,
                insured_amount = excluded.insured_amount,
                financial_sanctions_amount = excluded.financial_sanctions_amount,
                payment_reduction_amount = excluded.payment_reduction_amount,
                penalty_amount = excluded.penalty_amount,
                event_description = excluded.event_description,
                insurance_organization = excluded.insurance_organization,
                expert_name = excluded.expert_name,
                expert_specialty = excluded.expert_specialty,
                medical_document_number = excluded.medical_document_number,
                gender = excluded.gender,
                birth_date = excluded.birth_date,
                medical_organization = excluded.medical_organization,
                examination_form = excluded.examination_form,
                examination_period = excluded.examination_period,
                care_form = excluded.care_form,
                care_conditions = excluded.care_conditions,
                care_profile = excluded.care_profile,
                care_period = excluded.care_period,
                case_outcome = excluded.case_outcome,
                primary_diagnosis = excluded.primary_diagnosis,
                diagnosis_complication = excluded.diagnosis_complication,
                comorbid_diagnosis = excluded.comorbid_diagnosis,
                operation = excluded.operation,
                clinical_statistical_group = excluded.clinical_statistical_group,
                defect_code = excluded.defect_code,
                defect_description = excluded.defect_description,
                recommendations = excluded.recommendations,
                processed_at = excluded.processed_at,
                status = excluded.status,
                error_message = excluded.error_message,
                extraction_version = excluded.extraction_version;
            SELECT id FROM records
            WHERE full_path = $path AND file_size = $fileSize AND file_modified_at = $modified AND case_key = $caseKey;
            """;
        AddParameters(command, record);
        record.Id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateAsync(InsuranceRecord record, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE records SET
                claim_number = $claimNumber,
                client_full_name = $clientName,
                event_date = $eventDate,
                claim_type = $claimType,
                check_type = $checkType,
                policy_number = $policyNumber,
                insured_amount = $amount,
                financial_sanctions_amount = $financialSanctionsAmount,
                payment_reduction_amount = $paymentReductionAmount,
                penalty_amount = $penaltyAmount,
                event_description = $description,
                insurance_organization = $insuranceOrganization,
                expert_name = $expertName,
                expert_specialty = $expertSpecialty,
                medical_document_number = $medicalDocumentNumber,
                gender = $gender,
                birth_date = $birthDate,
                medical_organization = $medicalOrganization,
                examination_form = $examinationForm,
                examination_period = $examinationPeriod,
                care_form = $careForm,
                care_conditions = $careConditions,
                care_profile = $careProfile,
                care_period = $carePeriod,
                case_outcome = $caseOutcome,
                primary_diagnosis = $primaryDiagnosis,
                diagnosis_complication = $diagnosisComplication,
                comorbid_diagnosis = $comorbidDiagnosis,
                operation = $operation,
                clinical_statistical_group = $clinicalStatisticalGroup,
                defect_code = $defectCode,
                defect_description = $defectDescription,
                recommendations = $recommendations,
                status = $status,
                error_message = $error,
                external_id = $externalId,
                send_status = $sendStatus,
                sent_at = $sentAt,
                api_error_message = $apiError
            WHERE id = $id;
            """;
        AddParameters(command, record);
        command.Parameters.AddWithValue("$id", record.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InsuranceRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var records = new List<InsuranceRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM records ORDER BY processed_at DESC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            records.Add(ReadRecord(reader));

        return records;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO settings(key, value) VALUES($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameters(SqliteCommand command, InsuranceRecord record)
    {
        command.Parameters.AddWithValue("$claimNumber", record.ClaimNumber);
        command.Parameters.AddWithValue("$clientName", record.ClientFullName);
        command.Parameters.AddWithValue("$eventDate", DbDateValue(record.EventDate));
        command.Parameters.AddWithValue("$claimType", record.ClaimType);
        command.Parameters.AddWithValue("$checkType", record.CheckType);
        command.Parameters.AddWithValue("$policyNumber", record.PolicyNumber);
        command.Parameters.AddWithValue("$amount", record.InsuredAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$financialSanctionsAmount", record.FinancialSanctionsAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$paymentReductionAmount", record.PaymentReductionAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$penaltyAmount", record.PenaltyAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$description", record.EventDescription);
        command.Parameters.AddWithValue("$insuranceOrganization", record.InsuranceOrganization);
        command.Parameters.AddWithValue("$expertName", record.ExpertName);
        command.Parameters.AddWithValue("$expertSpecialty", record.ExpertSpecialty);
        command.Parameters.AddWithValue("$medicalDocumentNumber", record.MedicalDocumentNumber);
        command.Parameters.AddWithValue("$gender", record.Gender);
        command.Parameters.AddWithValue("$birthDate", DbDateValue(record.BirthDate));
        command.Parameters.AddWithValue("$medicalOrganization", record.MedicalOrganization);
        command.Parameters.AddWithValue("$examinationForm", record.ExaminationForm);
        command.Parameters.AddWithValue("$examinationPeriod", record.ExaminationPeriod);
        command.Parameters.AddWithValue("$careForm", record.CareForm);
        command.Parameters.AddWithValue("$careConditions", record.CareConditions);
        command.Parameters.AddWithValue("$careProfile", record.CareProfile);
        command.Parameters.AddWithValue("$carePeriod", record.CarePeriod);
        command.Parameters.AddWithValue("$caseOutcome", record.CaseOutcome);
        command.Parameters.AddWithValue("$primaryDiagnosis", record.PrimaryDiagnosis);
        command.Parameters.AddWithValue("$diagnosisComplication", record.DiagnosisComplication);
        command.Parameters.AddWithValue("$comorbidDiagnosis", record.ComorbidDiagnosis);
        command.Parameters.AddWithValue("$operation", record.Operation);
        command.Parameters.AddWithValue("$clinicalStatisticalGroup", record.ClinicalStatisticalGroup);
        command.Parameters.AddWithValue("$defectCode", record.DefectCode);
        command.Parameters.AddWithValue("$defectDescription", record.DefectDescription);
        command.Parameters.AddWithValue("$recommendations", record.Recommendations);
        command.Parameters.AddWithValue("$fileName", record.SourceFileName);
        command.Parameters.AddWithValue("$path", record.FullPath);
        command.Parameters.AddWithValue("$caseKey", record.CaseKey);
        command.Parameters.AddWithValue("$fileSize", record.FileSize);
        command.Parameters.AddWithValue("$modified", record.FileModifiedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$processed", record.ProcessedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$status", (int)record.Status);
        command.Parameters.AddWithValue("$error", record.ErrorMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$externalId", record.ExternalId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$sendStatus", (int)record.SendStatus);
        command.Parameters.AddWithValue("$sentAt", DbValue(record.SentAt));
        command.Parameters.AddWithValue("$apiError", record.ApiErrorMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$extractionVersion", CurrentExtractionVersion);
    }

    private static object DbValue(DateTime? value) =>
        value?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value;

    private static object DbDateValue(DateTime? value) =>
        value?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        ?? (object)DBNull.Value;

    private static InsuranceRecord ReadRecord(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        ClaimNumber = reader.GetString(reader.GetOrdinal("claim_number")),
        ClientFullName = reader.GetString(reader.GetOrdinal("client_full_name")),
        EventDate = ReadDateOnly(reader, "event_date"),
        ClaimType = reader.GetString(reader.GetOrdinal("claim_type")),
        CheckType = reader.GetString(reader.GetOrdinal("check_type")),
        PolicyNumber = reader.GetString(reader.GetOrdinal("policy_number")),
        InsuredAmount = ReadDecimal(reader, "insured_amount"),
        FinancialSanctionsAmount = ReadDecimal(reader, "financial_sanctions_amount"),
        PaymentReductionAmount = ReadDecimal(reader, "payment_reduction_amount"),
        PenaltyAmount = ReadDecimal(reader, "penalty_amount"),
        EventDescription = reader.GetString(reader.GetOrdinal("event_description")),
        InsuranceOrganization = reader.GetString(reader.GetOrdinal("insurance_organization")),
        ExpertName = reader.GetString(reader.GetOrdinal("expert_name")),
        ExpertSpecialty = reader.GetString(reader.GetOrdinal("expert_specialty")),
        MedicalDocumentNumber = reader.GetString(reader.GetOrdinal("medical_document_number")),
        Gender = reader.GetString(reader.GetOrdinal("gender")),
        BirthDate = ReadDateOnly(reader, "birth_date"),
        MedicalOrganization = reader.GetString(reader.GetOrdinal("medical_organization")),
        ExaminationForm = reader.GetString(reader.GetOrdinal("examination_form")),
        ExaminationPeriod = reader.GetString(reader.GetOrdinal("examination_period")),
        CareForm = reader.GetString(reader.GetOrdinal("care_form")),
        CareConditions = reader.GetString(reader.GetOrdinal("care_conditions")),
        CareProfile = reader.GetString(reader.GetOrdinal("care_profile")),
        CarePeriod = reader.GetString(reader.GetOrdinal("care_period")),
        CaseOutcome = reader.GetString(reader.GetOrdinal("case_outcome")),
        PrimaryDiagnosis = reader.GetString(reader.GetOrdinal("primary_diagnosis")),
        DiagnosisComplication = reader.GetString(reader.GetOrdinal("diagnosis_complication")),
        ComorbidDiagnosis = reader.GetString(reader.GetOrdinal("comorbid_diagnosis")),
        Operation = reader.GetString(reader.GetOrdinal("operation")),
        ClinicalStatisticalGroup = reader.GetString(reader.GetOrdinal("clinical_statistical_group")),
        DefectCode = reader.GetString(reader.GetOrdinal("defect_code")),
        DefectDescription = reader.GetString(reader.GetOrdinal("defect_description")),
        Recommendations = reader.GetString(reader.GetOrdinal("recommendations")),
        SourceFileName = reader.GetString(reader.GetOrdinal("source_file_name")),
        FullPath = reader.GetString(reader.GetOrdinal("full_path")),
        CaseKey = reader.GetString(reader.GetOrdinal("case_key")),
        FileSize = reader.GetInt64(reader.GetOrdinal("file_size")),
        FileModifiedAt = ReadDate(reader, "file_modified_at") ?? DateTime.MinValue,
        ProcessedAt = ReadDate(reader, "processed_at") ?? DateTime.MinValue,
        Status = (DocumentStatus)reader.GetInt32(reader.GetOrdinal("status")),
        ErrorMessage = ReadNullableString(reader, "error_message"),
        ExternalId = ReadNullableString(reader, "external_id"),
        SendStatus = (SendStatus)reader.GetInt32(reader.GetOrdinal("send_status")),
        SentAt = ReadDate(reader, "sent_at"),
        ApiErrorMessage = ReadNullableString(reader, "api_error_message")
    };

    private static DateTime? ReadDate(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : DateTime.Parse(reader.GetString(ordinal), null, System.Globalization.DateTimeStyles.RoundtripKind);
    }

    private static DateTime? ReadDateOnly(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal))
            return null;

        var value = DateTime.Parse(
            reader.GetString(ordinal),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        return value.Kind == DateTimeKind.Utc ? value.ToLocalTime().Date : value.Date;
    }

    private static decimal? ReadDecimal(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : decimal.Parse(reader.GetString(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? ReadNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static async Task EnsureRecordColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var schema = connection.CreateCommand();
        schema.CommandText = "PRAGMA table_info(records);";
        await using (var reader = await schema.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                existing.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        var columns = new Dictionary<string, string>
        {
            ["check_type"] = "TEXT NOT NULL DEFAULT ''",
            ["financial_sanctions_amount"] = "TEXT NULL",
            ["payment_reduction_amount"] = "TEXT NULL",
            ["penalty_amount"] = "TEXT NULL",
            ["insurance_organization"] = "TEXT NOT NULL DEFAULT ''",
            ["expert_name"] = "TEXT NOT NULL DEFAULT ''",
            ["expert_specialty"] = "TEXT NOT NULL DEFAULT ''",
            ["medical_document_number"] = "TEXT NOT NULL DEFAULT ''",
            ["gender"] = "TEXT NOT NULL DEFAULT ''",
            ["birth_date"] = "TEXT NULL",
            ["medical_organization"] = "TEXT NOT NULL DEFAULT ''",
            ["examination_form"] = "TEXT NOT NULL DEFAULT ''",
            ["examination_period"] = "TEXT NOT NULL DEFAULT ''",
            ["care_form"] = "TEXT NOT NULL DEFAULT ''",
            ["care_conditions"] = "TEXT NOT NULL DEFAULT ''",
            ["care_profile"] = "TEXT NOT NULL DEFAULT ''",
            ["care_period"] = "TEXT NOT NULL DEFAULT ''",
            ["case_outcome"] = "TEXT NOT NULL DEFAULT ''",
            ["primary_diagnosis"] = "TEXT NOT NULL DEFAULT ''",
            ["diagnosis_complication"] = "TEXT NOT NULL DEFAULT ''",
            ["comorbid_diagnosis"] = "TEXT NOT NULL DEFAULT ''",
            ["operation"] = "TEXT NOT NULL DEFAULT ''",
            ["clinical_statistical_group"] = "TEXT NOT NULL DEFAULT ''",
            ["defect_code"] = "TEXT NOT NULL DEFAULT ''",
            ["defect_description"] = "TEXT NOT NULL DEFAULT ''",
            ["recommendations"] = "TEXT NOT NULL DEFAULT ''",
            ["case_key"] = "TEXT NOT NULL DEFAULT ''",
            ["extraction_version"] = "INTEGER NOT NULL DEFAULT 0"
        };

        foreach (var (name, definition) in columns)
        {
            if (existing.Contains(name))
                continue;

            var migration = connection.CreateCommand();
            migration.CommandText = $"ALTER TABLE records ADD COLUMN {name} {definition};";
            await migration.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureIndexesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var drop = connection.CreateCommand();
        drop.CommandText = "DROP INDEX IF EXISTS ux_records_file_identity;";
        await drop.ExecuteNonQueryAsync(cancellationToken);

        var create = connection.CreateCommand();
        create.CommandText =
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_records_file_case_identity
                ON records(full_path, file_size, file_modified_at, case_key);
            """;
        await create.ExecuteNonQueryAsync(cancellationToken);
    }
}
