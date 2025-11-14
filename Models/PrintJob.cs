using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace MenuBuPrinterAgent.Models;

internal sealed class PrintJob
{
    public int Id { get; init; }
    public string JobType { get; init; } = string.Empty;
    public string JobKind { get; init; } = string.Empty;
    public int PayloadVersion { get; init; }
    public JsonObject Payload { get; init; } = new();
    public JsonObject Options { get; init; } = new();
    public JsonObject Metadata { get; init; } = new();
    public IReadOnlyList<string> PrinterTags { get; init; } = Array.Empty<string>();
    public DateTime CreatedAt { get; init; }
}
