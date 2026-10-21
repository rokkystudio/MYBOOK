# MYBOOK TODO

Текущая версия: 1.2.17

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

- [ ] Character/list/table styles: paragraph `basedOn` и character styles через `sprmCIstd` уже поддерживаются; `sprmCIstd` применяется между paragraph-style character properties и direct CHPX с проверкой style kind. Остались list/table styles, `sprmCMajority`/reset semantics и дополнительные style properties.
- [ ] Расширить PAPX на остальные paragraph SPRM; list membership через `sprmPIlvl`/`sprmPIlfo` уже сохраняется в нейтральной paragraph-модели: level 0-8, skip-numbering `0x0C`, signed LFO index, preserve-indents semantics для отрицательного iLfo и explicit not-in-list/reset values. HTML сохраняет эти свойства как metadata. `PlfLst`/`LSTF`/appended `LVL` и `PlfLfo` без `LFOLVL` overrides уже читаются: 1-based iLfo разрешается в lsid/LVL, а paragraph model получает `startAt`, `nfc`, follow-character и bullet literal; FIB ranges `fcPlfLst/lcbPlfLst` и `fcPlfLfo/lcbPlfLfo` читаются из Word 97 indices 73/74. Stateful numbering/restart уже формирует фактические markers по `LVL.xst`: поддерживаются decimal, upper/lower Roman, upper/lower Latin letter и bullets, inherited placeholders, `fLegal`, follow-character и `fNoRestart`/`ilvlRestartLim`; markers рендерятся перед paragraph text. Остались `LFOLVL` overrides и дополнительные locale-specific MSONFC formats; modern paragraph shading через `sprmPShd` (`SHDOperand`) сохраняет foreground/background/pattern в `DocumentShading`, solid pattern рендерится как background-color, остальные patterns сохраняются как HTML metadata. Modern paragraph borders `sprmPBrcTop`/`Left`/`Bottom`/`Right`/`Between` (`BrcOperand`) сохраняются в нейтральной модели; внешние стороны рендерятся CSS, `Between` сохраняется как metadata до реализации соседней paragraph-border semantics. Legacy paragraph shading `sprmPShd80` тоже поддерживается: 16-bit `Shd80` декодирует Ico foreground/background и 6-bit pattern, `Shd80Nil` сбрасывает shading. Legacy paragraph borders `sprmPBrcTop80`/`Left80`/`Bottom80`/`Right80`/`Between80` декодируют 4-byte `Brc80`. Word 95 `sprmPBrcTop70`/`Left70`/`Bottom70`/`Right70`/`Between70` декодируют 16-bit `BRC70`: line width/style, border type, shadow, Ico color и spacing сохраняются в той же нейтральной border-модели. Остались bar-border variants, полноценный patterned shading renderer и точная визуальная логика `Between`. Custom tabs через `sprmPChgTabsPapx` уже поддерживаются с delete/add semantics, left/center/right/decimal alignment и none/dots/hyphens/underline/heavy leaders, а HTML metadata использует общий `DocumentTabStop`. Bar/list tabs и middle-dot leader пока не представлены нейтральной моделью и приводят к явной unsupported-ошибке. Keep/keep-next, widow control, bidi, outline level, page-break-before и nested-table depth (`sprmPItap`/`sprmPDtap`) уже поддерживаются.
- [ ] Таблицы: `sprmTDefTable`, structural/grid-width overrides, TC80/BRC80 и modern cell borders, table-wide borders, preferred widths/autofit, logical alignment/RTL, shading, padding/vertical alignment, row properties, no-wrap/fit-text, `sprmTTextFlow` и nested tables поддерживаются. Nested cells сохраняют ordered block content и рекурсивные `DocumentTable`; depth>1 cell/row marks обрабатываются через `sprmPFInnerTableCell`/`sprmPFInnerTtp`. Остались patterned shading visual rendering, table styles и advanced border conflict cases.
- [ ] Изображения и OfficeArt: inline `U+0001` и floating `U+0008` raster PNG/JPEG читаются; floating path разрешает `PlcfSpa` → `OfficeArtFSP.spid` → `FOPT.pib` → BStore/FBSE/BLIP и показывает image в reflow. Остались `PlcSpaHdr`, точное floating positioning/wrap, complex/delayed/fill BLIP references, WMF/EMF/DIB/TIFF и полноценный shape rendering.
- [ ] Headers/footers: text stories и Even/Odd/First variants по секциям читаются; остались `PlcSpaHdr` drawings, header/footer-specific fields и layout properties.
- [ ] Footnotes/endnotes: PLC references и note text stories поддерживаются с anchors/links; остались точные custom-symbol glyphs через `sprmCSymbol`, note-specific fields и layout properties.
- [ ] Sections: `PlcfSed`/SEPX, section break kind, orientation, page size, margins, gutter, header/footer distances, title-page и bidi поддерживаются; остались columns, page numbering, page borders/document grid и применение этих свойств в реальном pagination/layout engine.
- [ ] Поля Word, кроме уже поддержанных безопасных HYPERLINK fields.
- [ ] Дополнительные кодировки и charset/font mapping.
- [ ] Старые версии Word Binary Format.
- [ ] Реальная постраничная layout/pagination модель для превью страниц.

