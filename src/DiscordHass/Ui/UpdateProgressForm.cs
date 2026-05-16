using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DiscordHass.App;
using DiscordHass.Update;

namespace DiscordHass.Ui;

internal sealed class UpdateProgressForm : Form
{
    private readonly UpdateService _updates;
    private readonly UpdateAvailable _target;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progress;
    private readonly Button _cancelButton;
    private readonly CancellationTokenSource _cts = new();
    private bool _completed;

    public UpdateProgressForm(UpdateService updates, UpdateAvailable target)
    {
        _updates = updates;
        _target  = target;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = $"{AppConstants.DisplayName} — Updating";
        ClientSize = new Size(460, 180);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = true;
        BackColor = ThemeColors.Background;
        ForeColor = ThemeColors.OnSurface;

        _titleLabel = new Label
        {
            Text = $"Downloading {target.TagName}…",
            Location = new Point(20, 20),
            AutoSize = false,
            Width = 440,
            Height = 22,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = ThemeColors.OnSurface,
        };

        _progress = new ProgressBar
        {
            Location = new Point(20, 56),
            Width = 440,
            Height = 24,
            Minimum = 0,
            Maximum = 1000,
            Value = 0,
            Style = ProgressBarStyle.Continuous,
        };

        _statusLabel = new Label
        {
            Location = new Point(20, 92),
            AutoSize = false,
            Width = 440,
            Height = 22,
            ForeColor = ThemeColors.OnSurfaceDim,
            Text = $"Connecting to GitHub… ({FormatBytes(target.ExeAssetSize)} expected)",
        };

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(360, 130),
            Width = 100,
            Height = 28,
        };
        _cancelButton.Click += (_, _) =>
        {
            _cts.Cancel();
            _cancelButton.Enabled = false;
            _statusLabel.Text = "Cancelling…";
        };

        Controls.AddRange(new Control[] { _titleLabel, _progress, _statusLabel, _cancelButton });

        FormClosing += (_, e) =>
        {
            if (!_completed)
            {
                _cts.Cancel();
            }
        };

        ResumeLayout(performLayout: true);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await RunUpdateAsync().ConfigureAwait(true);
    }

    private async Task RunUpdateAsync()
    {
        Progress<DownloadProgress> progress = new(p =>
        {
            if (IsDisposed) return;
            int promille = p.TotalBytes is > 0 ? (int)Math.Round(p.Fraction * 1000) : 0;
            _progress.Value = Math.Clamp(promille, 0, 1000);
            string statusLine = p.TotalBytes is > 0
                ? $"Downloading… {FormatBytes(p.BytesRead)} / {FormatBytes(p.TotalBytes.Value)}"
                : $"Downloading… {FormatBytes(p.BytesRead)}";
            _statusLabel.Text = statusLine;
        });

        try
        {
            bool ok = await _updates.DownloadAndInstallAsync(progress, _cts.Token).ConfigureAwait(true);
            if (!ok)
            {
                _titleLabel.Text = "Update failed";
                _titleLabel.ForeColor = ThemeColors.StatusError;
                _statusLabel.Text = _updates.LastError ?? "Unknown error.";
                _cancelButton.Text = "Close";
                _cancelButton.Enabled = true;
                _cancelButton.Click -= null!;
                _cancelButton.Click += (_, _) => Close();
                return;
            }

            // On success the installer has spawned the new exe and the bridge is about
            // to exit; show a brief "Restarting…" message before the process dies.
            _completed = true;
            _titleLabel.Text = $"Installed {_target.TagName} — restarting…";
            _statusLabel.Text = "The new version is starting in a moment.";
            _progress.Value = 1000;
            _cancelButton.Enabled = false;

            await Task.Delay(800).ConfigureAwait(true);

            Application.Exit();
        }
        catch (OperationCanceledException)
        {
            _titleLabel.Text = "Update cancelled";
            _titleLabel.ForeColor = ThemeColors.OnSurfaceDim;
            _statusLabel.Text = "No changes were made.";
            _cancelButton.Text = "Close";
            _cancelButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _titleLabel.Text = "Update failed";
            _titleLabel.ForeColor = ThemeColors.StatusError;
            _statusLabel.Text = ex.Message;
            _cancelButton.Text = "Close";
            _cancelButton.Enabled = true;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        const long mb = 1024L * 1024L;
        if (bytes >= mb) return $"{bytes / (double)mb:0.0} MB";
        const long kb = 1024L;
        if (bytes >= kb) return $"{bytes / (double)kb:0.0} KB";
        return $"{bytes} B";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}
