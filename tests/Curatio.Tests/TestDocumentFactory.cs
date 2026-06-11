using Curatio.Core;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Curatio.Tests;

internal static class TestDocumentFactory
{
    public static string Create(string directory, string fileName, params string[] paragraphs)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body(
            paragraphs.Select(text => new Paragraph(new Run(new Text(text))))));
        mainPart.Document.Save();
        return path;
    }

    public static string CreateWithHeader(string directory, string fileName, string headerText, string bodyText)
    {
        var path = Create(directory, fileName, bodyText);
        using var document = WordprocessingDocument.Open(path, true);
        var mainPart = document.MainDocumentPart!;
        var headerPart = mainPart.AddNewPart<HeaderPart>();
        headerPart.Header = new Header(new Paragraph(new Run(new Text(headerText))));
        headerPart.Header.Save();

        var relationshipId = mainPart.GetIdOfPart(headerPart);
        var documentRoot = mainPart.Document!;
        documentRoot.Body!.AppendChild(new SectionProperties(
            new HeaderReference { Type = HeaderFooterValues.Default, Id = relationshipId }));
        documentRoot.Save();
        return path;
    }

    public static ExtractionRuleSet Rules() => new()
    {
        Fields = new Dictionary<string, string[]>
        {
            ["claimNumber"] = [@"(?:Номер страхового случая|Номер убытка)\s*[:№-]\s*(?<value>[A-ZА-Я0-9\-/]+)"],
            ["clientFullName"] = [@"(?:ФИО клиента|Страхователь)\s*[:\-]\s*(?<value>[^\r\n]+)"],
            ["eventDate"] = [@"(?:Дата события|Дата происшествия)\s*[:\-]\s*(?<value>\d{1,2}[./-]\d{1,2}[./-]\d{4})"],
            ["claimType"] = [@"Тип страхового случая\s*[:\-]\s*(?<value>[^\r\n]+)"],
            ["policyNumber"] = [@"(?:Номер полиса|Полис)\s*[:№-]\s*(?<value>[A-ZА-Я0-9\-/]+)"],
            ["insuredAmount"] = [@"Страховая сумма\s*[:\-]\s*(?<value>[\d\s]+(?:[.,]\d{1,2})?)"],
            ["eventDescription"] = [@"Описание события\s*[:\-]\s*(?<value>[^\r\n]+)"],
            ["expertName"] = [@"Эксперт качества медицинской помощи\s*:\s*(?<value>[^\r\n]+)"],
            ["expertSpecialty"] = [@"Специальность эксперта качества медицинской помощи\s*:\s*(?<value>[^\r\n]+)"],
            ["medicalDocumentNumber"] = [@"Медицинская документация\s*№\s*(?<value>[^\r\n]+)"],
            ["gender"] = [@"(?:^|\n)Пол\s*(?<value>Мужской|Женский)"],
            ["birthDate"] = [@"Дата рождения застрахованного лица\s*(?<value>\d{1,2}\.\d{1,2}\.\d{4})"],
            ["careForm"] = [@"Форма оказания медицинской помощи\s*:\s*(?<value>[^\r\n]+)"],
            ["carePeriod"] = [@"Период оказания медицинской помощи\s*:\s*(?<value>[^\r\n]+)"],
            ["primaryDiagnosis"] = [@"Диагноз клинический заключительный по МКБ\s*:\s*основной\s*:\s*(?<value>[^\r\n]+)"],
            ["diagnosisComplication"] = [@"Диагноз клинический заключительный по МКБ[\s\S]*?осложнение\s*:\s*(?<value>[^\r\n]+)"],
            ["comorbidDiagnosis"] = [@"Диагноз клинический заключительный по МКБ[\s\S]*?сопутствующий\s*:\s*(?<value>[^\r\n]+)"],
            ["operation"] = [@"(?:^|\n)Операция\s*:\s*(?<value>[^\r\n]+)"],
            ["clinicalStatisticalGroup"] = [@"КСГ\s*:\s*(?<value>[^\r\n]+)"]
        }
    };
}
