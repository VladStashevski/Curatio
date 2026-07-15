using Curatio.Core;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text.Json;

namespace Curatio.Infrastructure;

public sealed class OpenXmlDocumentTextReader : IDocumentTextReader
{
    public async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken) =>
        (await ReadDocumentAsync(path, cancellationToken)).Text;

    public Task<DocumentReadResult> ReadDocumentAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = WordprocessingDocument.Open(path, false);
            var mainPart = document.MainDocumentPart
                ?? throw new InvalidDataException("В документе отсутствует основная часть.");
            var body = mainPart.Document?.Body
                ?? throw new InvalidDataException("В документе отсутствует основная часть.");

            var roots = new List<(string Part, OpenXmlElement Root)> { ("body", body) };
            roots.AddRange(mainPart.HeaderParts
                .Select((part, index) => (Part: $"header:{index}", Root: part.Header as OpenXmlElement))
                .Where(item => item.Root is not null)
                .Select(item => (item.Part, item.Root!)));
            roots.AddRange(mainPart.FooterParts
                .Select((part, index) => (Part: $"footer:{index}", Root: part.Footer as OpenXmlElement))
                .Where(item => item.Root is not null)
                .Select(item => (item.Part, item.Root!)));

            if (mainPart.FootnotesPart?.Footnotes is { } footnotes)
                roots.Add(("footnotes", footnotes));
            if (mainPart.EndnotesPart?.Endnotes is { } endnotes)
                roots.Add(("endnotes", endnotes));

            var paragraphs = roots
                .SelectMany(item => item.Root.Descendants<Paragraph>())
                .Select(paragraph => paragraph.InnerText)
                .Where(text => !string.IsNullOrWhiteSpace(text));

            var tableRows = roots
                .SelectMany(item => item.Root.Descendants<Table>())
                .SelectMany(table => table.Elements<TableRow>())
                .Select(row => string.Join(
                    '\t',
                    row.Elements<TableCell>()
                        .Select(cell => NormalizeCell(cell.InnerText))))
                .Where(row => !string.IsNullOrWhiteSpace(row))
                .Select(row => $"{DocumentTextMarkers.TableRowPrefix}\t{row}");

            var text = string.Join(Environment.NewLine, paragraphs.Concat(tableRows));
            var structure = new
            {
                version = "curatio-ooxml-structure-v1",
                parts = roots.Select(item => new
                {
                    name = item.Part,
                    paragraphs = item.Root.Descendants<Paragraph>()
                        .Select((paragraph, index) => new
                        {
                            index,
                            text = paragraph.InnerText
                        })
                        .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.text)),
                    tables = item.Root.Descendants<Table>()
                        .Select((table, tableIndex) => new
                        {
                            tableIndex,
                            rows = table.Elements<TableRow>()
                                .Select((row, rowIndex) => new
                                {
                                    rowIndex,
                                    cells = row.Elements<TableCell>()
                                        .Select((cell, cellIndex) => new
                                        {
                                            cellIndex,
                                            text = NormalizeCell(cell.InnerText)
                                        })
                                })
                        })
                })
            };

            return new DocumentReadResult(
                text,
                JsonSerializer.Serialize(structure));
        }, cancellationToken);

    private static string NormalizeCell(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
