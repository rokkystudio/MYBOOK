# MYBOOK format support

Версия профиля: 1.5.9

## Ready for use

MYBOOK считает готовыми к обычному пользовательскому чтению все форматы,
которые присутствуют в `SupportedFormatRegistry`:

| Расширение | Профиль поддержки |
| --- | --- |
| `.fb2` | FictionBook 2: метаданные, body, текст, структура, таблицы, ссылки, anchors и встроенные изображения. |
| `.epub` | EPUB 2/3: package/manifest/spine, XHTML, CSS/resources, изображения/SVG, navigation/NCX, landmarks/page-list и note semantics. |
| `.html`, `.htm` | Локальный HTML открывается напрямую через изолированное virtual-host mapping WebView2. |
| `.txt` | Обычный текст как preformatted document. |
| `.md` | Markdown через фиксированный Markdig pipeline с advanced extensions. |
| `.rtf` | Основной RTF reading profile: Unicode/codepages, formatting, links/fields, tables/nesting, raster/vector pictures, Symbol charset и явно объявленные OEM codepages. |
| `.doc` | Word 97–2003 Binary/CFB reading profile и RTF-файлы с расширением .doc; текст, стили, списки, таблицы, sections, notes, headers/footers, fields, images и поддерживаемый OfficeArt. |
| `.docx` | Word OpenXML flow document: styles, lists, tables, images, relationships, hyperlinks, bookmarks и section metadata. |
| `.pdf` | PDF fixed-page reading profile: classic/xref streams, incremental updates, object streams, text/fonts, paths, images, links/outlines, encryption profiles и поддерживаемые stream filters. |

“Ready for use” означает, что зарегистрированный формат проходит единый путь
от открытия пользовательского файла до модели и итогового рендера, имеет
контролируемый отказ на повреждённом/неподдерживаемом вводе и покрыт
регрессионными fixtures. Это не означает реализацию каждого редкого элемента
соответствующей спецификации.

## Readiness gates

Перед выпуском профиля выполняются следующие проверки:

- Release build без warnings/errors.
- Полный regression suite.
- Все поддерживаемые файлы из локального `Books` corpus открываются; reader-backed
  документы дополнительно проходят полный `reader -> DocumentModel -> HTML renderer` путь.
- DOC/PDF проходят детерминированный malformed-input набор.
- Входные файлы имеют общий размерный предел; EPUB/DOCX имеют limits на ZIP entries,
  размер распакованных parts и compression ratio.
- XML для FB2/EPUB/DOCX читается без обработки DTD и внешних сущностей и с
  ограничением размера XML.
- RTF nesting и PDF page-tree nesting ограничены.
- Декодированные PDF Flate/LZW/RunLength streams имеют отдельный предел.
- Unsupported конструкции, для которых нельзя гарантировать корректную
  интерпретацию, завершаются явной ошибкой вместо приблизительного рендера.

## Known fidelity limits

Эти пункты не блокируют обычное чтение, но остаются backlog для расширения
совместимости и точности.

### DOCX

- Нет полноценного pagination/page-layout engine; документ читается как flow content.

### DOC

- Редкие list/table style и locale-specific numbering variants.
- Часть редких OfficeArt connector/fill/arrow presets.
- Некоторые header/footer, note, field и section layout semantics требуют
  полноценного pagination engine.
- Legacy Symbol/OEM byte mapping и старые до-Word-97 Binary variants не входят
  в готовый профиль.
- Нет полноценного pagination/page-layout engine.

### RTF

- Редкие row/cell и paragraph controls; row index/banding, last-row marker, table indent units и документированные Word table-look flags для borders/shading/font/color/best-fit, first/last row/column и banding уже сохраняются как metadata.
- Scale без goal-size использует только явно сохранённую PNG/JPEG/BMP physical-density metadata; при её отсутствии размер не угадывается. Bitmap `wbmbitspixel`/`wbmplanes`/`wbmwidthbytes` сохраняются как metadata; `wbitmap`, `macpict` и OS/2 Presentation Manager metafile явно помечены unsupported до появления точных decoder'ов. Отрицательное picture crop-extension пока сохраняется без layout/canvas расширения.
- Редкие charset/codepage combinations за пределами точных известных mappings.
- Некоторые декоративные border styles имеют CSS fallback, а не pixel-identical Word rendering.

### PDF

- ExtGState soft-mask group compositing не реализован полностью.
- Predefined Type0 CMaps для public Adobe CJK/KR collections входят в профиль: canonical Adobe resources дают code→CID, включая `usecmap` inheritance; без `/ToUnicode` CID→Unicode берётся из canonical `Registry-Ordering-UCS2` mapping resources.
- DeviceCMYK DCT post-processing и дополнительные редкие ColorSpace остаются вне профиля.
- CCITT Group 3/Group 4 filters входят в профиль; JPX и JBIG2 filters остаются вне профиля.
- Revision 5/6 Unicode passwords проходят SASLprep на фиксированных Unicode 3.2 tables, UTF-8 и truncation до 127 bytes; R6 user/owner authentication использует Algorithm 2.B hardened hash и AESV3. Legacy revisions сохраняют точный byte-password API.
- Password-protected PDF открывается через локализованный password prompt; неверный пароль запрашивается повторно, пароль не сохраняется. R2-R4 string passwords кодируются точным PDFDocEncoding, R5/R6 — SASLprep/UTF-8.

## Future formats

DjVu не зарегистрирован в `SupportedFormatRegistry` и не заявляется как
поддерживаемый формат MYBOOK 1.5.9. Его reader является отдельной будущей
функциональностью и должен пройти те же readiness gates до добавления в реестр.
