# MYBOOK format support

Версия профиля: 1.5.48

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

## Experimental DjVu inspection (not registered)

`DjvuContainerReader.Inspect` parses the DjVu IFF envelope (`AT&T`, `FORM:DJVU`, `FORM:DJVM`), validates chunk bounds/padding/nesting, and reads page `INFO` dimensions and DPI. Plain `TXTa` text is now extracted as validated UTF-8 with 24-bit length, attached to its page; compressed `TXTz` is explicitly unsupported. `TXTa` version-1 zone trees now decode PAGE/COLUMN/REGION/PARAGRAPH/LINE/WORD/CHARACTER geometry with DjVu parent/sibling delta coordinates and UTF-8 byte ranges; malformed trees and resource limits are validated. It now inspects DIRM v1 bundled/indirect headers, validates bundled component offsets and preserves the BZZ metadata tail as opaque. It does **not** decode BZZ component metadata, JB2/IW44, resolve indirect DJVM directories or render DjVu pages. `.djvu` remains outside `SupportedFormatRegistry` until complete reading is available. Synthetic regression fixtures cover single/multipage containers and malformed input.

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

- PDF Image XObject `/ImageMask true` использует stencil semantics: 1-bit samples с row padding, `/Decode [0 1]` и `[1 0]`, текущий nonstroking RGB color на каждом `Do` и RGBA output с прозрачностью. Для bilevel CCITT/JBIG2 используется их 8-bit decoded sample path. Дополнительные `/Mask` и `/SMask` для stencil отклоняются; stencil DCT/JPX не входят в профиль.
- ExtGState soft-mask group compositing не реализован полностью. Проверяются известные `/SMask /S` (Alpha/Luminosity) и тип `/G` (Form XObject stream); дополнительно проверяются `/BBox` (четыре конечных числа, положительная площадь), а `/Matrix` — шесть конечных компонентов с невырожденной линейной частью и `/Group /S /Transparency` у Form XObject `/G`. Словарь `/SMask` без обязательного `/G` теперь отклоняется `InvalidDataException` (вместо вывода без маски); для корректных `/SMask /G` с transparency group применяется явный `NotSupportedException` вместо молчаливого вывода без маски. `/SMask /TR` c не-Identity transfer и `/SMask /BC` явно отклоняются как unsupported (negative regressions), не игнорируются. Это не рендеринг transparency group.
- Predefined Type0 CMaps для public Adobe CJK/KR collections входят в профиль: canonical Adobe resources дают code→CID, включая `usecmap` inheritance; без `/ToUnicode` CID→Unicode берётся из canonical `Registry-Ordering-UCS2` mapping resources.
- CIE-based raster ColorSpace `CalGray`, `CalRGB` и `Lab` входят в профиль для 8-bit decoded samples с точными PDF gamma/matrix/Lab transforms и chromatic adaptation к sRGB. `CalGray` DCT входит в профиль через grayscale sample decode; `CalRGB` DCT — при effective JPEG `ColorTransform=1`, с приоритетом Adobe APP14 marker над `/DecodeParms /ColorTransform`. CIE `/SMask /Matte` входит в профиль с unpremultiply в исходных CIE components до gamma/matrix/Lab conversion. `Lab` DCT и `CalRGB` DCT с effective transform=0 поддерживаются через managed BitMiracle.LibJpeg.NET raw-component decoder (JCS_UNKNOWN), который отключает JPEG color conversion, проверяет geometry и ограничивает decoded raster 256 MiB. `ICCBased` `/N=1/3/4` входит в профиль через embedded ICC profile и системный WPF ICC/ICM engine; `/Range` и `/Decode` учитываются строго. ICC Gray/RGB/CMYK DCT post-processing входит в профиль, причём `/N=4` использует decoded CMYK JPEG samples до ICC conversion. `Separation` и `DeviceN` входят в профиль через общий PDF Function evaluator: `FunctionType 2`, sampled `FunctionType 0` с multilinear `/Order=1` и stitching `FunctionType 3`; alternate DeviceGray/DeviceRGB/DeviceCMYK поддерживаются. `FunctionType 4` calculator functions входят в профиль через отдельный VM с полным разрешённым operator set, PDF numeric grammar, 32-bit integer semantics и overflow promotion, PostScript round/bitshift/cvi behavior, синтаксическими conditional expressions для `if`/`ifelse`, top-N output stack semantics, строгими type/stack checks и resource limits. Sampled `FunctionType 0` `/Order=3` входит в профиль через Acrobat-compatible multidimensional cubic interpolation с cubic-convolution `a=-0.5`, quadratic edge handling и recursive interpolation по измерениям. ICCBased, Separation и DeviceN `/SMask /Matte` входят в профиль с unpremultiply в исходных component/tint coordinates до ICC profile conversion или tint transform. DeviceCMYK DCT post-processing входит в профиль через 8-bit CMYK sample decode с geometry validation и последующим PDF `/Decode`. Ненулевой CIE BlackPoint остаётся вне профиля. DeviceN+DCT не заявляется как поддерживаемый: PDF DCT profile ограничен максимум четырьмя components, поэтому произвольный N-channel JPEG не эмулируется.
- CCITT Group 3/Group 4, `JPXDecode` и `JBIG2Decode` входят в профиль. JPX декодируется pure-managed CoreJ2K 2.4.1.108 с geometry/component validation и decoder resource limits; PDF `/BitsPerComponent` и `/Decode` для JPX не используются, явный PDF `/ColorSpace` имеет приоритет над embedded JPX color mapping, а при отсутствии `/ColorSpace` используются embedded Gray/RGB/CMYK semantics. JBIG2 декодируется pure-managed SharpAstro.Jbig2 3.16.981 с optional `/JBIG2Globals`, строгим `/BitsPerComponent 1`, 256 MiB raster limit и обычным PDF `/Decode` после reconstruction. Поддержан arithmetic/MMR surface; Huffman-coded symbol/text variants, custom Huffman tables, MMR внутри pattern/halftone, HENABLESKIP и TPGRON остаются явным unsupported; synthetic SDHUFF regression проверяет NotSupportedException, а PDF adapter сохраняет контекст JBIG2Decode в диагностике. JPX RGB+alpha alpha-first `cdef` дополнительно проверяется end-to-end PDF→PNG pixel regression; alpha на позициях 1 и 2 теперь поддерживается: исходные компоненты читаются без embedded color mapping и переставляются по JP2 cdef; проверяется samples и конечный PDF→PNG BGRA; дополнительно покрыт `/SMaskInData=2` с `/Matte` и проверкой фактических пикселей для обеих средних позиций alpha. JPX JP2 `cdef` для embedded opacity проверяется: для Gray+alpha с alpha-first `cdef` выполняется нормализация метаданных перед CoreJ2K и перестановка decoded samples; для RGB+alpha (четыре компонента) с alpha-first `cdef` также выполняется нормализация JP2 metadata и перестановка RGBA samples; произвольные другие перестановки остаются unsupported. JPX с неявным Gray+alpha (два компонента) поддерживает `/SMaskInData=1` и `/SMaskInData=2` с точной Gray `/Matte` compensation; оба варианта покрыты end-to-end regressions. JPX `/SMaskInData=1` входит в профиль: embedded unassociated opacity извлекается из JPX alpha component и кодируется вместе с цветом в RGBA PNG; поддерживаются явный `/DeviceRGB` и embedded sRGB, а также согласованные компоненты остальных реализованных PDF ColorSpace. Проверяются alpha channel metadata и component counts. JPX `/SMaskInData=2` с preblended colors поддерживается для DeviceGray/DeviceRGB с обязательным `/Matte`: compensation выполняется в исходных цветовых компонентах до RGBA encoding. Для CalGray/CalRGB/Lab и ICCBased компенсация выполняется в исходных компонентах после PDF component decoding и до CIE/ICC преобразования; подтверждены end-to-end CalRGB и ICCBased RGB. Separation и DeviceN также компенсируют tint-компоненты до FunctionType tint transform; end-to-end JPX regressions покрывают Separation/FunctionType 2 и DeviceN/FunctionType 4. Indexed также поддерживает `/SMaskInData=2`: компенсация palette index выполняется до RGB lookup, с проверкой длины `/Matte`; покрыто end-to-end JPX regression.
- Revision 5/6 Unicode passwords проходят SASLprep на фиксированных Unicode 3.2 tables, UTF-8 и truncation до 127 bytes; R6 user/owner authentication использует Algorithm 2.B hardened hash и AESV3. Legacy revisions сохраняют точный byte-password API.
- Password-protected PDF открывается через локализованный password prompt; неверный пароль запрашивается повторно, пароль не сохраняется. R2-R4 string passwords кодируются точным PDFDocEncoding, R5/R6 — SASLprep/UTF-8.

## Future formats

DjVu не зарегистрирован в `SupportedFormatRegistry` и не заявляется как
поддерживаемый формат MYBOOK 1.5.48. Его reader является отдельной будущей
функциональностью и должен пройти те же readiness gates до добавления в реестр.
