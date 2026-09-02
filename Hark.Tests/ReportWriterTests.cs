using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Hark.App.Reporting;
using Hark.Oracle.Vision;
using Xunit;

namespace Hark.Tests;

public class ReportWriterTests
{
    // A minimal valid 1×1 PNG, so the image-embedding path (IHDR parse + EMU sizing) is exercised.
    private static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static SessionReport SampleReport() => new(
        "Hark test report",
        DateTime.Now,
        "Alice: hello there\nBob: general kenobi",
        Recap: null,
        Speakers: null,
        Beats: new List<ReportBeat>
        {
            new("Greeting", new List<InfographicNode>
            {
                new("Salutation", "blue", "A friendly hello."),
                new("Reply", "green", "The famous retort."),
            }, Png1x1),
            new("Topic without a scene", new List<InfographicNode>(), null),
        });

    [Fact]
    public async Task Docx_is_structurally_valid_with_embedded_image()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hark-test-{Guid.NewGuid():N}.docx");
        try
        {
            await new DocxReportWriter().WriteAsync(SampleReport(), path);
            Assert.True(new FileInfo(path).Length > 0, "the .docx should not be empty");

            using var doc = WordprocessingDocument.Open(path, false);
            var errors = new OpenXmlValidator().Validate(doc).ToList();
            Assert.True(errors.Count == 0,
                "OpenXML validation errors:\n" + string.Join("\n", errors.Select(e => e.Description)));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(".md")]
    [InlineData(".html")]
    public async Task Text_writers_produce_the_report_content(string ext)
    {
        IReportWriter writer = ext == ".md" ? new MarkdownReportWriter() : new HtmlReportWriter();
        var path = Path.Combine(Path.GetTempPath(), $"hark-test-{Guid.NewGuid():N}{ext}");
        try
        {
            await writer.WriteAsync(SampleReport(), path);
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("Hark test report", text);
            Assert.Contains("Greeting", text);
            Assert.Contains("data:image/png;base64,", text);   // the scene is embedded
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
