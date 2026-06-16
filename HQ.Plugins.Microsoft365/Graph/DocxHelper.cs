using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace HQ.Plugins.Microsoft365.Graph;

/// <summary>
/// Pure .docx (OpenXML) create/read helpers. No network — round-trip unit tested.
/// </summary>
public static class DocxHelper
{
    /// <summary>Builds a minimal .docx from text. Each line becomes a paragraph.</summary>
    public static byte[] CreateDocx(string text)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());

            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                var para = new Paragraph();
                var run = new Run();
                run.AppendChild(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
                para.AppendChild(run);
                body.AppendChild(para);
            }
            main.Document.Save();
        }
        return ms.ToArray();
    }

    /// <summary>Extracts plain text from a .docx byte array, one paragraph per line.</summary>
    public static string ExtractText(byte[] docxBytes)
    {
        using var ms = new MemoryStream(docxBytes);
        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "";

        var sb = new StringBuilder();
        foreach (var para in body.Descendants<Paragraph>())
        {
            foreach (var text in para.Descendants<Text>()) sb.Append(text.Text);
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }
}
