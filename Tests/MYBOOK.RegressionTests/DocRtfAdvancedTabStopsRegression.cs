using System.IO;
using Book.Documents;
using Book.Formats.Doc;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет DOC TBD bar/list tab stops и legacy middle-dot leader,
    /// RTF \tb/\tlmdot, нейтральную модель и HTML metadata.
    /// </summary>
    private static void TestDocRtfAdvancedTabStops(string directory)
    {
        static DocDocumentReader.DocParagraphFormatting ReadDocTabs(
            params byte[] descriptors)
        {
            var operand = new List<byte>
            {
                0,
                0,
                (byte)descriptors.Length
            };

            for (var index = 0; index < descriptors.Length; index++)
            {
                operand.AddRange(
                    BitConverter.GetBytes(
                        checked((short)((index + 1) * 720))));
            }

            operand.AddRange(descriptors);
            operand[0] = checked((byte)(operand.Count - 1));

            var grpprl = new List<byte>
            {
                0x0D,
                0xC6
            };
            grpprl.AddRange(operand);

            var formatting =
                new DocDocumentReader.DocParagraphFormatting();
            DocDocumentReader.ReadParagraphGrpprl(
                grpprl.ToArray(),
                formatting);
            return formatting;
        }

        static bool RejectsDocTabDescriptor(byte descriptor)
        {
            try
            {
                ReadDocTabs(descriptor);
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        var docFormatting =
            ReadDocTabs(0xFC, 0x0E, 0x28);

        Assert(
            docFormatting.TabStops.Count == 3 &&
            docFormatting.TabStops[0].PositionPoints == 36 &&
            docFormatting.TabStops[0].Alignment == DocumentTabAlignment.Bar &&
            docFormatting.TabStops[0].Leader == DocumentTabLeader.None &&
            docFormatting.TabStops[1].PositionPoints == 72 &&
            docFormatting.TabStops[1].Alignment == DocumentTabAlignment.List &&
            docFormatting.TabStops[1].Leader == DocumentTabLeader.Dots &&
            docFormatting.TabStops[2].PositionPoints == 108 &&
            docFormatting.TabStops[2].Alignment == DocumentTabAlignment.Left &&
            docFormatting.TabStops[2].Leader == DocumentTabLeader.MiddleDots,
            "DOC TabJC bar/list и legacy TabLC middle-dot должны сохраняться; bar игнорирует TabLC.");

        Assert(
            RejectsDocTabDescriptor(0x05) &&
            RejectsDocTabDescriptor(0x30) &&
            RejectsDocTabDescriptor(0x38),
            "DOC неизвестные TabJC/TabLC должны отклоняться без подстановки.");

        var docHtml = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "Advanced DOC tab stops",
                Blocks =
                [
                    new DocumentParagraph
                    {
                        Inlines =
                        [
                            new DocumentInline { Text = "DOC tabs" }
                        ],
                        TabStops = docFormatting.TabStops
                    }
                ]
            },
            CreateRenderTheme());

        AssertContains(
            docHtml,
            "data-tab-stops=\"36:bar:none;72:list:dots;108:left:middle-dots\"",
            "DOC HTML metadata для bar/list и middle-dot.");

        var path =
            Path.Combine(
                directory,
                "advanced-tab-stops.rtf");
        File.WriteAllText(
            path,
            @"{\rtf1\ansi\pard\tlmdot\tx720\tldot\tb1440\tqr\tx2160 Advance\tab value\par}");

        var model = RtfDocumentReader.Read(path);
        var rtfParagraph = model.Blocks
            .Cast<DocumentParagraph>()
            .Single(paragraph => paragraph.Inlines.Any(inline =>
                inline.Text.Contains("Advance", StringComparison.Ordinal)));

        Assert(
            rtfParagraph.TabStops.Count == 3 &&
            rtfParagraph.TabStops[0].PositionPoints == 36 &&
            rtfParagraph.TabStops[0].Alignment == DocumentTabAlignment.Left &&
            rtfParagraph.TabStops[0].Leader == DocumentTabLeader.MiddleDots &&
            rtfParagraph.TabStops[1].PositionPoints == 72 &&
            rtfParagraph.TabStops[1].Alignment == DocumentTabAlignment.Bar &&
            rtfParagraph.TabStops[1].Leader == DocumentTabLeader.Dots &&
            rtfParagraph.TabStops[2].PositionPoints == 108 &&
            rtfParagraph.TabStops[2].Alignment == DocumentTabAlignment.Right &&
            rtfParagraph.TabStops[2].Leader == DocumentTabLeader.None &&
            rtfParagraph.Inlines.Any(inline =>
                inline.Text.Contains('\t')),
            "RTF \\tb и \\tlmdot должны переноситься в TabStops с reset pending options.");

        var rtfHtml =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            rtfHtml,
            "data-tab-stops=\"36:left:middle-dots;72:bar:dots;108:right:none\"",
            "RTF HTML metadata для middle-dot и bar tabs.");
    }
}
