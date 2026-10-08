# MYBOOK TODO

Текущая версия: 1.0.29

## Архитектура

- [x] Единая нейтральная модель документа `DocumentModel`.
- [x] Единый HTML/WebView2 renderer для потоковых документов.
- [x] Разнести readers по `Sources/Formats/<Format>`.
- [x] Расширить `DocumentModel` моделью фиксированной страницы для PDF/DjVu.
- [ ] Добавить единый реестр поддерживаемых форматов вместо дублирования списков в MainWindow, Settings и FileAssociationService.
- [ ] Добавить набор regression-тестов для readers без запуска GUI.
- [ ] Добавить диагностический режим parser trace для сложных повреждённых документов.

## FB2

- [x] Метаданные, основной текст, emphasis, superscript.
- [x] Binary-обложка.
- [ ] Ссылки и внутренние якоря.
- [ ] Сноски и notes body.
- [ ] Встроенные изображения внутри текста.
- [ ] Таблицы и прочие редко используемые FB2-элементы.
- [ ] Корректная поддержка нескольких body.

## EPUB

- [x] ZIP-контейнер, container.xml, OPF manifest/spine.
- [x] Метаданные, XHTML-главы, базовая обложка.
- [ ] Корректная обработка относительных ссылок между главами.
- [ ] CSS из EPUB вместо потери авторских стилей.
- [ ] Шрифты EPUB.
- [ ] Все изображения и SVG-ресурсы.
- [ ] EPUB navigation document / NCX.
- [ ] Сноски, landmarks и page-list.
- [ ] EPUB 2/3 compatibility tests.

## DOCX

- [x] Собственный ZIP/XML reader без DocumentFormat.OpenXml.
- [x] Абзацы и базовые run-стили.
- [ ] styles.xml и наследование стилей.
- [ ] numbering.xml и списки.
- [ ] relationships.
- [ ] Изображения.
- [ ] Таблицы.
- [ ] Hyperlinks.
- [ ] Headers/footers.
- [ ] Footnotes/endnotes.
- [ ] Sections и page breaks.
- [ ] Embedded objects.

## DOC

- [x] Собственный CFB/OLE reader.
- [x] FAT, MiniFAT, directory streams.
- [x] FIB и CLX piece table.
- [x] Основной текст и абзацы.
- [x] RTF-документы с расширением `.doc` определяются по сигнатуре и открываются RTF reader.
- [ ] CHP/PAP formatting runs.
- [ ] Stylesheet.
- [ ] Таблицы.
- [ ] Изображения и OfficeArt.
- [ ] Hyperlinks.
- [ ] Headers/footers.
- [ ] Footnotes/endnotes.
- [ ] Sections.
- [ ] Поля Word.
- [ ] Дополнительные кодировки и charset/font mapping.
- [ ] Старые версии Word Binary Format.

## RTF

- [x] Автономное чтение без внешнего офисного ПО.
- [ ] Заменить WPF RTF parser собственным tokenizer/parser.
- [ ] Таблицы.
- [ ] Изображения.
- [ ] Цвета и размеры шрифтов.
- [ ] Стили абзацев.
- [ ] Hyperlinks.
- [ ] Unicode/codepage edge cases.

## Markdown / TXT / HTML

- [x] TXT.
- [x] Markdown.
- [x] HTML/HTM.
- [ ] Локальные ресурсы HTML с безопасным разрешением относительных путей.
- [ ] Настройки типографики для TXT/Markdown.
- [ ] Решить, оставляем ли Markdig или заменяем собственным Markdown parser.

## PDF

