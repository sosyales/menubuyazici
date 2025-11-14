using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MenuBuPrinterAgent;

internal sealed class UserSettings
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? PrinterName { get; set; }
    public string PrinterWidth { get; set; } = "58mm";
    public int FontSizeAdjustment { get; set; } = 0;
    public bool LaunchAtStartup { get; set; } = true;
    public Dictionary<string, string> PrinterMappings { get; set; } = new();
    public bool EnablePushChannel { get; set; } = true;
    public string PushEndpoint { get; set; } = "wss://menubu.com.tr/ws/print-jobs";

    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MenuBu", "printer-agent.json");

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new UserSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json);
            return settings ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // ignore persistence failures
        }
    }
}
