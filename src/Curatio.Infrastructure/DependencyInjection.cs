using System.Text.Json;
using Curatio.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Curatio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCuratioInfrastructure(
        this IServiceCollection services,
        string databasePath,
        string rulesPath)
    {
        var json = File.ReadAllText(rulesPath);
        var rules = JsonSerializer.Deserialize<ExtractionRuleSet>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("Не удалось прочитать правила извлечения.");

        services.AddSingleton(rules);
        services.AddSingleton<IInsuranceDataExtractor, RegexInsuranceDataExtractor>();
        services.AddSingleton<IDocumentTextReader, OpenXmlDocumentTextReader>();
        services.AddSingleton(_ => new SqliteRecordRepository(databasePath));
        services.AddSingleton<IRecordRepository>(provider => provider.GetRequiredService<SqliteRecordRepository>());
        services.AddSingleton<ISettingsStore>(provider => provider.GetRequiredService<SqliteRecordRepository>());
        services.AddSingleton<IRecordExportService, RecordExportService>();
        services.AddSingleton<IInsuranceApiSender, LocalInsuranceApiSender>();
        services.AddSingleton<IDocumentProcessingService, DocumentProcessingService>();
        return services;
    }
}
