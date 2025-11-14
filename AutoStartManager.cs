using System;
using Microsoft.Win32;

namespace MenuBuPrinterAgent;

internal static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MenuBuPrinterAgent";

    public static void EnsureStartupEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ??
                            Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            key?.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
        }
        catch
        {
            // ignore
        }
    }

    public static void RemoveStartupEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch
        {
            // ignore
        }
    }
}

