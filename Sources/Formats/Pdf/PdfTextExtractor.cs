using System.Globalization;
using System.IO;
using System.Text;

namespace MYBOOK.Formats.Pdf;

/// <summary>
/// Результат интерпретации PDF content stream с последовательным текстом
/// и позиционированными текстовыми фрагментами в пользовательских координатах PDF.
/// </summary>
internal sealed class PdfTextExtractionResult
{
    public required string Text { get; init; }
    public required IReadOnlyList<PdfTextRun> Runs { get; init; }
    public required IReadOnlyList<PdfImagePlacement> Images { get; init; }
    public required IReadOnlyList<PdfPathPlacement> Paths { get; init; }
}

/// <summary>
/// Представляет завершённый PDF path с fill/stroke параметрами.
/// </summary>
internal sealed class PdfPathPlacement
{
    public required IReadOnlyList<PdfPathCommand> Commands { get; init; }
    public required IReadOnlyList<PdfClipPlacement> Clips { get; init; }
    public required bool Fill { get; init; }
    public required bool Stroke { get; init; }
    public required bool EvenOddFill { get; init; }
    public required string FillColor { get; init; }
    public required string StrokeColor { get; init; }
    public required double StrokeWidth { get; init; }
    public required int LineCap { get; init; }
    public required int LineJoin { get; init; }
    public required double MiterLimit { get; init; }
    public required IReadOnlyList<double> DashArray { get; init; }
    public required double DashPhase { get; init; }
    public required double FillAlpha { get; init; }
    public required double StrokeAlpha { get; init; }
}

internal sealed class PdfClipPlacement
{
    public required IReadOnlyList<PdfPathCommand> Commands { get; init; }
    public required bool EvenOdd { get; init; }
}

internal enum PdfPathCommandKind
{
    Move,
    Line,
    Cubic,
    Close
}

internal sealed class PdfPathCommand
{
    public required PdfPathCommandKind Kind { get; init; }
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public double X3 { get; init; }
    public double Y3 { get; init; }
}

/// <summary>
/// Представляет Image XObject с CTM на момент оператора Do.
/// </summary>
internal sealed class PdfImagePlacement
{
    public required PdfImageResource Resource { get; init; }
    public required IReadOnlyList<PdfClipPlacement> Clips { get; init; }
    public required double A { get; init; }
    public required double B { get; init; }
    public required double C { get; init; }
    public required double D { get; init; }
    public required double E { get; init; }
    public required double F { get; init; }
    public required double Opacity { get; init; }
}

/// <summary>
/// Представляет текстовый фрагмент PDF в координатах страницы до CropBox/Rotate-преобразования.
/// </summary>
internal sealed class PdfTextRun
{
    public required string Text { get; init; }
    public required IReadOnlyList<PdfClipPlacement> Clips { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double FontSize { get; init; }
    public required double Opacity { get; init; }
}

/// <summary>
/// Интерпретирует PDF content stream для текста, Image XObject и vector paths.
/// Поддерживает graphics state `q/Q/cm/gs`, clipping `W/W*`, `Do`, path construction/painting,
/// colors, line width/cap/join/miter/dash, постоянную alpha-прозрачность,
/// text/line matrices, font state и позиционированные Tj/TJ-фрагменты.
/// </summary>
internal static class PdfTextExtractor
{
    /// <summary>
    /// Извлекает последовательный и геометрический текстовый слой content stream.
    /// </summary>
    public static PdfTextExtractionResult Extract(
        byte[] content,
        IReadOnlyDictionary<string, PdfFontResource> fontResources,
        IReadOnlyDictionary<string, PdfImageResource> imageResources,
        IReadOnlyDictionary<string, PdfExtGraphicsState> extGraphicsStates)
    {
        return new ContentParser(
            content,
            fontResources,
            imageResources,
            extGraphicsStates).Extract();
    }

    private sealed class ContentParser
    {
        private readonly byte[] data_;
        private readonly IReadOnlyDictionary<string, PdfFontResource> fontResources_;
        private readonly IReadOnlyDictionary<string, PdfImageResource> imageResources_;
        private readonly IReadOnlyDictionary<string, PdfExtGraphicsState> extGraphicsStates_;
        private readonly List<Operand> operands_ = new();
        private readonly StringBuilder output_ = new();
        private readonly List<PdfTextRun> runs_ = new();
        private readonly List<PdfImagePlacement> images_ = new();
        private readonly List<PdfPathPlacement> paths_ = new();
        private readonly List<PdfPathCommand> currentPath_ = new();
        private readonly List<PdfClipPlacement> activeClips_ = new();
        private readonly Stack<GraphicsState> graphicsStateStack_ = new();

