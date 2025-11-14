using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MenuBuPrinterAgent.Printing;

namespace MenuBuPrinterAgent.Services;

internal sealed class LocalPrintBridge : IDisposable
{
    private readonly PrinterManager _printerManager;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string[] _allowedOrigins;
    private readonly int _port;
    private Task? _listenerTask;
    private bool _started;
    private bool _disposed;

    public LocalPrintBridge(PrinterManager printerManager, int port = 9075, string[]? allowedOrigins = null)
    {
        _printerManager = printerManager ?? throw new ArgumentNullException(nameof(printerManager));
        _port = port;
        _allowedOrigins = allowedOrigins is { Length: > 0 }
            ? allowedOrigins
            : new[] { "https://menubu.com.tr", "https://www.menubu.com.tr" };
    }

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        var prefix = $"http://127.0.0.1:{_port}/";
        _listener.Prefixes.Add(prefix);
        _listener.Start();
        _listenerTask = Task.Run(() => ListenAsync(_cts.Token));
        _started = true;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleContextAsync(context), cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (HttpListenerException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalPrintBridge] Dinleme hatası: {ex.Message}");
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        using var _ = context.Response;
        try
        {
            if (context.Request.RemoteEndPoint?.Address is not { } clientAddress ||
                !IPAddress.IsLoopback(clientAddress))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                return;
            }

            if (!string.Equals(context.Request.Url?.AbsolutePath, "/print", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                ApplyCors(context.Response, context.Request.Headers["Origin"]);
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            ApplyCors(context.Response, context.Request.Headers["Origin"]);

            using var reader = new StreamReader(context.Request.InputStream,
                context.Request.ContentEncoding ?? Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                await WriteJsonAsync(context.Response, new { success = false, error = "İstek gövdesi boş." },
                    HttpStatusCode.BadRequest);
                return;
            }

            LocalPrintRequest? payload;
            try
            {
                payload = JsonSerializer.Deserialize<LocalPrintRequest>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                await WriteJsonAsync(context.Response, new { success = false, error = $"Geçersiz JSON: {ex.Message}" },
                    HttpStatusCode.BadRequest);
                return;
            }

            if (payload == null || string.IsNullOrWhiteSpace(payload.Html))
            {
                await WriteJsonAsync(context.Response, new { success = false, error = "HTML içeriği gerekli." },
                    HttpStatusCode.BadRequest);
                return;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            try
            {
                await _printerManager.PrintHtmlAsync(payload.Html, payload.PrinterWidth, linkedCts.Token);
                await WriteJsonAsync(context.Response, new { success = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalPrintBridge] Lokal yazdırma hatası: {ex.Message}");
                await WriteJsonAsync(context.Response,
                    new { success = false, error = ex.Message },
                    HttpStatusCode.InternalServerError);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalPrintBridge] İstek işlenemedi: {ex.Message}");
        }
    }

    private void ApplyCors(HttpListenerResponse response, string? origin)
    {
        if (origin != null && _allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            response.Headers["Access-Control-Allow-Origin"] = origin;
        }
        else
        {
            response.Headers["Access-Control-Allow-Origin"] = "null";
        }

        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object payload, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        response.ContentType = "application/json; charset=utf-8";
        response.StatusCode = (int)statusCode;
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var buffer = Encoding.UTF8.GetBytes(json);
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        try
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
        }
        catch
        {
            // ignored
        }

        _listener.Close();
        _cts.Dispose();
    }

    private sealed class LocalPrintRequest
    {
        public string? Html { get; set; }
        public string? PrinterWidth { get; set; }
        public JsonElement? Metadata { get; set; }
    }
}
