# MYBOOK format support

Версия профиля: 1.5.75

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
| `.djvu` | DjVu fixed-page reading profile: single-page, bundled и indirect DJVM containers; JB2/Smmr masks, IW44/JPEG backgrounds/foregrounds, FGbz palettes, compound composition, INFO rotation, NAVM outlines, shared/page ANTa/ANTz hyperlinks со всеми стандартными maparea shapes и selectable TXTa/TXTz text-zone overlay. |

“Ready for use” означает, что зарегистрированный формат проходит единый путь
от открытия пользовательского файла до модели и итогового рендера, имеет
контролируемый отказ на повреждённом/неподдерживаемом вводе и покрыт
регрессионными fixtures. Это не означает реализацию каждого редкого элемента
соответствующей спецификации.

## DjVu fixed-page profile

`.djvu` зарегистрирован в `SupportedFormatRegistry` как reader-backed fixed-page format. `DjvuDocumentReader` проходит общий `DocumentInputGuard`, разбирает container, рендерит каждую поддерживаемую страницу в opaque RGB PNG и создаёт `DocumentFixedPage` с page geometry, searchable/selectable text-zone overlay, links и outline navigation. Общий HTML/SVG renderer, WebView2 viewport scaling, боковая плёнка страниц, переходы `page-N`, Ctrl+F, recent documents и file associations работают через существующую инфраструктуру MYBOOK.

`DjvuContainerReader.Inspect` проверяет `AT&T`, `FORM:DJVU`/`FORM:DJVM`, big-endian chunk lengths, odd-byte padding, nesting/resource limits и обязательный первый `INFO` chunk страницы. Поддерживаются legacy 5-byte `INFO` (width/height/minor version) и current 10-byte `INFO` (major/minor version, raw DPI, gamma, rotation flags). Legacy metadata не дополняется синтетическими DPI/gamma/rotation значениями; fixed-page layout при отсутствии DPI использует один layout point на raster pixel, не меняя source metadata.

Visual profile включает:
- standalone/full-page JB2 `Sjbz` с local, bundled или indirect shared `Djbz`; local page dictionary имеет приоритет, иначе используется первый `Djbz`, найденный рекурсивно по ordered `INCL` graph;
- regular non-striped `Smmr` mask;
- progressive IW44 `BG44`/`FG44` с serial/slice/version/color/geometry validation и managed inverse-wavelet decoding;
- JPEG `BGjp`/`FGjp`;
- `FGbz` version 0 RGB palette с BZZ per-blit color indexes;
- compound composition из одного `Sjbz`/JB2 или `Smmr` mask, optional `BG44`/`BGjp` background и optional `FGbz`/`FG44`/`FGjp` foreground colors;
- DjVu 4-bit linear scaling для reduced backgrounds, integral nearest-neighbor expansion для reduced foreground color rasters и final `INFO` rotation.

`DIRM` v1 поддерживает bundled и indirect metadata, component roles, IDs, names и titles. Bundled offsets/sizes/roles сверяются с реальными `FORM:DJVU`/`DJVI`/`THUM`. Для indirect DJVM `DjvuIndirectDocumentResolver` загружает page/shared components относительно каталога index-файла: physical filename берётся из DIRM `Name`, если он присутствует, иначе из component `Id`, а `INCL` и NAVM продолжают разрешаться по component ID. Shared `FORM:DJVI` может рекурсивно включать другие shared components; dependency graph сохраняет `INCL` order, использует cache, ограничивает глубину/resources и явно отклоняет cycles. Rooted/path-traversal names, отсутствующие files, DIRM size mismatch и role/FORM mismatch отклоняются. Каждый `DjvuImageChunk` хранит собственный `SourcePath`, поэтому raster decoders читают payload непосредственно из page/shared component и не принимают index path. Суммарный объём реально открываемых external page/shared files ограничен 256 MiB.

