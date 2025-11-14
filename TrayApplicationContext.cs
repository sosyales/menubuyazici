using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using MenuBuPrinterAgent.Printing;
using MenuBuPrinterAgent.Services;
using MenuBuPrinterAgent.UI;

namespace MenuBuPrinterAgent;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _statusMenuItem = new("Durum: Bağlanmadı") { Enabled = false };
    private readonly ToolStripMenuItem _loginMenuItem = new("&Giriş Yap");
    private readonly ToolStripMenuItem _logoutMenuItem = new("&Çıkış Yap") { Enabled = false };
    private readonly ToolStripMenuItem _reconnectMenuItem = new("&Yeniden Bağlan") { Enabled = false };
    private readonly ToolStripMenuItem _printerMenuItem = new("&Yazıcı Ayarla") { Enabled = false };
    private readonly ToolStripMenuItem _printerMappingMenuItem = new("Yazıcı Eşleştir") { Enabled = false };
    private readonly ToolStripMenuItem _clearQueueMenuItem = new("&Kuyruğu Temizle") { Enabled = false };
    private readonly ToolStripMenuItem _exitMenuItem = new("Çı&kış");

    private readonly UserSettings _settings = UserSettings.Load();
    private readonly HttpClient _httpClient = new();
    private readonly PrinterManager _printerManager;
    private readonly LocalPrintBridge _localBridge;
    private PrintJobPushClient? _pushClient;
    private MenuBuApiClient? _apiClient;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly System.Threading.Timer _connectionGuardTimer;
    private System.Threading.Timer? _heartbeatTimer;
    private readonly SynchronizationContext _syncContext;
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private bool _initialPromptCompleted;
    private bool _hasShownPendingJobsPrompt;
    private bool _isDisposed;
    private bool _isConnected;
    private bool _exitRequested;
    private bool _systemShutdown;
    private bool _reconnectInProgress;
    private DateTime _lastSuccessfulPoll = DateTime.UtcNow;
    private readonly HashSet<int> _ignoredJobIds = new();
    private readonly HashSet<int> _processedJobIds = new();
    private readonly HashSet<int> _inFlightJobIds = new();
    private string? _lastConnectionError;

    public TrayApplicationContext()
    {
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        AutoStartManager.EnsureStartupEntry();
        _printerManager = new PrinterManager(_httpClient)
        {
            SelectedPrinter = _settings.PrinterName
        };
        _localBridge = new LocalPrintBridge(_printerManager);
        _localBridge.Start();

        _pollTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _pollTimer.Tick += async (_, _) => await PollJobsAsync();
        _connectionGuardTimer = new System.Threading.Timer(_ => CheckConnectionHealth(), null, TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(45));
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += (_, _) =>
        {
            _systemShutdown = true;
            _exitRequested = true;
            ExitThread();
        };

        _trayIcon = new NotifyIcon
        {
            Icon = ResourceHelper.GetTrayIcon(),
            Text = "MenuBu Yazıcı Ajanı",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _loginMenuItem.Click += async (_, _) => await PromptLoginAsync();
        _logoutMenuItem.Click += (_, _) => Logout();
        _reconnectMenuItem.Click += async (_, _) => await ReconnectAsync();
        _printerMenuItem.Click += (_, _) => ShowPrinterDialog();
        _printerMappingMenuItem.Click += (_, _) => ShowPrinterMappingDialog();
        _clearQueueMenuItem.Click += async (_, _) => await ClearQueueAsync();
        _exitMenuItem.Click += (_, _) => ExitThread();

        _trayIcon.DoubleClick += (_, _) => ShowStatusBalloon();
        _trayIcon.BalloonTipClicked += async (_, _) =>
        {
            if (!_isConnected && !string.IsNullOrWhiteSpace(_settings.Email))
            {
                await ReconnectAsync();
            }
        };

        if (!string.IsNullOrWhiteSpace(_settings.Email) && !string.IsNullOrWhiteSpace(_settings.Password))
        {
            _ = AttemptAutoLoginAsync();
        }
        else
        {
            ShowStatus("Giriş yapın", connected: false);
        }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_loginMenuItem);
        menu.Items.Add(_logoutMenuItem);
        menu.Items.Add(_reconnectMenuItem);
        menu.Items.Add(_printerMenuItem);
        menu.Items.Add(_printerMappingMenuItem);
        menu.Items.Add(_clearQueueMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitMenuItem);
        return menu;
    }

    private async Task AttemptAutoLoginAsync()
    {
        try
        {
            await AuthenticateAsync(_settings.Email, _settings.Password, silent: true);
        }
        catch
        {
            ShowStatus("Giriş gerekli", connected: false);
        }
    }

    private async Task PromptLoginAsync()
    {
        using var loginForm = new LoginForm(_settings.Email, _settings.Password);
        if (loginForm.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            await AuthenticateAsync(loginForm.Email, loginForm.Password, silent: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Giriş başarısız: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ShowStatus("Giriş yapılamadı", connected: false);
        }
    }

    private async Task AuthenticateAsync(string email, string password, bool silent)
    {
        CleanupClient();

        _apiClient = new MenuBuApiClient(email, password);
        IReadOnlyList<Models.PrintJob> initialJobs;
        try
        {
            initialJobs = await _apiClient.AuthenticateAndFetchInitialJobsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            CleanupClient();
            if (!silent)
            {
                MessageBox.Show($"Giriş sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            throw;
        }

        _settings.Email = email;
        _settings.Password = password;
        _settings.PrinterName = _printerManager.SelectedPrinter;
        _settings.PrinterWidth = _apiClient.PrinterWidth;
        _settings.Save();
        AutoStartManager.EnsureStartupEntry();
        _printerManager.PrinterWidth = _settings.PrinterWidth;
        _printerManager.FontSizeAdjustment = _settings.FontSizeAdjustment;

        _initialPromptCompleted = _hasShownPendingJobsPrompt;
        _ignoredJobIds.Clear();
        _processedJobIds.Clear();
        _inFlightJobIds.Clear();

        _loginMenuItem.Enabled = false;
        _logoutMenuItem.Enabled = true;
        _reconnectMenuItem.Enabled = true;
        _printerMenuItem.Enabled = true;
        _printerMappingMenuItem.Enabled = true;
        _clearQueueMenuItem.Enabled = true;

        ShowStatus($"Bağlandı: {_apiClient.BusinessName}", connected: true);
        RestartPushChannel();
        StartHeartbeat();

        EnsureTimer();
        await HandleInitialJobsAsync(initialJobs);
    }

    private void EnsureTimer()
    {
        if (!_pollTimer.Enabled)
        {
            _pollTimer.Start();
        }

        _ = PollJobsAsync();
    }

    private async Task HandleInitialJobsAsync(IReadOnlyList<Models.PrintJob> jobs)
    {
        if (_initialPromptCompleted)
        {
            return;
        }

        if (jobs.Count == 0)
        {
            _initialPromptCompleted = true;
            return;
        }

        var result = MessageBox.Show(
            $"{jobs.Count} adet bekleyen yazdırma bulunuyor. Hepsini yazdırmak ister misiniz?",
            "Bekleyen Yazdırmalar",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            foreach (var job in jobs.OrderBy(j => j.CreatedAt))
            {
                await ProcessJobAsync(job);
            }
        }
        else
        {
            foreach (var job in jobs)
            {
                _ignoredJobIds.Add(job.Id);
            }
        }

        _initialPromptCompleted = true;
        _hasShownPendingJobsPrompt = true;
    }

    private async Task PollJobsAsync()
    {
        if (_apiClient == null)
        {
            return;
        }

        if (!await _pollLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            IReadOnlyList<Models.PrintJob> jobs;
            try
            {
                jobs = await _apiClient.GetPendingJobsAsync(CancellationToken.None);
                _lastSuccessfulPoll = DateTime.UtcNow;
                _reconnectInProgress = false;
                if (!_isConnected)
                {
                    ShowStatus($"Bağlandı: {_apiClient.BusinessName}", connected: true);
                    NotifyConnectionRestored();
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Bağlantı hatası: {ex.Message}", connected: false);
                NotifyConnectionLost(ex.Message);
                _lastConnectionError = ex.Message;
                return;
            }

            if (!_initialPromptCompleted)
            {
                await HandleInitialJobsAsync(jobs);
                return;
            }

            foreach (var job in jobs.OrderBy(j => j.CreatedAt))
            {
                if (_ignoredJobIds.Contains(job.Id) || _processedJobIds.Contains(job.Id) || _inFlightJobIds.Contains(job.Id))
                {
                    continue;
                }

                await ProcessJobAsync(job);
            }
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private async Task ProcessJobAsync(Models.PrintJob job)
    {
        if (_apiClient == null)
        {
            return;
        }

        if (!_inFlightJobIds.Add(job.Id))
        {
            return;
        }

        try
        {
            await SafeUpdateJobStatus(job.Id, "printing", null);
            
            var targetPrinter = SelectPrinterForJob(job);
            var previousPrinter = _printerManager.SelectedPrinter;
            
            if (targetPrinter != null)
            {
                _printerManager.SelectedPrinter = targetPrinter;
            }
            
            try
            {
                var payloadJson = job.Payload.ToJsonString();
                using var payloadDoc = JsonDocument.Parse(payloadJson);
                await _printerManager.PrintAsync(payloadDoc, CancellationToken.None);
                await SafeUpdateJobStatus(job.Id, "printed", null);
                _processedJobIds.Add(job.Id);
                
            }
            finally
            {
                _printerManager.SelectedPrinter = previousPrinter;
            }
        }
        catch (Exception ex)
        {
            var errorMsg = ex.Message;
            if (ex.InnerException != null)
            {
                errorMsg = ex.InnerException.Message;
            }
            
            // Kısa hata mesajı
            var shortError = errorMsg.Length > 100 ? errorMsg.Substring(0, 100) + "..." : errorMsg;
            
            await SafeUpdateJobStatus(job.Id, "failed", errorMsg);
            _syncContext.Post(_ => 
                _trayIcon.ShowBalloonTip(5000, "Yazdırma Hatası", $"İş #{job.Id}: {shortError}", ToolTipIcon.Warning), null);
        }
        finally
        {
            _inFlightJobIds.Remove(job.Id);
        }
    }

    private async Task SafeUpdateJobStatus(int jobId, string status, string? message)
    {
        if (_apiClient == null)
        {
            return;
        }

        try
        {
            await _apiClient.UpdateJobStatusAsync(jobId, status, message, CancellationToken.None);
        }
        catch
        {
            // ignore
        }
    }

    private async Task ReconnectAsync()
    {
        if (_apiClient == null)
        {
            await PromptLoginAsync();
            return;
        }

        ShowStatus("Yeniden bağlanıyor...", connected: false);
        try
        {
            await AuthenticateAsync(_settings.Email, _settings.Password, silent: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Yeniden bağlanma başarısız: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowPrinterDialog()
    {
        using var form = new PrinterSettingsForm(_printerManager.SelectedPrinter, _settings.PrinterWidth, _settings.FontSizeAdjustment);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _printerManager.SelectedPrinter = form.SelectedPrinter;
            _printerManager.PrinterWidth = form.PrinterWidth;
            _printerManager.FontSizeAdjustment = form.FontSizeAdjustment;
            _settings.PrinterName = form.SelectedPrinter;
            _settings.PrinterWidth = form.PrinterWidth;
            _settings.FontSizeAdjustment = form.FontSizeAdjustment;
            _settings.Save();
            var message = form.SelectedPrinter is null ? "Varsayılan yazıcı kullanılacak" : $"Yazıcı: {form.SelectedPrinter}";
            _syncContext.Post(_ => 
                _trayIcon.ShowBalloonTip(2000, "Ayarlar Kaydedildi", message, ToolTipIcon.Info), null);
        }
    }

    private void ShowPrinterMappingDialog()
    {
        if (_apiClient == null)
        {
            MessageBox.Show("Önce giriş yapmalısınız.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var printerConfigs = _apiClient.PrinterConfigs;
        if (printerConfigs.Count == 0)
        {
            MessageBox.Show("Siteden henüz yazıcı tanımlanmamış. Lütfen önce web panelinden yazıcı ekleyin.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new PrinterMappingForm(printerConfigs, _settings.PrinterMappings);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _settings.PrinterMappings = form.PrinterMappings;
            _settings.Save();
            var count = form.PrinterMappings.Count;
            var message = count > 0 ? $"{count} yazıcı eşleştirildi" : "Eşleştirmeler temizlendi";
            _syncContext.Post(_ => 
                _trayIcon.ShowBalloonTip(2000, "Eşleştirme Kaydedildi", message, ToolTipIcon.Info), null);
        }
    }

    private void Logout()
    {
        CleanupClient();
        _hasShownPendingJobsPrompt = false;
        _settings.Email = string.Empty;
        _settings.Password = string.Empty;
        _settings.Save();
        ShowStatus("Çıkış yapıldı", connected: false);
        _syncContext.Post(_ => 
            _trayIcon.ShowBalloonTip(2000, "Çıkış Yapıldı", "Hesaptan çıkış yapıldı.", ToolTipIcon.Info), null);
        _loginMenuItem.Enabled = true;
        _logoutMenuItem.Enabled = false;
        _reconnectMenuItem.Enabled = false;
        _printerMenuItem.Enabled = false;
        _printerMappingMenuItem.Enabled = false;
        _clearQueueMenuItem.Enabled = false;
    }

    private void ShowStatus(string message, bool connected)
    {
        void Update()
        {
            _isConnected = connected;
            _statusMenuItem.Text = $"Durum: {message}";
            _trayIcon.Text = $"MenuBu Yazıcı Ajanı - {message}".Trim();
        }

        if (SynchronizationContext.Current == _syncContext)
        {
            Update();
        }
        else
        {
            _syncContext.Post(_ => Update(), null);
        }
    }

    private void ShowStatusBalloon()
    {
        void Show()
        {
            var tipText = string.IsNullOrWhiteSpace(_statusMenuItem.Text)
                ? "Durum bilgisi alınamadı"
                : _statusMenuItem.Text;
            _trayIcon.ShowBalloonTip(2000, "MenuBu Yazıcı", tipText, _isConnected ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }

        if (SynchronizationContext.Current == _syncContext)
        {
            Show();
        }
        else
        {
            _syncContext.Post(_ => Show(), null);
        }
    }

    private void CleanupClient()
    {
        if (_pollTimer.Enabled)
        {
            _pollTimer.Stop();
        }
        _pollLock.Reset();
        StopPushChannel();
        StopHeartbeat();
        _apiClient?.Dispose();
        _apiClient = null;
        _initialPromptCompleted = false;
        _ignoredJobIds.Clear();
        _processedJobIds.Clear();
        _inFlightJobIds.Clear();
        _lastConnectionError = null;
    }

    private void RestartPushChannel()
    {
        StopPushChannel();
        if (!_settings.EnablePushChannel || string.IsNullOrWhiteSpace(_settings.PushEndpoint) || _apiClient == null)
        {
            return;
        }

        if (!Uri.TryCreate(_settings.PushEndpoint, UriKind.Absolute, out var endpoint))
        {
            return;
        }

        _pushClient = new PrintJobPushClient(endpoint, MenuBuApiClient.ParseJob);
        _pushClient.JobsReceived += OnPushJobsReceived;
        _pushClient.ConnectionError += OnPushChannelError;
        _pushClient.Start(_settings.Email, _settings.Password, _apiClient.BusinessId, MenuBuApiClient.AgentVersion);
    }

    private void StopPushChannel()
    {
        if (_pushClient == null)
        {
            return;
        }

        _pushClient.JobsReceived -= OnPushJobsReceived;
        _pushClient.ConnectionError -= OnPushChannelError;
        _pushClient.Dispose();
        _pushClient = null;
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();
        if (_apiClient == null)
        {
            return;
        }

        _heartbeatTimer = new System.Threading.Timer(_ => _ = SendHeartbeatCoreAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(25));
    }

    private void StopHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    private async Task SendHeartbeatCoreAsync()
    {
        if (_apiClient == null)
        {
            return;
        }

        try
        {
            await _apiClient.SendHeartbeatAsync(CancellationToken.None);
        }
        catch
        {
            // ignore heartbeat failures
        }
    }

    private async Task ClearQueueAsync()
    {
        if (_apiClient == null)
        {
            await PromptLoginAsync();
            return;
        }

        if (MessageBox.Show("Bekleyen yazdırma kuyruğunu temizlemek istediğinizden emin misiniz?\nBu işlem yalnızca bu işletmeye ait bekleyen işleri temizler.", "Kuyruğu Temizle", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _pollTimer.Stop();
        ShowStatus("Kuyruk temizleniyor...", _isConnected);

        try
        {
            var cleared = await _apiClient.ClearPendingJobsAsync(CancellationToken.None);
            _ignoredJobIds.Clear();
            _processedJobIds.Clear();
            _inFlightJobIds.Clear();

            var message = cleared > 0 ? $"{cleared} iş temizlendi" : "Bekleyen iş bulunamadı";
            _syncContext.Post(_ => 
                _trayIcon.ShowBalloonTip(2000, "Kuyruk Temizlendi", message, ToolTipIcon.Info), null);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kuyruk temizlenemedi: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            EnsureTimer();
        }
    }

    protected override void ExitThreadCore()
    {
        if (!_systemShutdown && !_exitRequested)
        {
            var result = MessageBox.Show(
                "Uygulamayı kapatırsanız yazdırma işlemleri durur. Çıkmak istediğinize emin misiniz?",
                "MenuBu Yazıcı Ajanı",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                return;
            }
            _exitRequested = true;
        }

        if (_isDisposed)
        {
            base.ExitThreadCore();
            return;
        }

        _isDisposed = true;
        CleanupClient();
        _connectionGuardTimer.Dispose();
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _localBridge.Dispose();
        _printerManager.Dispose();
        _httpClient.Dispose();
        base.ExitThreadCore();
    }

    private void NotifyConnectionLost(string message)
    {
        if (_lastConnectionError == message)
        {
            return;
        }

        _lastConnectionError = message;
        var shortMsg = message?.Length > 50 ? message.Substring(0, 50) + "..." : message;
        var tipText = $"Bağlantı kesildi. 15 saniye sonra otomatik yeniden denenecek.";
        _syncContext.Post(_ =>
        {
            _trayIcon.ShowBalloonTip(4000, "Bağlantı Hatası", tipText, ToolTipIcon.Warning);
        }, null);
        
        // 15 saniye sonra otomatik yeniden bağlanma denemesi
        _ = Task.Delay(15000).ContinueWith(async _ =>
        {
            if (_lastConnectionError != null && !string.IsNullOrWhiteSpace(_settings.Email))
            {
                try
                {
                    await AuthenticateAsync(_settings.Email, _settings.Password, silent: true);
                }
                catch
                {
                    // Sessizce başarısız ol, kullanıcı manuel bağlanabilir
                }
            }
        });
    }

    private void NotifyConnectionRestored()
    {
        if (_lastConnectionError == null)
        {
            return;
        }

        _lastConnectionError = null;
    }

    private void OnPushJobsReceived(IReadOnlyList<Models.PrintJob> jobs)
    {
        foreach (var job in jobs.OrderBy(j => j.CreatedAt))
        {
            if (_ignoredJobIds.Contains(job.Id) || _processedJobIds.Contains(job.Id) || _inFlightJobIds.Contains(job.Id))
            {
                continue;
            }

            _ = ProcessJobAsync(job);
        }
    }

    private void OnPushChannelError(string message)
    {
        Debug.WriteLine($"[PushChannel] {message}");
    }

    private void CheckConnectionHealth()
    {
        if (_apiClient == null || !_isConnected || _reconnectInProgress)
        {
            return;
        }

        if (DateTime.UtcNow - _lastSuccessfulPoll < TimeSpan.FromSeconds(60))
        {
            return;
        }

        _reconnectInProgress = true;
        _syncContext.Post(async _ =>
        {
            try
            {
                await ReconnectAsync();
            }
            finally
            {
                _reconnectInProgress = false;
            }
        }, null);
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable || string.IsNullOrWhiteSpace(_settings.Email))
        {
            return;
        }
        _syncContext.Post(async _ => await ReconnectAsync(), null);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && !string.IsNullOrWhiteSpace(_settings.Email))
        {
            _syncContext.Post(async _ => await ReconnectAsync(), null);
        }
    }

    private string? SelectPrinterForJob(Models.PrintJob job)
    {
        if (job.PrinterTags.Count > 0)
        {
            foreach (var tag in job.PrinterTags)
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                if (_settings.PrinterMappings.TryGetValue(tag, out var mappedByTag))
                {
                    return mappedByTag;
                }
            }
        }

        // Payload'dan printer_id veya printer_name al
        if (job.Payload.TryGetPropertyValue("printer_id", out var printerIdNode) && printerIdNode != null)
        {
            var printerId = printerIdNode.GetValue<int>();
            var config = _apiClient?.PrinterConfigs.FirstOrDefault(p => p.Id == printerId);
            if (config != null && _settings.PrinterMappings.TryGetValue(config.Name, out var mappedPrinter))
            {
                return mappedPrinter;
            }
        }
        
        if (job.Payload.TryGetPropertyValue("printer_name", out var printerNameNode) && printerNameNode != null)
        {
            var printerName = printerNameNode.GetValue<string>();
            if (!string.IsNullOrEmpty(printerName) && _settings.PrinterMappings.TryGetValue(printerName, out var mappedPrinter))
            {
                return mappedPrinter;
            }
        }

        if (job.Metadata.TryGetPropertyValue("printer_id", out var metadataPrinterIdNode) && metadataPrinterIdNode != null)
        {
            var metadataPrinterId = metadataPrinterIdNode.GetValue<int>();
            var config = _apiClient?.PrinterConfigs.FirstOrDefault(p => p.Id == metadataPrinterId);
            if (config != null && _settings.PrinterMappings.TryGetValue(config.Name, out var mappedPrinter))
            {
                return mappedPrinter;
            }
        }

        if (job.Metadata.TryGetPropertyValue("printer_name", out var metadataPrinterNameNode) && metadataPrinterNameNode != null)
        {
            var metadataPrinterName = metadataPrinterNameNode.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(metadataPrinterName) && _settings.PrinterMappings.TryGetValue(metadataPrinterName, out var mappedByMetadata))
            {
                return mappedByMetadata;
            }
        }
        
        if (_apiClient == null || _apiClient.PrinterConfigs.Count == 0)
        {
            return null; // Varsayılan yazıcı kullanılacak
        }

        var jobTypeToken = string.IsNullOrWhiteSpace(job.JobKind) ? job.JobType : job.JobKind;
        var jobType = jobTypeToken?.ToLowerInvariant() ?? "receipt";
        
        // Önce default yazıcıyı bul
        foreach (var config in _apiClient.PrinterConfigs)
        {
            if (!config.IsActive)
            {
                continue;
            }
            
            var printerType = config.PrinterType.ToLowerInvariant();
            if (printerType == "all" || printerType == jobType)
            {
                if (config.IsDefault)
                {
                    // Eşleştirme var mı kontrol et
                    if (_settings.PrinterMappings.TryGetValue(config.Name, out var mappedPrinter))
                    {
                        return mappedPrinter;
                    }
                    // Eşleştirme yoksa null dön (varsayılan kullanılacak)
                    return null;
                }
            }
        }
        
        // Default yoksa ilk uygun yazıcıyı bul
        foreach (var config in _apiClient.PrinterConfigs)
        {
            if (!config.IsActive)
            {
                continue;
            }
            
            var printerType = config.PrinterType.ToLowerInvariant();
            if (printerType == "all" || printerType == jobType)
            {
                // Eşleştirme var mı kontrol et
                if (_settings.PrinterMappings.TryGetValue(config.Name, out var mappedPrinter))
                {
                    return mappedPrinter;
                }
                // Eşleştirme yoksa null dön (varsayılan kullanılacak)
                return null;
            }
        }
        
        return null; // Varsayılan yazıcı kullanılacak
    }
}

internal static class SemaphoreExtensions
{
    public static void Reset(this SemaphoreSlim semaphore)
    {
        try
        {
            if (semaphore.Wait(0))
            {
                semaphore.Release();
            }
        }
        catch
        {
            // ignore
        }
    }
}
