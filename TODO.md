# MYBOOK TODO

Текущая версия: 1.1.15

Текущая цель: полностью завершить чтение, нейтральную модель и рендер всех уже поддерживаемых форматов. Задачи интерфейса и оболочки не имеют приоритета до закрытия форматных блоков.

В этом файле перечислены только незавершённые задачи.

## Архитектура и тесты

- [ ] Добавить общий слой пользовательских render overrides для всех потоковых форматов: полное или частичное переопределение семейства шрифта, размера, начертания, цвета текста и фона без потери исходной модели документа.
- [ ] Добавить независимые коррекции яркости, контраста и насыщенности для текста, фона и изображений, включая изображения внутри страницы, с нейтральными значениями без визуального изменения исходного документа.
- [ ] Сохранять исходные встроенные шрифты/цвета/стили форматов в модели независимо от пользовательских render overrides, чтобы настройки чтения применялись только на этапе рендера.
- [ ] Автоматизировать regression-тесты на открытых corpus-файлах из `Books`.
- [ ] Добавить fuzz tests для бинарных readers DOC/PDF/DjVu.
- [ ] Ввести ограничения памяти, размера потоков и глубины вложенности для недоверенных документов.

## DOCX

- [ ] Реальная постраничная layout/pagination модель для превью страниц.

## DOC

- [ ] Character/list/table styles, включая `sprmCIstd`, и дополнительные style properties вне уже поддержанного paragraph style inheritance.
- [ ] Расширить PAPX на list properties, nested-table depth, tabs, borders/shading и остальные paragraph SPRM; keep/keep-next, widow control, bidi, outline level и page-break-before уже поддерживаются.
- [ ] Таблицы: `sprmTDefTable`/cell definitions, widths, merges, borders/shading и nested tables.
- [ ] Изображения и OfficeArt: inline `U+0001` и floating `U+0008` raster PNG/JPEG читаются; floating path разрешает `PlcfSpa` → `OfficeArtFSP.spid` → `FOPT.pib` → BStore/FBSE/BLIP и показывает image в reflow. Остались `PlcSpaHdr`, точное floating positioning/wrap, complex/delayed/fill BLIP references, WMF/EMF/DIB/TIFF и полноценный shape rendering.
- [ ] Headers/footers: text stories и Even/Odd/First variants по секциям читаются; остались `PlcSpaHdr` drawings, header/footer-specific fields и layout properties.
- [ ] Footnotes/endnotes: PLC references и note text stories поддерживаются с anchors/links; остались точные custom-symbol glyphs через `sprmCSymbol`, note-specific fields и layout properties.
- [ ] Sections: `PlcfSed`/SEPX, section break kind, orientation, page size, margins, gutter, header/footer distances, title-page и bidi поддерживаются; остались columns, page numbering, page borders/document grid и применение этих свойств в реальном pagination/layout engine.
- [ ] Поля Word, кроме уже поддержанных безопасных HYPERLINK fields.
- [ ] Дополнительные кодировки и charset/font mapping.
- [ ] Старые версии Word Binary Format.
- [ ] Реальная постраничная layout/pagination модель для превью страниц.

## RTF

- [ ] Таблицы: nested tables и дополнительные row/cell properties; horizontal `clmgf`/`clmrg` и vertical `clvmgf`/`clvmrg` merges уже сохраняются как `ColumnSpan`/`RowSpan`.
- [ ] Изображения: WMF/EMF и дополнительные варианты `pict`; стандартный BI_RGB `dibitmap` уже преобразуется в BMP, PNG/JPEG поддерживаются напрямую.
- [ ] Стили абзацев: tab stops и редкие paragraph controls; alignment, indents, spacing, `slmult`, keep/keep-next, widow control и bidi direction уже поддерживаются.
- [ ] Расширенные Unicode/codepage edge cases: Symbol/OEM charsets, редкие charset/codepage combinations и malformed fallback; известные `fcharset` mapping и DBCS hex sequences уже поддерживаются.

## Markdown / TXT / HTML

- [ ] Локальные ресурсы HTML с безопасным разрешением относительных путей.
- [ ] Настройки типографики для TXT/Markdown.
- [ ] Решить, оставляем ли Markdig или заменяем собственным Markdown parser.

## PDF

- [ ] ExtGState: blend modes, soft mask как graphics state и overprint.
- [ ] Predefined named Type0 CMap без встроенного `/Encoding` stream.
- [ ] Image XObject: Indexed/JPEG + `/Matte`, нестандартный `/Decode` поверх DCT/JPEG и дополнительные ColorSpace.
- [ ] Image filters: CCITT, JPX и JBIG2; LZW с `EarlyChange` 0/1 и predictor parameters уже поддерживается для streams и Image XObject.
- [ ] Шифрование/password-protected PDF.

## DjVu

- [ ] IFF/DjVu container.
- [ ] `FORM:DJVU` / `FORM:DJVM`.
- [ ] Directory/navigation.
- [ ] Text layer.
- [ ] INFO chunks.
- [ ] JB2 decoder.
- [ ] IW44 decoder.
- [ ] Background/foreground composition.
- [ ] Постраничный renderer.
- [ ] Links/annotations.

## Интерфейс

- [ ] Поиск по документу.
- [ ] Настройка шрифта, размера, ширины текста и межстрочного интервала.
- [ ] Последняя позиция чтения для каждого документа.
- [ ] Закладки.
- [ ] Режим одной/двух страниц для фиксированной верстки.
- [ ] Горячие клавиши чтения и навигации.

## Settings

- [ ] Добавить DjVu в файловые ассоциации после появления рабочего reader.
- [ ] Вкладка чтения/типографики.
- [ ] Вкладка поведения приложения.
- [ ] Настройки WebView2 и безопасности локального HTML.

## Сборка и качество

- [ ] Release publish.
- [ ] Single-file/self-contained стратегия.
- [ ] Проверка наличия WebView2 Runtime и понятное сообщение пользователю.

## Финальное тестирование форматов

- [ ] После полного завершения reader/model/renderer каждого поддерживаемого формата провести отдельный финальный regression-прогон на реальных документах, а не только на синтетических fixtures.
- [ ] Для сложных форматов PDF, DOCX, DOC, EPUB и DjVu собрать расширенный corpus с документами из разных генераторов, версий спецификаций и с редкими/пограничными возможностями.
- [ ] Проверить для каждого полностью готового формата не только извлечение текста, но и структуру, стили, изображения, таблицы, ссылки, навигацию, встроенные ресурсы и итоговый HTML/SVG/page rendering.
- [ ] Выполнить визуальное сравнение сложных документов с эталонным отображением и добавить найденные расхождения как постоянные regression fixtures.
- [ ] Перед признанием формата полностью готовым прогнать malformed/truncated/unsupported edge cases и убедиться, что ошибки явные и не приводят к скрыто повреждённому рендеру.

## Тестовый корпус

- Ручные regression-файлы: `C:\FILES\PROJECTS\MYBOOK\Books`.
