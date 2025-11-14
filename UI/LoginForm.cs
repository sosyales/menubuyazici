using System;
using System.Drawing;
using System.Windows.Forms;

namespace MenuBuPrinterAgent.UI;

internal sealed class LoginForm : Form
{
    private readonly TextBox _emailBox = new() { PlaceholderText = "Email", Width = 260 };
    private readonly TextBox _passwordBox = new() { PlaceholderText = "Şifre", UseSystemPasswordChar = true, Width = 260 };
    private readonly Button _loginButton = new() { Text = "Giriş Yap", DialogResult = DialogResult.OK, Width = 120 };
    private readonly Button _cancelButton = new() { Text = "Vazgeç", DialogResult = DialogResult.Cancel, Width = 120 };

    public string Email => _emailBox.Text.Trim();
    public string Password => _passwordBox.Text;

    public LoginForm(string? email = null, string? password = null)
    {
        Text = "MenuBu Yazıcı Ajanı - Giriş";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(320, 160);
        Icon = ResourceHelper.GetTrayIcon();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 5
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var emailLabel = new Label { Text = "Email", AutoSize = true };
        var passwordLabel = new Label { Text = "Şifre", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };

        if (!string.IsNullOrWhiteSpace(email))
        {
            _emailBox.Text = email;
        }
        if (!string.IsNullOrWhiteSpace(password))
        {
            _passwordBox.Text = password;
        }

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0)
        };
        buttonPanel.Controls.Add(_loginButton);
        buttonPanel.Controls.Add(_cancelButton);

        AcceptButton = _loginButton;
        CancelButton = _cancelButton;

        layout.Controls.Add(emailLabel);
        layout.Controls.Add(_emailBox);
        layout.Controls.Add(passwordLabel);
        layout.Controls.Add(_passwordBox);
        layout.Controls.Add(buttonPanel);

        Controls.Add(layout);
    }
}

