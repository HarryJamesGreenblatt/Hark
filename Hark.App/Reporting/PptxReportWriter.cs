using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Hark.Core.Summarization;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Hark.App.Reporting;

/// <summary>Renders a <see cref="SessionReport"/> as a PowerPoint (.pptx) deck — one slide per vision beat
/// (the beat card as a slide) plus a title slide and the recap/speakers, on a dark cinematic surface.</summary>
public sealed class PptxReportWriter : IReportWriter
{
    public string Extension => ".pptx";
    public string FilterName => "PowerPoint";

    // 16:9 slide, and the palette (dark deck so the scenes pop; mirrors the app/HTML accent language).
    private const long SlideW = 12192000L, SlideH = 6858000L;
    private const long Margin = 548640L;                         // ~0.6"
    private const string Bg = "0B0D10", Ink = "E6EAEF", Dim = "9AA2AC", Accent = "E24A3A";

    public Task WriteAsync(SessionReport report, string path)
    {
        Build(report, path);
        return Task.CompletedTask;
    }

    private static void Build(SessionReport report, string path)
    {
        using var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentationPart = doc.AddPresentationPart();

        // Master + a blank layout + theme (the minimal valid scaffold; the master paints the dark background).
        var masterPart = presentationPart.AddNewPart<SlideMasterPart>();
        var layoutPart = masterPart.AddNewPart<SlideLayoutPart>();
        layoutPart.SlideLayout = new SlideLayout(new CommonSlideData(EmptyTree()),
            new P.ColorMapOverride(new D.MasterColorMapping())) { Type = SlideLayoutValues.Blank };
        layoutPart.AddPart(masterPart);   // the layout's back-relationship to its master (PowerPoint requires it)
        var themePart = masterPart.AddNewPart<ThemePart>();
        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(ThemeXml))) themePart.FeedData(ms);

        masterPart.SlideMaster = new SlideMaster(
            new CommonSlideData(
                new Background(new BackgroundProperties(new D.SolidFill(new D.RgbColorModelHex { Val = Bg }))),
                EmptyTree()),
            new P.ColorMap
            {
                Background1 = D.ColorSchemeIndexValues.Light1,
                Text1 = D.ColorSchemeIndexValues.Dark1,
                Background2 = D.ColorSchemeIndexValues.Light2,
                Text2 = D.ColorSchemeIndexValues.Dark2,
                Accent1 = D.ColorSchemeIndexValues.Accent1,
                Accent2 = D.ColorSchemeIndexValues.Accent2,
                Accent3 = D.ColorSchemeIndexValues.Accent3,
                Accent4 = D.ColorSchemeIndexValues.Accent4,
                Accent5 = D.ColorSchemeIndexValues.Accent5,
                Accent6 = D.ColorSchemeIndexValues.Accent6,
                Hyperlink = D.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = D.ColorSchemeIndexValues.FollowedHyperlink,
            },
            new SlideLayoutIdList(new SlideLayoutId { Id = 2147483649U, RelationshipId = masterPart.GetIdOfPart(layoutPart) }));

        var slides = new List<SlidePart>();

        // Title slide.
        slides.Add(NewSlide(presentationPart, layoutPart,
            TextShape(2, "title", Margin, 2360000L, SlideW - 2 * Margin, 1300000L,
                Center(Para(Run(report.Title, Ink, bold: true, sz: 4000)))),
            TextShape(3, "meta", Margin, 3720000L, SlideW - 2 * Margin, 700000L,
                Center(Para(Run(MetaLine(report), Dim, sz: 1800))))));

        // One slide per beat — the beat card as a slide.
        int n = 1;
        foreach (var beat in report.Beats)
            slides.Add(BeatSlide(presentationPart, layoutPart, beat, n++));

        // Conversation summary.
        if (report.Recap is not null)
            slides.Add(RecapSlide(presentationPart, layoutPart, report.Recap));

        // Speakers.
        if (report.Speakers is { Speakers.Count: > 0 })
            slides.Add(SpeakersSlide(presentationPart, layoutPart, report.Speakers));

        // Presentation part: master, slide list, sizes.
        var slideIdList = new SlideIdList();
        uint sid = 256U;
        foreach (var sp in slides)
            slideIdList.Append(new SlideId { Id = sid++, RelationshipId = presentationPart.GetIdOfPart(sp) });

        presentationPart.Presentation = new Presentation(
            new SlideMasterIdList(new SlideMasterId { Id = 2147483648U, RelationshipId = presentationPart.GetIdOfPart(masterPart) }),
            slideIdList,
            new SlideSize { Cx = (Int32Value)(int)SlideW, Cy = (Int32Value)(int)SlideH },
            new NotesSize { Cx = 6858000, Cy = 9144000 });

        // Standard presentation-level parts a real deck carries; their absence can trigger a repair prompt.
        presentationPart.AddNewPart<PresentationPropertiesPart>().PresentationProperties = new PresentationProperties();
        presentationPart.AddNewPart<ViewPropertiesPart>().ViewProperties = new ViewProperties();
        presentationPart.AddNewPart<TableStylesPart>().TableStyleList =
            new D.TableStyleList { Default = "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}" };

        presentationPart.Presentation.Save();
    }

    // ── slides ──

    private static SlidePart BeatSlide(PresentationPart pp, SlideLayoutPart layout, ReportBeat beat, int index)
    {
        bool hasScene = beat.Scene is not null;
        const long titleY = 320040L, contentY = 1420000L;
        long contentBottom = SlideH - Margin;
        long nodesW = hasScene ? 5720000L : SlideW - 2 * Margin;

        // Numbered title.
        var title = TextShape(2, "title", Margin, titleY, SlideW - 2 * Margin, 900000L,
            Para(Run(index.ToString(), Accent, bold: true, sz: 3200),
                 Run("   " + (beat.Title?.Trim() ?? string.Empty), Ink, bold: true, sz: 3200)));

        // Node list.
        var nodeParas = new List<D.Paragraph>();
        foreach (var node in beat.Nodes)
        {
            var runs = new List<OpenXmlElement>
            {
                Run("\u25CF  ", Strip(ReportPalette.Hex(node.Color)), sz: 1600),
                Run(node.Label?.Trim() ?? string.Empty, Ink, bold: true, sz: 1600),
            };
            if (!string.IsNullOrWhiteSpace(node.Detail))
                runs.Add(Run("   " + node.Detail.Trim(), Dim, sz: 1400));
            nodeParas.Add(Para(runs.ToArray()));
        }
        if (nodeParas.Count == 0) nodeParas.Add(Para(Run(string.Empty, Ink, sz: 1600)));
        var nodes = TextShape(3, "nodes", Margin, contentY, nodesW, contentBottom - contentY, nodeParas.ToArray());

        var shapes = new List<OpenXmlElement> { title, nodes };
        if (hasScene)
        {
            long boxX = Margin + nodesW + 360000L;
            long boxW = SlideW - Margin - boxX;
            long boxH = contentBottom - contentY;
            var slidePart = NewSlide(pp, layout, shapes.ToArray());
            AppendPicture(slidePart, beat.Scene!, boxX, contentY, boxW, boxH);
            return slidePart;
        }
        return NewSlide(pp, layout, shapes.ToArray());
    }

    private static SlidePart RecapSlide(PresentationPart pp, SlideLayoutPart layout, MeetingRecap recap)
    {
        var body = new List<D.Paragraph>();
        if (!string.IsNullOrWhiteSpace(recap.Overview))
            body.Add(SpaceAfter(Para(Run(recap.Overview.Trim(), Ink, sz: 1800)), 1400));
        foreach (var t in recap.Topics)
        {
            var runs = new List<OpenXmlElement> { Run("\u25CF  ", Accent, sz: 1600), Run(t.Title?.Trim() ?? string.Empty, Ink, bold: true, sz: 1600) };
            if (!string.IsNullOrWhiteSpace(t.Summary)) runs.Add(Run("   " + t.Summary.Trim(), Dim, sz: 1400));
            body.Add(Para(runs.ToArray()));
        }
        return SectionSlide(pp, layout, "Conversation summary", body);
    }

    private static SlidePart SpeakersSlide(PresentationPart pp, SlideLayoutPart layout, SpeakerRecap recap)
    {
        var body = new List<D.Paragraph>();
        foreach (var s in recap.Speakers)
        {
            var runs = new List<OpenXmlElement> { Run("\u25CF  ", Accent, sz: 1600), Run(s.Speaker?.Trim() ?? string.Empty, Ink, bold: true, sz: 1600) };
            if (!string.IsNullOrWhiteSpace(s.Summary)) runs.Add(Run("   " + s.Summary.Trim(), Dim, sz: 1400));
            body.Add(Para(runs.ToArray()));
        }
        return SectionSlide(pp, layout, "Speakers", body);
    }

    private static SlidePart SectionSlide(PresentationPart pp, SlideLayoutPart layout, string heading, List<D.Paragraph> body) =>
        NewSlide(pp, layout,
            TextShape(2, "heading", Margin, 320040L, SlideW - 2 * Margin, 900000L,
                Para(Run(heading, Accent, bold: true, sz: 2800))),
            TextShape(3, "body", Margin, 1420000L, SlideW - 2 * Margin, SlideH - 1420000L - Margin, body.ToArray()));

    // ── low-level builders ──

    private static SlidePart NewSlide(PresentationPart pp, SlideLayoutPart layout, params OpenXmlElement[] shapes)
    {
        var slidePart = pp.AddNewPart<SlidePart>();
        var tree = new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new D.TransformGroup()));
        foreach (var s in shapes) tree.Append(s);
        slidePart.Slide = new Slide(new CommonSlideData(tree));
        slidePart.AddPart(layout);
        return slidePart;
    }

    private static P.Shape TextShape(uint id, string name, long x, long y, long cx, long cy, params D.Paragraph[] paragraphs)
    {
        var body = new P.TextBody(
            new D.BodyProperties { Wrap = D.TextWrappingValues.Square, Anchor = D.TextAnchoringTypeValues.Top },
            new D.ListStyle());
        foreach (var p in paragraphs) body.Append(p);
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new D.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new D.Transform2D(new D.Offset { X = x, Y = y }, new D.Extents { Cx = cx, Cy = cy }),
                new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle }),
            body);
    }

    private static void AppendPicture(SlidePart slidePart, byte[] png, long x, long y, long maxCx, long maxCy)
    {
        var imagePart = slidePart.AddImagePart(ImagePartType.Png);
        using (var ms = new MemoryStream(png)) imagePart.FeedData(ms);
        var relId = slidePart.GetIdOfPart(imagePart);

        var (w, h) = PngSize(png);
        long cx = maxCx, cy = (w > 0 && h > 0) ? maxCx * h / w : maxCx;
        if (cy > maxCy) { cy = maxCy; cx = (w > 0 && h > 0) ? maxCy * w / h : maxCy; }
        long ox = x + (maxCx - cx) / 2, oy = y + (maxCy - cy) / 2;   // centre in the box

        var pic = new P.Picture(
            new P.NonVisualPictureProperties(
                new P.NonVisualDrawingProperties { Id = 10U, Name = "scene" },
                new P.NonVisualPictureDrawingProperties(new D.PictureLocks { NoChangeAspect = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.BlipFill(new D.Blip { Embed = relId }, new D.Stretch(new D.FillRectangle())),
            new P.ShapeProperties(
                new D.Transform2D(new D.Offset { X = ox, Y = oy }, new D.Extents { Cx = cx, Cy = cy }),
                new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle }));

        slidePart.Slide?.CommonSlideData?.ShapeTree?.Append(pic);
    }

    private static D.Paragraph Para(params OpenXmlElement[] runs)
    {
        var p = new D.Paragraph(new D.ParagraphProperties(new D.NoBullet()));
        foreach (var r in runs) p.Append(r);
        return p;
    }

    private static D.Paragraph Center(D.Paragraph p)
    {
        p.ParagraphProperties ??= new D.ParagraphProperties();
        p.ParagraphProperties.Alignment = D.TextAlignmentTypeValues.Center;
        return p;
    }

    private static D.Paragraph SpaceAfter(D.Paragraph p, int points)
    {
        p.ParagraphProperties ??= new D.ParagraphProperties();
        p.ParagraphProperties.PrependChild(new D.SpaceAfter(new D.SpacingPoints { Val = points * 100 }));
        return p;
    }

    private static D.Run Run(string? text, string colorHex, bool bold = false, int sz = 1600) =>
        new(new D.RunProperties(new D.SolidFill(new D.RgbColorModelHex { Val = colorHex })) { Bold = bold, FontSize = sz, Language = "en-US" },
            new D.Text(text ?? string.Empty));

    private static P.ShapeTree EmptyTree() => new(
        new P.NonVisualGroupShapeProperties(
            new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
            new P.NonVisualGroupShapeDrawingProperties(),
            new P.ApplicationNonVisualDrawingProperties()),
        new P.GroupShapeProperties(new D.TransformGroup()));

    private static string MetaLine(SessionReport report)
    {
        var facts = new List<string>();
        if (report.Speakers is { Speakers.Count: > 0 } sp) facts.Add(Plural(sp.Speakers.Count, "speaker"));
        if (report.Beats.Count > 0) facts.Add(Plural(report.Beats.Count, "vision beat"));
        var meta = report.Timestamp.ToString("f");
        if (facts.Count > 0) meta += "   \u00b7   " + string.Join("   \u00b7   ", facts);
        return meta;
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    private static string Strip(string hex) => hex.StartsWith('#') ? hex[1..] : hex;

    private static (int w, int h) PngSize(byte[] png)
    {
        if (png.Length < 24) return (0, 0);
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }

    private const string ThemeXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Hark\">" +
        "<a:themeElements>" +
        "<a:clrScheme name=\"Hark\">" +
        "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1>" +
        "<a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
        "<a:dk2><a:srgbClr val=\"1F2430\"/></a:dk2>" +
        "<a:lt2><a:srgbClr val=\"E6EAEF\"/></a:lt2>" +
        "<a:accent1><a:srgbClr val=\"E24A3A\"/></a:accent1>" +
        "<a:accent2><a:srgbClr val=\"3B82F6\"/></a:accent2>" +
        "<a:accent3><a:srgbClr val=\"22C55E\"/></a:accent3>" +
        "<a:accent4><a:srgbClr val=\"F59E0B\"/></a:accent4>" +
        "<a:accent5><a:srgbClr val=\"A855F7\"/></a:accent5>" +
        "<a:accent6><a:srgbClr val=\"EF4444\"/></a:accent6>" +
        "<a:hlink><a:srgbClr val=\"3B82F6\"/></a:hlink>" +
        "<a:folHlink><a:srgbClr val=\"A855F7\"/></a:folHlink>" +
        "</a:clrScheme>" +
        "<a:fontScheme name=\"Hark\">" +
        "<a:majorFont><a:latin typeface=\"Segoe UI\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
        "<a:minorFont><a:latin typeface=\"Segoe UI\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont>" +
        "</a:fontScheme>" +
        "<a:fmtScheme name=\"Hark\">" +
        "<a:fillStyleLst>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "</a:fillStyleLst>" +
        "<a:lnStyleLst>" +
        "<a:ln><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>" +
        "<a:ln><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>" +
        "<a:ln><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>" +
        "</a:lnStyleLst>" +
        "<a:effectStyleLst>" +
        "<a:effectStyle><a:effectLst/></a:effectStyle>" +
        "<a:effectStyle><a:effectLst/></a:effectStyle>" +
        "<a:effectStyle><a:effectLst/></a:effectStyle>" +
        "</a:effectStyleLst>" +
        "<a:bgFillStyleLst>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "</a:bgFillStyleLst>" +
        "</a:fmtScheme>" +
        "</a:themeElements>" +
        "</a:theme>";
}
