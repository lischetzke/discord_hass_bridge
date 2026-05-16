using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DiscordHass.App;
using DiscordHass.Config;

namespace DiscordHass.Update;

internal enum UpdateState
{
    Idle,
    Checking,
    UpdateAvailable,
    Downloading,
    Verifying,
    Installing,
    Faulted,
}

internal sealed class UpdateService : IAsyncDisposable
{
    private static readonly TimeSpan InitialCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval    = TimeSpan.FromHours(24);

    private readonly AppConfig _config;
    private readonly ConfigStore _configStore;
    private readonly UpdateChecker _checker;
    private readonly UpdateDownloader _downloader;
    private readonly HttpClient _http;

    private readonly SemaphoreSlim _checkLock    = new(1, 1);
    private readonly SemaphoreSlim _downloadLock = new(1, 1);
    private CancellationTokenSource _cts = new();
    private Task? _runner;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public string? LastError { get; private set; }
    public UpdateAvailable? Available { get; private set; }
    public DownloadProgress LastDownloadProgress { get; private set; }
    public ReleaseVersion CurrentVersion { get; }

    public event EventHandler? StateChanged;
    public event EventHandler<DownloadProgress>? DownloadProgressed;

    public UpdateService(AppConfig config, ConfigStore configStore)
    {
        _config = config;
        _configStore = configStore;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _checker = new UpdateChecker(_http);
        _downloader = new UpdateDownloader(_http);

        ReleaseVersion.TryParse(AppConstants.GetVersionString(), out ReleaseVersion v);
        CurrentVersion = v;
    }

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

    public async Task<bool> CheckNowAsync(CancellationToken ct = default)
    {
        await _checkLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SetState(UpdateState.Checking, null);
            UpdateAvailable? avail = await _checker.CheckAsync(CurrentVersion, ct).ConfigureAwait(false);
            _config.LastUpdateCheckUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            try { _configStore.Save(_config); } catch { /* tolerate */ }

            Available = avail;
            if (avail is null)
            {
                SetState(UpdateState.Idle, null);
                return false;
            }
            SetState(UpdateState.UpdateAvailable, null);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            SetState(UpdateState.Faulted, ex.Message);
            return false;
        }
        finally
        {
            _checkLock.Release();
        }
    }

    /// <summary>
    /// Downloads + verifies + installs the available update. On success, control does not
    /// return: the new exe is launched and the caller is expected to exit. On failure,
    /// returns false with <see cref="LastError"/> populated.
    /// </summary>
    public async Task<bool> DownloadAndInstallAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        UpdateAvailable? avail = Available;
        if (avail is null)
        {
            SetState(UpdateState.Faulted, "No update is available to install");
            return false;
        }

        await _downloadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Defense-in-depth: UpdateChecker.SelectExeAsset already rejects unsafe names,
            // but re-validate so a future bug in the checker can't redirect the staging path.
            if (!UpdateChecker.IsSafeAssetName(avail.ExeAssetName))
            {
                throw new UpdateDownloadException($"Refusing unsafe asset name: {avail.ExeAssetName}");
            }
            string safeName = Path.GetFileName(avail.ExeAssetName);
            string stagingPath = Path.Combine(AppPaths.UpdateStagingDir, safeName);
            Directory.CreateDirectory(AppPaths.UpdateStagingDir);

            SetState(UpdateState.Downloading, null);
            IProgress<DownloadProgress> combinedProgress = new Progress<DownloadProgress>(p =>
            {
                LastDownloadProgress = p;
                DownloadProgressed?.Invoke(this, p);
                progress?.Report(p);
            });
            await _downloader.DownloadAndVerifyAsync(avail, stagingPath, combinedProgress, ct).ConfigureAwait(false);

            SetState(UpdateState.Installing, null);
            await UpdateInstaller.SwapAndRelaunchAsync(stagingPath).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            SetState(UpdateState.Faulted, ex.Message);
            return false;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await SafeDelay(InitialCheckDelay, ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                if (_config.CheckUpdatesAutomatically)
                {
                    try { await CheckNowAsync(ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    catch { /* CheckNowAsync swallows non-cancellation already */ }
                }
                await SafeDelay(CheckInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    private void SetState(UpdateState newState, string? error)
    {
        State = newState;
        LastError = error;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
        _checkLock.Dispose();
        _downloadLock.Dispose();
        _http.Dispose();
    }
}
