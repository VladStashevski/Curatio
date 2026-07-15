using System.Text;
using ClosedXML.Excel;
using Curatio.Core;
using Curatio.Infrastructure;

namespace Curatio.Tests;

public sealed class ExportTests
{
    [Fact]
    public async Task ExportsCyrillicToCsvAndXlsx()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"curatio-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var csvPath = Path.Combine(directory, "result.csv");
        var xlsxPath = Path.Combine(directory, "result.xlsx");
        var records = new[]
        {
            new InsuranceRecord
            {
                ClaimNumber = "ТЕСТ-1",
                ClientFullName = "Синтетический Клиент",
                PolicyNumber = "ПОЛИС-1",
                SourceFileName = "пример.docx",
                FullPath = "/tmp/пример.docx",
                ProcessedAt = DateTime.UtcNow,
                Status = DocumentStatus.Processed
            }
        };
        var service = new RecordExportService();

        await service.ExportCsvAsync(records, csvPath, CancellationToken.None);
        await service.ExportXlsxAsync(records, xlsxPath, CancellationToken.None);

        var csv = await File.ReadAllTextAsync(csvPath, Encoding.UTF8);
        Assert.Contains("Синтетический Клиент", csv);
        using var workbook = new XLWorkbook(xlsxPath);
        Assert.Equal("Синтетический Клиент", workbook.Worksheet(1).Cell(2, 3).GetString());
        Assert.True(File.Exists(Path.ChangeExtension(csvPath, ".curatio.json")));
        Assert.True(File.Exists(Path.ChangeExtension(xlsxPath, ".curatio.json")));
    }
}
