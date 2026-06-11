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
}
