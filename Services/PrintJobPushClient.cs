using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MenuBuPrinterAgent.Models;

namespace MenuBuPrinterAgent.Services;

internal sealed class PrintJobPushClient : IDisposable
{
    private readonly Uri _endpoint;
    private readonly Func<JsonElement, PrintJob> _jobFactory;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public event Action<IReadOnlyList<PrintJob>>? JobsReceived;
    public event Action<string>? ConnectionError;

    public PrintJobPushClient(Uri endpoint, Func<JsonElement, PrintJob> jobFactory)
    {
        _endpoint = endpoint;
        _jobFactory = jobFactory;
    }

    public void Start(string email, string password, int businessId, string agentVersion)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunAsync(email, password, businessId, agentVersion, _cts.Token));
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignored
        }

        _socket?.Dispose();
        _socket = null;
    }

    private async Task RunAsync(string email, string password, int businessId, string agentVersion, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                _socket = new ClientWebSocket();
                _socket.Options.SetRequestHeader("User-Agent", "MenuBu-Printer-Agent/2.0");
                await _socket.ConnectAsync(_endpoint, token);

                var handshake = JsonSerializer.Serialize(new
                {
                    type = "auth",
                    email,
                    password,
                    business_id = businessId,
                    agent_version = agentVersion
                });

                await _socket.SendAsync(
                    Encoding.UTF8.GetBytes(handshake),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken: token);

                await ListenAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ConnectionError?.Invoke(ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
        }
    }

    private async Task ListenAsync(CancellationToken token)
    {
        if (_socket == null)
        {
            return;
        }

        var buffer = new byte[8192];
        var builder = new StringBuilder();

        while (!token.IsCancellationRequested && _socket.State == WebSocketState.Open)
        {
            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage)
            {
                continue;
            }

            var message = builder.ToString();
            builder.Clear();
            HandleMessage(message);
        }
    }

    private void HandleMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            var messageType = root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;

            if (!string.IsNullOrWhiteSpace(messageType))
            {
                switch (messageType.ToLowerInvariant())
                {
                    case "jobs":
                        if (root.TryGetProperty("jobs", out var jobsArray) && jobsArray.ValueKind == JsonValueKind.Array)
                        {
                            DispatchJobs(jobsArray);
                            return;
                        }
                        break;
                    case "job":
                        if (root.TryGetProperty("job", out var singleJob) && singleJob.ValueKind == JsonValueKind.Object)
                        {
                            var job = _jobFactory(singleJob);
                            JobsReceived?.Invoke(new[] { job });
                            return;
                        }
                        break;
                    case "ready":
                        return;
                    case "error":
                        var errorMessage = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                            ? messageElement.GetString()
                            : "Push kanalı hatası";
                        if (!string.IsNullOrWhiteSpace(errorMessage))
                        {
                            ConnectionError?.Invoke(errorMessage!);
                        }
                        return;
                }
            }

            if (root.TryGetProperty("jobs", out var jobsEl) && jobsEl.ValueKind == JsonValueKind.Array)
            {
                DispatchJobs(jobsEl);
                return;
            }

            if (root.TryGetProperty("job", out var singleJobNode) && singleJobNode.ValueKind == JsonValueKind.Object)
            {
                var job = _jobFactory(singleJobNode);
                JobsReceived?.Invoke(new[] { job });
            }
        }
        catch (Exception ex)
        {
            ConnectionError?.Invoke($"Push mesajı çözümlenemedi: {ex.Message}");
        }
    }

    private void DispatchJobs(JsonElement jobsElement)
    {
        if (jobsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var jobs = new List<PrintJob>();
        foreach (var jobNode in jobsElement.EnumerateArray())
        {
            jobs.Add(_jobFactory(jobNode));
        }

        if (jobs.Count > 0)
        {
            JobsReceived?.Invoke(jobs);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
