using System.Text.Json;
using Curatio.Core;
using Curatio.Infrastructure;

namespace Curatio.Tests;

public sealed class CorpusGoldenTests
{
    [Fact]
    public async Task KnownCorpusIsReproducibleAndFinanciallyExact()
    {
        var corpus = Environment.GetEnvironmentVariable("CURATIO_GOLDEN_CORPUS");
        var rulesPath = Environment.GetEnvironmentVariable("CURATIO_RULES_PATH");
        if (string.IsNullOrWhiteSpace(corpus) || string.IsNullOrWhiteSpace(rulesPath))
            return;

        Assert.Equal(130, Directory.EnumerateFiles(corpus, "*.docx").Count());
        var rules = JsonSerializer.Deserialize<ExtractionRuleSet>(
            await File.ReadAllTextAsync(rulesPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(rules);

        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"curatio-golden-{Guid.NewGuid():N}.db");
        var repository = new SqliteRecordRepository(databasePath);
        await repository.InitializeAsync();
        var service = new DocumentProcessingService(
            new OpenXmlDocumentTextReader(),
            new RegexInsuranceDataExtractor(rules!),
            repository);

        var result = await service.ScanAsync(
            corpus,
            false,
            null,
            CancellationToken.None);
        var cases = result.Records
            .Where(record => record.SourceDocumentType is not "registry" and not "cover_letter")
            .Where(record => record.Status != DocumentStatus.Error)
            .ToArray();

        Assert.Equal(49, cases.Length);
        Assert.Equal(11_241_869.90m, cases.Sum(record => record.InsuredAmount ?? 0m));
        Assert.Equal(544_686.78m, cases.Sum(record => record.FinancialSanctionsAmount ?? 0m));
        Assert.Equal(527_859.30m, cases.Sum(record => record.PaymentReductionAmount ?? 0m));
        Assert.Equal(16_827.48m, cases.Sum(record => record.PenaltyAmount ?? 0m));
        var registries = result.Records
            .Where(record => record.SourceDocumentType == "registry")
            .ToArray();
        Assert.Equal(29, registries.Length);
        Assert.All(registries, record => Assert.NotNull(record.InsuredAmount));
        Assert.Equal(
            cases.Sum(record => record.InsuredAmount ?? 0m),
            registries.Sum(record => record.InsuredAmount ?? 0m));
        Assert.DoesNotContain(cases, record => record.CaseOutcome.TrimEnd().EndsWith('|'));
        var contaminatedKsg = cases
            .Where(record => record.ClinicalStatisticalGroup.Contains("По случаю", StringComparison.OrdinalIgnoreCase)
                || record.ClinicalStatisticalGroup.Contains("Дефект", StringComparison.OrdinalIgnoreCase))
            .Select(record => $"{record.SourceFileName}: {record.ClinicalStatisticalGroup}")
            .ToArray();
        Assert.True(contaminatedKsg.Length == 0, string.Join(Environment.NewLine, contaminatedKsg));
        Assert.All(result.Records, record =>
        {
            Assert.Equal(64, record.SourceFileHash.Length);
            Assert.NotEqual("{}", record.SourceStructureJson);
            Assert.NotEqual("[]", record.FieldEvidenceJson);
        });

        var exportPath = Environment.GetEnvironmentVariable("CURATIO_GOLDEN_EXPORT_PATH");
        if (!string.IsNullOrWhiteSpace(exportPath))
        {
            await new RecordExportService().ExportXlsxAsync(
                result.Records,
                exportPath,
                CancellationToken.None);
            Assert.True(File.Exists(exportPath));
            Assert.True(File.Exists(Path.ChangeExtension(exportPath, ".curatio.json")));
        }
    }
}
