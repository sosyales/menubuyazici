using System;
using System.Drawing;
using System.Reflection;

namespace MenuBuPrinterAgent;

internal static class ResourceHelper
{
    private static Icon? _cachedIcon;

    public static Icon GetTrayIcon()
    {
        if (_cachedIcon != null)
        {
            return _cachedIcon;
        }

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("MenuBuPrinterAgent.Resources.icon.png");
        if (stream == null)
        {
            return SystemIcons.Application;
        }

        using var bitmap = new Bitmap(stream);
        _cachedIcon = Icon.FromHandle(bitmap.GetHicon());
        return _cachedIcon;
    }
}

