using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

/// <summary>
/// Проверяет font-level RTF fcharset/cpg decoding по документированной
/// таблице RTF 1.9.1, включая Mac/DBCS/OEM кодировки и explicit failures
/// для charset без однозначной codepage.
/// </summary>
internal static partial class Program
{
    /// <summary>
    /// Проверяет все однозначно сопоставленные fcharset values, приоритет
    /// font-level cpg, отсутствие утечки cpg между шрифтами, Symbol mapping
    /// и явный отказ для invalid/obsolete/unknown charset values.
    /// </summary>
    private static void TestRtfFontCharsets(
        string directory)
    {
        Encoding.RegisterProvider(
            CodePagesEncodingProvider.Instance);

        static string EscapeBytes(
            int codePage,
            string text)
        {
            var bytes =
                Encoding.GetEncoding(
                    codePage)
                    .GetBytes(
                        text);

            return string.Concat(
                bytes.Select(
                    value =>
                        $"\\'{value:X2}"));
        }

        static string ReadText(
            string directory,
            string name,
            string rtf)
        {
            var path = Path.Combine(
                directory,
                name);

            File.WriteAllText(
                path,
                rtf,
                Encoding.ASCII);

            return FlattenText(
                SupportedFormatRegistry.ReadDocument(
                    path));
        }

        static string ReadRawText(
            string directory,
            string name,
            params byte[][] parts)
        {
            var path = Path.Combine(
                directory,
                name);
            var bytes =
                parts.SelectMany(
                    part => part)
                    .ToArray();

            File.WriteAllBytes(
                path,
                bytes);

            return FlattenText(
                SupportedFormatRegistry.ReadDocument(
                    path));
        }

        var mappings = new[]
        {
            (Charset: 0, CodePage: 1252, Text: "é"),
            (Charset: 77, CodePage: 10000, Text: "é"),
            (Charset: 78, CodePage: 10001, Text: "あ"),
            (Charset: 79, CodePage: 10003, Text: "가"),
            (Charset: 80, CodePage: 10008, Text: "中"),
            (Charset: 81, CodePage: 10002, Text: "中"),
            (Charset: 83, CodePage: 10005, Text: "א"),
            (Charset: 84, CodePage: 10004, Text: "ا"),
            (Charset: 85, CodePage: 10006, Text: "Ω"),
            (Charset: 86, CodePage: 10081, Text: "Ğ"),
            (Charset: 87, CodePage: 10021, Text: "ก"),
            (Charset: 88, CodePage: 10029, Text: "Ł"),
            (Charset: 89, CodePage: 10007, Text: "Я"),
            (Charset: 128, CodePage: 932, Text: "あ"),
            (Charset: 129, CodePage: 949, Text: "가"),
            (Charset: 130, CodePage: 1361, Text: "가"),
            (Charset: 134, CodePage: 936, Text: "中"),
            (Charset: 136, CodePage: 950, Text: "中"),
            (Charset: 161, CodePage: 1253, Text: "Ω"),
            (Charset: 162, CodePage: 1254, Text: "Ğ"),
            (Charset: 163, CodePage: 1258, Text: "Ơ"),
            (Charset: 177, CodePage: 1255, Text: "א"),
            (Charset: 178, CodePage: 1256, Text: "ا"),
            (Charset: 186, CodePage: 1257, Text: "Ā"),
            (Charset: 204, CodePage: 1251, Text: "Я"),
            (Charset: 222, CodePage: 874, Text: "ก"),
            (Charset: 238, CodePage: 1250, Text: "Ł"),
            (Charset: 254, CodePage: 437, Text: "¢"),
            (Charset: 255, CodePage: 850, Text: "ø")
        };

        for (var index = 0;
             index < mappings.Length;
             index++)
        {
            var mapping =
                mappings[index];
            var escaped =
                EscapeBytes(
                    mapping.CodePage,
                    mapping.Text);
            var rtf =
                "{\\rtf1\\ansi\\ansicpg1251" +
                "{\\fonttbl{\\f0\\fcharset" +
                mapping.Charset +
                " Test;}}" +
                "\\f0 " +
                escaped +
                "}";

            var text =
                ReadText(
                    directory,
                    $"rtf-fcharset-{mapping.Charset}.rtf",
                    rtf);

            AssertContains(
                text,
                mapping.Text,
                $"RTF fcharset{mapping.Charset} must decode with codepage {mapping.CodePage}.");
        }

        var explicitAnsi =
            ReadText(
                directory,
                "rtf-fcharset-explicit-ansi.rtf",
                "{\\rtf1\\ansi\\ansicpg1251" +
                "{\\fonttbl" +
                "{\\f0 NoCharset;}" +
                "{\\f1\\fcharset0 ExplicitAnsi;}" +
                "}" +
                "\\f0 \\'E9 \\f1 \\'E9}");
        AssertContains(
            explicitAnsi,
            "й",
            "RTF font without fcharset must inherit document ansicpg.");
        AssertContains(
            explicitAnsi,
            "é",
            "RTF explicit fcharset0 must use the documented ANSI 1252 codepage.");

        var cpgOverrideText =
            ReadText(
                directory,
                "rtf-font-cpg-override.rtf",
                "{\\rtf1\\ansi\\ansicpg1251" +
                "{\\fonttbl" +
                "{\\f0\\fcharset204\\cpg1252 WesternOverride;}" +
                "{\\f1\\fcharset204 Cyrillic;}" +
                "{\\f2\\fcharset1\\cpg10000 MacExplicit;}" +
                "{\\f3\\fcharset2\\cpg1252 SymbolOverridden;}" +
                "{\\f4\\fcharset0\\cpg42 SymbolExplicit;}" +
                "}" +
                "\\f0 \\'E9 " +
                "\\f1 \\'E9 " +
                "\\f2 " +
                EscapeBytes(
                    10000,
                    "é") +
                " \\f3 \\'41 \\f4 \\'41}");

        AssertContains(
            cpgOverrideText,
            "é й é A",
            "RTF font-level cpg must override fcharset and must not leak into the following font.");
        AssertContains(
            cpgOverrideText,
            "",
            "RTF explicit cpg42 must use Symbol private-use mapping.");

        var oemDifference =
            ReadText(
                directory,
                "rtf-font-oem-defaults.rtf",
                "{\\rtf1\\ansi\\ansicpg1252" +
                "{\\fonttbl" +
                "{\\f0\\fcharset254 Pc437;}" +
                "{\\f1\\fcharset255 Oem850;}" +
                "}" +
                "\\f0 \\'9B \\f1 \\'9B}");
        AssertContains(
            oemDifference,
            "¢ ø",
            "RTF fcharset254 and fcharset255 must remain distinct PC437/OEM850 mappings.");

        var shiftJis =
            Encoding.GetEncoding(
                932);
        var rawCharacter =
            shiftJis.GetBytes(
                "あ");
        var mixedCharacter =
            shiftJis.GetBytes(
                "ア");
        var escapedSpecialCharacter =
            shiftJis.GetBytes(
                "ソ");

        Assert(rawCharacter.Length == 2 &&
            mixedCharacter.Length == 2 &&
            mixedCharacter[1] is >= 0x20 and <= 0x7F &&
            mixedCharacter[1] is not (0x5C or 0x7B or 0x7D) &&
            escapedSpecialCharacter.Length == 2 &&
            escapedSpecialCharacter[1] == 0x5C,
            "RTF DBCS regression characters must exercise raw/raw, escaped/raw and escaped-special boundaries.");

        var dbcsPrefix =
            Encoding.ASCII.GetBytes(
                "{\\rtf1\\ansi\\ansicpg1252" +
                "{\\fonttbl{\\f0\\fcharset128 MS Gothic;}}" +
                "\\f0 ");
        var dbcsSuffix =
            Encoding.ASCII.GetBytes(
                "}");

        var rawDbcs =
            ReadRawText(
                directory,
                "rtf-dbcs-raw-raw.rtf",
                dbcsPrefix,
                rawCharacter,
                dbcsSuffix);
        AssertContains(
            rawDbcs,
            "あ",
            "RTF raw/raw DBCS bytes must decode as one codepage sequence.");

        var escapedRawDbcs =
            ReadRawText(
                directory,
                "rtf-dbcs-escaped-raw.rtf",
                dbcsPrefix,
                Encoding.ASCII.GetBytes(
                    $"\\'{mixedCharacter[0]:X2}"),
                [mixedCharacter[1]],
                dbcsSuffix);
        AssertContains(
            escapedRawDbcs,
            "ア",
            "RTF escaped lead plus raw DBCS trailing byte must decode as the documented mixed form.");

        var escapedSpecialDbcs =
            ReadRawText(
                directory,
                "rtf-dbcs-escaped-special.rtf",
                dbcsPrefix,
                Encoding.ASCII.GetBytes(
                    $"\\'{escapedSpecialCharacter[0]:X2}\\\\"),
                dbcsSuffix);
        AssertContains(
            escapedSpecialDbcs,
            "ソ",
            "RTF escaped DBCS lead plus escaped special trailing byte must decode without replacement fallback.");

        static void AssertRawRejected(
            string directory,
            string fileName,
            params byte[][] parts)
        {
            var path =
                Path.Combine(
                    directory,
                    fileName);
            File.WriteAllBytes(
                path,
                parts.SelectMany(
                    part => part)
                    .ToArray());

            var rejected = false;

            try
            {
                _ = SupportedFormatRegistry.ReadDocument(
                    path);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Assert(rejected,
                $"RTF malformed DBCS byte sequence must be rejected: {fileName}");
        }

        AssertRawRejected(
            directory,
            "rtf-dbcs-raw-escaped-invalid.rtf",
            dbcsPrefix,
            [mixedCharacter[0]],
            Encoding.ASCII.GetBytes(
                $"\\'{mixedCharacter[1]:X2}}}"));

        AssertRawRejected(
            directory,
            "rtf-dbcs-incomplete-escaped.rtf",
            dbcsPrefix,
            Encoding.ASCII.GetBytes(
                $"\\'{mixedCharacter[0]:X2}}}"));

        AssertRawRejected(
            directory,
            "rtf-dbcs-invalid-trail.rtf",
            dbcsPrefix,
            Encoding.ASCII.GetBytes(
                $"\\'{mixedCharacter[0]:X2}"),
            [0x7F],
            dbcsSuffix);

        var symbolText =
            ReadText(
                directory,
                "rtf-font-symbol.rtf",
                "{\\rtf1\\ansi" +
                "{\\fonttbl{\\f0\\fcharset2 Symbol;}}" +
                "\\f0 \\'41}");
        AssertContains(
            symbolText,
            "",
            "RTF fcharset2 must preserve Windows Symbol private-use byte mapping.");

        static void AssertRejected(
            string directory,
            string fileName,
            string fontControls,
            Type exceptionType)
        {
            var path =
                Path.Combine(
                    directory,
                    fileName);
            var rtf =
                "{\\rtf1\\ansi\\ansicpg1252" +
                "{\\fonttbl{\\f0" +
                fontControls +
                " Test;}}" +
                "\\f0 \\'41}";

            File.WriteAllText(
                path,
                rtf,
                Encoding.ASCII);

            Exception? error = null;

            try
            {
                _ = SupportedFormatRegistry.ReadDocument(
                    path);
            }
            catch (Exception caught)
            {
                error = caught;
            }

            Assert(error != null &&
                exceptionType.IsAssignableFrom(
                    error.GetType()),
                $"RTF {fontControls} must fail explicitly with {exceptionType.Name}.");
        }

        foreach (var charset in
                 new[]
                 {
                     1,
                     82,
                     179,
                     180,
                     181,
                     200
                 })
        {
            AssertRejected(
                directory,
                $"rtf-fcharset-{charset}-unsupported.rtf",
                $"\\fcharset{charset}",
                typeof(NotSupportedException));
        }

        AssertRejected(
            directory,
            "rtf-fcharset-invalid.rtf",
            "\\fcharset3",
            typeof(InvalidDataException));
        AssertRejected(
            directory,
            "rtf-fcharset-missing.rtf",
            "\\fcharset",
            typeof(InvalidDataException));
        AssertRejected(
            directory,
            "rtf-cpg-missing.rtf",
            "\\cpg",
            typeof(InvalidDataException));
        AssertRejected(
            directory,
            "rtf-cpg-zero.rtf",
            "\\cpg0",
            typeof(InvalidDataException));
        AssertRejected(
            directory,
            "rtf-cpg-unknown.rtf",
            "\\cpg999999",
            typeof(InvalidDataException));
    }
}
