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

    // 16:9 slide; the palette (dark deck so the scenes pop; mirrors the app/HTML accent language).
    private const long SlideW = 12192000L, SlideH = 6858000L;
    private const long Margin = 640080L;                          // ~0.67"
    private const long PanelW = 5030000L;                         // full-bleed scene panel on the right
    private const long PanelX = SlideW - PanelW;
    private const long Gutter = 460000L;
    private const string Bg = "0B0D10", Ink = "E6EAEF", Dim = "9AA2AC", Accent = "E24A3A", Faint = "5A626C";

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

        int total = 1 + report.Beats.Count
            + (report.Recap is not null ? 1 : 0)
            + (report.Speakers is { Speakers.Count: > 0 } ? 1 : 0);

        byte[]? hero = null;
        foreach (var b in report.Beats) if (b.Scene is not null) { hero = b.Scene; break; }

        var slides = new List<SlidePart> { TitleSlide(presentationPart, layoutPart, report, hero) };
        int beatNo = 1, page = 2;
        foreach (var beat in report.Beats)
            slides.Add(BeatSlide(presentationPart, layoutPart, beat, beatNo++, page++, total));
        if (report.Recap is not null)
            slides.Add(RecapSlide(presentationPart, layoutPart, report.Recap, page++, total));
        if (report.Speakers is { Speakers.Count: > 0 })
            slides.Add(SpeakersSlide(presentationPart, layoutPart, report.Speakers, page++, total));

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

    /// <summary>Cinematic hero: the first scene full-bleed under a scrim, with the title set in the lower third.</summary>
    private static SlidePart TitleSlide(PresentationPart pp, SlideLayoutPart layout, SessionReport report, byte[]? hero)
    {
        var sp = NewSlide(pp, layout);
        if (hero is not null)
        {
            CoverPicture(sp, hero, 0, 0, SlideW, SlideH, 10U);
            Add(sp, RectShape(11, "scrim", 0, 0, SlideW, SlideH, "000000", 62000));
            Add(sp, TextShape(2, "eyebrow", Margin, 4160000L, 9000000L, 320000L,
                P(null, 0, 0, 0, Run("HARK \u00b7 SESSION REPORT", Accent, bold: true, sz: 1300, spc: 240))));
            Add(sp, TextShape(3, "title", Margin, 4520000L, SlideW - 2 * Margin, 1500000L,
                P(null, 0, 0, 0, Run(report.Title, Ink, bold: true, sz: 4400))));
            Add(sp, TextShape(4, "meta", Margin, 6060000L, SlideW - 2 * Margin, 500000L,
                P(null, 0, 0, 0, Run(MetaLine(report), Dim, sz: 1500))));
        }
        else
        {
            Add(sp, TextShape(2, "eyebrow", Margin, 2560000L, SlideW - 2 * Margin, 320000L,
                P(D.TextAlignmentTypeValues.Center, 0, 0, 0, Run("HARK \u00b7 SESSION REPORT", Accent, bold: true, sz: 1300, spc: 240))));
            Add(sp, TextShape(3, "title", Margin, 2920000L, SlideW - 2 * Margin, 1400000L,
                P(D.TextAlignmentTypeValues.Center, 0, 0, 0, Run(report.Title, Ink, bold: true, sz: 4400))));
            Add(sp, TextShape(4, "meta", Margin, 4360000L, SlideW - 2 * Margin, 500000L,
                P(D.TextAlignmentTypeValues.Center, 0, 0, 0, Run(MetaLine(report), Dim, sz: 1500))));
        }
        return sp;
    }

    private static SlidePart BeatSlide(PresentationPart pp, SlideLayoutPart layout, ReportBeat beat, int beatNo, int page, int total)
    {
        bool hasScene = beat.Scene is not null;
        bool imageLeft = hasScene && beatNo % 2 == 0;   // alternate the scene side for editorial rhythm
        var sp = NewSlide(pp, layout);

        long textX = !hasScene ? Margin : (imageLeft ? PanelW + Gutter : Margin);
        long textW = !hasScene ? 9400000L : PanelX - Gutter - Margin;
        if (hasScene) CoverPicture(sp, beat.Scene!, imageLeft ? 0 : PanelX, 0, PanelW, SlideH, 10U);

        Add(sp, TextShape(2, "kicker", textX, 540000L, textW, 300000L,
            P(null, 0, 0, 0, Run($"VISION \u00b7 BEAT {beatNo:00}", Accent, bold: true, sz: 1200, spc: 220))));
        Add(sp, TextShape(3, "title", textX, 880000L, textW, 1480000L,
            P(null, 0, 0, 0, Run(beat.Title?.Trim() ?? string.Empty, Ink, bold: true, sz: 3000))));
        Add(sp, RectShape(4, "rule", textX, 2500000L, 520000L, 40000L, Accent));

        var nodeParas = new List<D.Paragraph>();
        foreach (var node in beat.Nodes)
            nodeParas.Add(NodePara(Strip(ReportPalette.Hex(node.Color)), node.Label?.Trim() ?? string.Empty, node.Detail, 9));
        if (nodeParas.Count == 0) nodeParas.Add(P(null, 0, 0, 0, Run(string.Empty, Ink, sz: 1600)));
        Add(sp, TextShape(5, "nodes", textX, 2760000L, textW, SlideH - 2760000L - 560000L, nodeParas.ToArray()));

        Footer(sp, page, total, textX, textX + textW);
        return sp;
    }

    private static SlidePart RecapSlide(PresentationPart pp, SlideLayoutPart layout, MeetingRecap recap, int page, int total)
    {
        var body = new List<D.Paragraph>();
        if (!string.IsNullOrWhiteSpace(recap.Overview))
            body.Add(P(null, 0, 0, 14, Run(recap.Overview.Trim(), Ink, sz: 1700)));
        foreach (var t in recap.Topics)
            body.Add(NodePara(Accent, t.Title?.Trim() ?? string.Empty, t.Summary, 10));
        return SectionSlide(pp, layout, "Topics", "Conversation summary", body, page, total);
    }

    private static SlidePart SpeakersSlide(PresentationPart pp, SlideLayoutPart layout, SpeakerRecap recap, int page, int total)
    {
        var body = new List<D.Paragraph>();
        foreach (var s in recap.Speakers)
            body.Add(NodePara(Accent, s.Speaker?.Trim() ?? string.Empty, s.Summary, 10));
        return SectionSlide(pp, layout, "People", "Speakers", body, page, total);
    }

    private static SlidePart SectionSlide(PresentationPart pp, SlideLayoutPart layout, string kicker, string heading, List<D.Paragraph> body, int page, int total)
    {
        var sp = NewSlide(pp, layout);
        Add(sp, TextShape(2, "kicker", Margin, 540000L, 9400000L, 300000L,
            P(null, 0, 0, 0, Run(kicker.ToUpperInvariant(), Accent, bold: true, sz: 1200, spc: 220))));
        Add(sp, TextShape(3, "heading", Margin, 880000L, 9400000L, 1000000L,
            P(null, 0, 0, 0, Run(heading, Ink, bold: true, sz: 3000))));
        Add(sp, RectShape(4, "rule", Margin, 1980000L, 520000L, 40000L, Accent));
        Add(sp, TextShape(5, "body", Margin, 2260000L, 10200000L, SlideH - 2260000L - 560000L, body.ToArray()));
        Footer(sp, page, total, Margin, SlideW - Margin);
        return sp;
    }

    // ── low-level builders ──

    private static SlidePart NewSlide(PresentationPart pp, SlideLayoutPart layout)
    {
        var slidePart = pp.AddNewPart<SlidePart>();
        slidePart.Slide = new Slide(new CommonSlideData(EmptyTree()));
        slidePart.AddPart(layout);
        return slidePart;
    }

    private static void Add(SlidePart slidePart, OpenXmlElement shape) =>
        slidePart.Slide?.CommonSlideData?.ShapeTree?.Append(shape);

    /// <summary>A small footer: the HARK wordmark (at <paramref name="leftX"/>) and the page number (right-aligned at <paramref name="rightX"/>).</summary>
    private static void Footer(SlidePart sp, int page, int total, long leftX, long rightX)
    {
        const long fy = SlideH - 470000L;
        Add(sp, TextShape(6, "wm", leftX, fy, 3000000L, 300000L,
            P(null, 0, 0, 0, Run("HARK", Faint, sz: 1000, spc: 300))));
        Add(sp, TextShape(7, "pg", rightX - 3000000L, fy, 3000000L, 300000L,
            P(D.TextAlignmentTypeValues.Right, 0, 0, 0, Run($"{page:00} / {total:00}", Faint, sz: 1000, spc: 150))));
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

    /// <summary>A filled rectangle (accent rule or scrim); <paramref name="alpha"/> is 0–100000 (100000 = opaque).</summary>
    private static P.Shape RectShape(uint id, string name, long x, long y, long cx, long cy, string hex, int? alpha = null)
    {
        var clr = new D.RgbColorModelHex { Val = hex };
        if (alpha is int a) clr.Append(new D.Alpha { Val = a });
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new D.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new D.Transform2D(new D.Offset { X = x, Y = y }, new D.Extents { Cx = cx, Cy = cy }),
                new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle },
                new D.SolidFill(clr),
                new D.Outline(new D.NoFill())),
            new P.TextBody(new D.BodyProperties(), new D.ListStyle(), new D.Paragraph()));
    }

    /// <summary>Places a PNG to fill the box edge-to-edge, centre-cropped (cover), via a source rectangle.</summary>
    private static void CoverPicture(SlidePart slidePart, byte[] png, long x, long y, long cx, long cy, uint id)
    {
        var imagePart = slidePart.AddImagePart(ImagePartType.Png);
        using (var ms = new MemoryStream(png)) imagePart.FeedData(ms);
        var relId = slidePart.GetIdOfPart(imagePart);

        var (w, h) = PngSize(png);
        int l = 0, t = 0, r = 0, b = 0;
        if (w > 0 && h > 0)
        {
            double ai = (double)w / h, ap = (double)cx / cy;
            if (ai > ap) { int c = (int)((1 - ap / ai) / 2 * 100000); l = c; r = c; }        // crop sides
            else if (ai < ap) { int c = (int)((1 - ai / ap) / 2 * 100000); t = c; b = c; }   // crop top/bottom
        }
        var blipFill = new P.BlipFill(new D.Blip { Embed = relId });
        if ((l | t | r | b) != 0) blipFill.Append(new D.SourceRectangle { Left = l, Top = t, Right = r, Bottom = b });
        blipFill.Append(new D.Stretch(new D.FillRectangle()));

        Add(slidePart, new P.Picture(
            new P.NonVisualPictureProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "scene" },
                new P.NonVisualPictureDrawingProperties(new D.PictureLocks { NoChangeAspect = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            blipFill,
            new P.ShapeProperties(
                new D.Transform2D(new D.Offset { X = x, Y = y }, new D.Extents { Cx = cx, Cy = cy }),
                new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle })));
    }

    /// <summary>A node/topic line: a coloured dot + bold label, with the detail on the next line under the label.</summary>
    private static D.Paragraph NodePara(string dotColor, string label, string? detail, int spaceAfterPts)
    {
        var runs = new List<OpenXmlElement>
        {
            Run("\u25CF  ", dotColor, sz: 1500),
            Run(label, Ink, bold: true, sz: 1600),
        };
        if (!string.IsNullOrWhiteSpace(detail))
        {
            runs.Add(new D.Break());
            runs.Add(Run(detail!.Trim(), Dim, sz: 1400));
        }
        return P(null, 250000L, -250000L, spaceAfterPts, runs.ToArray());
    }

    private static D.Paragraph P(D.TextAlignmentTypeValues? align, long marL, long indent, int spaceAfterPts, params OpenXmlElement[] runs)
    {
        var pPr = new D.ParagraphProperties();
        if (marL != 0) pPr.LeftMargin = (int)marL;
        if (indent != 0) pPr.Indent = (int)indent;
        if (align is { } a) pPr.Alignment = a;
        if (spaceAfterPts > 0) pPr.Append(new D.SpaceAfter(new D.SpacingPoints { Val = spaceAfterPts * 100 }));
        pPr.Append(new D.NoBullet());
        var para = new D.Paragraph(pPr);
        foreach (var run in runs) para.Append(run);
        return para;
    }

    private static D.Run Run(string? text, string colorHex, bool bold = false, int sz = 1600, int spc = 0)
    {
        var rp = new D.RunProperties(new D.SolidFill(new D.RgbColorModelHex { Val = colorHex }))
        { Bold = bold, FontSize = sz, Language = "en-US" };
        if (spc != 0) rp.Spacing = spc;
        return new D.Run(rp, new D.Text(text ?? string.Empty));
    }

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
