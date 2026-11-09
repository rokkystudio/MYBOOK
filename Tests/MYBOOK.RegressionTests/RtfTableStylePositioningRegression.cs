using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет neutral RTF table-style positioning и wrapping:
    /// explicit reference frames, offset kinds, signed Int32 twips, converted
    /// points, placement precedence, wrap distances, overlap, conditional
    /// entries, stylesheet isolation и parameterless/numeric grammar.
    /// </summary>
    private static void TestRtfTableStylePositioning(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-positioning.rtf");

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
                "{\\stylesheet" +
                "{\\*\\ts220\\tsrowd" +
                "\\tphcol\\tphmrg\\tphpg" +
                "\\tposx100\\tposnegx-320" +
                "\\tpvmrg\\tpvpg\\tpvpara" +
                "\\tposy240\\tposnegy-480" +
                "\\tdfrmtxtLeft80\\tdfrmtxtRight-40" +
                "\\tdfrmtxtTop0\\tdfrmtxtBottom60" +
                "\\tabsnoovrlp Anchored;}" +
                "{\\*\\ts221\\tsrowd" +
                "\\tposx100\\tposxc\\tposy200\\tposyil Placement;}" +
                "{\\*\\ts222\\tsrowd" +
                "\\tdfrmtxtTop0\\tabsnoovrlp Explicit zero;}" +
                "{\\*\\ts223\\tsrowd Plain;}" +
                "{\\*\\ts220\\tsrowd\\tscfirstrow" +
                "\\tphmrg\\tposxr\\tpvmrg\\tposyt Conditional;}" +
                "{\\*\\ts224\\tsrowd" +
                "{\\*\\keycode\\shift\\ctrl t}" +
                "\\tposxi Nested keycode;}" +
                "}" +
                "\\trowd\\cellx1000\\intbl Body\\cell\\row}");

        Assert(
            model.TableStyles.Count == 6,
            "RTF stylesheet должен сохранять шесть ordered table-style entries.");

        var anchored =
            model.TableStyles[0].RowFormatting?.Positioning;

        Assert(
            anchored is
            {
                HorizontalReferenceFrame: DocumentTableHorizontalReferenceFrame.Page,
                HorizontalPosition: DocumentTableHorizontalPositionKind.NegativeOffset,
                HorizontalOffsetTwips: -320,
                HorizontalOffsetPoints: -16,
                VerticalReferenceFrame: DocumentTableVerticalReferenceFrame.Paragraph,
                VerticalPosition: DocumentTableVerticalPositionKind.NegativeOffset,
                VerticalOffsetTwips: -480,
                VerticalOffsetPoints: -24,
                TextDistanceLeftTwips: 80,
                TextDistanceLeftPoints: 4,
                TextDistanceRightTwips: -40,
                TextDistanceRightPoints: -2,
                TextDistanceTopTwips: 0,
                TextDistanceTopPoints: 0,
                TextDistanceBottomTwips: 60,
                TextDistanceBottomPoints: 3,
                PreventOverlap: true
            },
            "RTF positioned table style должен сохранять explicit frames, last offset kinds, signed twips, points и wrap.");

        var placement =
            model.TableStyles[1].RowFormatting?.Positioning;

        Assert(
            placement is
            {
                HorizontalReferenceFrame: null,
                HorizontalPosition: DocumentTableHorizontalPositionKind.Center,
                HorizontalOffsetTwips: null,
                HorizontalOffsetPoints: null,
                VerticalReferenceFrame: null,
                VerticalPosition: DocumentTableVerticalPositionKind.Inline,
                VerticalOffsetTwips: null,
                VerticalOffsetPoints: null,
                TextDistanceLeftTwips: null,
                TextDistanceRightTwips: null,
                TextDistanceTopTwips: null,
                TextDistanceBottomTwips: null,
                PreventOverlap: null
            },
            "RTF placement flags должны сбрасывать предыдущий numeric offset, не добавляя body defaults.");

        var explicitZero =
            model.TableStyles[2].RowFormatting?.Positioning;

        Assert(
            explicitZero is
            {
                HorizontalReferenceFrame: null,
                HorizontalPosition: null,
                VerticalReferenceFrame: null,
                VerticalPosition: null,
                TextDistanceTopTwips: 0,
                TextDistanceTopPoints: 0,
                TextDistanceLeftTwips: null,
                TextDistanceRightTwips: null,
                TextDistanceBottomTwips: null,
                PreventOverlap: true
            },
            "RTF explicit zero wrap distance и overlap должны отличаться от отсутствующих controls.");

        Assert(
            model.TableStyles[3].RowFormatting == null,
            "RTF table style без row properties должен сохранять null RowFormatting.");

        var conditional =
            model.TableStyles[4].RowFormatting?.Positioning;

        Assert(
            model.TableStyles[4].Handle == 220 &&
            model.TableStyles[4].Conditions ==
                DocumentTableStyleCondition.FirstRow &&
            conditional is
            {
                HorizontalReferenceFrame: DocumentTableHorizontalReferenceFrame.Margin,
                HorizontalPosition: DocumentTableHorizontalPositionKind.Right,
                HorizontalOffsetTwips: null,
                VerticalReferenceFrame: DocumentTableVerticalReferenceFrame.Margin,
                VerticalPosition: DocumentTableVerticalPositionKind.Top,
                VerticalOffsetTwips: null,
                TextDistanceLeftTwips: null,
                PreventOverlap: null
            } &&
            anchored?.HorizontalPosition ==
                DocumentTableHorizontalPositionKind.NegativeOffset,
            "RTF conditional entry с повторным style handle должна иметь независимые positioning controls.");

        var nested =
            model.TableStyles[5].RowFormatting?.Positioning;

        Assert(
            nested is
            {
                HorizontalReferenceFrame: null,
                HorizontalPosition: DocumentTableHorizontalPositionKind.Inside,
                HorizontalOffsetTwips: null,
                VerticalPosition: null,
                PreventOverlap: null
            },
            "RTF nested keycode не должен менять table-style positioning.");

        var bodyTable =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            bodyTable.Positioning == null,
            "RTF table-style positioning не должен создавать positioning metadata body flow table.");

        var bodyPositioned =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet{\\*\\ts225\\tsrowd\\tphpg\\tposxr Style;}}" +
                "\\trowd\\tphmrg\\tposxc\\tpvmrg\\tposyt" +
                "\\tdfrmtxtLeft40\\tabsnoovrlp" +
                "\\cellx1000\\intbl Body\\cell\\row}");

        var positionedTable =
            bodyPositioned.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            positionedTable.Positioning is
            {
                HorizontalReferenceFrame: DocumentTableHorizontalReferenceFrame.Margin,
                HorizontalPosition: DocumentTableHorizontalPositionKind.Center,
                VerticalReferenceFrame: DocumentTableVerticalReferenceFrame.Margin,
                VerticalPosition: DocumentTableVerticalPositionKind.Top,
                TextDistanceLeftPoints: 2,
                PreventOverlap: true
            } &&
            bodyPositioned.TableStyles.Single().RowFormatting?.Positioning is
            {
                HorizontalReferenceFrame: DocumentTableHorizontalReferenceFrame.Page,
                HorizontalPosition: DocumentTableHorizontalPositionKind.Right
            },
            "RTF body positioned table должен использовать только body controls, не table-style metadata.");

        var boundary =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet{\\*\\ts226\\tsrowd" +
                "\\tposnegx-2147483648\\tposy2147483647" +
                "\\tdfrmtxtleft-2147483648 Extreme;}}" +
                "Body\\par}");

        Assert(
            boundary.TableStyles.Single().RowFormatting?.Positioning is
            {
                HorizontalPosition: DocumentTableHorizontalPositionKind.NegativeOffset,
                HorizontalOffsetTwips: int.MinValue,
                VerticalPosition: DocumentTableVerticalPositionKind.Offset,
                VerticalOffsetTwips: int.MaxValue,
                TextDistanceLeftTwips: int.MinValue
            } &&
            boundary.TableStyles.Single().RowFormatting?.Positioning?.HorizontalOffsetPoints ==
                int.MinValue / 20.0 &&
            boundary.TableStyles.Single().RowFormatting?.Positioning?.VerticalOffsetPoints ==
                int.MaxValue / 20.0,
            "RTF position/wrap values должны сохранять signed Int32 boundaries.");

        var horizontalPlacements =
            new (string Control, DocumentTableHorizontalPositionKind Kind)[]
            {
                ("tposxc", DocumentTableHorizontalPositionKind.Center),
                ("tposxi", DocumentTableHorizontalPositionKind.Inside),
                ("tposxo", DocumentTableHorizontalPositionKind.Outside),
                ("tposxl", DocumentTableHorizontalPositionKind.Left),
                ("tposxr", DocumentTableHorizontalPositionKind.Right)
            };

        foreach (var (control, kind) in horizontalPlacements)
        {
            var flags =
                Read(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts227\\tsrowd\\" +
                    control +
                    " Flag;}}" +
                    "Body\\par}");

            Assert(
                flags.TableStyles.Single().RowFormatting?.Positioning is
                {
                    HorizontalReferenceFrame: null,
                    HorizontalOffsetTwips: null
                } &&
                flags.TableStyles.Single().RowFormatting?.Positioning?.HorizontalPosition ==
                    kind,
                $"RTF {control} должен сохранять точный horizontal placement kind.");
        }

        var verticalPlacements =
            new (string Control, DocumentTableVerticalPositionKind Kind)[]
            {
                ("tposyt", DocumentTableVerticalPositionKind.Top),
                ("tposyil", DocumentTableVerticalPositionKind.Inline),
                ("tposyb", DocumentTableVerticalPositionKind.Bottom),
                ("tposyc", DocumentTableVerticalPositionKind.Center),
                ("tposyin", DocumentTableVerticalPositionKind.Inside),
                ("tposyout", DocumentTableVerticalPositionKind.Outside)
            };

        foreach (var (control, kind) in verticalPlacements)
        {
            var flags =
                Read(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts228\\tsrowd\\" +
                    control +
                    " Flag;}}" +
                    "Body\\par}");

            Assert(
                flags.TableStyles.Single().RowFormatting?.Positioning is
                {
                    VerticalReferenceFrame: null,
                    VerticalOffsetTwips: null
                } &&
                flags.TableStyles.Single().RowFormatting?.Positioning?.VerticalPosition ==
                    kind,
                $"RTF {control} должен сохранять точный vertical placement kind.");
        }

        var horizontalFrames =
            new (string Control, DocumentTableHorizontalReferenceFrame Frame)[]
            {
                ("tphcol", DocumentTableHorizontalReferenceFrame.Column),
                ("tphmrg", DocumentTableHorizontalReferenceFrame.Margin),
                ("tphpg", DocumentTableHorizontalReferenceFrame.Page)
            };

        foreach (var (control, frame) in horizontalFrames)
        {
            var reference =
                Read(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts231\\tsrowd\\" +
                    control +
                    " Frame;}}" +
                    "Body\\par}");

            Assert(
                reference.TableStyles.Single().RowFormatting?.Positioning is
                {
                    HorizontalPosition: null,
                    HorizontalOffsetTwips: null,
                    VerticalReferenceFrame: null
                } &&
                reference.TableStyles.Single().RowFormatting?.Positioning?.HorizontalReferenceFrame ==
                    frame,
                $"RTF {control} должен сохранять explicit horizontal reference frame.");
        }

        var verticalFrames =
            new (string Control, DocumentTableVerticalReferenceFrame Frame)[]
            {
                ("tpvmrg", DocumentTableVerticalReferenceFrame.Margin),
                ("tpvpg", DocumentTableVerticalReferenceFrame.Page),
                ("tpvpara", DocumentTableVerticalReferenceFrame.Paragraph)
            };

        foreach (var (control, frame) in verticalFrames)
        {
            var reference =
                Read(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts232\\tsrowd\\" +
                    control +
                    " Frame;}}" +
                    "Body\\par}");

            Assert(
                reference.TableStyles.Single().RowFormatting?.Positioning is
                {
                    VerticalPosition: null,
                    VerticalOffsetTwips: null,
                    HorizontalReferenceFrame: null
                } &&
                reference.TableStyles.Single().RowFormatting?.Positioning?.VerticalReferenceFrame ==
                    frame,
                $"RTF {control} должен сохранять explicit vertical reference frame.");
        }

        var invalidControls =
            new[]
            {
                "tphcol0",
                "tphmrg1",
                "tphpg0",
                "tpvmrg1",
                "tpvpg0",
                "tpvpara1",
                "tposxc1",
                "tposxi0",
                "tposxo1",
                "tposxl0",
                "tposxr1",
                "tposyt1",
                "tposyil0",
                "tposyb1",
                "tposyc0",
                "tposyin1",
                "tposyout0",
                "tabsnoovrlp0",
                "tabsnoovrlp1",
                "tposx",
                "tposnegx",
                "tposy",
                "tposnegy",
                "tdfrmtxtLeft",
                "tdfrmtxtRight",
                "tdfrmtxtTop",
                "tdfrmtxtBottom",
                "tposx2147483648",
                "tposnegy-2147483649",
                "tdfrmtxtTop2147483648"
            };

        foreach (var control in invalidControls)
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts229\\tsrowd\\" +
                    control +
                    " Invalid;}}" +
                    "Body\\par}"),
                $"RTF {control} должен отклоняться при invalid table-style positioning grammar.");
        }

        Assert(
            RejectsInvalid(
                "{\\rtf1\\ansi" +
                "{\\stylesheet\\tposxc{\\*\\ts230\\tsrowd Valid;}}" +
                "Body\\par}"),
            "RTF table positioning controls вне table-style entry должны завершаться structural error.");
    }
}
