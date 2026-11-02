using Book.Documents;
using Book.Formats.Doc;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет DOC textual MSONFC с exact fr-FR 0x040C, включая
    /// французские формы, language-slot routing и strict profile boundaries.
    /// </summary>
    private static void TestDocFrenchTextualNumbering()
    {
        static string?[] ReadMarkers(
            byte formatCode,
            int startAt,
            ushort? defaultLanguageId = 0x040C,
            ushort? eastAsiaLanguageId = null,
            ushort? complexScriptLanguageId = null,
            byte? languageHint = null)
        {
            var placeholderOffsets = new byte[9];
            placeholderOffsets[0] = 1;

            const int listId = 7340;
            var definition = new DocDocumentReader.DocListDefinition(
                listId,
                true,
                [
                    new DocDocumentReader.DocListLevelDefinition(
                        startAt,
                        formatCode,
                        2,
                        "\0",
                        placeholderOffsets,
                        false,
                        false,
                        0,
                        defaultLanguageId,
                        eastAsiaLanguageId,
                        complexScriptLanguageId,
                        languageHint)
                ]);
            var tables = new DocDocumentReader.DocListTables(
                new Dictionary<int, DocDocumentReader.DocListDefinition>
                {
                    [listId] = definition
                },
                [new DocDocumentReader.DocListOverrideDefinition(
                    listId,
                    new Dictionary<int, DocDocumentReader.DocListLevelOverride>())]);
            var blocks = DocDocumentReader.BuildParagraphs(
                "A\rB\r",
                [
                    new DocDocumentReader.DocParagraphFormattingSpan(0, 2, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 }),
                    new DocDocumentReader.DocParagraphFormattingSpan(2, 4, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 })
                ],
                tables);

            return blocks.Cast<DocumentParagraph>()
                .Select(paragraph => paragraph.ListMarkerText)
                .ToArray();
        }

        Assert(
            ReadMarkers(0x05, 1).SequenceEqual(["1er", "2e"]),
            "DOC ordinal fr-FR должен использовать 1er и 2e.");
        Assert(
            ReadMarkers(0x05, 11).SequenceEqual(["11e", "12e"]),
            "DOC ordinal fr-FR должен использовать e для всех значений, кроме 1.");
        Assert(
            ReadMarkers(0x05, 217483647).SequenceEqual(["217483647e", null]),
            "DOC ordinal fr-FR должен соблюдать общий Word display ceiling.");
        Assert(
            ReadMarkers(0x06, 1).SequenceEqual(["Un", "Deux"]),
            "DOC cardinalText fr-FR должен писать названия чисел с прописной первой буквы.");
        Assert(
            ReadMarkers(0x06, 16).SequenceEqual(["Seize", "Dix-sept"]),
            "DOC cardinalText fr-FR должен различать шестнадцать и составные 17..19.");
        Assert(
            ReadMarkers(0x06, 20).SequenceEqual(["Vingt", "Vingt et un"]),
            "DOC cardinalText fr-FR должен выводить et un в десятках 20..60.");
        Assert(
            ReadMarkers(0x06, 70).SequenceEqual(["Soixante-dix", "Soixante et onze"]),
            "DOC cardinalText fr-FR должен использовать special 71.");
        Assert(
            ReadMarkers(0x06, 79).SequenceEqual(["Soixante-dix-neuf", "Quatre-vingts"]),
            "DOC cardinalText fr-FR должен использовать plural vingt только в exact 80.");
        Assert(
            ReadMarkers(0x06, 81).SequenceEqual(["Quatre-vingt-un", "Quatre-vingt-deux"]),
            "DOC cardinalText fr-FR не должен использовать et и plural s после 80.");
        Assert(
            ReadMarkers(0x06, 90).SequenceEqual(["Quatre-vingt-dix", "Quatre-vingt-onze"]),
            "DOC cardinalText fr-FR должен строить 90..99 как quatre-vingt plus 10..19.");
        Assert(
            ReadMarkers(0x06, 99).SequenceEqual(["Quatre-vingt-dix-neuf", "Cent"]),
            "DOC cardinalText fr-FR должен переключать десятки и сотни.");
        Assert(
            ReadMarkers(0x06, 100).SequenceEqual(["Cent", "Cent un"]),
            "DOC cardinalText fr-FR должен опускать un перед cent.");
        Assert(
            ReadMarkers(0x06, 200).SequenceEqual(["Deux cents", "Deux cent un"]),
            "DOC cardinalText fr-FR должен ставить s у exact 200 и опускать его перед remainder.");
        Assert(
            ReadMarkers(0x06, 280).SequenceEqual(["Deux cent quatre-vingts", "Deux cent quatre-vingt-un"]),
            "DOC cardinalText fr-FR должен различать exact 80 внутри сотен.");
        Assert(
            ReadMarkers(0x06, 1000).SequenceEqual(["Mille", "Mille un"]),
            "DOC cardinalText fr-FR должен опускать un перед mille.");
        Assert(
            ReadMarkers(0x06, 1200).SequenceEqual(["Mille deux cents", "Mille deux cent un"]),
            "DOC cardinalText fr-FR должен сохранять plural cent только у exact сотни.");
        Assert(
            ReadMarkers(0x06, 19999).SequenceEqual(["Dix-neuf mille neuf cent quatre-vingt-dix-neuf", null]),
            "DOC cardinalText fr-FR должен прекращать вывод за строгим профилем 19999.");
        Assert(
            ReadMarkers(0x06, 20000).All(marker => marker == null),
            "DOC cardinalText fr-FR не должен синтезировать число 20000.");
        Assert(
            ReadMarkers(0x07, 1).SequenceEqual(["Premier", "Deuxième"]),
            "DOC ordinalText fr-FR должен использовать Premier/Deuxième.");
        Assert(
            ReadMarkers(0x07, 4).SequenceEqual(["Quatrième", "Cinquième"]),
            "DOC ordinalText fr-FR должен удалять конечное e и добавлять u после cinq.");
        Assert(
            ReadMarkers(0x07, 8).SequenceEqual(["Huitième", "Neuvième"]),
            "DOC ordinalText fr-FR должен преобразовывать neuf в neuv.");
        Assert(
            ReadMarkers(0x07, 20).SequenceEqual(["Vingtième", "Vingt et unième"]),
            "DOC ordinalText fr-FR должен обрабатывать exact tens и составные формы.");
        Assert(
            ReadMarkers(0x07, 70).SequenceEqual(["Soixante-dixième", "Soixante et onzième"]),
            "DOC ordinalText fr-FR должен сохранять et в 71.");
        Assert(
            ReadMarkers(0x07, 80).SequenceEqual(["Quatre-vingtième", "Quatre-vingt-unième"]),
            "DOC ordinalText fr-FR должен удалять plural s у quatre-vingts.");
        Assert(
            ReadMarkers(0x07, 100).SequenceEqual(["Centième", "Cent unième"]),
            "DOC ordinalText fr-FR должен различать exact cent и составное 101.");
        Assert(
            ReadMarkers(0x07, 200).SequenceEqual(["Deux centième", "Deux cent unième"]),
            "DOC ordinalText fr-FR должен удалять plural s у exact 200.");
        Assert(
            ReadMarkers(0x07, 1000).SequenceEqual(["Millième", "Mille unième"]),
            "DOC ordinalText fr-FR должен различать exact mille и составное 1001.");
        Assert(
            ReadMarkers(0x07, 19999).SequenceEqual(["Dix-neuf mille neuf cent quatre-vingt-dix-neuvième", null]),
            "DOC ordinalText fr-FR должен прекращать вывод за строгим профилем 19999.");
        Assert(
            ReadMarkers(0x07, 20000).All(marker => marker == null),
            "DOC ordinalText fr-FR не должен синтезировать число 20000.");
        Assert(
            ReadMarkers(0x06, 1, defaultLanguageId: null).All(marker => marker == null),
            "DOC fr-FR без explicit LID не должен получать locale fallback.");
        Assert(
            ReadMarkers(0x06, 1, defaultLanguageId: 0x0C0C).All(marker => marker == null),
            "DOC fr-CA не должен приравниваться к exact fr-FR.");
        Assert(
            ReadMarkers(
                    0x06,
                    21,
                    defaultLanguageId: 0x0409,
                    eastAsiaLanguageId: 0x040C,
                    languageHint: 0x01)
                .SequenceEqual(["Vingt et un", "Vingt-deux"]),
            "DOC language hint должен выбирать fr-FR из East Asia slot.");
        Assert(
            ReadMarkers(
                    0x07,
                    21,
                    defaultLanguageId: 0x0409,
                    complexScriptLanguageId: 0x040C,
                    languageHint: 0x02)
                .SequenceEqual(["Vingt et unième", "Vingt-deuxième"]),
            "DOC language hint должен выбирать fr-FR из complex-script slot.");
        Assert(
            ReadMarkers(
                    0x06,
                    21,
                    defaultLanguageId: 0x040C,
                    eastAsiaLanguageId: 0x0411,
                    languageHint: 0x01)
                .All(marker => marker == null),
            "DOC East Asia hint не должен применять fr-FR из default slot.");
    }
}
