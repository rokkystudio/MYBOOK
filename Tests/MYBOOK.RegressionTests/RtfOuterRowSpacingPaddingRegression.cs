using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет outer row spacing/padding RTF через rowspcout/rowpadout:
    /// raw values и units, twips-to-points для unit 3, explicit unit 0,
    /// отсутствующий unit, trowd reset, nested rows и HTML metadata.
    /// </summary>
    private static void TestRtfOuterRowSpacingPadding(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-outer-row-spacing-padding.rtf");

        DocumentModel Read(string source)
        {
            File.WriteAllText(
                path,
                source,
                Encoding.Latin1);

            return RtfDocumentReader.Read(path);
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

        var model =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\trspol20\\trspofl3" +
                "\\trspor40\\trspofr3" +
                "\\trspot60\\trspoft3" +
                "\\trspob80\\trspofb3" +
                "\\trpadol100\\trpadofl3" +
                "\\trpador120\\trpadofr3" +
                "\\trpadot140\\trpadoft3" +
                "\\trpadob160\\trpadofb3" +
                "\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd" +
                "\\trspol-21" +
                "\\trspor22\\trspofr0" +
                "\\trpadot-23" +
                "\\trpadob24\\trpadofb0" +
                "\\cellx1000\\intbl B\\cell\\row" +
                "\\trowd" +
                "\\cellx1000\\intbl C\\cell\\row}");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            table.Rows.Count == 3,
            "RTF outer row spacing/padding regression должна содержать три строки.");

        var first =
            table.Rows[0];

        AssertMeasurement(
            first.OuterCellSpacingLeft,
            20,
            3,
            1,
            "RTF trspol/trspofl");
        AssertMeasurement(
            first.OuterCellSpacingRight,
            40,
            3,
            2,
            "RTF trspor/trspofr");
        AssertMeasurement(
            first.OuterCellSpacingTop,
            60,
            3,
            3,
            "RTF trspot/trspoft");
        AssertMeasurement(
            first.OuterCellSpacingBottom,
            80,
            3,
            4,
            "RTF trspob/trspofb");
        AssertMeasurement(
            first.OuterPaddingLeft,
            100,
            3,
            5,
            "RTF trpadol/trpadofl");
        AssertMeasurement(
            first.OuterPaddingRight,
            120,
            3,
            6,
            "RTF trpador/trpadofr");
        AssertMeasurement(
            first.OuterPaddingTop,
            140,
            3,
            7,
            "RTF trpadot/trpadoft");
        AssertMeasurement(
            first.OuterPaddingBottom,
            160,
            3,
            8,
            "RTF trpadob/trpadofb");

        var second =
            table.Rows[1];

        AssertMeasurement(
            second.OuterCellSpacingLeft,
            -21,
            null,
            null,
            "RTF outer spacing без unit");
        AssertMeasurement(
            second.OuterCellSpacingRight,
            22,
            0,
            null,
            "RTF outer spacing explicit Null unit");
        AssertMeasurement(
            second.OuterPaddingTop,
            -23,
            null,
            null,
            "RTF outer padding без unit");
        AssertMeasurement(
            second.OuterPaddingBottom,
            24,
            0,
            null,
            "RTF outer padding explicit Null unit");

        Assert(
            second.OuterCellSpacingTop == null &&
            second.OuterCellSpacingBottom == null &&
            second.OuterPaddingLeft == null &&
            second.OuterPaddingRight == null,
            "RTF omitted outer row measurements не должны синтезироваться.");

        var third =
            table.Rows[2];

        Assert(
            third.OuterCellSpacingTop == null &&
            third.OuterCellSpacingRight == null &&
            third.OuterCellSpacingBottom == null &&
            third.OuterCellSpacingLeft == null &&
            third.OuterPaddingTop == null &&
            third.OuterPaddingRight == null &&
            third.OuterPaddingBottom == null &&
            third.OuterPaddingLeft == null,
            "RTF trowd должен сбрасывать весь outer spacing/padding state.");

        var precedenceModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\trspol10\\trspol30\\trspofl0\\trspofl3" +
                "\\trpadot40\\trpadot50\\trpadoft0\\trpadoft3" +
                "\\cellx1000\\intbl P\\cell\\row}");

        var precedenceRow =
            precedenceModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single();

        AssertMeasurement(
            precedenceRow.OuterCellSpacingLeft,
            30,
            3,
            1.5,
            "RTF outer spacing last-property precedence");
        AssertMeasurement(
            precedenceRow.OuterPaddingTop,
            50,
            3,
            2.5,
            "RTF outer padding last-property precedence");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\cellx4000" +
                "\\intbl\\itap1 Outer before\\par" +
                "\\intbl\\itap2 Nested\\nestcell" +
                "{\\*\\nesttableprops\\trowd" +
                "\\trspol200\\trspofl3" +
                "\\trpadot220\\trpadoft3" +
                "\\cellx1000\\nestrow}" +
                "\\intbl\\itap1 Outer after\\cell\\row" +
                "\\pard\\itap0 End\\par}");

        var outerTable =
            nestedModel.Blocks
                .OfType<DocumentTable>()
                .Single();
        var nestedTable =
            outerTable.Rows
                .SelectMany(row => row.Cells)
                .SelectMany(cell => cell.Blocks)
                .OfType<DocumentTable>()
                .Single();

        Assert(
            outerTable.Rows.Single().OuterCellSpacingLeft == null &&
            outerTable.Rows.Single().OuterPaddingTop == null,
            "RTF nested outer measurement state не должен протекать в parent row.");
        AssertMeasurement(
            nestedTable.Rows.Single().OuterCellSpacingLeft,
            200,
            3,
            10,
            "RTF nested trspol/trspofl");
        AssertMeasurement(
            nestedTable.Rows.Single().OuterPaddingTop,
            220,
            3,
            11,
            "RTF nested trpadot/trpadoft");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-row-outer-cell-spacing-left-value=\"20\"",
            "RTF outer spacing raw value HTML metadata");
        AssertContains(
            html,
            "data-row-outer-cell-spacing-left-unit=\"3\"",
            "RTF outer spacing unit HTML metadata");
        AssertContains(
            html,
            "data-row-outer-cell-spacing-left-pt=\"1\"",
            "RTF outer spacing points HTML metadata");
        AssertContains(
            html,
            "data-row-outer-padding-top-value=\"140\"",
            "RTF outer padding raw value HTML metadata");
        AssertContains(
            html,
            "data-row-outer-padding-top-unit=\"3\"",
            "RTF outer padding unit HTML metadata");
        AssertContains(
            html,
            "data-row-outer-padding-top-pt=\"7\"",
            "RTF outer padding points HTML metadata");
        AssertContains(
            html,
            "data-row-outer-cell-spacing-left-value=\"-21\"",
            "RTF outer spacing missing-unit raw HTML metadata");
        AssertContains(
            html,
            "data-row-outer-cell-spacing-right-unit=\"0\"",
            "RTF outer spacing explicit Null HTML metadata");
        AssertContains(
            html,
            "data-row-outer-padding-bottom-unit=\"0\"",
            "RTF outer padding explicit Null HTML metadata");

        var numericControls =
            new[]
            {
                "trspol",
                "trspor",
                "trspot",
                "trspob",
                "trspofl",
                "trspofr",
                "trspoft",
                "trspofb",
                "trpadol",
                "trpador",
                "trpadot",
                "trpadob",
                "trpadofl",
                "trpadofr",
                "trpadoft",
                "trpadofb"
            };

        foreach (var control in numericControls)
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi\\trowd\\" +
                    control +
                    "\\cellx1000\\intbl X\\cell\\row}"),
                $"RTF {control} без numeric parameter должен завершаться structural error.");
        }

        Assert(
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\trspofl3" +
                "\\cellx1000\\intbl X\\cell\\row}") &&
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\trpadoft3" +
                "\\cellx1000\\intbl X\\cell\\row}"),
            "RTF outer measurement unit без соответствующего value должен отклоняться.");

        Assert(
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\trspol20\\trspofl1" +
                "\\cellx1000\\intbl X\\cell\\row}") &&
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\trpadot20\\trpadoft2" +
                "\\cellx1000\\intbl X\\cell\\row}"),
            "RTF outer row measurement должен принимать только документированные units 0 и 3.");
    }

    private static void AssertMeasurement(
        DocumentTableMeasurement? measurement,
        int expectedValue,
        int? expectedUnit,
        double? expectedPoints,
        string name)
    {
        Assert(
            measurement != null &&
            measurement.Value == expectedValue &&
            measurement.Unit == expectedUnit &&
            measurement.Points == expectedPoints,
            $"{name}: ожидались value={expectedValue}, unit={expectedUnit?.ToString() ?? "null"}, points={expectedPoints?.ToString() ?? "null"}.");
    }
}
