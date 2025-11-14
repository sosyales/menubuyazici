using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Windows.Forms;
using MenuBuPrinterAgent.Models;

namespace MenuBuPrinterAgent.UI;

internal sealed class PrinterMappingForm : Form
{
    private readonly TableLayoutPanel _mappingPanel = new();
    private readonly Button _okButton = new() { Text = "Kaydet", DialogResult = DialogResult.OK, Width = 120 };
    private readonly Button _cancelButton = new() { Text = "İptal", DialogResult = DialogResult.Cancel, Width = 120 };
    private readonly Dictionary<string, ComboBox> _mappingCombos = new();

    public Dictionary<string, string> PrinterMappings { get; private set; } = new();

    public PrinterMappingForm(IReadOnlyList<PrinterConfig> printerConfigs, Dictionary<string, string> currentMappings)
    {
        Text = "Yazıcı Eşleştirme";
        Icon = ResourceHelper.GetTrayIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, Math.Min(600, 150 + printerConfigs.Count * 80));

        var windowsPrinters = PrinterSettings.InstalledPrinters.Cast<string>().ToList();
        windowsPrinters.Insert(0, "(Varsayılan Yazıcı)");

        _mappingPanel.Dock = DockStyle.Fill;
        _mappingPanel.Padding = new Padding(16);
        _mappingPanel.ColumnCount = 2;
        _mappingPanel.RowCount = printerConfigs.Count + 2;
        _mappingPanel.AutoScroll = true;

        _mappingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        _mappingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));

        var headerLabel = new Label
        {
            Text = "Siteden tanımlı yazıcılarınızı bilgisayarınızdaki yazıcılarla eşleştirin:",
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, 12)
        };
        _mappingPanel.Controls.Add(headerLabel);
        _mappingPanel.SetColumnSpan(headerLabel, 2);

        foreach (var config in printerConfigs.Where(c => c.IsActive))
        {
            var label = new Label
            {
                Text = $"{config.Name} ({config.PrinterType})",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 8, 0, 0)
            };

            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 250,
                Anchor = AnchorStyles.Left
            };

            foreach (var printer in windowsPrinters)
            {
                combo.Items.Add(printer);
            }

            if (currentMappings.TryGetValue(config.Name, out var mappedPrinter) && windowsPrinters.Contains(mappedPrinter))
            {
                combo.SelectedItem = mappedPrinter;
            }
            else
            {
                combo.SelectedIndex = 0; // Varsayılan
            }

            _mappingCombos[config.Name] = combo;
            _mappingPanel.Controls.Add(label);
            _mappingPanel.Controls.Add(combo);
        }

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0)
        };
        buttonPanel.Controls.Add(_okButton);
        buttonPanel.Controls.Add(_cancelButton);

        _mappingPanel.Controls.Add(buttonPanel);
        _mappingPanel.SetColumnSpan(buttonPanel, 2);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.Add(_mappingPanel);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            PrinterMappings = new Dictionary<string, string>();
            foreach (var kvp in _mappingCombos)
            {
                if (kvp.Value.SelectedItem is string selected && selected != "(Varsayılan Yazıcı)")
                {
                    PrinterMappings[kvp.Key] = selected;
                }
            }
        }
        base.OnFormClosing(e);
    }
}