        private int position_;
        private PdfFontResource? currentFontResource_;
        private AffineMatrix currentTransformation_ = AffineMatrix.Identity;
        private TextMatrix textMatrix_ = TextMatrix.Identity;
        private TextMatrix lineMatrix_ = TextMatrix.Identity;
        private double fontSize_ = 12;
        private double leading_;
        private double characterSpacing_;
        private double wordSpacing_;
        private double horizontalScale_ = 1;
        private double textRise_;
        private double lineWidth_ = 1;
        private int lineCap_;
        private int lineJoin_;
        private double miterLimit_ = 10;
        private IReadOnlyList<double> dashArray_ = Array.Empty<double>();
        private double dashPhase_;
        private double strokeAlpha_ = 1;
        private double fillAlpha_ = 1;
        private string strokeColor_ = "#000000";
        private string fillColor_ = "#000000";
        private TextPoint? currentPathPoint_;
        private TextPoint? subpathStart_;
        private bool pendingClip_;
        private bool pendingClipEvenOdd_;

        public ContentParser(
            byte[] data,
            IReadOnlyDictionary<string, PdfFontResource> fontResources,
            IReadOnlyDictionary<string, PdfImageResource> imageResources,
            IReadOnlyDictionary<string, PdfExtGraphicsState> extGraphicsStates)
        {
            data_ = data;
            fontResources_ = fontResources;
            imageResources_ = imageResources;
            extGraphicsStates_ = extGraphicsStates;
        }

        public PdfTextExtractionResult Extract()
        {
            while (true)
            {
                SkipWhiteSpaceAndComments();

                if (position_ >= data_.Length)
                {
                    break;
                }

                var value = data_[position_];

                if (value == (byte)'(')
                {
                    operands_.Add(new StringOperand(ReadLiteralString()));
                    continue;
                }

                if (value == (byte)'<')
                {
                    if (position_ + 1 < data_.Length &&
                        data_[position_ + 1] == (byte)'<')
                    {
                        SkipDictionary();
                    }
                    else
                    {
                        operands_.Add(new StringOperand(ReadHexString()));
                    }

                    continue;
                }

                if (value == (byte)'[')
                {
                    operands_.Add(new ArrayOperand(ReadArray()));
                    continue;
                }

                if (IsNumberStart(value))
                {
                    operands_.Add(new NumberOperand(ReadNumber()));
                    continue;
                }

                if (value == (byte)'/')
                {
                    operands_.Add(new NameOperand(ReadName()));
                    continue;
                }

                ApplyOperator(ReadToken());
            }

            return new PdfTextExtractionResult
            {
                Text = NormalizeOutput(output_.ToString()),
                Runs = runs_.ToArray(),
                Images = images_.ToArray(),
                Paths = paths_.ToArray()
            };
        }

        private void ApplyOperator(string token)
        {
            switch (token)
            {
                case "q":
                    SaveGraphicsState();
                    break;

                case "Q":
                    RestoreGraphicsState();
                    break;

                case "cm":
                    ConcatenateTransformation();
                    break;

                case "Do":
                    DrawXObject();
                    break;

                case "w":
                    lineWidth_ = Math.Abs(GetLastNumber());
                    break;

                case "J":
                    lineCap_ = ReadLineStyleInteger("J");
                    break;

                case "j":
                    lineJoin_ = ReadLineStyleInteger("j");
                    break;

                case "M":
                    miterLimit_ = Math.Max(1, GetLastNumber());
                    break;

                case "d":
                    SetDashPattern();
                    break;

                case "gs":
                    ApplyExtGraphicsState();
                    break;

                case "G":
                    strokeColor_ = GrayColor(GetNumbers());
                    break;

                case "g":
                    fillColor_ = GrayColor(GetNumbers());
                    break;

                case "RG":
                    strokeColor_ = RgbColor(GetNumbers());
                    break;

                case "rg":
                    fillColor_ = RgbColor(GetNumbers());
                    break;

                case "K":
                    strokeColor_ = CmykColor(GetNumbers());
                    break;

                case "k":
                    fillColor_ = CmykColor(GetNumbers());
                    break;

                case "m":
                    MovePath();
                    break;

                case "l":
                    LinePath();
                    break;

                case "c":
                    CubicPath();
                    break;

                case "v":
                    CubicPathV();
                    break;

                case "y":
                    CubicPathY();
                    break;

                case "h":
                    ClosePath();
                    break;

                case "re":
                    RectanglePath();
                    break;

                case "W":
                    pendingClip_ = true;
                    pendingClipEvenOdd_ = false;
                    break;

                case "W*":
                    pendingClip_ = true;
                    pendingClipEvenOdd_ = true;
                    break;

                case "S":
                    PaintPath(fill: false, stroke: true, evenOdd: false, close: false);
                    break;

                case "s":
                    PaintPath(fill: false, stroke: true, evenOdd: false, close: true);
                    break;

                case "f":
                case "F":
                    PaintPath(fill: true, stroke: false, evenOdd: false, close: false);
                    break;

                case "f*":
                    PaintPath(fill: true, stroke: false, evenOdd: true, close: false);
                    break;

                case "B":
                    PaintPath(fill: true, stroke: true, evenOdd: false, close: false);
                    break;

                case "B*":
                    PaintPath(fill: true, stroke: true, evenOdd: true, close: false);
                    break;

                case "b":
                    PaintPath(fill: true, stroke: true, evenOdd: false, close: true);
                    break;

                case "b*":
                    PaintPath(fill: true, stroke: true, evenOdd: true, close: true);
                    break;

                case "n":
                    ApplyPendingClip();
                    ClearPath();
                    break;

                case "BT":
                    BeginTextObject();
                    break;
                case "ET":
                    break;
                case "Tf":
                    SelectFont();
                    break;
                case "Tm":
                    SetTextMatrix();
                    break;
                case "Td":
                    MoveTextPosition(false);
                    break;
                case "TD":
                    MoveTextPosition(true);
                    break;
                case "T*":
                    MoveToNextLine();
                    break;
                case "TL":
                    leading_ = GetLastNumber();
                    break;
                case "Tc":
                    characterSpacing_ = GetLastNumber();
                    break;
                case "Tw":
                    wordSpacing_ = GetLastNumber();
                    break;
                case "Tz":
                    horizontalScale_ = GetLastNumber() / 100.0;
                    break;
                case "Ts":
                    textRise_ = GetLastNumber();
                    break;
                case "Tj":
                    ShowLastString();
                    break;
                case "TJ":
                    ShowLastArray();
                    break;
                case "'":
                    MoveToNextLine();
                    ShowLastString();
                    break;
                case "\"":
                    ApplyDoubleQuote();
                    break;
            }

            operands_.Clear();
        }

