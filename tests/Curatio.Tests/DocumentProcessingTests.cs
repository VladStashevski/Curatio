using Curatio.Core;
using Curatio.Infrastructure;

namespace Curatio.Tests;

public sealed class DocumentProcessingTests
{
    [Fact]
    public async Task ReadsTextFromHeaders()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"curatio-header-{Guid.NewGuid():N}");
        var path = TestDocumentFactory.CreateWithHeader(
            directory,
            "header.docx",
            "Номер полиса: HEADER-777",
            "Описание события: Текст документа");

        var text = await new OpenXmlDocumentTextReader().ReadTextAsync(path, CancellationToken.None);

        Assert.Contains("HEADER-777", text);
        Assert.Contains("Текст документа", text);
    }

    [Fact]
    public async Task PreservesStructuredOoxmlCoordinates()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"curatio-structure-{Guid.NewGuid():N}");
        var path = TestDocumentFactory.CreateWithHeader(
            directory,
            "structure.docx",
            "Заголовок",
            "Содержимое документа");

        var document = await new OpenXmlDocumentTextReader()
            .ReadDocumentAsync(path, CancellationToken.None);

        Assert.Contains("Содержимое документа", document.Text);
        Assert.Contains("\"parts\"", document.StructureJson);
        Assert.Contains("\"paragraphs\"", document.StructureJson);
        Assert.Contains("\"tables\"", document.StructureJson);
    }

    [Fact]
    public async Task RetainsRegistryAsSourceOnlyRecord()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"curatio-registry-{Guid.NewGuid():N}");
        TestDocumentFactory.Create(
            directory,
            "810001_Реестр_заключений_ЭКМП.docx",
            "Номер страхового случая: REGISTRY-1",
            "Номер полиса: POLICY-1");
        var repository = new SqliteRecordRepository(Path.Combine(directory, "test.db"));
        await repository.InitializeAsync();
        var service = new DocumentProcessingService(
            new OpenXmlDocumentTextReader(),
            new RegexInsuranceDataExtractor(TestDocumentFactory.Rules()),
            repository);

        var result = await service.ScanAsync(directory, false, null, CancellationToken.None);
        var record = Assert.Single(result.Records);

        Assert.Equal("registry", record.SourceDocumentType);
        Assert.Equal(DocumentStatus.Unprocessed, record.Status);
        Assert.Equal(64, record.SourceFileHash.Length);
        Assert.NotEqual("{}", record.SourceStructureJson);
    }

    [Fact]
    public async Task ReadsSyntheticDocxAndContinuesAfterCorruptedDocument()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"curatio-docs-{Guid.NewGuid():N}");
        TestDocumentFactory.Create(
            directory,
            "valid.docx",
            "Номер страхового случая: TEST-001",
            "ФИО клиента: Тестов Клиент Примерович",
            "Дата события: 10.06.2026",
            "Номер полиса: POLICY-1");
        await File.WriteAllTextAsync(Path.Combine(directory, "corrupted.docx"), "not a zip package");
        await File.WriteAllTextAsync(Path.Combine(directory, "~$temporary.docx"), "temporary");

        var repository = new SqliteRecordRepository(Path.Combine(directory, "test.db"));
        await repository.InitializeAsync();
        var service = new DocumentProcessingService(
            new OpenXmlDocumentTextReader(),
            new RegexInsuranceDataExtractor(TestDocumentFactory.Rules()),
            repository);

        var result = await service.ScanAsync(directory, false, null, CancellationToken.None);

        Assert.Equal(2, result.Records.Count);
        var processed = Assert.Single(result.Records, record => record.Status == DocumentStatus.Processed);
        var corrupted = Assert.Single(result.Records, record => record.Status == DocumentStatus.Error);
        Assert.Contains("ooxml:body/paragraph:", processed.FieldEvidenceJson);
        Assert.Equal(64, corrupted.SourceFileHash.Length);
        Assert.Equal(2, result.Logs.Count);
        Assert.Contains(result.Logs, log => log.Message.Contains("Временный", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReplacesConclusionFootnoteReferenceWithMatchingProtocolDescription()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"curatio-defect-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var conclusionPath = Path.Combine(
            directory,
            "810077_Заключение_ЭКМП_810115190001Э_ВД_3.8._82.docx");
        var protocolPath = Path.Combine(
            directory,
            "810077_ЭЗ_ЭКМП_810115190001Э_ВД_3.8._82.docx");
        await File.WriteAllTextAsync(conclusionPath, "stub");
        await File.WriteAllTextAsync(protocolPath, "stub");

        var reader = new StubDocumentTextReader(new Dictionary<string, string>
        {
            [conclusionPath] =
                """
                Заключение по результатам экспертизы качества медицинской помощи от 09.07.2026 г. № 810115190001Э
                Номер полиса обязательного медицинского страхования:
                8152820845000678
                Медицинская документация №
                5926
                __CURATIO_TABLE_ROW__	1	онкологии	5926	3.8.	см.сноску 1	70 471.05	5 609.16
                """,
            [protocolPath] =
                """
                Заключение по результатам экспертизы качества медицинской помощи от 09.07.2026 г. № 810115190001Э
                Номер полиса обязательного медицинского страхования:
                8152820845000678
                Медицинская документация №
                5926
                4) преемственность (обоснованность перевода, содержание рекомендаций):
                Пациент госпитализирован без медицинских показаний, помощь могла быть оказана амбулаторно. Код дефекта 3.8.
                II. Выводы:
                Медицинская помощь оказана ненадлежаще.
                """
        });
        var repository = new SqliteRecordRepository(Path.Combine(directory, "test.db"));
        await repository.InitializeAsync();
        var service = new DocumentProcessingService(
            reader,
            new RegexInsuranceDataExtractor(TestDocumentFactory.Rules()),
            repository);

        var result = await service.ScanAsync(directory, false, null, CancellationToken.None);

        var record = Assert.Single(result.Records);
        Assert.Equal("3.8", record.DefectCode);
        Assert.Contains("госпитализирован без медицинских показаний", record.DefectDescription);
        Assert.DoesNotContain("сноск", record.DefectDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(70471.05m, record.PaymentReductionAmount);
        Assert.Equal(5609.16m, record.PenaltyAmount);
    }

    private sealed class StubDocumentTextReader(IReadOnlyDictionary<string, string> texts) : IDocumentTextReader
    {
        public Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(texts[Path.GetFullPath(path)]);
        }
    }
}
