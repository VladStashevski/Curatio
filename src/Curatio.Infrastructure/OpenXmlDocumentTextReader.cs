using Curatio.Core;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Curatio.Infrastructure;

public sealed class OpenXmlDocumentTextReader : IDocumentTextReader
{
    public Task<string> ReadTextAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = WordprocessingDocument.Open(path, false);
            var mainPart = document.MainDocumentPart
                ?? throw new InvalidDataException("В документе отсутствует основная часть.");
            var body = mainPart.Document?.Body
                ?? throw new InvalidDataException("В документе отсутствует основная часть.");

            var roots = new List<OpenXmlElement> { body };
            roots.AddRange(mainPart.HeaderParts
                .Select(part => part.Header)
                .OfType<OpenXmlElement>());
            roots.AddRange(mainPart.FooterParts
                .Select(part => part.Footer)
                .OfType<OpenXmlElement>());

            if (mainPart.FootnotesPart?.Footnotes is { } footnotes)
                roots.Add(footnotes);
            if (mainPart.EndnotesPart?.Endnotes is { } endnotes)
                roots.Add(endnotes);

            return string.Join(
                Environment.NewLine,
                roots.SelectMany(root => root.Descendants<Paragraph>())
                    .Select(paragraph => paragraph.InnerText)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
        }, cancellationToken);
}
