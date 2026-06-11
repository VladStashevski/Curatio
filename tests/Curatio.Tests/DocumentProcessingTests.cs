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
        Assert.Contains(result.Records, record => record.Status == DocumentStatus.Processed);
        Assert.Contains(result.Records, record => record.Status == DocumentStatus.Error);
        Assert.Equal(2, result.Logs.Count);
        Assert.Contains(result.Logs, log => log.Message.Contains("Временный", StringComparison.Ordinal));
    }
}
