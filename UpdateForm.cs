using System.ComponentModel;
using System.Diagnostics;
using System.Net;

namespace NextDnsDoh;

internal sealed class UpdateForm : Form
{
    private const string SilentInstallArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";

    private readonly UpdateInfo _update;
    private readonly Button _updateButton;
    private readonly Button _closeButton;
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private WebClient? _client;
    private string? _installerPath;

    public UpdateForm(string installedVersion, UpdateInfo update)
    {
        _update = update;

        Text = "NextDNS DoH - Update";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = true;
        Icon = TrayIcons.Create(enabled: true);
        ShowInTaskbar = true;
        ClientSize = new Size(420, 368);
        Font = new Font("Segoe UI", 9F);

        var installedLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 16),
            Text = $"Installed version: {installedVersion}"
        };

        var newLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 38),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Text = $"New version: {update.DisplayVersion}"
        };

        var notesLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 68),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Text = "What's new:"
        };

        var notes = new TextBox
        {
            Location = new Point(16, 88),
            Size = new Size(388, 180),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Text = string.IsNullOrWhiteSpace(update.ReleaseNotes)
                ? "No release notes."
                : update.ReleaseNotes
        };
        notes.GotFocus += (_, _) => notes.Select(0, 0);

        var link = new LinkLabel
        {
            AutoSize = true,
            Location = new Point(16, 278),
            Text = "Release page on GitHub",
            LinkColor = Color.FromArgb(0, 102, 204),
            ActiveLinkColor = Color.FromArgb(0, 82, 164)
        };
        link.LinkClicked += (_, _) => OpenUrl(update.ReleasePageUrl);

        _status = new Label
        {
            AutoSize = false,
            Location = new Point(16, 302),
            Size = new Size(388, 18),
            ForeColor = SystemColors.GrayText,
            Text = "Installing asks for Administrator rights once."
        };

        _progress = new ProgressBar
        {
            Location = new Point(16, 332),
            Size = new Size(196, 22),
            Visible = false
        };

        _updateButton = new Button
        {
            Text = "Update now",
            Location = new Point(224, 330),
            Size = new Size(96, 26)
        };
        _updateButton.Click += (_, _) => StartDownload();

        _closeButton = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.Cancel,
            Location = new Point(329, 330),
            Size = new Size(75, 26)
        };

        AcceptButton = _updateButton;
        CancelButton = _closeButton;
        Controls.AddRange(new Control[]
        {
            installedLabel, newLabel, notesLabel, notes, link,
            _status, _progress, _updateButton, _closeButton
        });
        FormClosed += (_, _) => Icon?.Dispose();
        FormClosing += OnFormClosing;
    }

    private void StartDownload()
    {
        _updateButton.Enabled = false;
        _progress.Visible = true;
        _progress.Value = 0;
        _status.ForeColor = SystemColors.GrayText;
        _status.Text = "Downloading the installer...";

        _installerPath = Path.Combine(
            Path.GetTempPath(),
            $"NextDNS-DoH-{_update.DisplayVersion}.exe");

        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            _client = new WebClient();
            _client.Headers.Add(HttpRequestHeader.UserAgent, "nextdns-doh");
            _client.DownloadProgressChanged += OnDownloadProgress;
            _client.DownloadFileCompleted += OnDownloadCompleted;
            _client.DownloadFileAsync(new Uri(_update.DownloadUrl), _installerPath);
        }
        catch (Exception ex)
        {
            DisposeClient();
            Fail(ex.Message);
        }
    }

    private void OnDownloadProgress(object sender, DownloadProgressChangedEventArgs e)
    {
        _progress.Value = Math.Min(100, Math.Max(0, e.ProgressPercentage));
    }

    private void OnDownloadCompleted(object sender, AsyncCompletedEventArgs e)
    {
        DisposeClient();

        if (e.Cancelled)
        {
            return;
        }

        if (e.Error is not null)
        {
            Fail(e.Error.Message);
            return;
        }

        if (_update.Size > 0 && new FileInfo(_installerPath!).Length != _update.Size)
        {
            Fail("The downloaded installer is incomplete.");
            return;
        }

        _status.Text = "Starting the installer...";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _installerPath!,
                Arguments = SilentInstallArguments,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
            return;
        }

        // Setup closes this app and starts the new version once it is done.
        Close();
    }

    private void Fail(string reason)
    {
        DeleteInstaller();
        _progress.Visible = false;
        _status.ForeColor = SystemColors.ControlText;
        _status.Text = "The update could not be installed.";
        _updateButton.Enabled = true;

        var answer = MessageBox.Show(
            this,
            $"{reason}{Environment.NewLine}{Environment.NewLine}Open the release page to download the update yourself?",
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer == DialogResult.Yes)
        {
            OpenUrl(_update.ReleasePageUrl);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_client is null)
        {
            return;
        }

        _client.CancelAsync();
        DisposeClient();
        DeleteInstaller();
    }

    private void DisposeClient()
    {
        if (_client is null)
        {
            return;
        }

        _client.DownloadProgressChanged -= OnDownloadProgress;
        _client.DownloadFileCompleted -= OnDownloadCompleted;
        _client.Dispose();
        _client = null;
    }

    private void DeleteInstaller()
    {
        if (string.IsNullOrEmpty(_installerPath))
        {
            return;
        }

        try
        {
            File.Delete(_installerPath);
        }
        catch
        {
            // A leftover file in %TEMP% is harmless.
        }

        _installerPath = null;
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
