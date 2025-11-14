using System;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MenuBuPrinterAgent;

internal static class Program
{
    private static bool _isRestarting;

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => HandleFatalException(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                HandleFatalException(ex);
            }
            else
            {
                HandleFatalException(new Exception("Bilinmeyen uygulama hatası"));
            }
        };
        SystemEvents.SessionEnding += (_, _) => RestartApplication();

        Application.Run(new TrayApplicationContext());
    }

    private static void HandleFatalException(Exception ex)
    {
        try
        {
            MessageBox.Show(
                $"Beklenmeyen bir hata gerçekleşti. Uygulama yeniden başlatılacak.\n\nDetay: {ex.Message}",
                "MenuBu Yazıcı Ajanı",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch
        {
            // ignored
        }

        RestartApplication();
    }

    private static void RestartApplication()
    {
        if (_isRestarting) return;
        _isRestarting = true;
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                });
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            Environment.Exit(1);
        }
    }
}
