using Curatio.Core;

namespace Curatio.Tests;

public sealed class ExtractionTests
{
    private readonly RegexInsuranceDataExtractor _extractor = new(TestDocumentFactory.Rules());

    [Theory]
    [InlineData("Номер страхового случая: CLM-2026-001", "ФИО клиента: Иванов Тест Тестович")]
    [InlineData("Номер убытка № LOSS/77", "Страхователь - Петров Тест Петрович")]
    public void ExtractsFieldsFromDifferentLabels(string claimLine, string clientLine)
    {
        var text =
            $"""
            {claimLine}
            {clientLine}
            Дата происшествия: 10.06.2026
            Тип страхового случая: Повреждение имущества
            Полис № POL-100
            Страховая сумма: 125 000,50
            Описание события: Синтетический тестовый случай
            """;

        var record = _extractor.Extract(text, "test.docx", 100, DateTime.UtcNow);

        Assert.NotEmpty(record.ClaimNumber);
        Assert.Contains("Тест", record.ClientFullName);
        Assert.Equal(new DateTime(2026, 6, 10), record.EventDate);
        Assert.Equal(125000.50m, record.InsuredAmount);
        Assert.Equal(DocumentStatus.Processed, record.Status);
    }

    [Fact]
    public void MissingRequiredFieldsMarksRecordForReview()
    {
        var record = _extractor.Extract(
            "Описание события: Недостаточно данных",
            "incomplete.docx",
            10,
            DateTime.UtcNow);

        Assert.Equal(DocumentStatus.NeedsReview, record.Status);
        Assert.Empty(record.PolicyNumber);
    }

    [Fact]
    public void ExtractsMedicalExpertiseFieldsWithoutInventingClientName()
    {
        var text =
            """
            Эксперт качества медицинской помощи:
            Тестов Эксперт Примерович
            Специальность эксперта качества медицинской помощи:
            Терапия
            Медицинская документация №
            12345
            Номер полиса обязательного медицинского страхования: 8155230875000050
            Пол
            Женский
            Дата рождения застрахованного лица
            24.04.1967
            Форма оказания медицинской помощи:
            Экстренная
            Период оказания медицинской помощи:
            с 01.04.2026 по 05.04.2026
            Диагноз клинический заключительный по МКБ:
            основной:
            J10 - Синтетический диагноз
            """;

        var record = _extractor.Extract(
            text,
            "810077_Тестов.docx",
            100,
            DateTime.UtcNow);

        Assert.Empty(record.ClientFullName);
        Assert.Equal("Тестов Эксперт Примерович", record.ExpertName);
        Assert.Equal("12345", record.MedicalDocumentNumber);
        Assert.Equal(new DateTime(1967, 4, 24), record.BirthDate);
        Assert.Equal("Экстренная", record.CareForm);
        Assert.Contains("J10", record.PrimaryDiagnosis);
    }

    [Fact]
    public void ExtractsSingleCaseAmountWithoutConfusingSanctions()
    {
        var text =
            """
            Заключение по результатам экспертизы качества медицинской помощи от 25.06.2026 г. № 810078530006Э
            Номер полиса обязательного медицинского страхования:
            8157340891000121
            Медицинская документация №
            45512
            Период оказания медицинской помощи:
            с
            28.12.2025
            по
            17.01.2026
            Диагноз, установленный медицинской организацией:
            C18.6 Злокачественное новообразование
            Всего проверено случаев оказания медицинской помощи
            1
            на сумму
            61 889.71
            рублей
            __CURATIO_TABLE_ROW__	1	гастроэнтерологии	45512	3.2.1.	см.сноску 1	6 188.97	0.00
            """;

        var record = _extractor.Extract(text, "single.docx", 100, DateTime.UtcNow);

        Assert.Equal("810078530006Э", record.ClaimNumber);
        Assert.Equal(61889.71m, record.InsuredAmount);
        Assert.Equal(6188.97m, record.PaymentReductionAmount);
        Assert.Equal(0m, record.PenaltyAmount);
        Assert.Equal("Экспертная", record.CheckType);
        Assert.Equal(DocumentStatus.Processed, record.Status);
    }

    [Fact]
    public void ExtractsMultipleCasesFromSummaryTable()
    {
        var text =
            """
            Заключение по результатам экспертизы качества медицинской помощи от 25.06.2026 г. № 81012474-1
            Эксперт качества медицинской помощи:
            Онуфрийчук Олег Николаевич
            Наименование медицинской организации:
            БУ "Тестовая больница"
            __CURATIO_TABLE_ROW__	1		офтальмологии	8173250823000075	26.06.1947	4922	H34.8	13.02.2026	16.02.2026	63 719.39	Не выявлено	Нет	ДА	0.00	0.00	0.00
            __CURATIO_TABLE_ROW__	2		офтальмологии	7358840871000057	28.01.1951	4200	H26.2	09.02.2026	16.02.2026	51 516.37	Не выявлено	Нет	ДА	0.00	0.00	0.00
            """;

        var records = _extractor.ExtractRecords(text, "summary.docx", 100, DateTime.UtcNow);

        Assert.Equal(2, records.Count);
        Assert.Equal("81012474-1/1", records[0].ClaimNumber);
        Assert.Equal("8173250823000075", records[0].PolicyNumber);
        Assert.Equal("4922", records[0].MedicalDocumentNumber);
        Assert.Equal(63719.39m, records[0].InsuredAmount);
        Assert.Equal("с 13.02.2026 по 16.02.2026", records[0].CarePeriod);
        Assert.Equal("7358840871000057", records[1].PolicyNumber);
        Assert.Equal(51516.37m, records[1].InsuredAmount);
        Assert.All(records, record => Assert.Equal("Экспертная", record.CheckType));
    }

    [Fact]
    public void DetectsEconomicCheckType()
    {
        var record = _extractor.Extract(
            """
            Заключение по результатам медико-экономической экспертизы от 05.06.2026 г. № 81014183-1
            Номер полиса обязательного медицинского страхования:
            8149040840000591
            Медицинская документация №
            10477
            Всего проверено случаев оказания медицинской помощи
            1
            на сумму
            76 736.65
            рублей
            """,
            "810077_81008_Заключение_МЭЭ_26-06_81014183-1.docx",
            100,
            DateTime.UtcNow);

        Assert.Equal("Экономическая", record.CheckType);
    }
}
