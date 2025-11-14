using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MenuBuPrinterAgent.Printing;

internal sealed class HtmlPrinter : IDisposable
{
    private WebView2? _webView;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _printLock = new(1, 1);
    private bool _isInitialized;
    private bool _disposed;
    private CoreWebView2Environment? _environment;
    private Version? _runtimeVersion;
    private static readonly Version MinimumSilentPrintVersion = new(118, 0, 0, 0);

    public string? SelectedPrinter { get; set; }
    public string PrinterWidth { get; set; } = "58mm";

    public async Task PrintHtmlAsync(string html, CancellationToken cancellationToken)
    {
        await _printLock.WaitAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        if (_webView == null)
        {
            _printLock.Release();
            throw new InvalidOperationException("WebView2 başlatılamadı");
        }

        try
        {
            var preparedHtml = PrepareHtml(html);
            await LoadHtmlAsync(preparedHtml, cancellationToken);

            await PrintWithCoreAsync(_webView.CoreWebView2, cancellationToken);
        }
        finally
        {
            _printLock.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized) return;

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MenuBuPrinterAgent",
                "WebView2");

            _webView = new WebView2
            {
                CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = userDataFolder
                }
            };

            _environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(_environment);
            _runtimeVersion = ParseRuntimeVersion(_webView.CoreWebView2.Environment.BrowserVersionString);

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task LoadHtmlAsync(string html, CancellationToken cancellationToken)
    {
        if (_webView == null)
        {
            throw new InvalidOperationException("WebView2 mevcut değil");
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!args.IsSuccess)
            {
                tcs.TrySetException(new InvalidOperationException($"HTML yüklenemedi: {args.WebErrorStatus}"));
            }
            else
            {
                tcs.TrySetResult(true);
            }
        }

        _webView.NavigationCompleted += Handler;
        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        try
        {
            _webView.NavigateToString(html);
            await tcs.Task;
        }
        finally
        {
            _webView.NavigationCompleted -= Handler;
        }
    }

    private async Task PrintWithCoreAsync(CoreWebView2 coreWebView2, CancellationToken cancellationToken)
    {
        if (_environment == null)
        {
            throw new InvalidOperationException("WebView2 ortamı oluşturulamadı");
        }

        if (_runtimeVersion == null || _runtimeVersion < MinimumSilentPrintVersion)
        {
            throw new InvalidOperationException("Sessiz yazdırma için WebView2 Runtime 118+ gereklidir. Lütfen runtime'ı güncelleyin.");
        }

        var settings = _environment.CreatePrintSettings();
        settings.ShouldPrintHeaderAndFooter = false;
        settings.ShouldPrintBackgrounds = true;
        settings.ScaleFactor = GetScaleFactor();
        if (!string.IsNullOrWhiteSpace(SelectedPrinter))
        {
            settings.PrinterName = SelectedPrinter;
        }

        const int maxAttempts = 3;
        CoreWebView2PrintStatus lastStatus = CoreWebView2PrintStatus.OtherError;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            lastStatus = await coreWebView2.PrintAsync(settings);
            if (lastStatus == CoreWebView2PrintStatus.Succeeded)
            {
                return;
            }

            if (lastStatus == CoreWebView2PrintStatus.PrinterUnavailable)
            {
                throw new InvalidOperationException("Yazdırma başarısız: Yazıcıya ulaşılamadı.");
            }

            await Task.Delay(200 * attempt, cancellationToken);
        }

        throw new InvalidOperationException($"Yazdırma başarısız: {lastStatus}.");
    }

    private double GetScaleFactor() => 1.0;

    private string PrepareHtml(string html)
    {
        var (pageWidth, bodyWidth) = GetTargetDimensions();
        var styleBlock =
            $"<style id=\"menubu-print-style\">@page{{size:{pageWidth} auto;margin:0;}}html,body{{margin:0;padding:0;background:#fff;}}body{{margin:0 auto;padding:0;width:{bodyWidth};max-width:{bodyWidth};color:#000;}}img,table{{max-width:100%;}}*{{box-sizing:border-box;word-break:break-word;-webkit-print-color-adjust:exact;}}</style>";

        if (string.IsNullOrWhiteSpace(html))
        {
            return $"<!DOCTYPE html><html><head>{styleBlock}</head><body></body></html>";
        }

        var headIndex = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (headIndex >= 0)
        {
            var headCloseIndex = html.IndexOf('>', headIndex);
            if (headCloseIndex >= 0)
            {
                return html.Insert(headCloseIndex + 1, styleBlock);
            }
        }

        var htmlIndex = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        if (htmlIndex >= 0)
        {
            var htmlCloseIndex = html.IndexOf('>', htmlIndex);
            if (htmlCloseIndex >= 0)
            {
                return html.Insert(htmlCloseIndex + 1, $"<head>{styleBlock}</head>");
            }
        }

        return $"<!DOCTYPE html><html><head>{styleBlock}</head><body>{html}</body></html>";
    }

    private (string PageWidth, string BodyWidth) GetTargetDimensions()
    {
        var isWide = PrinterWidth.StartsWith("80", StringComparison.OrdinalIgnoreCase);
        return isWide ? ("300px", "300px") : ("219px", "219px");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _webView?.Dispose();
        _initLock.Dispose();
        _printLock.Dispose();
        _environment = null;
    }

    private static Version? ParseRuntimeVersion(string? versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return null;
        }

        var firstToken = versionString.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (Version.TryParse(firstToken, out var version))
        {
            return version;
        }

        var numericPart = new string(firstToken.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        if (Version.TryParse(numericPart, out var fallback))
        {
            return fallback;
        }

        return null;
    }
}
