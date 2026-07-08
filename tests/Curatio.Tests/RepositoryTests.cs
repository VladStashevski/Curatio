using Curatio.Core;
using Curatio.Infrastructure;

namespace Curatio.Tests;

public sealed class RepositoryTests
{
    [Fact]
    public async Task DetectsDuplicateByPathSizeAndModificationDate()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"curatio-tests-{Guid.NewGuid():N}");
        var repository = new SqliteRecordRepository(Path.Combine(directory, "test.db"));
        await repository.InitializeAsync();
        var modified = DateTime.UtcNow;
        var path = Path.Combine(directory, "sample.docx");
        var record = new InsuranceRecord
        {
            SourceFileName = "sample.docx",
            FullPath = path,
            FileSize = 42,
            FileModifiedAt = modified,
            ProcessedAt = DateTime.UtcNow,
            Status = DocumentStatus.Processed,
            ExpertName = "Тестов Эксперт",
            MedicalDocumentNumber = "12345",
            BirthDate = new DateTime(1980, 1, 2),
            PrimaryDiagnosis = "J10",
            DiagnosisComplication = "H65.0",
            ComorbidDiagnosis = "D64.9",
            Operation = "-",
            ClinicalStatisticalGroup = "st12.012",
            DefectCode = "3.11",
            DefectDescription = "В представленной ПМД отсутствует лист врачебных назначений"
        };

        await repository.SaveAsync(record, CancellationToken.None);

        Assert.True(await repository.IsImportedAsync(path, 42, modified, CancellationToken.None));
        Assert.False(await repository.IsImportedAsync(path, 43, modified, CancellationToken.None));
        var loaded = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("Тестов Эксперт", loaded.ExpertName);
        Assert.Equal("12345", loaded.MedicalDocumentNumber);
        Assert.Equal(new DateTime(1980, 1, 2), loaded.BirthDate?.Date);
        Assert.Equal("J10", loaded.PrimaryDiagnosis);
        Assert.Equal("H65.0", loaded.DiagnosisComplication);
        Assert.Equal("D64.9", loaded.ComorbidDiagnosis);
        Assert.Equal("st12.012", loaded.ClinicalStatisticalGroup);
        Assert.Equal("3.11", loaded.DefectCode);
        Assert.Contains("лист врачебных назначений", loaded.DefectDescription);

        record.ExpertName = "Обновлённый Эксперт";
        await repository.SaveAsync(record, CancellationToken.None);

        loaded = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("Обновлённый Эксперт", loaded.ExpertName);
    }

    [Fact]
    public async Task PersistsSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"curatio-settings-{Guid.NewGuid():N}");
        var repository = new SqliteRecordRepository(Path.Combine(directory, "test.db"));
        await repository.InitializeAsync();

        await repository.SetAsync("lastFolder", "/synthetic/documents");

        Assert.Equal("/synthetic/documents", await repository.GetAsync("lastFolder"));
    }

    [Fact]
    public async Task DeleteAllRemovesRecordsAndKeepsSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"curatio-clear-{Guid.NewGuid():N}");
        var repository = new SqliteRecordRepository(Path.Combine(directory, "test.db"));
        await repository.InitializeAsync();
        var modified = DateTime.UtcNow;
        var path = Path.Combine(directory, "sample.docx");
        var record = new InsuranceRecord
        {
            SourceFileName = "sample.docx",
            FullPath = path,
            FileSize = 42,
            FileModifiedAt = modified,
            ProcessedAt = DateTime.UtcNow,
            Status = DocumentStatus.Processed
        };

        await repository.SaveAsync(record, CancellationToken.None);
        await repository.SetAsync("lastFolder", "/synthetic/documents");

        var deleted = await repository.DeleteAllAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Empty(await repository.GetAllAsync());
        Assert.False(await repository.IsImportedAsync(path, 42, modified, CancellationToken.None));
        Assert.Equal("/synthetic/documents", await repository.GetAsync("lastFolder"));
    }
}
