using System.Diagnostics;

namespace NextDnsDoh;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _idBox;

    public string ConfigurationId => _idBox.Text.Trim();

    public SettingsForm(string currentId)
    {
        Text = "NextDNS DoH configuration";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = true;
        Icon = TrayIcons.Create(enabled: true);
        ShowInTaskbar = true;
        ClientSize = new Size(360, 150);
        Font = new Font("Segoe UI", 9F);

        var intro = new LinkLabel
        {
            AutoSize = false,
            Location = new Point(16, 16),
            Size = new Size(328, 32),
            Text = "Enter the ID from my.nextdns.io.\nhttps://dns.nextdns.io/[ID]",
            LinkColor = Color.FromArgb(0, 102, 204),
            ActiveLinkColor = Color.FromArgb(0, 82, 164)
        };
        var linkText = "my.nextdns.io";
        var linkIndex = intro.Text.IndexOf(linkText, StringComparison.Ordinal);
        intro.Links.Add(linkIndex, linkText.Length, "https://my.nextdns.io");
        intro.LinkClicked += (_, e) =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = (string)e.Link.LinkData,
                UseShellExecute = true
            });
        };

        var idLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 56),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Text = "ID:"
        };

        _idBox = new TextBox
        {
            Location = new Point(16, 76),
            Size = new Size(328, 23),
            Text = currentId
        };

        var save = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Location = new Point(188, 112),
            Size = new Size(75, 26)
        };
        save.Click += (_, _) =>
        {
            try
            {
                DnsManager.NormalizeId(_idBox.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        };

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(269, 112),
            Size = new Size(75, 26)
        };

        AcceptButton = save;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { intro, idLabel, _idBox, save, cancel });
        FormClosed += (_, _) => Icon?.Dispose();
    }
}