- [x] Заголовок PDF и версия.
- [x] Поиск `startxref`.
- [x] Classic xref table.
- [x] Trailer dictionary.
- [x] Indirect objects.
- [x] Dictionary/array/name/string/number parser.
- [x] Stream objects.
- [x] FlateDecode.
- [x] XRef streams.
- [x] Object streams.
- [x] Incremental updates через `/Prev` и hybrid `/XRefStm`.
- [x] Page tree.
- [x] MediaBox/CropBox/Rotate.
- [x] Content stream tokenization.
- [x] Базовый graphics state: `q`, `Q`, `cm`, стек CTM и text-state параметров.
- [ ] ExtGState (`gs`) и graphics state. Поддержаны `/LW`, `/LC`, `/LJ`, `/ML`, `/D`, `/CA`, `/ca`; остаются blend mode, soft mask и overprint.
- [x] Text operators и позиционирование: `Tm`, `Td`, `TD`, `T*`, `Tf`, `Tc`, `Tw`, `Tz`, `Ts`, `Tj`, `TJ` и quote operators формируют позиционированные text runs.
- [x] Font resources и ToUnicode CMap для текстового слоя.
- [x] Точные glyph widths/advance для simple fonts через `/FirstChar` + `/Widths` и Type0 `Identity-H/Identity-V` через `/DW` + `/W`.
- [x] Type0 custom Encoding CMap stream: `codespacerange`, `cidchar`, `cidrange` и code→CID mapping для точных widths.
- [ ] Predefined named Type0 CMap без встроенного `/Encoding` stream.
- [ ] Встроенные изображения. Поддержаны Image XObject с `DCTDecode` (JPEG), `DeviceGray`/`DeviceRGB`/`DeviceCMYK`, 1/2/4/8-bit `Indexed`, TIFF predictor 2, PNG predictors 10..15, `/Decode` arrays для raster/Indexed, 8-bit `DeviceGray` `/SMask` включая `/Decode`, `/SMask /Matte` для 8-bit Gray/RGB/CMYK raster, filter arrays с `ASCIIHexDecode`/`ASCII85Decode` → `FlateDecode`/`DCTDecode` и aligned `/DecodeParms`; остаются Indexed/JPEG + `/Matte`, нестандартный `/Decode` поверх DCT/JPEG, дополнительные ColorSpace и LZW/CCITT/JPX/JBIG2 filters.
- [x] Базовые paths/fills/strokes: `m/l/c/v/y/h/re`, `S/s/f/F/f*/B/B*/b/b*/n`, `w`, Gray/RGB/CMYK colors.
- [x] Line cap/join, miter limit и dash pattern: `J`, `j`, `M`, `d` + SVG stroke styles.
- [x] Clipping paths: `W`, `W*`, последовательное пересечение clipping областей и восстановление через `q/Q`.
- [x] Точный mixed paint order между paths/images/text через `PaintOrder` из исходного content stream.
- [x] Постраничная модель MYBOOK.
- [x] SVG renderer фиксированной страницы для геометрического текстового слоя через исходные PDF points и `viewBox`.
- [x] Ссылки и outlines: Link annotations (`URI`, `GoTo`, `/Dest`), named destinations (`/Dests`, `/Names /Dests`) и outline tree в `DocumentModel`; UI-боковая панель остаётся отдельным этапом.
- [ ] Шифрование PDF — отдельный этап.

## DjVu

- [ ] IFF/DjVu container.
- [ ] FORM:DJVU / FORM:DJVM.
- [ ] Directory/navigation.
- [ ] Text layer.
- [ ] INFO chunks.
- [ ] JB2 decoder.
- [ ] IW44 decoder.
- [ ] Background/foreground composition.
- [ ] Постраничный renderer.
- [ ] Links/annotations.

## Интерфейс

- [x] NeoUI title bar с оконными кнопками.
- [x] Отдельная toolbar-полоса.
- [x] Открытие файла, язык, тема, Settings.
- [x] Иконка приложения из `Resources/app.ico`.
- [x] Запоминание последней папки открытия.
- [x] Оглавление/навигационная боковая панель для `DocumentModel.Outlines` с переходом к фиксированным страницам.
- [ ] Переход к странице/главе.
- [ ] Поиск по документу.
- [ ] Масштаб +/−/100%.
- [ ] Настройка шрифта, размера, ширины текста и межстрочного интервала.
- [ ] Полноэкранный режим чтения.
- [ ] Последняя позиция чтения для каждого документа.
- [ ] Недавние документы.
- [ ] Закладки.
- [ ] Режим одной/двух страниц для фиксированной верстки.
- [ ] Горячие клавиши чтения и навигации.

## Settings

- [x] Вкладка файловых ассоциаций.
- [x] FB2/EPUB/HTML/HTM/TXT/MD/RTF/DOC/DOCX/PDF.
- [x] PDF после появления рабочего reader.
- [ ] DjVu после появления рабочего reader.
- [ ] Вкладка чтения/типографики.
- [ ] Вкладка поведения приложения.
- [ ] Настройки WebView2/безопасности локального HTML.

## Тестовые книги

- Корпус ручных regression-тестов: `C:\\FILES\\PROJECTS\\MYBOOK\\Books`.

## Сборка и качество

- [x] `Build/bin` и `Build/obj`.
- [x] `Resources/app.ico` как единый источник иконки.
- [ ] Release publish.
- [ ] Single-file/self-contained стратегия.
- [ ] Проверка наличия WebView2 Runtime и понятное сообщение.
- [ ] Автоматические parser tests на открытых corpus-файлах.
- [ ] Fuzz tests для бинарных readers DOC/PDF/DjVu.
- [ ] Ограничения памяти/размера для недоверенных документов.
