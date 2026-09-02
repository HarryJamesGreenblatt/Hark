using System;
using System.IO;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Hark.Core.Summarization;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Hark.App.Reporting;

/// <summary>Renders a <see cref="SessionReport"/> as a Word (.docx) document via the Open XML SDK (scene images embedded inline).</summary>
public sealed class DocxReportWriter : IReportWriter
{
    public string Extension => ".docx";
    public string FilterName => "Word document";

    public Task WriteAsync(SessionReport report, string path)
    {
        Build(report, path);
        return Task.CompletedTask;
    }

    private static void Build(SessionReport report, string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document();
        var body = main.Document.AppendChild(new Body());

        Heading(body, report.Title, 1);
        Para(body, report.Timestamp.ToString("f"), italic: true);

        if (!string.IsNullOrWhiteSpace(report.Transcript))
        {
            Heading(body, "Transcript", 2);
            foreach (var line in report.Transcript.Replace("\r\n", "\n").Split('\n'))
                Para(body, line);
        }
        if (report.Recap is not null) AppendRecap(body, report.Recap);
        if (report.Speakers is { Speakers.Count: > 0 }) AppendSpeakers(body, report.Speakers);
        if (report.Beats.Count > 0) AppendVision(main, body, report);

        main.Document.Save();
    }

    private static void AppendRecap(Body body, MeetingRecap recap)
    {
        Heading(body, "Conversation summary", 2);
        if (!string.IsNullOrWhiteSpace(recap.Overview)) Para(body, recap.Overview.Trim());
        foreach (var t in recap.Topics)
        {
            Heading(body, t.Title?.Trim(), 3);
            if (!string.IsNullOrWhiteSpace(t.Summary)) Para(body, t.Summary.Trim());
            foreach (var d in t.Details) Bullet(body, d?.Trim());
        }
        if (recap.FollowUps.Count > 0)
        {
            Heading(body, "Follow-up tasks", 3);
            foreach (var f in recap.FollowUps)
                Bullet(body, string.IsNullOrWhiteSpace(f.Owner) ? f.Task?.Trim() : $"{f.Task?.Trim()} \u2014 {f.Owner.Trim()}");
        }
    }

    private static void AppendSpeakers(Body body, SpeakerRecap recap)
    {
        Heading(body, "Speakers", 2);
        foreach (var s in recap.Speakers)
        {
            Heading(body, s.Speaker?.Trim(), 3);
            if (!string.IsNullOrWhiteSpace(s.Summary)) Para(body, s.Summary.Trim());
            foreach (var p in s.Points) Bullet(body, p?.Trim());
        }
    }

    private static void AppendVision(MainDocumentPart main, Body body, SessionReport report)
    {
        Heading(body, "Vision slideshow", 2);
        int n = 1, imageId = 1;
        foreach (var beat in report.Beats)
        {
            Heading(body, $"{n++}. {beat.Title?.Trim()}", 3);
            foreach (var node in beat.Nodes)
                Bullet(body, string.IsNullOrWhiteSpace(node.Detail)
                    ? node.Label?.Trim()
                    : $"{node.Label?.Trim()} \u2014 {node.Detail.Trim()}");
            if (beat.Scene is not null) AppendImage(main, body, beat.Scene, imageId++);
        }
    }

    // ── low-level Open XML helpers ──

    private static void Heading(Body body, string? text, int level)
    {
        int halfPt = level switch { 1 => 36, 2 => 28, 3 => 24, _ => 22 };
        var run = new Run(new Text(text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve })
        {
            RunProperties = new RunProperties(new Bold(), new FontSize { Val = halfPt.ToString() }),
        };
        var p = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "240", After = "80" }), run);
        body.AppendChild(p);
    }

    private static void Para(Body body, string? text, bool italic = false)
    {
        var run = new Run(new Text(text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve });
        if (italic) run.RunProperties = new RunProperties(new Italic());
        body.AppendChild(new Paragraph(run));
    }

    private static void Bullet(Body body, string? text)
    {
        var run = new Run(new Text("\u2022  " + (text ?? string.Empty)) { Space = SpaceProcessingModeValues.Preserve });
        body.AppendChild(new Paragraph(new ParagraphProperties(new Indentation { Left = "360" }), run));
    }

    /// <summary>Embeds a PNG as an inline drawing, sized to ~360px wide keeping aspect.</summary>
    private static void AppendImage(MainDocumentPart main, Body body, byte[] png, int imageId)
    {
        var imagePart = main.AddImagePart(ImagePartType.Png);
        using (var ms = new MemoryStream(png)) imagePart.FeedData(ms);
        var relId = main.GetIdOfPart(imagePart);

        var (w, h) = PngSize(png);
        const long maxWidthEmu = 360L * 9525L;   // 9525 EMU per pixel at 96 dpi
        long widthEmu = maxWidthEmu;
        long heightEmu = (w > 0 && h > 0) ? maxWidthEmu * h / w : maxWidthEmu;

        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = (UInt32Value)(uint)imageId, Name = $"scene{imageId}" },
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = (UInt32Value)(uint)imageId, Name = $"scene{imageId}.png" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    )
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
            });

        body.AppendChild(new Paragraph(new Run(drawing)));
    }

    /// <summary>Reads a PNG's pixel dimensions from its IHDR header (bytes 16-23, big-endian).</summary>
    private static (int w, int h) PngSize(byte[] png)
    {
        if (png.Length < 24) return (0, 0);
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }
}
