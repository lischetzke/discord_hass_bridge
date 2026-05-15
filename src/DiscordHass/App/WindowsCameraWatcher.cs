using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DiscordHass.App;

/// <summary>
/// Polls HKCU's Capability Access Manager for camera usage by any Discord executable,
/// and raises <see cref="CameraStateChanged"/> when the result transitions. Required
/// because Discord's local RPC API does not expose <c>self_video</c> to
/// user-registered applications — see <see cref="CapabilityAccessParser"/> for the
/// background.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsCameraWatcher : IAsyncDisposable
{
    private const string WebcamConsentKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\NonPackaged";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1000);

    private CancellationTokenSource _cts = new();
    private Task? _runner;
    private bool _lastReported;

    /// <summary>Latest sampled state. Race-free for boolean reads in CLR.</summary>
    public bool CameraInUse => _lastReported;

    /// <summary>Path of the exe currently holding the camera, if any. Null when no Discord exe is using it.</summary>
    public string? InUseExePath { get; private set; }

    public event EventHandler<bool>? CameraStateChanged;

    public void Start()
    {
        if (_runner is not null) return;
        _cts = new CancellationTokenSource();
        _runner = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        if (_runner is not null)
        {
            try { await _runner.ConfigureAwait(false); } catch { /* shutdown */ }
            _runner = null;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Sample sample = Probe();
                if (sample.InUse != _lastReported)
                {
                    _lastReported = sample.InUse;
                    InUseExePath = sample.ExePath;
                    CameraStateChanged?.Invoke(this, sample.InUse);
                }
            }
            catch
            {
                // Swallow — never throw out of this background loop.
            }

            try { await Task.Delay(PollInterval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static Sample Probe()
    {
        using RegistryKey? root = Registry.CurrentUser.OpenSubKey(WebcamConsentKeyPath);
        if (root is null) return Sample.False;

        List<CapabilityAccessEntry> entries = new();
        foreach (string name in root.GetSubKeyNames())
        {
            using RegistryKey? sub = root.OpenSubKey(name);
            if (sub is null) continue;
            long start = CapabilityAccessParser.AsFileTimeLong(sub.GetValue("LastUsedTimeStart"));
            long stop  = CapabilityAccessParser.AsFileTimeLong(sub.GetValue("LastUsedTimeStop"));
            entries.Add(new CapabilityAccessEntry(name, start, stop));
        }

        string? exe = CapabilityAccessParser.FindInUseExe(entries, CapabilityAccessParser.DiscordExeBasenames);
        return exe is null ? Sample.False : new Sample(true, exe);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    private readonly record struct Sample(bool InUse, string? ExePath)
    {
        public static readonly Sample False = new(false, null);
    }
}
