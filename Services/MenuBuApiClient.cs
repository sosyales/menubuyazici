using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MenuBuPrinterAgent.Models;

namespace MenuBuPrinterAgent.Services;

internal sealed class MenuBuApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _email;
    private readonly string _password;
    private string? _apiKey;
    private static readonly string _agentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    public int BusinessId { get; private set; }
    public string BusinessName { get; private set; } = "MenuBu";
    public string PrinterWidth { get; private set; } = "58mm";
    public IReadOnlyList<PrinterConfig> PrinterConfigs { get; private set; } = Array.Empty<PrinterConfig>();
    internal string Email => _email;
    internal string Password => _password;
    public string? ApiKey => _apiKey;
    internal static string AgentVersion => _agentVersion;

    public MenuBuApiClient(string email, string password, HttpMessageHandler? handler = null)
    {
        _email = email;
        _password = password;
        _httpClient = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MenuBu-Printer-Agent/2.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<IReadOnlyList<PrintJob>> AuthenticateAndFetchInitialJobsAsync(CancellationToken cancellationToken)
    {
        var requestUri = new UriBuilder("https://menubu.com.tr/api/print-jobs.php");
        requestUri.Query = BuildAuthQuery();

        using var response = await _httpClient.GetAsync(requestUri.Uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("success", out var successEl) || !successEl.GetBoolean())
        {
            throw new InvalidOperationException("Giriş başarısız.");
        }

        BusinessId = doc.RootElement.GetProperty("business_id").GetInt32();
        BusinessName = doc.RootElement.GetProperty("business_name").GetString() ?? "MenuBu";
        PrinterWidth = doc.RootElement.TryGetProperty("printer_width", out var pw) ? pw.GetString() ?? "58mm" : "58mm";

        await EnsureApiKeyAsync(cancellationToken);
        await LoadPrinterConfigsAsync(cancellationToken);

        var jobs = new List<PrintJob>();
        if (doc.RootElement.TryGetProperty("jobs", out var jobsElement) && jobsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var jobEl in jobsElement.EnumerateArray())
            {
                jobs.Add(ParseJob(jobEl));
            }
        }
        return jobs;
    }

    public async Task<IReadOnlyList<PrintJob>> GetPendingJobsAsync(CancellationToken cancellationToken)
    {
        var requestUri = new UriBuilder("https://menubu.com.tr/api/print-jobs.php");
        requestUri.Query = BuildAuthQuery();

        using var response = await _httpClient.GetAsync(requestUri.Uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("success", out var successEl) || !successEl.GetBoolean())
        {
            throw new InvalidOperationException("Sunucu hatası: cevap geçersiz.");
        }

        var jobs = new List<PrintJob>();
        if (doc.RootElement.TryGetProperty("jobs", out var jobsElement) && jobsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var jobEl in jobsElement.EnumerateArray())
            {
                jobs.Add(ParseJob(jobEl));
            }
        }
        return jobs;
    }

    public async Task UpdateJobStatusAsync(int jobId, string status, string? error = null, CancellationToken cancellationToken = default)
    {
        await EnsureApiKeyAsync(cancellationToken);

        var requestUri = new UriBuilder($"https://menubu.com.tr/api/print-jobs.php");
        requestUri.Query = $"id={jobId}";

        var payload = new Dictionary<string, string?>
        {
            ["key"] = _apiKey,
            ["status"] = status,
            ["error_message"] = error
        };

        using var response = await _httpClient.PostAsync(requestUri.Uri,
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<int> ClearPendingJobsAsync(CancellationToken cancellationToken)
    {
        await EnsureApiKeyAsync(cancellationToken);

        if (BusinessId == 0)
        {
            throw new InvalidOperationException("İşletme kimliği bulunamadı. Önce giriş yapın.");
        }

        var requestUri = new UriBuilder("https://menubu.com.tr/api/print-jobs.php")
        {
            Query = $"action=clear&business_id={BusinessId}"
        };

        var payload = new Dictionary<string, string?>
        {
            ["key"] = _apiKey
        };

        using var response = await _httpClient.PostAsync(requestUri.Uri,
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("success", out var successEl) || !successEl.GetBoolean())
        {
            var message = doc.RootElement.TryGetProperty("message", out var messageEl) ? messageEl.GetString() : null;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "Kuyruk temizlenemedi." : message);
        }

        return doc.RootElement.TryGetProperty("cleared", out var clearedEl) ? clearedEl.GetInt32() : 0;
    }

    private async Task EnsureApiKeyAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_apiKey))
        {
            return;
        }

        var requestUri = new UriBuilder("https://menubu.com.tr/api/print-auth.php");
        requestUri.Query = $"email={Uri.EscapeDataString(_email)}&password={Uri.EscapeDataString(_password)}";

        using var response = await _httpClient.GetAsync(requestUri.Uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _apiKey = _email;
            return;
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        _apiKey = doc.RootElement.TryGetProperty("api_key", out var keyEl)
            ? keyEl.GetString()
            : _email;
    }

    public async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        if (BusinessId <= 0)
        {
            return;
        }

        if (string.IsNullOrEmpty(_apiKey))
        {
            await EnsureApiKeyAsync(cancellationToken);
        }

        if (string.IsNullOrEmpty(_apiKey))
        {
            return;
        }

        var payload = new
        {
            business_id = BusinessId,
            key = _apiKey,
            agent_version = AgentVersion
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var endpoint = new Uri("https://menubu.com.tr/api/printer-agent-heartbeat.php");
        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    internal static PrintJob ParseJob(JsonElement jobEl)
    {
        var created = jobEl.TryGetProperty("created_at", out var createdEl) && DateTime.TryParse(createdEl.GetString(), out var parsed)
            ? parsed
            : DateTime.UtcNow;

        var jobType = jobEl.TryGetProperty("job_type", out var jobTypeEl) ? jobTypeEl.GetString() ?? "receipt" : "receipt";
        var jobKind = jobEl.TryGetProperty("job_kind", out var jobKindEl) ? jobKindEl.GetString() ?? jobType : jobType;

        var payloadVersion = 1;
        if (jobEl.TryGetProperty("payload_version", out var payloadVersionEl))
        {
            if (payloadVersionEl.ValueKind == JsonValueKind.Number && payloadVersionEl.TryGetInt32(out var numericVersion))
            {
                payloadVersion = numericVersion;
            }
            else if (payloadVersionEl.ValueKind == JsonValueKind.String && int.TryParse(payloadVersionEl.GetString(), out var parsedVersion))
            {
                payloadVersion = parsedVersion;
            }
        }

        var payloadObject = ParsePayload(jobEl);
        var optionsObject = ParseOptions(jobEl);
        var metadataObject = ParseMetadata(jobEl);
        var printerTags = ParsePrinterTags(jobEl);

        return new PrintJob
        {
            Id = jobEl.GetProperty("id").GetInt32(),
            JobType = jobType,
            JobKind = jobKind,
            PayloadVersion = payloadVersion,
            Payload = payloadObject,
            Options = optionsObject,
            Metadata = metadataObject,
            PrinterTags = printerTags,
            CreatedAt = created
        };
    }

    private static JsonObject ParsePayload(JsonElement jobEl)
    {
        if (jobEl.TryGetProperty("payload", out var payloadEl))
        {
            return ParseJsonObject(payloadEl);
        }

        return new JsonObject();
    }

    private static JsonObject ParseOptions(JsonElement jobEl)
    {
        if (jobEl.TryGetProperty("options", out var optionsEl))
        {
            return ParseJsonObject(optionsEl);
        }

        if (jobEl.TryGetProperty("options_json", out var optionsJsonEl) && optionsJsonEl.ValueKind == JsonValueKind.String)
        {
            return ParseJsonObject(optionsJsonEl.GetString());
        }

        return new JsonObject();
    }

    private static JsonObject ParseMetadata(JsonElement jobEl)
    {
        if (jobEl.TryGetProperty("metadata", out var metadataEl))
        {
            return ParseJsonObject(metadataEl);
        }

        if (jobEl.TryGetProperty("metadata_json", out var metadataJsonEl) && metadataJsonEl.ValueKind == JsonValueKind.String)
        {
            return ParseJsonObject(metadataJsonEl.GetString());
        }

        return new JsonObject();
    }

    private static JsonObject ParseJsonObject(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => JsonNode.Parse(element.GetRawText()) as JsonObject ?? new JsonObject(),
            JsonValueKind.String => ParseJsonObject(element.GetString()),
            _ => new JsonObject()
        };

    private static JsonObject ParseJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static IReadOnlyList<string> ParsePrinterTags(JsonElement jobEl)
    {
        if (jobEl.TryGetProperty("printer_tags", out var tagsEl))
        {
            return ExtractPrinterTags(tagsEl);
        }

        if (jobEl.TryGetProperty("printer_tags_json", out var tagsJsonEl) && tagsJsonEl.ValueKind == JsonValueKind.String)
        {
            return ParsePrinterTags(tagsJsonEl.GetString());
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> ExtractPrinterTags(JsonElement tagsEl)
    {
        if (tagsEl.ValueKind == JsonValueKind.Array)
        {
            var tags = new List<string>();
            foreach (var item in tagsEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var tag = item.GetString();
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        tags.Add(tag);
                    }
                }
            }
            return tags;
        }

        if (tagsEl.ValueKind == JsonValueKind.String)
        {
            return ParsePrinterTags(tagsEl.GetString());
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> ParsePrinterTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonArray array)
            {
                var tags = new List<string>();
                foreach (var entry in array)
                {
                    var tag = entry?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        tags.Add(tag);
                    }
                }
                return tags;
            }
        }
        catch
        {
            // ignore json parse failures
        }

        return new[] { json };
    }

    private async Task LoadPrinterConfigsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = new UriBuilder("https://menubu.com.tr/api/printer-configs.php");
            requestUri.Query = $"business_id={BusinessId}";

            using var response = await _httpClient.GetAsync(requestUri.Uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                PrinterConfigs = Array.Empty<PrinterConfig>();
                return;
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("printers", out var printersEl) || printersEl.ValueKind != JsonValueKind.Array)
            {
                PrinterConfigs = Array.Empty<PrinterConfig>();
                return;
            }

            var configs = new List<PrinterConfig>();
            foreach (var printerEl in printersEl.EnumerateArray())
            {
                configs.Add(new PrinterConfig
                {
                    Id = printerEl.GetProperty("id").GetInt32(),
                    Name = printerEl.GetProperty("name").GetString() ?? "Yazıcı",
                    PrinterType = printerEl.GetProperty("printer_type").GetString() ?? "all",
                    PrinterWidth = printerEl.TryGetProperty("printer_width", out var width) ? width.GetString() ?? "58mm" : "58mm",
                    IsActive = printerEl.TryGetProperty("is_active", out var active) && active.GetInt32() == 1,
                    IsDefault = printerEl.TryGetProperty("is_default", out var def) && def.GetInt32() == 1
                });
            }
            PrinterConfigs = configs;
        }
        catch
        {
            PrinterConfigs = Array.Empty<PrinterConfig>();
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private string BuildAuthQuery()
    {
        var email = Uri.EscapeDataString(_email);
        var password = Uri.EscapeDataString(_password);
        var version = Uri.EscapeDataString(_agentVersion);
        return $"email={email}&password={password}&agent_version={version}";
    }
}
