using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF Positioned Wrapped Tables: reference frames, horizontal
    /// и vertical position controls, text distances, overlap flag, одинаковые
    /// свойства всех строк, nested tables и HTML metadata без layout-эмуляции.
    /// </summary>
    private static void TestRtfPositionedWrappedTables(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-positioned-wrapped-tables.rtf");

        DocumentModel Read(string source)
        {
            File.WriteAllText(
                path,
                source,
                Encoding.Latin1);

            return RtfDocumentReader.Read(path);
        }

        DocumentTablePositioning ReadPositioning(string controls)
        {
            var model =
                Read(
                    "{\\rtf1\\ansi" +
                    "\\trowd" +
                    controls +
                    "\\cellx1000\\intbl X\\cell\\row}");

            return model.Blocks
                       .OfType<DocumentTable>()
                       .Single()
                       .Positioning ??
                   throw new InvalidDataException(
                       "RTF positioned table не создала positioning metadata.");
        }

        bool RejectsInvalid(string source)
        {
            try
            {
                Read(source);
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        Assert(
            Read(
                    @"{\rtf1\ansi\trowd\cellx1000\intbl Plain\cell\row}")
                .Blocks
                .OfType<DocumentTable>()
                .Single()
                .Positioning == null,
            "RTF обычная flow table не должна получать positioned-table metadata.");

        var horizontalReferences =
            new[]
            {
                (
                    Control: @"\tphcol",
                    Expected: DocumentTableHorizontalReferenceFrame.Column),
                (
                    Control: @"\tphmrg",
                    Expected: DocumentTableHorizontalReferenceFrame.Margin),
                (
                    Control: @"\tphpg",
                    Expected: DocumentTableHorizontalReferenceFrame.Page)
            };

        foreach (var item in horizontalReferences)
        {
            Assert(
                ReadPositioning(item.Control).HorizontalReferenceFrame ==
                item.Expected,
                $"RTF {item.Control} должен сохранять точный horizontal reference frame.");
        }

        var horizontalPositions =
            new[]
            {
                (
                    Control: @"\tposx240",
                    Expected: DocumentTableHorizontalPositionKind.Offset,
                    Offset: (double?)12),
                (
                    Control: @"\tposnegx-240",
                    Expected: DocumentTableHorizontalPositionKind.NegativeOffset,
                    Offset: (double?)-12),
                (
                    Control: @"\tposxc",
                    Expected: DocumentTableHorizontalPositionKind.Center,
                    Offset: (double?)null),
                (
                    Control: @"\tposxi",
                    Expected: DocumentTableHorizontalPositionKind.Inside,
                    Offset: (double?)null),
                (
                    Control: @"\tposxo",
                    Expected: DocumentTableHorizontalPositionKind.Outside,
                    Offset: (double?)null),
                (
                    Control: @"\tposxl",
                    Expected: DocumentTableHorizontalPositionKind.Left,
                    Offset: (double?)null),
                (
                    Control: @"\tposxr",
                    Expected: DocumentTableHorizontalPositionKind.Right,
                    Offset: (double?)null)
            };

        foreach (var item in horizontalPositions)
        {
            var positioning =
                ReadPositioning(
                    item.Control);

            Assert(
                positioning.HorizontalPosition == item.Expected &&
                positioning.HorizontalOffsetPoints == item.Offset,
                $"RTF {item.Control} должен сохранять horizontal position kind/offset.");
        }

        var verticalReferences =
            new[]
            {
                (
                    Control: @"\tpvmrg",
                    Expected: DocumentTableVerticalReferenceFrame.Margin),
                (
                    Control: @"\tpvpg",
                    Expected: DocumentTableVerticalReferenceFrame.Page),
                (
                    Control: @"\tpvpara",
                    Expected: DocumentTableVerticalReferenceFrame.Paragraph)
            };

        foreach (var item in verticalReferences)
        {
            Assert(
                ReadPositioning(item.Control).VerticalReferenceFrame ==
                item.Expected,
                $"RTF {item.Control} должен сохранять точный vertical reference frame.");
        }

        var verticalPositions =
            new[]
            {
                (
                    Control: @"\tposy300",
                    Expected: DocumentTableVerticalPositionKind.Offset,
                    Offset: (double?)15),
                (
                    Control: @"\tposnegy-300",
                    Expected: DocumentTableVerticalPositionKind.NegativeOffset,
                    Offset: (double?)-15),
                (
                    Control: @"\tposyt",
                    Expected: DocumentTableVerticalPositionKind.Top,
                    Offset: (double?)null),
                (
                    Control: @"\tposyil",
                    Expected: DocumentTableVerticalPositionKind.Inline,
                    Offset: (double?)null),
                (
                    Control: @"\tposyb",
                    Expected: DocumentTableVerticalPositionKind.Bottom,
                    Offset: (double?)null),
                (
                    Control: @"\tposyc",
                    Expected: DocumentTableVerticalPositionKind.Center,
                    Offset: (double?)null),
                (
                    Control: @"\tposyin",
                    Expected: DocumentTableVerticalPositionKind.Inside,
                    Offset: (double?)null),
                (
                    Control: @"\tposyout",
                    Expected: DocumentTableVerticalPositionKind.Outside,
                    Offset: (double?)null)
            };

        foreach (var item in verticalPositions)
        {
            var positioning =
                ReadPositioning(
                    item.Control);

            Assert(
                positioning.VerticalPosition == item.Expected &&
                positioning.VerticalOffsetPoints == item.Offset,
                $"RTF {item.Control} должен сохранять vertical position kind/offset.");
        }

        const string fullPositioning =
            @"\tphmrg\tposnegx-240\tpvpg\tposnegy-480" +
            @"\tdfrmtxtLeft20\tdfrmtxtRight40" +
            @"\tdfrmtxtTop60\tdfrmtxtBottom80\tabsnoovrlp";

        var positionedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                fullPositioning +
                "\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd" +
                fullPositioning +
                "\\cellx1000\\intbl B\\cell\\row}");

        var positionedTable =
            positionedModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            positionedTable.Rows.Count == 2 &&
            positionedTable.Positioning is
            {
                HorizontalReferenceFrame: DocumentTableHorizontalReferenceFrame.Margin,
                HorizontalPosition: DocumentTableHorizontalPositionKind.NegativeOffset,
                HorizontalOffsetPoints: -12,
                VerticalReferenceFrame: DocumentTableVerticalReferenceFrame.Page,
                VerticalPosition: DocumentTableVerticalPositionKind.NegativeOffset,
                VerticalOffsetPoints: -24,
                TextDistanceLeftPoints: 1,
                TextDistanceRightPoints: 2,
                TextDistanceTopPoints: 3,
                TextDistanceBottomPoints: 4,
                PreventOverlap: true
            },
            "RTF positioned table должна сохранять общий row-position/wrapping profile всех строк.");

        var defaultNormalizedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\tphcol\\tposxc\\tpvmrg\\tposyc" +
                "\\tdfrmtxtLeft0\\tdfrmtxtRight0" +
                "\\tdfrmtxtTop0\\tdfrmtxtBottom0\\tabsnoovrlp0" +
                "\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd\\tposxc\\tposyc" +
                "\\cellx1000\\intbl B\\cell\\row}");

        Assert(
            defaultNormalizedModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Positioning is
            {
                HorizontalReferenceFrame: DocumentTableHorizontalReferenceFrame.Column,
                HorizontalPosition: DocumentTableHorizontalPositionKind.Center,
                VerticalReferenceFrame: DocumentTableVerticalReferenceFrame.Margin,
                VerticalPosition: DocumentTableVerticalPositionKind.Center,
                TextDistanceLeftPoints: 0,
                TextDistanceRightPoints: 0,
                TextDistanceTopPoints: 0,
                TextDistanceBottomPoints: 0,
                PreventOverlap: false
            },
            "RTF positioned-table equality должна учитывать документированные default reference/distance/overlap values.");

        var precedence =
            ReadPositioning(
                @"\tphcol\tphpg\tphmrg" +
                @"\tposx100\tposxc\tposxr" +
                @"\tpvmrg\tpvpara\tpvpg" +
                @"\tposy100\tposyt\tposyb");

        Assert(
            precedence.HorizontalReferenceFrame ==
                DocumentTableHorizontalReferenceFrame.Margin &&
            precedence.HorizontalPosition ==
                DocumentTableHorizontalPositionKind.Right &&
            precedence.HorizontalOffsetPoints == null &&
            precedence.VerticalReferenceFrame ==
                DocumentTableVerticalReferenceFrame.Page &&
            precedence.VerticalPosition ==
                DocumentTableVerticalPositionKind.Bottom &&
            precedence.VerticalOffsetPoints == null,
            "RTF positioned-table controls должны применять обычный last-property precedence.");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\cellx4000" +
                "\\intbl\\itap1 Outer before\\par" +
                "\\intbl\\itap2 Nested positioned\\nestcell" +
                "{\\*\\nesttableprops\\trowd" +
                "\\tphpg\\tposxc\\tpvpara\\tposyc" +
                "\\tdfrmtxtLeft100\\tabsnoovrlp" +
                "\\cellx1000\\nestrow}" +
                "\\intbl\\itap1 Outer after\\cell\\row" +
                "\\pard\\itap0 End\\par}");

        var nestedTable =
            nestedModel.Blocks
                .OfType<DocumentTable>()
                .SelectMany(table => table.Rows)
                .SelectMany(row => row.Cells)
                .SelectMany(cell => cell.Blocks)
                .OfType<DocumentTable>()
                .Single();

        Assert(
            nestedTable.Positioning is
            {
                HorizontalReferenceFrame: DocumentTableHorizontalReferenceFrame.Page,
                HorizontalPosition: DocumentTableHorizontalPositionKind.Center,
                VerticalReferenceFrame: DocumentTableVerticalReferenceFrame.Paragraph,
                VerticalPosition: DocumentTableVerticalPositionKind.Center,
                TextDistanceLeftPoints: 5,
                PreventOverlap: true
            },
            "RTF nested table должна иметь независимый positioned-table profile.");

        var resetModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\tphmrg\\tposxc\\cellx1000\\intbl P\\cell\\row" +
                "\\pard Between\\par" +
                "\\trowd\\cellx1000\\intbl Plain\\cell\\row}");

        var resetTables =
            resetModel.Blocks
                .OfType<DocumentTable>()
                .ToArray();

        Assert(
            resetTables.Length == 2 &&
            resetTables[0].Positioning != null &&
            resetTables[1].Positioning == null,
            "RTF FlushTable/trowd должны сбрасывать positioned-table state между таблицами.");

        var html =
            DocumentHtmlRenderer.Render(
                positionedModel,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-table-positioned=\"true\"",
            "RTF positioned table HTML marker");
        AssertContains(
            html,
            "data-table-position-horizontal-reference=\"margin\"",
            "RTF horizontal reference HTML metadata");
        AssertContains(
            html,
            "data-table-position-horizontal-kind=\"negative-offset\"",
            "RTF horizontal position HTML metadata");
        AssertContains(
            html,
            "data-table-position-horizontal-offset-pt=\"-12\"",
            "RTF horizontal offset HTML metadata");
        AssertContains(
            html,
            "data-table-position-vertical-reference=\"page\"",
            "RTF vertical reference HTML metadata");
        AssertContains(
            html,
            "data-table-position-vertical-kind=\"negative-offset\"",
            "RTF vertical position HTML metadata");
        AssertContains(
            html,
            "data-table-position-vertical-offset-pt=\"-24\"",
            "RTF vertical offset HTML metadata");
        AssertContains(
            html,
            "data-table-wrap-distance-left-pt=\"1\"",
            "RTF left wrap distance HTML metadata");
        AssertContains(
            html,
            "data-table-wrap-distance-right-pt=\"2\"",
            "RTF right wrap distance HTML metadata");
        AssertContains(
            html,
            "data-table-wrap-distance-top-pt=\"3\"",
            "RTF top wrap distance HTML metadata");
        AssertContains(
            html,
            "data-table-wrap-distance-bottom-pt=\"4\"",
            "RTF bottom wrap distance HTML metadata");
        AssertContains(
            html,
            "data-table-prevent-overlap=\"true\"",
            "RTF table no-overlap HTML metadata");

        Assert(
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\tposx\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\tposnegx\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\tposy\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\tposnegy\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\tdfrmtxtLeft\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\tdfrmtxtRight\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\tdfrmtxtTop\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\tdfrmtxtBottom\cellx1000\intbl X\cell\row}"),
            "RTF numeric positioned-table controls без parameter должны завершаться structural error.");

        Assert(
            RejectsInvalid(
                @"{\rtf1\ansi" +
                @"\trowd\tphmrg\tposxc\cellx1000\intbl A\cell\row" +
                @"\trowd\tphpg\tposxc\cellx1000\intbl B\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi" +
                @"\trowd\tphmrg\tposxc\cellx1000\intbl A\cell\row" +
                @"\trowd\cellx1000\intbl B\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi" +
                @"\trowd\tdfrmtxtLeft20\cellx1000\intbl A\cell\row" +
                @"\trowd\tdfrmtxtLeft21\cellx1000\intbl B\cell\row}"),
            "RTF positioned table с различными row positioning/wrapping properties должна отклоняться.");
    }
}