        private int ReadLineStyleInteger(string operatorName)
        {
            var value = GetLastNumber();

            if (value != Math.Truncate(value) ||
                value < 0 ||
                value > 2)
            {
                throw new InvalidDataException(
                    $"PDF operator {operatorName} ожидает целое значение 0..2.");
            }

            return (int)value;
        }

        private void SetDashPattern()
        {
            var array = operands_
                .OfType<ArrayOperand>()
                .LastOrDefault();

            var phase = operands_
                .OfType<NumberOperand>()
                .LastOrDefault();

            if (array == null || phase == null)
            {
                throw new InvalidDataException(
                    "PDF d ожидает массив dash pattern и phase.");
            }

            var values = array.Items
                .OfType<NumberOperand>()
                .Select(item => item.Value)
                .ToArray();

            if (values.Length != array.Items.Count ||
                values.Any(value => value < 0))
            {
                throw new InvalidDataException(
                    "PDF dash pattern должен содержать только неотрицательные числа.");
            }

            dashArray_ = values;
            dashPhase_ = phase.Value;
        }

        private void ApplyExtGraphicsState()
        {
            var name = operands_
                .OfType<NameOperand>()
                .LastOrDefault();

            if (name == null ||
                !extGraphicsStates_.TryGetValue(
                    name.Name,
                    out var state))
            {
                return;
            }

            if (state.LineWidth is { } lineWidth)
            {
                lineWidth_ = Math.Abs(lineWidth);
            }

            if (state.LineCap is { } lineCap)
            {
                lineCap_ = lineCap;
            }

            if (state.LineJoin is { } lineJoin)
            {
                lineJoin_ = lineJoin;
            }

            if (state.MiterLimit is { } miterLimit)
            {
                miterLimit_ = Math.Max(1, miterLimit);
            }

            if (state.DashArray != null)
            {
                dashArray_ = state.DashArray.ToArray();
                dashPhase_ = state.DashPhase ?? 0;
            }

            if (state.StrokeAlpha is { } strokeAlpha)
            {
                strokeAlpha_ = Math.Clamp(strokeAlpha, 0, 1);
            }

            if (state.FillAlpha is { } fillAlpha)
            {
                fillAlpha_ = Math.Clamp(fillAlpha, 0, 1);
            }
        }

        private void MovePath()
        {
            var numbers = GetNumbers();

            if (numbers.Length < 2)
            {
                throw new InvalidDataException(
                    "PDF m должен содержать два числа.");
            }

            var point = currentTransformation_.Transform(
                numbers[^2],
                numbers[^1]);

            currentPath_.Add(new PdfPathCommand
            {
                Kind = PdfPathCommandKind.Move,
                X1 = point.X,
                Y1 = point.Y
            });

            currentPathPoint_ = point;
            subpathStart_ = point;
        }

        private void LinePath()
        {
            var numbers = GetNumbers();

            if (numbers.Length < 2)
            {
                throw new InvalidDataException(
                    "PDF l должен содержать два числа.");
            }

            var point = currentTransformation_.Transform(
                numbers[^2],
                numbers[^1]);

            currentPath_.Add(new PdfPathCommand
            {
                Kind = PdfPathCommandKind.Line,
                X1 = point.X,
                Y1 = point.Y
            });

            currentPathPoint_ = point;
        }

        private void CubicPath()
        {
            var numbers = GetNumbers();

            if (numbers.Length != 6)
            {
                throw new InvalidDataException(
                    "PDF c должен содержать шесть чисел.");
            }

            var p1 = currentTransformation_.Transform(
                numbers[0],
                numbers[1]);

            var p2 = currentTransformation_.Transform(
                numbers[2],
                numbers[3]);

            var p3 = currentTransformation_.Transform(
                numbers[4],
                numbers[5]);

            currentPath_.Add(new PdfPathCommand
            {
                Kind = PdfPathCommandKind.Cubic,
                X1 = p1.X,
                Y1 = p1.Y,
                X2 = p2.X,
                Y2 = p2.Y,
                X3 = p3.X,
                Y3 = p3.Y
            });

            currentPathPoint_ = p3;
        }

