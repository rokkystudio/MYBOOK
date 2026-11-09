using System.IO;
using System.IO.Compression;
using Book.Documents;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет DOCX OPC core properties: dc:title и dc:creator из корневой
    /// связи _rels/.rels, оба официальных relationship types, URI-encoded
    /// пути, literal percent, root-relative Target, отсутствие метаданных,
    /// fallback к имени файла, отсутствие поиска part по предполагаемому пути
    /// и явные ошибки для некорректных отношений и XML структуры.
    /// DOCX ZIP создаются исключительно во временном каталоге.
    /// </summary>
    private static void TestDocxCoreProperties(string directory)
    {
        const string opcType =
            "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
        const string ecmaType =
            "http://schemas.openxmlformats.org/officedocument/2006/relationships/metadata/core-properties";

        const string properCoreXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <cp:coreProperties
                xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                xmlns:dc="http://purl.org/dc/elements/1.1/">
              <dc:title>  История &amp; романы  </dc:title>
              <dc:creator>  Иван Петров  </dc:creator>
              <cp:lastModifiedBy>Не автор</cp:lastModifiedBy>
            </cp:coreProperties>
            """;

        const string emptyTitleCoreXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <cp:coreProperties
                xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                xmlns:dc="http://purl.org/dc/elements/1.1/">
              <dc:title>  </dc:title>
              <dc:creator>  Елена Сидорова  </dc:creator>
            </cp:coreProperties>
            """;

        /// <summary>
        /// Формирует _rels/.rels из элементов Relationship и сохраняет
        /// исходные значения Type, Target и TargetMode для чтения OPC.
        /// </summary>
        string Relationships(string items)
        {
            return
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                """ +
                items +
                """
                </Relationships>
                """;
        }

        /// <summary>
        /// Создаёт DOCX ZIP с простым абзацем, необязательной root relationship
        /// и необязательным core-properties part по указанному ZIP-пути.
        /// </summary>
        string CreateDocx(
            string fileName,
            string? relationshipsXml,
            string? corePath,
            string? coreXml)
        {
            var path = Path.Combine(
                directory,
                fileName);

            using (var archive = ZipFile.Open(
                       path,
                       ZipArchiveMode.Create))
            {
                WriteZipEntry(
                    archive,
                    "word/document.xml",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:body>
                        <w:p><w:r><w:t>DOCX metadata body</w:t></w:r></w:p>
                      </w:body>
                    </w:document>
                    """);

                if (relationshipsXml != null)
                {
                    WriteZipEntry(
                        archive,
                        "_rels/.rels",
                        relationshipsXml);
                }

                if (corePath != null && coreXml != null)
                {
                    WriteZipEntry(
                        archive,
                        corePath,
                        coreXml);
                }
            }

            return path;
        }

        var validPath = CreateDocx(
            "docx-core-relationship.docx",
            Relationships(
                $"""
                <Relationship Id="rId1"
                              Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                              Target="word/document.xml"/>
                <Relationship Id="rId2"
                              Type="{opcType}"
                              Target="meta/%D0%9D%D0%BE%D0%B2%D0%B0%D1%8F%20%D0%BA%D0%BD%D0%B8%D0%B3%D0%B0.xml"
                              TargetMode="Internal"/>
                """),
            "meta/Новая книга.xml",
            properCoreXml);

        var valid = SupportedFormatRegistry.ReadDocument(
            validPath);

        AssertEqual(
            "История & романы",
            valid.Title,
            "DOCX dc:title должен декодироваться из OPC core properties.");

        AssertEqual(
            "Иван Петров",
            valid.Author,
            "DOCX dc:creator должен определять автора и не брать cp:lastModifiedBy.");

        AssertContains(
            FlattenText(valid),
            "DOCX metadata body",
            "DOCX чтение core metadata не должно влиять на основные блоки.");

        var ecmaPath = CreateDocx(
            "docx-ecma-core.docx",
            Relationships(
                $"""
                <Relationship Id="rId3"
                              Type="{ecmaType}"
                              Target="/custom/core%25details.xml"/>
                """),
            "custom/core%details.xml",
            emptyTitleCoreXml);

        var ecma = SupportedFormatRegistry.ReadDocument(
            ecmaPath);

        AssertEqual(
            "docx-ecma-core",
            ecma.Title,
            "DOCX пустой dc:title должен использовать имя файла без расширения.");

        AssertEqual(
            "Елена Сидорова",
            ecma.Author,
            "DOCX ECMA relationship должен читать dc:creator из root-relative path с %25.");

        var noRelationshipPath = CreateDocx(
            "docx-without-root-relationships.docx",
            relationshipsXml: null,
            "docProps/core.xml",
            properCoreXml);

        var noRelationship = SupportedFormatRegistry.ReadDocument(
            noRelationshipPath);

        AssertEqual(
            "docx-without-root-relationships",
            noRelationship.Title,
            "DOCX без корневых relationships должен использовать имя файла.");

        AssertEqual(
            string.Empty,
            noRelationship.Author,
            "DOCX не должен читать core.xml по предполагаемому пути без OPC relationship.");

        var unrelatedPath = CreateDocx(
            "docx-unrelated-relationships.docx",
            Relationships(
                """
                <Relationship Id="rId5"
                              Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties"
                              Target="docProps/core.xml"/>
                """),
            "docProps/core.xml",
            properCoreXml);

        var unrelated = SupportedFormatRegistry.ReadDocument(
            unrelatedPath);

        AssertEqual(
            "docx-unrelated-relationships",
            unrelated.Title,
            "DOCX связь другого Type не должна использоваться как core-properties.");

        AssertEqual(
            string.Empty,
            unrelated.Author,
            "DOCX unrelated root relationship не должна задавать автора.");

        var emptyMetadataPath = CreateDocx(
            "docx-empty-core-fields.docx",
            Relationships(
                $"""
                <Relationship Id="rId6" Type="{opcType}" Target="docProps/core.xml"/>
                """),
            "docProps/core.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <cp:coreProperties
                xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                xmlns:dc="http://purl.org/dc/elements/1.1/">
              <cp:lastModifiedBy>Last editor</cp:lastModifiedBy>
            </cp:coreProperties>
            """);

        var emptyMetadata = SupportedFormatRegistry.ReadDocument(
            emptyMetadataPath);

        AssertEqual(
            "docx-empty-core-fields",
            emptyMetadata.Title,
            "DOCX без dc:title должен использовать имя файла.");

        AssertEqual(
            string.Empty,
            emptyMetadata.Author,
            "DOCX без dc:creator должен сохранять пустого автора.");

        /// <summary>
        /// Проверяет отклонение конкретной ошибки в root OPC relationship
        /// или core-properties part без чтения внешних документов.
        /// </summary>
        void AssertInvalid(
            string fileName,
            string relationshipsXml,
            string? corePath,
            string? coreXml,
            string diagnostic)
        {
            var path = CreateDocx(
                fileName,
                relationshipsXml,
                corePath,
                coreXml);

            var rejected = false;

            try
            {
                SupportedFormatRegistry.ReadDocument(
                    path);
            }
            catch (InvalidDataException exception)
            {
                rejected = exception.Message.Contains(
                    diagnostic,
                    StringComparison.Ordinal);
            }

            Assert(
                rejected,
                $"DOCX {fileName} должен отклоняться с диагностикой {diagnostic}.");
        }

        AssertInvalid(
            "docx-core-external.docx",
            Relationships(
                $"""
                <Relationship Id="rId1" Type="{opcType}"
                              Target="https://example.invalid/core.xml"
                              TargetMode="External"/>
                """),
            null,
            null,
            "Internal");

        AssertInvalid(
            "docx-core-absolute-url.docx",
            Relationships(
                $"""
                <Relationship Id="rId1" Type="{opcType}"
                              Target="https://example.invalid/core.xml"/>
                """),
            null,
            null,
            "Target");

        AssertInvalid(
            "docx-core-network-path.docx",
            Relationships(
                $"""
                <Relationship Id="rId1" Type="{opcType}"
                              Target="//example.invalid/core.xml"/>
                """),
            null,
            null,
            "Target");

        AssertInvalid(
            "docx-core-invalid-percent.docx",
            Relationships(
                $"""
                <Relationship Id="rId1" Type="{opcType}"
                              Target="docProps/core%ZZ.xml"/>
                """),
            null,
            null,
            "percent escape");

        AssertInvalid(
            "docx-core-escaped-parent.docx",
            Relationships(
                $"""
                <Relationship Id="rId1" Type="{opcType}"
                              Target="%2E%2E/docProps/core.xml"/>
                """),
            null,
            null,
            "за пределы пакета");

        AssertInvalid(
            "docx-core-query.docx",
            Relationships(
                $"""
                <Relationship Id="rId1" Type="{opcType}"
                              Target="docProps/core.xml?version=1"/>
                """),
            null,
            null,
            "Target");

        AssertInvalid(
            "docx-core-fragment.docx",
            Relationships(
                $"""
                <Relationship Id="rId1" Type="{opcType}"
                              Target="docProps/core.xml#part"/>
                """),
            null,
            null,
            "Target");

        AssertInvalid(
            "docx-core-no-target.docx",
            Relationships(
                $"""
                <Relationship Id="rId1" Type="{opcType}"/>
                """),
            null,
            null,
            "Target");

        AssertInvalid(
            "docx-core-no-part.docx",
            Relationships(
                $"""
                <Relationship Id="rId1" Type="{opcType}"
                              Target="docProps/missing.xml"/>
                """),
            null,
            null,
            "отсутствующий part");

        AssertInvalid(
            "docx-core-duplicate-relations.docx",
            Relationships(
                $"""
                <Relationship Id="rId1" Type="{opcType}" Target="docProps/core.xml"/>
                <Relationship Id="rId2" Type="{ecmaType}" Target="docProps/alternate.xml"/>
                """),
            "docProps/core.xml",
            properCoreXml,
            "несколько");

        AssertInvalid(
            "docx-core-unexpected-root.docx",
            Relationships(
                $"""
                <Relationship Id="rId1" Type="{opcType}" Target="docProps/core.xml"/>
                """),
            "docProps/core.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <coreProperties xmlns:dc="http://purl.org/dc/elements/1.1/">
              <dc:title>Unnamespaced coreProperties</dc:title>
            </coreProperties>
            """,
            "cp:coreProperties");

        AssertInvalid(
            "docx-core-duplicate-title.docx",
            Relationships(
                $"""
                <Relationship Id="rId1" Type="{opcType}" Target="docProps/core.xml"/>
                """),
            "docProps/core.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <cp:coreProperties
                xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                xmlns:dc="http://purl.org/dc/elements/1.1/">
              <dc:title>First</dc:title>
              <dc:title>Second</dc:title>
            </cp:coreProperties>
            """,
            "повторные dc:title");

        AssertInvalid(
            "docx-core-duplicate-creator.docx",
            Relationships(
                $"""
                <Relationship Id="rId1" Type="{opcType}" Target="docProps/core.xml"/>
                """),
            "docProps/core.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <cp:coreProperties
                xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                xmlns:dc="http://purl.org/dc/elements/1.1/">
              <dc:creator>First creator</dc:creator>
              <dc:creator>Second creator</dc:creator>
            </cp:coreProperties>
            """,
            "повторные dc:title или dc:creator");

        AssertInvalid(
            "docx-core-incorrect-relationships-root.docx",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Relationships/>
            """,
            null,
            null,
            "корневой Relationships");
    }
}
