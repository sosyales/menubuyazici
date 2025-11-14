using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Windows.Forms;

namespace MenuBuPrinterAgent.UI;

internal sealed class PrinterSettingsForm : Form
{
    private readonly ComboBox _printerCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _widthCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly NumericUpDown _fontSizeAdjust = new() { Minimum = -3, Maximum = 3, Value = 0, Width = 260 };
    private readonly Button _okButton = new() { Text = "Kaydet", DialogResult = DialogResult.OK, Width = 120 };
    private readonly Button _cancelButton = new() { Text = "İptal", DialogResult = DialogResult.Cancel, Width = 120 };
    private readonly CheckBox _useDefaultCheck = new() { Text = "Varsayılan yazıcıyı kullan", AutoSize = true };

    public string? SelectedPrinter { get; private set; }
    public string PrinterWidth { get; private set; } = "58mm";
    public int FontSizeAdjustment { get; private set; }

    public PrinterSettingsForm(string? currentPrinter, string printerWidth = "58mm", int fontSizeAdjustment = 0)
    {
        Text = "Yazıcı Ayarları";
        Icon = ResourceHelper.GetTrayIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(340, 280);

        var printers = PrinterSettings.InstalledPrinters.Cast<string>().ToList();
        foreach (var printer in printers)
        {
            _printerCombo.Items.Add(printer);
        }

        var defaultPrinter = new PrinterSettings().PrinterName;
        if (!string.IsNullOrWhiteSpace(currentPrinter) && printers.Contains(currentPrinter))
        {
            _printerCombo.SelectedItem = currentPrinter;
            _useDefaultCheck.Checked = false;
        }
        else
        {
            _printerCombo.SelectedItem = defaultPrinter;
            _useDefaultCheck.Checked = string.IsNullOrWhiteSpace(currentPrinter);
        }

        _useDefaultCheck.CheckedChanged += (_, _) =>
        {
            _printerCombo.Enabled = !_useDefaultCheck.Checked;
        };

        _widthCombo.Items.AddRange(new object[] { "58mm (Küçük)", "80mm (Büyük)" });
        _widthCombo.SelectedIndex = printerWidth.StartsWith("80") ? 1 : 0;
        _fontSizeAdjust.Value = Math.Max(-3, Math.Min(3, fontSizeAdjustment));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 9,
            AutoScroll = true
        };

        layout.Controls.Add(new Label { Text = "Yazıcı Seçin", AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        layout.Controls.Add(_printerCombo);
        layout.Controls.Add(_useDefaultCheck);
        layout.Controls.Add(new Label { Text = "", Height = 8 });
        layout.Controls.Add(new Label { Text = "Fiş Boyutu", AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        layout.Controls.Add(_widthCombo);
        layout.Controls.Add(new Label { Text = "", Height = 8 });
        layout.Controls.Add(new Label { Text = "Font Boyutu Ayarı (-3 ile +3 arası)", AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        layout.Controls.Add(_fontSizeAdjust);

        var buttonPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        buttonPanel.Controls.Add(_okButton);
        buttonPanel.Controls.Add(_cancelButton);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        layout.Controls.Add(buttonPanel);
        Controls.Add(layout);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            if (_useDefaultCheck.Checked)
            {
                SelectedPrinter = null;
            }
            else if (_printerCombo.SelectedItem is string printer)
            {
                SelectedPrinter = printer;
            }
            else
            {
                MessageBox.Show(this, "Lütfen bir yazıcı seçin veya varsayılanı kullanmayı seçin.", "Uyarı",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                e.Cancel = true;
                return;
            }

            PrinterWidth = _widthCombo.SelectedIndex == 1 ? "80mm" : "58mm";
            FontSizeAdjustment = (int)_fontSizeAdjust.Value;
        }
        base.OnFormClosing(e);
    }
}