        private void CubicPathV()
        {
            if (currentPathPoint_ == null)
            {
                throw new InvalidDataException(
                    "PDF v используется без current point.");
            }

            var numbers = GetNumbers();

            if (numbers.Length != 4)
            {
                throw new InvalidDataException(
                    "PDF v должен содержать четыре числа.");
            }

            var p1 = currentPathPoint_.Value;
            var p2 = currentTransformation_.Transform(
                numbers[0],
                numbers[1]);
            var p3 = currentTransformation_.Transform(
                numbers[2],
                numbers[3]);

            currentPath_.Add(new PdfPathCommand
            {
                Kind = PdfPathCommandKind.Cubic,
                X1 = p1.X,
                Y1 = p1.Y,
                X2 = p2.X,
                Y2 = p2.Y,
                X3 = p3.X,
                Y3 = p3.Y
            });

            currentPathPoint_ = p3;
        }

        private void CubicPathY()
        {
            var numbers = GetNumbers();

            if (numbers.Length != 4)
            {
                throw new InvalidDataException(
                    "PDF y должен содержать четыре числа.");
            }

            var p1 = currentTransformation_.Transform(
                numbers[0],
                numbers[1]);
            var p3 = currentTransformation_.Transform(
                numbers[2],
                numbers[3]);

            currentPath_.Add(new PdfPathCommand
            {
                Kind = PdfPathCommandKind.Cubic,
                X1 = p1.X,
                Y1 = p1.Y,
                X2 = p3.X,
                Y2 = p3.Y,
                X3 = p3.X,
                Y3 = p3.Y
            });

            currentPathPoint_ = p3;
        }

        private void ClosePath()
        {
            if (currentPath_.Count == 0)
            {
                return;
            }

            currentPath_.Add(new PdfPathCommand
            {
                Kind = PdfPathCommandKind.Close
            });

            currentPathPoint_ = subpathStart_;
        }

        private void RectanglePath()
        {
            var numbers = GetNumbers();

            if (numbers.Length != 4)
            {
                throw new InvalidDataException(
                    "PDF re должен содержать четыре числа.");
            }

            var x = numbers[0];
            var y = numbers[1];
            var width = numbers[2];
            var height = numbers[3];

            var p0 = currentTransformation_.Transform(x, y);
            var p1 = currentTransformation_.Transform(
                x + width,
                y);
            var p2 = currentTransformation_.Transform(
                x + width,
                y + height);
            var p3 = currentTransformation_.Transform(
                x,
                y + height);

            currentPath_.Add(new PdfPathCommand
            {
                Kind = PdfPathCommandKind.Move,
                X1 = p0.X,
                Y1 = p0.Y
            });

            currentPath_.Add(new PdfPathCommand
            {
                Kind = PdfPathCommandKind.Line,
                X1 = p1.X,
                Y1 = p1.Y
            });

            currentPath_.Add(new PdfPathCommand
            {
                Kind = PdfPathCommandKind.Line,
                X1 = p2.X,
                Y1 = p2.Y
            });

            currentPath_.Add(new PdfPathCommand
            {
                Kind = PdfPathCommandKind.Line,
                X1 = p3.X,
                Y1 = p3.Y
            });

            currentPath_.Add(new PdfPathCommand
            {
                Kind = PdfPathCommandKind.Close
            });

            currentPathPoint_ = p0;
            subpathStart_ = p0;
        }

        private void PaintPath(
            bool fill,
            bool stroke,
            bool evenOdd,
            bool close)
        {
            if (close)
            {
                ClosePath();
            }

            if (currentPath_.Count > 0 &&
                (fill || stroke))
            {
                paths_.Add(new PdfPathPlacement
                {
                    Commands = currentPath_.ToArray(),
                    Clips = activeClips_.ToArray(),
                    Fill = fill,
                    Stroke = stroke,
                    EvenOddFill = evenOdd,
                    FillColor = fillColor_,
                    StrokeColor = strokeColor_,
                    StrokeWidth = Math.Max(
                        0,
                        lineWidth_ *
                        currentTransformation_.ApproximateScale()),
                    LineCap = lineCap_,
                    LineJoin = lineJoin_,
                    MiterLimit = miterLimit_,
                    DashArray = dashArray_
                        .Select(value =>
                            value *
                            currentTransformation_.ApproximateScale())
                        .ToArray(),
                    DashPhase =
                        dashPhase_ *
                        currentTransformation_.ApproximateScale(),
                    FillAlpha = fillAlpha_,
                    StrokeAlpha = strokeAlpha_
                });
            }

            ApplyPendingClip();
            ClearPath();
        }

