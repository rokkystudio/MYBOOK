# MYBOOK TODO

Текущая версия: 1.0.37

В этом файле перечислены только незавершённые задачи.

## Архитектура и тесты

- [ ] Добавить настоящий CFB Word Binary `.doc` fixture в `Books` и постоянный regression-тест прямого `DocDocumentReader`.
- [ ] Автоматизировать regression-тесты на открытых corpus-файлах из `Books`.
- [ ] Добавить fuzz tests для бинарных readers DOC/PDF/DjVu.
- [ ] Ввести ограничения памяти, размера потоков и глубины вложенности для недоверенных документов.

## FB2

- [ ] Встроенные изображения внутри текста.
- [ ] Таблицы и прочие редко используемые FB2-элементы.
- [ ] Корректная семантика нескольких `body`, кроме уже поддержанного `body name="notes"`.

## EPUB

- [ ] CSS из EPUB с безопасным применением авторских стилей.
- [ ] Встроенные шрифты EPUB.
- [ ] Все изображения и SVG-ресурсы.
- [ ] EPUB navigation document и NCX.
- [ ] Сноски, landmarks и page-list.
- [ ] EPUB 2/3 compatibility tests на отдельном corpus.

## DOCX

- [ ] `styles.xml` и наследование стилей.
- [ ] `numbering.xml`, маркированные и нумерованные списки.
- [ ] Relationships.
- [ ] Изображения.
- [ ] Таблицы.
- [ ] Hyperlinks.
- [ ] Headers/footers.
- [ ] Footnotes/endnotes.
- [ ] Sections и page breaks.
- [ ] Embedded objects.
- [ ] Реальная постраничная layout/pagination модель для превью страниц.

## DOC

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
- [ ] Реальная постраничная layout/pagination модель для превью страниц.

## RTF

- [ ] Заменить WPF RTF parser собственным tokenizer/parser.
- [ ] Таблицы.
- [ ] Изображения.
- [ ] Цвета и размеры шрифтов.
- [ ] Стили абзацев.
- [ ] Hyperlinks.
- [ ] Unicode/codepage edge cases.

## Markdown / TXT / HTML

- [ ] Локальные ресурсы HTML с безопасным разрешением относительных путей.
- [ ] Настройки типографики для TXT/Markdown.
- [ ] Решить, оставляем ли Markdig или заменяем собственным Markdown parser.

## PDF

- [ ] ExtGState: blend modes, soft mask как graphics state и overprint.
- [ ] Predefined named Type0 CMap без встроенного `/Encoding` stream.
- [ ] Image XObject: Indexed/JPEG + `/Matte`, нестандартный `/Decode` поверх DCT/JPEG и дополнительные ColorSpace.
- [ ] Image filters: LZW, CCITT, JPX и JBIG2.
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
- [ ] Масштаб +/−/100%.
- [ ] Настройка шрифта, размера, ширины текста и межстрочного интервала.
- [ ] Полноэкранный режим чтения.
- [ ] Последняя позиция чтения для каждого документа.
- [ ] Недавние документы.
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

## Тестовый корпус

- Ручные regression-файлы: `C:\FILES\PROJECTS\MYBOOK\Books`.
