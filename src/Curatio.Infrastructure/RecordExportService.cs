using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Curatio.Core;

namespace Curatio.Infrastructure;

public sealed class RecordExportService : IRecordExportService
{
    private static readonly string[] Headers =
    [
        "Номер заключения", "Дата заключения", "ФИО застрахованного", "Номер полиса",
        "Номер меддокумента", "Пол", "Дата рождения", "Страховая организация",
        "Медицинская организация", "Эксперт", "Специальность эксперта",
        "Форма экспертизы", "Вид проверки", "Срок экспертизы", "Форма помощи", "Условия помощи",
        "Профиль помощи", "Период помощи", "Исход случая", "Основной диагноз",
        "Осложнение диагноза", "Сопутствующие диагнозы", "Операция", "КСГ",
        "Код дефекта", "Дефект", "Выводы", "Рекомендации", "Сумма случая", "Финансовые санкции",
        "Неоплата/уменьшение", "Штраф", "Исходный файл",
        "Полный путь", "Дата обработки", "Статус"
    ];

    public Task ExportXlsxAsync(IEnumerable<InsuranceRecord> records, string path, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Страховые случаи");
            for (var column = 0; column < Headers.Length; column++)
                sheet.Cell(1, column + 1).Value = Headers[column];

            var row = 2;
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = Values(record);
                for (var column = 0; column < values.Length; column++)
                    sheet.Cell(row, column + 1).Value = XLCellValue.FromObject(values[column]);
                row++;
            }

            var header = sheet.Range(1, 1, 1, Headers.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBE8");
            var amountStart = Array.IndexOf(Headers, "Сумма случая") + 1;
            var amountEnd = Array.IndexOf(Headers, "Штраф") + 1;
            sheet.Columns(amountStart, amountEnd).Style.NumberFormat.Format = "#,##0.00";
            sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents(8, 50);
            sheet.RangeUsed()?.SetAutoFilter();
            workbook.SaveAs(path);
        }, cancellationToken);

    public async Task ExportCsvAsync(IEnumerable<InsuranceRecord> records, string path, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        await writer.WriteLineAsync(string.Join(';', Headers.Select(Escape)));
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(';', Values(record).Select(value => Escape(Convert.ToString(value, CultureInfo.GetCultureInfo("ru-RU")) ?? ""))));
        }
    }

    private static object[] Values(InsuranceRecord record) =>
    [
        record.ClaimNumber,
        record.EventDate?.ToString("dd.MM.yyyy") ?? "",
        record.ClientFullName,
        record.PolicyNumber,
        record.MedicalDocumentNumber,
        record.Gender,
        record.BirthDate?.ToString("dd.MM.yyyy") ?? "",
        record.InsuranceOrganization,
        record.MedicalOrganization,
        record.ExpertName,
        record.ExpertSpecialty,
        record.ExaminationForm,
        record.CheckType,
        record.ExaminationPeriod,
        record.CareForm,
        record.CareConditions,
        record.CareProfile,
        record.CarePeriod,
        record.CaseOutcome,
        record.PrimaryDiagnosis,
        record.DiagnosisComplication,
        record.ComorbidDiagnosis,
        record.Operation,
        record.ClinicalStatisticalGroup,
        record.DefectCode,
        record.DefectDescription,
        record.EventDescription,
        record.Recommendations,
        record.InsuredAmount ?? (object)"",
        record.FinancialSanctionsAmount ?? (object)"",
        record.PaymentReductionAmount ?? (object)"",
        record.PenaltyAmount ?? (object)"",
        record.SourceFileName,
        record.FullPath,
        record.ProcessedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
        StatusText(record.Status)
    ];

    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string StatusText(DocumentStatus status) => status switch
    {
        DocumentStatus.Unprocessed => "Не обработан",
        DocumentStatus.Processed => "Обработан",
        DocumentStatus.NeedsReview => "Требует проверки",
        DocumentStatus.Error => "Ошибка",
        _ => status.ToString()
    };
}