## RTF

- [ ] Таблицы: cell borders через `clbrdr*` и row outer/inside borders через `trbrdrt`/`trbrdrl`/`trbrdrb`/`trbrdrr`/`trbrdrh`/`trbrdrv` сохраняются с width/color/spacing/shadow/frame. Поддерживаются `brdrs`, `brdrdb`, `brdrdot`, `brdrdash`, `brdrhair`, `brdrdashd`/`brdrdashdot`, `brdrdashdd`/`brdrdashdotdot`, `brdrtriple`; точные Word border type codes сохраняются в модели, HTML использует доступный CSS fallback для hairline/dot-dash/triple. Cell shading через `clshdng`/`clcfpat`/`clcbpat` сохраняется в нейтральной модели; точные 0%/100% fills визуально рендерятся, промежуточные patterned fills сохраняют процент и цвета как metadata. Horizontal `clmgf`/`clmrg` и vertical `clvmgf`/`clvmrg` merges сохраняются как `ColumnSpan`/`RowSpan`; `cellx` задаёт фактическую ширину ячеек; `trhdr`, `trkeep`, `trrh`, `clvertal*`, twips-padding через `clpad*` + unit 3, `clNoWrap` и `clFitText` поддерживаются. Остались nested tables, декоративные border styles без точного CSS-эквивалента, визуальный renderer patterned shading и остальные редкие row/cell properties.
- [ ] Изображения: PNG/JPEG и стандартный BI_RGB `dibitmap` поддерживаются; `picw`/`pich` сохраняются как исходные pixel-размеры, `picwgoal`/`pichgoal` вместе с `picscalex`/`picscaley` задают точные target-размеры в points и применяются HTML renderer. `piccropl`/`piccropt`/`piccropr`/`piccropb` сохраняются как signed crop offsets в points и выводятся в HTML metadata. Остались WMF/EMF, визуальное применение crop clipping и дополнительные варианты `pict`; scale без goal-размера сохраняется как metadata без угадывания DPI.
- [ ] Стили абзацев: `tx` tab stops с `tql`/`tqc`/`tqr`/`tqdec` alignment и `tldot`/`tlhyph`/`tlul`/`tlth`/`tleq` leaders сохраняются в нейтральной модели и HTML metadata; символы `tab` сохраняются в reflow. `pagebb` сохраняется как page-break-before и применяется HTML renderer, `outlinelevel` сохраняется в нейтральной модели и HTML metadata. Остались точное позиционирование custom tab stops в layout и другие редкие paragraph controls; alignment, indents, spacing, `slmult`, keep/keep-next, widow control и bidi direction уже поддерживаются.
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
