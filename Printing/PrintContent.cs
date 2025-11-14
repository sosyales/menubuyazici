using System.Collections.Generic;
using System.Drawing;

namespace MenuBuPrinterAgent.Printing;

internal sealed class PrintContent
{
    public List<PrintLine> Lines { get; } = new();
    public Bitmap? QrImage { get; set; }
    public string? Html { get; set; }
    public string PrinterWidth { get; set; } = "58mm";
}

internal sealed class PrintLine
{
    public PrintLineKind Kind { get; init; } = PrintLineKind.Text;
    public string Text { get; init; } = string.Empty;
    public PrintLineStyle Style { get; init; } = PrintLineStyle.Normal;
    public PrintLineAlignment Alignment { get; init; } = PrintLineAlignment.Left;
    public float? CustomSpacing { get; init; }
    public IReadOnlyList<PrintColumn>? Columns { get; init; }
}

internal sealed class PrintColumn
{
    public string Text { get; init; } = string.Empty;
    public PrintLineStyle Style { get; init; } = PrintLineStyle.Normal;
    public PrintLineAlignment Alignment { get; init; } = PrintLineAlignment.Left;
    /// <summary>
    /// Optional width fraction (0-1). When null, columns share remaining width equally.
    /// </summary>
    public float? WidthFraction { get; init; }
}

internal enum PrintLineKind
{
    Text,
    Separator,
    Spacer,
    Columns
}

internal enum PrintLineStyle
{
    Normal,
    Bold,
    Small
}

internal enum PrintLineAlignment
{
    Left,
    Center,
    Right
}