BZZ `NAVM` разбирается в bounded recursive outline. Internal page references используют DjVu resolution order ID → signed relative displacement → TITLE → page number → NAME; relative targets разрешаются относительно содержащей страницы. External HTTP/HTTPS/mailto URLs остаются внешними. Outline передаётся в `DocumentOutlineItem` и использует общую навигацию MYBOOK.

`ANTa` и BZZ-compressed `ANTz` проходят strict UTF-8 validation. Reader разбирает все пять стандартных `maparea` shapes: `rect`, `oval`, `poly`, `text` и `line`, сохраняет optional `(url href target)` metadata и точную bottom-left pixel geometry. `text` hyperlink использует его прямоугольную область; `line` сохраняет обе конечные точки и спецификационную толщину `(width w)`, default 1 pixel. Page и shared `INCL` рекурсивно разворачивают annotations из bundled или indirect role-0/role-3 `FORM:DJVI` точно в позиции marker, не меняя chunk order. Fixed-page renderer переводит geometry в top-left coordinates и создаёт transparent SVG `rect`/`ellipse`/`polygon`/`line` hit regions, внутренние DjVu page targets и безопасные внешние URL. Визуальные эффекты maparea (`border`/`hilite`, line color/arrow, text `backclr`/`textclr`/`pushpin`) и decorative mapareas с пустым URL не имитируются; они остаются отдельным visual-annotation profile.

`TXTa` и BZZ-compressed `TXTz` извлекают strict UTF-8 page text. Version-1 text-zone tree разбирает PAGE/COLUMN/REGION/PARAGRAPH/LINE/WORD/CHARACTER geometry и UTF-8 byte ranges. `DjvuTextOverlayBuilder` берёт terminal zones без дублирования parent ranges, сохраняет исходные UTF-8 gaps между соседними zones, строго отклоняет split-codepoint/overlap boundaries и создаёт transparent `DocumentFixedTextRun` overlay. Непустые OCR zones могут выходить за `INFO` bounds, как допускает DjVu text model: координаты не clamp-ятся и не отбрасываются, а SVG viewport скрывает внешнюю часть; interactive maparea geometry остаётся строго page-bounded. Общий SVG renderer использует точную zone width через `textLength/spacingAndGlyphs`, top-aligned geometry, INFO rotation и `user-select:text`; последовательность run text точно восстанавливает `DocumentFixedPage.Text`. Ctrl+F не перехватывается fullscreen hotkey и передаётся WebView2 с включёнными browser accelerator keys.

Resource limits ограничивают file/chunk/nesting/dependency counts, decoded BZZ/IW44/JB2 raster sizes, shapes/blits/comments/mapareas, page count, суммарный indirect component input и PNG payload fixed-page model. Неизвестные/конфликтующие visual layer families, duplicate local `Djbz`, cyclic/too-deep shared dependency graphs, striped `Smmr` и unsupported annotation profiles завершаются явной ошибкой вместо частичного рендера.

Regression покрывает container/DIRM/NAVM/text/annotation parsing, все пять стандартных maparea shapes, exact line width/default, bundled/indirect nested shared dependencies и cycle rejection, external `SourcePath` binding, local-vs-included JB2 dictionary precedence, JPEG/IW44/JB2/Smmr decoding, FGbz, compound composition, reader → `DocumentModel` → PNG/text overlay → HTML/SVG, rotation geometry, out-of-page OCR zones, UTF-8 boundaries, traversal/role/size errors и malformed input. Основной real validation corpus содержит 7 корректных DjVu файлов / 147 страниц; 102 text-bearing pages создают 38 859 selectable runs. Дополнительно real `boy_jb2.djvu`, подключённый как external page через indirect DIRM index, проходит полный reader/render path. Real bundled `czech.djvu` полностью проходит 85-page reader/render: 83 text-bearing pages, 49 links и 42 790 384 bytes encoded PNG; page 16 JB2 mask SHA-256 regression-lock подтверждает first-recursive shared `Djbz` resolution. Четыре legacy fixtures с некорректным UTF-8 внутри `TXTz` завершаются строгой `InvalidDataException`, без producer-specific byte fallback.

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