        private void ApplyPendingClip()
        {
            if (!pendingClip_)
            {
                return;
            }

            if (currentPath_.Count == 0)
            {
                throw new InvalidDataException(
                    "PDF W/W* используется без текущего path.");
            }

            activeClips_.Add(new PdfClipPlacement
            {
                Commands = currentPath_.ToArray(),
                EvenOdd = pendingClipEvenOdd_
            });

            pendingClip_ = false;
            pendingClipEvenOdd_ = false;
        }

        private void ClearPath()
        {
            currentPath_.Clear();
            currentPathPoint_ = null;
            subpathStart_ = null;
        }

        private static string GrayColor(double[] values)
        {
            if (values.Length < 1)
            {
                throw new InvalidDataException(
                    "PDF gray color operator ожидает один компонент.");
            }

            var value = ToByte(values[^1]);
            return $"#{value:X2}{value:X2}{value:X2}";
        }

        private static string RgbColor(double[] values)
        {
            if (values.Length < 3)
            {
                throw new InvalidDataException(
                    "PDF RGB color operator ожидает три компонента.");
            }

            var r = ToByte(values[^3]);
            var g = ToByte(values[^2]);
            var b = ToByte(values[^1]);

            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private static string CmykColor(double[] values)
        {
            if (values.Length < 4)
            {
                throw new InvalidDataException(
                    "PDF CMYK color operator ожидает четыре компонента.");
            }

            var c = Clamp01(values[^4]);
            var m = Clamp01(values[^3]);
            var y = Clamp01(values[^2]);
            var k = Clamp01(values[^1]);

            var r = ToByte(
                (1 - c) * (1 - k));
            var g = ToByte(
                (1 - m) * (1 - k));
            var b = ToByte(
                (1 - y) * (1 - k));

            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private static byte ToByte(double value)
        {
            return (byte)Math.Round(
                Clamp01(value) * 255);
        }

        private static double Clamp01(double value)
        {
            return Math.Clamp(
                value,
                0,
                1);
        }

        private void SaveGraphicsState()
        {
            graphicsStateStack_.Push(new GraphicsState(
                currentTransformation_,
                currentFontResource_,
                fontSize_,
                leading_,
                characterSpacing_,
                wordSpacing_,
                horizontalScale_,
                textRise_,
                lineWidth_,
                lineCap_,
                lineJoin_,
                miterLimit_,
                dashArray_.ToArray(),
                dashPhase_,
                strokeAlpha_,
                fillAlpha_,
                strokeColor_,
                fillColor_,
                activeClips_.ToArray()));
        }

        private void RestoreGraphicsState()
        {
            if (graphicsStateStack_.Count == 0)
            {
                throw new InvalidDataException(
                    "PDF operator Q не имеет соответствующего q.");
            }

            var state = graphicsStateStack_.Pop();

            currentTransformation_ = state.Transformation;
            currentFontResource_ = state.FontResource;
            fontSize_ = state.FontSize;
            leading_ = state.Leading;
            characterSpacing_ = state.CharacterSpacing;
            wordSpacing_ = state.WordSpacing;
            horizontalScale_ = state.HorizontalScale;
            textRise_ = state.TextRise;
            lineWidth_ = state.LineWidth;
            lineCap_ = state.LineCap;
            lineJoin_ = state.LineJoin;
            miterLimit_ = state.MiterLimit;
            dashArray_ = state.DashArray.ToArray();
            dashPhase_ = state.DashPhase;
            strokeAlpha_ = state.StrokeAlpha;
            fillAlpha_ = state.FillAlpha;
            strokeColor_ = state.StrokeColor;
            fillColor_ = state.FillColor;
            activeClips_.Clear();
            activeClips_.AddRange(state.Clips);
        }

        private void ConcatenateTransformation()
        {
            var numbers = GetNumbers();

            if (numbers.Length != 6)
            {
                throw new InvalidDataException(
                    "PDF cm должен содержать шесть чисел.");
            }

            var matrix = new AffineMatrix(
                numbers[0],
                numbers[1],
                numbers[2],
                numbers[3],
                numbers[4],
                numbers[5]);

            currentTransformation_ =
                currentTransformation_.Multiply(matrix);
        }

        private void DrawXObject()
        {
            var name = operands_
                .OfType<NameOperand>()
                .LastOrDefault();

            if (name == null ||
                !imageResources_.TryGetValue(
                    name.Name,
                    out var image))
            {
                return;
            }

            images_.Add(new PdfImagePlacement
            {
                Resource = image,
                Clips = activeClips_.ToArray(),
                A = currentTransformation_.A,
                B = currentTransformation_.B,
                C = currentTransformation_.C,
                D = currentTransformation_.D,
                E = currentTransformation_.E,
                F = currentTransformation_.F,
                Opacity = fillAlpha_
            });
        }

        private void BeginTextObject()
        {
            if (output_.Length > 0 && output_[^1] != '\n')
            {
                AppendLineBreak();
            }

            textMatrix_ = TextMatrix.Identity;
            lineMatrix_ = TextMatrix.Identity;
        }

        private void SelectFont()
        {
            var font = operands_.OfType<NameOperand>().LastOrDefault();
            var size = operands_.OfType<NumberOperand>().LastOrDefault();

            if (size != null)
            {
                fontSize_ = Math.Abs(size.Value);
            }

            currentFontResource_ =
                font != null &&
                fontResources_.TryGetValue(font.Name, out var resource)
                    ? resource
                    : null;
        }

        private void SetTextMatrix()
        {
            var numbers = GetNumbers();

            if (numbers.Length != 6)
            {
                throw new InvalidDataException(
                    "PDF Tm должен содержать шесть чисел.");
            }

            textMatrix_ = new TextMatrix(
                numbers[0], numbers[1], numbers[2],
                numbers[3], numbers[4], numbers[5]);
            lineMatrix_ = textMatrix_;
        }

        private void MoveTextPosition(bool setLeading)
        {
            var numbers = GetNumbers();

            if (numbers.Length < 2)
            {
                throw new InvalidDataException(
                    "PDF Td/TD должен содержать два числа.");
            }

            var tx = numbers[^2];
            var ty = numbers[^1];

            if (setLeading)
            {
                leading_ = -ty;
            }

            lineMatrix_ = lineMatrix_.Translate(tx, ty);
            textMatrix_ = lineMatrix_;
            AppendLineBreak();
        }

        private void MoveToNextLine()
        {
            lineMatrix_ = lineMatrix_.Translate(0, -leading_);
            textMatrix_ = lineMatrix_;
            AppendLineBreak();
        }

        private void ApplyDoubleQuote()
        {
            var numbers = operands_
                .OfType<NumberOperand>()
                .Select(item => item.Value)
                .ToArray();

            if (numbers.Length >= 2)
            {
                wordSpacing_ = numbers[0];
                characterSpacing_ = numbers[1];
            }

            MoveToNextLine();
            ShowLastString();
        }

        private void ShowLastString()
        {
            if (operands_.LastOrDefault() is StringOperand value)
            {
                ShowText(value.Data);
            }
        }

        private void ShowLastArray()
        {
            if (operands_.LastOrDefault() is not ArrayOperand value)
            {
                return;
            }

            foreach (var item in value.Items)
            {
                switch (item)
                {
                    case StringOperand text:
                        ShowText(text.Data);
                        break;

                    case NumberOperand number:
                        AdvanceText(
                            -number.Value / 1000.0 *
                            fontSize_ *
                            horizontalScale_);

                        if (number.Value < -180)
                        {
                            output_.Append(' ');
                        }
                        break;
                }
            }
        }

        private void ShowText(byte[] data)
        {
            var text = DecodePdfString(data);

            if (text.Length == 0)
            {
                return;
            }

            var localOrigin = textMatrix_.Transform(0, textRise_);
            var localTop = textMatrix_.Transform(0, textRise_ + 1);

            var origin = currentTransformation_.Transform(
                localOrigin.X,
                localOrigin.Y);

            var top = currentTransformation_.Transform(
                localTop.X,
                localTop.Y);

            var matrixScale = Math.Sqrt(
                Math.Pow(top.X - origin.X, 2) +
                Math.Pow(top.Y - origin.Y, 2));

            if (matrixScale <= 0)
            {
                matrixScale = 1;
            }

            runs_.Add(new PdfTextRun
            {
                Text = text,
                Clips = activeClips_.ToArray(),
                X = origin.X,
                Y = origin.Y,
                FontSize = Math.Max(0.1, fontSize_ * matrixScale),
                Opacity = fillAlpha_
            });

            output_.Append(text);

            var advance = currentFontResource_?.HasWidthMetrics == true
                ? currentFontResource_.MeasureAdvance(
                    data,
                    fontSize_,
                    characterSpacing_,
                    wordSpacing_,
                    horizontalScale_)
                : EstimateAdvance(text);

            AdvanceText(advance);
        }

        private double EstimateAdvance(string text)
        {
            var runeCount = 0;
            var spaces = 0;

            foreach (var rune in text.EnumerateRunes())
            {
                runeCount++;

                if (Rune.IsWhiteSpace(rune))
                {
                    spaces++;
                }
            }

            if (runeCount == 0)
            {
                return 0;
            }

            var glyphAdvance = runeCount * fontSize_ * 0.5;
            var characterAdvance =
                Math.Max(0, runeCount - 1) * characterSpacing_;
            var wordAdvance = spaces * wordSpacing_;

            return (glyphAdvance + characterAdvance + wordAdvance) *
                   horizontalScale_;
        }

        private void AdvanceText(double distance)
        {
            textMatrix_ = textMatrix_.Translate(distance, 0);
        }

        private double GetLastNumber()
        {
            return operands_.OfType<NumberOperand>().LastOrDefault()?.Value
                   ?? throw new InvalidDataException(
                       "PDF text operator ожидает числовой операнд.");
        }

        private double[] GetNumbers()
        {
            return operands_
                .OfType<NumberOperand>()
                .Select(item => item.Value)
                .ToArray();
        }

        private void AppendLineBreak()
        {
            if (output_.Length == 0 || output_[^1] == '\n')
            {
                return;
            }

            output_.AppendLine();
        }

        private List<Operand> ReadArray()
        {
            position_++;
            var result = new List<Operand>();

            while (true)
            {
                SkipWhiteSpaceAndComments();

                if (position_ >= data_.Length)
                {
                    throw new InvalidDataException(
                        "Незакрытый массив в PDF content stream.");
                }

                var value = data_[position_];

                if (value == (byte)']')
                {
                    position_++;
                    return result;
                }

                if (value == (byte)'(')
                {
                    result.Add(new StringOperand(ReadLiteralString()));
                    continue;
                }

                if (value == (byte)'<')
                {
                    result.Add(new StringOperand(ReadHexString()));
                    continue;
                }

                if (IsNumberStart(value))
                {
                    result.Add(new NumberOperand(ReadNumber()));
                    continue;
                }

                ReadToken();
                result.Add(new OtherOperand());
            }
        }

        private byte[] ReadLiteralString()
        {
            position_++;
            var result = new List<byte>();
            var depth = 1;

            while (position_ < data_.Length)
            {
                var value = data_[position_++];

                if (value == (byte)'\\')
                {
                    if (position_ >= data_.Length)
                    {
                        break;
                    }

                    var escaped = data_[position_++];

                    switch (escaped)
                    {
                        case (byte)'n': result.Add((byte)'\n'); break;
                        case (byte)'r': result.Add((byte)'\r'); break;
                        case (byte)'t': result.Add((byte)'\t'); break;
                        case (byte)'b': result.Add((byte)'\b'); break;
                        case (byte)'f': result.Add((byte)'\f'); break;
                        case (byte)'(':
                        case (byte)')':
                        case (byte)'\\':
                            result.Add(escaped);
                            break;
                        case (byte)'\r':
                            if (position_ < data_.Length &&
                                data_[position_] == (byte)'\n')
                            {
                                position_++;
                            }
                            break;
                        case (byte)'\n':
                            break;
                        default:
                            if (escaped is >= (byte)'0' and <= (byte)'7')
                            {
                                var octal = escaped - (byte)'0';
                                var count = 1;

                                while (count < 3 &&
                                       position_ < data_.Length &&
                                       data_[position_] is >= (byte)'0' and <= (byte)'7')
                                {
                                    octal =
                                        octal * 8 +
                                        data_[position_] -
                                        (byte)'0';
                                    position_++;
                                    count++;
                                }

                                result.Add((byte)octal);
                            }
                            else
                            {
                                result.Add(escaped);
                            }
                            break;
                    }

                    continue;
                }

                if (value == (byte)'(')
                {
                    depth++;
                    result.Add(value);
                    continue;
                }

                if (value == (byte)')')
                {
                    depth--;

                    if (depth == 0)
                    {
                        return result.ToArray();
                    }

                    result.Add(value);
                    continue;
                }

                result.Add(value);
            }

            throw new InvalidDataException(
                "Незакрытая PDF string в content stream.");
        }

        private byte[] ReadHexString()
        {
            position_++;
            var nibbles = new List<int>();

            while (position_ < data_.Length)
            {
                var value = data_[position_++];

                if (value == (byte)'>')
                {
                    if ((nibbles.Count & 1) != 0)
                    {
                        nibbles.Add(0);
                    }

                    var result = new byte[nibbles.Count / 2];

                    for (var index = 0; index < result.Length; index++)
                    {
                        result[index] =
                            (byte)((nibbles[index * 2] << 4) |
                                   nibbles[index * 2 + 1]);
                    }

                    return result;
                }

                if (IsWhiteSpace(value))
                {
                    continue;
                }

                nibbles.Add(ReadHexNibble(value));
            }

            throw new InvalidDataException(
                "Незакрытая PDF hex string в content stream.");
        }

        private double ReadNumber()
        {
            var start = position_;

            if (data_[position_] is (byte)'+' or (byte)'-')
            {
                position_++;
            }

            while (position_ < data_.Length &&
                   (data_[position_] is >= (byte)'0' and <= (byte)'9' ||
                    data_[position_] == (byte)'.'))
            {
                position_++;
            }

            var token = Encoding.ASCII.GetString(
                data_,
                start,
                position_ - start);

            return double.Parse(
                token,
                NumberStyles.Float,
                CultureInfo.InvariantCulture);
        }

        private string ReadName()
        {
            position_++;
            var start = position_;

            while (position_ < data_.Length &&
                   !IsWhiteSpace(data_[position_]) &&
                   !IsDelimiter(data_[position_]))
            {
                position_++;
            }

            return Encoding.ASCII.GetString(
                data_,
                start,
                position_ - start);
        }

        private string ReadToken()
        {
            SkipWhiteSpaceAndComments();
            var start = position_;

            if (data_[position_] is (byte)'\'' or (byte)'"')
            {
                position_++;
                return ((char)data_[start]).ToString();
            }

            while (position_ < data_.Length &&
                   !IsWhiteSpace(data_[position_]) &&
                   !IsDelimiter(data_[position_]))
            {
                position_++;
            }

            if (position_ == start)
            {
                position_++;
                return ((char)data_[start]).ToString();
            }

            return Encoding.ASCII.GetString(
                data_,
                start,
                position_ - start);
        }

        private void SkipDictionary()
        {
            position_ += 2;
            var depth = 1;

            while (position_ < data_.Length && depth > 0)
            {
                if (position_ + 1 < data_.Length &&
                    data_[position_] == (byte)'<' &&
                    data_[position_ + 1] == (byte)'<')
                {
                    depth++;
                    position_ += 2;
                    continue;
                }

                if (position_ + 1 < data_.Length &&
                    data_[position_] == (byte)'>' &&
                    data_[position_ + 1] == (byte)'>')
                {
                    depth--;
                    position_ += 2;
                    continue;
                }

                position_++;
            }
        }

        private void SkipWhiteSpaceAndComments()
        {
            while (position_ < data_.Length)
            {
                if (IsWhiteSpace(data_[position_]))
                {
                    position_++;
                    continue;
                }

                if (data_[position_] == (byte)'%')
                {
                    while (position_ < data_.Length &&
                           data_[position_] is not (byte)'\r' and not (byte)'\n')
                    {
                        position_++;
                    }

                    continue;
                }

                break;
            }
        }

        private string DecodePdfString(byte[] data)
        {
            return currentFontResource_?.Decode(data)
                   ?? PdfFontDecoder.DecodeFallback(data);
        }

        private static string NormalizeOutput(string value)
        {
            var lines = value
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();

            return string.Join(Environment.NewLine, lines);
        }

        private static bool IsNumberStart(byte value)
        {
            return value is >= (byte)'0' and <= (byte)'9' or
                   (byte)'+' or (byte)'-' or (byte)'.';
        }

        private static bool IsWhiteSpace(byte value)
        {
            return value is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;
        }

        private static bool IsDelimiter(byte value)
        {
            return value is (byte)'(' or (byte)')' or
                   (byte)'<' or (byte)'>' or
                   (byte)'[' or (byte)']' or
                   (byte)'{' or (byte)'}' or
                   (byte)'/' or (byte)'%';
        }

        private static int ReadHexNibble(byte value)
        {
            if (value is >= (byte)'0' and <= (byte)'9')
            {
                return value - (byte)'0';
            }

            if (value is >= (byte)'A' and <= (byte)'F')
            {
                return value - (byte)'A' + 10;
            }

            if (value is >= (byte)'a' and <= (byte)'f')
            {
                return value - (byte)'a' + 10;
            }

            throw new InvalidDataException(
                "Некорректная hex-цифра в PDF.");
        }

        private abstract record Operand;
        private sealed record StringOperand(byte[] Data) : Operand;
        private sealed record ArrayOperand(IReadOnlyList<Operand> Items) : Operand;
        private sealed record NumberOperand(double Value) : Operand;
        private sealed record NameOperand(string Name) : Operand;
        private sealed record OtherOperand : Operand;
    }

    private readonly record struct GraphicsState(
        AffineMatrix Transformation,
        PdfFontResource? FontResource,
        double FontSize,
        double Leading,
        double CharacterSpacing,
        double WordSpacing,
        double HorizontalScale,
        double TextRise,
        double LineWidth,
        int LineCap,
        int LineJoin,
        double MiterLimit,
        IReadOnlyList<double> DashArray,
        double DashPhase,
        double StrokeAlpha,
        double FillAlpha,
        string StrokeColor,
        string FillColor,
        IReadOnlyList<PdfClipPlacement> Clips);

    private readonly record struct AffineMatrix(
        double A,
        double B,
        double C,
        double D,
        double E,
        double F)
    {
        public static AffineMatrix Identity =>
            new(1, 0, 0, 1, 0, 0);

        public AffineMatrix Multiply(AffineMatrix other)
        {
            return new AffineMatrix(
                A * other.A + C * other.B,
                B * other.A + D * other.B,
                A * other.C + C * other.D,
                B * other.C + D * other.D,
                A * other.E + C * other.F + E,
                B * other.E + D * other.F + F);
        }

        public TextPoint Transform(double x, double y)
        {
            return new TextPoint(
                A * x + C * y + E,
                B * x + D * y + F);
        }

        public double ApproximateScale()
        {
            var determinant = A * D - B * C;
            return Math.Sqrt(Math.Abs(determinant));
        }
    }

    private readonly record struct TextPoint(double X, double Y);

    private readonly record struct TextMatrix(
        double A,
        double B,
        double C,
        double D,
        double E,
        double F)
    {
        public static TextMatrix Identity =>
            new(1, 0, 0, 1, 0, 0);

        public TextMatrix Translate(double x, double y)
        {
            return new TextMatrix(
                A,
                B,
                C,
                D,
                A * x + C * y + E,
                B * x + D * y + F);
        }

        public TextPoint Transform(double x, double y)
        {
            return new TextPoint(
                A * x + C * y + E,
                B * x + D * y + F);
        }
    }
}
