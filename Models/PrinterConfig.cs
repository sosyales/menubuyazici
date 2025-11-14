namespace MenuBuPrinterAgent.Models;

internal sealed class PrinterConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PrinterType { get; set; } = "all";
    public string PrinterWidth { get; set; } = "58mm";
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
}
