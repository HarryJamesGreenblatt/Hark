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

    // Palette mirrors the HTML report's design language, mapped to a light, printable Word surface.
    private const string Ink = "1F2430";
    private const string Dim = "6B7280";
    private const string Faint = "9AA2AC";
    private const string Accent = "E23A2E";
    private const string CardShade = "F6F7F9";
    private const string CardBorder = "E3E7EC";

    private static void Build(SessionReport report, string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document();
        var body = main.Document.AppendChild(new Body());

        // Hero: title + a meta line of counts, mirroring the HTML header.
        body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "40" }),
            R(report.Title, color: Ink, bold: true, halfPt: 40)));
        var facts = new System.Collections.Generic.List<string>();
        if (report.Speakers is { Speakers.Count: > 0 } sp) facts.Add(Plural(sp.Speakers.Count, "speaker"));
        if (report.Beats.Count > 0) facts.Add(Plural(report.Beats.Count, "vision beat"));
        var meta = report.Timestamp.ToString("f");
        if (facts.Count > 0) meta += "   \u00b7   " + string.Join("   \u00b7   ", facts);
        body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "200" }),
            R(meta, color: Dim, halfPt: 20)));

        // Vision leads, then the recaps, then the raw transcript last — matching the HTML/Markdown layout.
        if (report.Beats.Count > 0) AppendVision(main, body, report.Beats);
        if (report.Recap is not null) AppendRecap(body, report.Recap);
        if (report.Speakers is { Speakers.Count: > 0 }) AppendSpeakers(body, report.Speakers);
        if (!string.IsNullOrWhiteSpace(report.Transcript))
        {
            SectionHead(body, "Transcript");
            foreach (var line in report.Transcript.Replace("\r\n", "\n").Split('\n'))
                body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "20" }),
                    R(line, color: Ink, halfPt: 19)));
        }

        main.Document.Save();
    }

    private static void AppendVision(MainDocumentPart main, Body body, System.Collections.Generic.IReadOnlyList<ReportBeat> beats)
    {
        SectionHead(body, "Vision");
        int n = 1, imageId = 1;
        foreach (var beat in beats)
            BeatCard(main, body, beat, n++, ref imageId);
    }

    /// <summary>The beat card: a keep-together table with the mind-map nodes beside the scene image
    /// (collapsing to a single column when there's no scene). The Word twin of the HTML beat grid.</summary>
    private static void BeatCard(MainDocumentPart main, Body body, ReportBeat beat, int index, ref int imageId)
    {
        bool hasScene = beat.Scene is not null;
        var grid = hasScene
            ? new TableGrid(new GridColumn { Width = "5760" }, new GridColumn { Width = "3600" })
            : new TableGrid(new GridColumn { Width = "9360" });

        var table = new Table(
            new TableProperties(
                new TableWidth { Width = "9360", Type = TableWidthUnitValues.Dxa },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4, Color = CardBorder },
                    new LeftBorder { Val = BorderValues.Single, Size = 4, Color = CardBorder },
                    new BottomBorder { Val = BorderValues.Single, Size = 4, Color = CardBorder },
                    new RightBorder { Val = BorderValues.Single, Size = 4, Color = CardBorder },
                    new InsideHorizontalBorder { Val = BorderValues.None },
                    new InsideVerticalBorder { Val = BorderValues.None }),
                new TableLayout { Type = TableLayoutValues.Fixed }),
            grid);

        // Left cell — the numbered title and the coloured node list.
        var left = new TableCell(new TableCellProperties(
            new TableCellWidth { Width = hasScene ? "5760" : "9360", Type = TableWidthUnitValues.Dxa },
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = CardShade },
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top }));
        left.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "140" }),
            R(index.ToString(), color: Accent, bold: true, halfPt: 26),
            R("   " + (beat.Title?.Trim() ?? string.Empty), color: Ink, bold: true, halfPt: 26)));
        if (beat.Nodes.Count == 0)
            left.Append(new Paragraph());   // a cell must end with a paragraph
        foreach (var node in beat.Nodes)
        {
            var p = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "80" }),
                R("\u25CF  ", color: Strip(ReportPalette.Hex(node.Color)), halfPt: 18),
                R(node.Label?.Trim() ?? string.Empty, color: Ink, bold: true, halfPt: 20));
            if (!string.IsNullOrWhiteSpace(node.Detail))
                p.Append(R("   " + node.Detail.Trim(), color: Dim, halfPt: 19));
            left.Append(p);
        }

        var row = new TableRow(new TableRowProperties(new CantSplit()), left);

        if (hasScene)
        {
            var right = new TableCell(new TableCellProperties(
                new TableCellWidth { Width = "3600", Type = TableWidthUnitValues.Dxa },
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = CardShade },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
                ImageParagraph(main, beat.Scene!, imageId++, 2160000L));   // ~2.36" wide, keeps aspect
            row.Append(right);
        }

        table.Append(row);
        body.AppendChild(table);
        body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "160" })));   // spacer between cards
    }

    private static void AppendRecap(Body body, MeetingRecap recap)
    {
        SectionHead(body, "Conversation summary");
        if (!string.IsNullOrWhiteSpace(recap.Overview))
            body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "160" }),
                R(recap.Overview.Trim(), color: Ink, halfPt: 24)));
        foreach (var t in recap.Topics)
        {
            SubHead(body, t.Title?.Trim());
            if (!string.IsNullOrWhiteSpace(t.Summary))
                body.AppendChild(new Paragraph(R(t.Summary.Trim(), color: Dim, halfPt: 21)));
            foreach (var d in t.Details) Bullet(body, d?.Trim());
        }
        if (recap.FollowUps.Count > 0)
        {
            SubHead(body, "Follow-up tasks");
            foreach (var f in recap.FollowUps)
            {
                var p = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "60" }, new Indentation { Left = "360" }),
                    R("\u2610  ", color: Faint, halfPt: 21),
                    R(f.Task?.Trim() ?? string.Empty, color: Ink, halfPt: 21));
                if (!string.IsNullOrWhiteSpace(f.Owner))
                    p.Append(R("   " + f.Owner.Trim(), color: Accent, bold: true, halfPt: 19));
                body.AppendChild(p);
            }
        }
    }

    private static void AppendSpeakers(Body body, SpeakerRecap recap)
    {
        SectionHead(body, "Speakers");
        foreach (var s in recap.Speakers)
        {
            SubHead(body, s.Speaker?.Trim());
            if (!string.IsNullOrWhiteSpace(s.Summary))
                body.AppendChild(new Paragraph(R(s.Summary.Trim(), color: Dim, halfPt: 21)));
            foreach (var p in s.Points) Bullet(body, p?.Trim());
        }
    }

    // ── low-level Open XML helpers ──

    /// <summary>A styled run; only the requested properties are attached.</summary>
    private static Run R(string? text, string? color = null, bool bold = false, int? halfPt = null)
    {
        var rp = new RunProperties();
        if (bold) rp.Append(new Bold());
        if (color is not null) rp.Append(new DocumentFormat.OpenXml.Wordprocessing.Color { Val = color });
        if (halfPt is not null) rp.Append(new FontSize { Val = halfPt.Value.ToString() });
        var run = new Run(new Text(text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve });
        if (rp.HasChildren) run.RunProperties = rp;
        return run;
    }

    /// <summary>An uppercase, accent-coloured section label with a faint underline rule (the HTML sec-head).</summary>
    private static void SectionHead(Body body, string label)
    {
        var pp = new ParagraphProperties(
            new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 6, Color = CardBorder, Space = 6 }),
            new SpacingBetweenLines { Before = "360", After = "160" });
        var run = new Run(new Text(label) { Space = SpaceProcessingModeValues.Preserve })
        {
            RunProperties = new RunProperties(new Bold(), new Caps(), new DocumentFormat.OpenXml.Wordprocessing.Color { Val = Accent },
                new Spacing { Val = 30 }, new FontSize { Val = "20" }),
        };
        body.AppendChild(new Paragraph(pp, run));
    }

    /// <summary>A topic/speaker sub-heading.</summary>
    private static void SubHead(Body body, string? text) =>
        body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "160", After = "40" }),
            R(text, color: Ink, bold: true, halfPt: 24)));

    private static void Bullet(Body body, string? text) =>
        body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "40" }, new Indentation { Left = "360" }),
            R("\u2022  ", color: Faint, halfPt: 21), R(text, color: Ink, halfPt: 21)));

    private static string Strip(string hex) => hex.StartsWith('#') ? hex[1..] : hex;

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>Builds an inline-image paragraph, sized to <paramref name="maxWidthEmu"/> wide keeping aspect.</summary>
    private static Paragraph ImageParagraph(MainDocumentPart main, byte[] png, int imageId, long maxWidthEmu)
    {
        var imagePart = main.AddImagePart(ImagePartType.Png);
        using (var ms = new MemoryStream(png)) imagePart.FeedData(ms);
        var relId = main.GetIdOfPart(imagePart);

        var (w, h) = PngSize(png);
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

        return new Paragraph(new Run(drawing));
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
