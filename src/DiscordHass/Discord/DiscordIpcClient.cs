using System;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordHass.Discord;

internal sealed class DiscordIpcClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();

    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private int _nextNonce;

    public bool IsConnected => _pipe?.IsConnected ?? false;

    public event EventHandler? Disconnected;
    public event EventHandler<DiscordIpcEvent>? EventReceived;

    public async Task ConnectAndHandshakeAsync(string clientId, CancellationToken ct)
    {
        for (int i = 0; i <= 9; i++)
        {
            string pipeName = $"discord-ipc-{i}";
            NamedPipeClientStream candidate = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await candidate.ConnectAsync(timeout: 1000, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                candidate.Dispose();
                continue;
            }
            catch (Exception)
            {
                candidate.Dispose();
                continue;
            }

            // Send Handshake
            string handshakeJson = JsonSerializer.Serialize(new { v = 1, client_id = clientId }, JsonOptions);
            await DiscordIpcProtocol.WriteFrameAsync(candidate, new DiscordIpcFrame(DiscordOpCode.Handshake, handshakeJson), ct).ConfigureAwait(false);

            DiscordIpcFrame ready = await DiscordIpcProtocol.ReadFrameAsync(candidate, ct).ConfigureAwait(false);
            if (ready.Op != DiscordOpCode.Frame)
            {
                candidate.Dispose();
                throw new DiscordIpcCommandException($"Unexpected handshake response op: {ready.Op}");
            }

            DiscordIpcMessage? msg = JsonSerializer.Deserialize<DiscordIpcMessage>(ready.Json, JsonOptions);
            if (msg?.Evt != "READY")
            {
                candidate.Dispose();
                throw new DiscordIpcCommandException($"Expected READY event, got: {ready.Json}");
            }

            _pipe = candidate;
            _receiveCts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));
            return;
        }

        throw new DiscordIpcCommandException("Could not connect to any discord-ipc-N pipe; is Discord running?");
    }

    public Task<JsonElement> SendCommandAsync(string cmd, object args, CancellationToken ct)
    {
        return SendFrameAsync(cmd, args, evt: null, ct);
    }

    public Task<JsonElement> SubscribeAsync(string evt, object args, CancellationToken ct)
    {
        return SendFrameAsync("SUBSCRIBE", args, evt, ct);
    }

    public Task<JsonElement> UnsubscribeAsync(string evt, object args, CancellationToken ct)
    {
        return SendFrameAsync("UNSUBSCRIBE", args, evt, ct);
    }

    private async Task<JsonElement> SendFrameAsync(string cmd, object args, string? evt, CancellationToken ct)
    {
        NamedPipeClientStream pipe = _pipe ?? throw new InvalidOperationException("Not connected");

        string nonce = Interlocked.Increment(ref _nextNonce).ToString(System.Globalization.CultureInfo.InvariantCulture);
        TaskCompletionSource<JsonElement> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[nonce] = tcs;

        object payload = evt is null
            ? new { cmd, nonce, args }
            : new { cmd, nonce, evt, args };
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        RpcDebugLog.LogSend(evt ?? cmd, json);

        try
        {
            await DiscordIpcProtocol.WriteFrameAsync(pipe, new DiscordIpcFrame(DiscordOpCode.Frame, json), ct).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(nonce, out _);
            throw;
        }

        using CancellationTokenRegistration reg = ct.Register(() => tcs.TrySetCanceled(ct));
        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(nonce, out _);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        NamedPipeClientStream pipe = _pipe!;
        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                DiscordIpcFrame frame = await DiscordIpcProtocol.ReadFrameAsync(pipe, ct).ConfigureAwait(false);
                RpcDebugLog.LogRecv(frame.Op.ToString(), frame.Json);
                switch (frame.Op)
                {
                    case DiscordOpCode.Frame:
                        DispatchFrame(frame.Json);
                        break;
                    case DiscordOpCode.Ping:
                        await DiscordIpcProtocol.WriteFrameAsync(pipe, new DiscordIpcFrame(DiscordOpCode.Pong, frame.Json), ct).ConfigureAwait(false);
                        break;
                    case DiscordOpCode.Close:
                        return;
                    default:
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            FailPending(new DiscordIpcCommandException($"Receive loop terminated: {ex.Message}"));
        }
        finally
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DispatchFrame(string json)
    {
        DiscordIpcMessage? msg = JsonSerializer.Deserialize<DiscordIpcMessage>(json, JsonOptions);
        if (msg is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(msg.Nonce) && _pending.TryRemove(msg.Nonce!, out TaskCompletionSource<JsonElement>? tcs))
        {
            if (msg.Evt == "ERROR")
            {
                string errMsg = "Discord IPC error";
                string? code = null;
                if (msg.Data is JsonElement data && data.ValueKind == JsonValueKind.Object)
                {
                    if (data.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String)
                    {
                        errMsg = m.GetString() ?? errMsg;
                    }
                    if (data.TryGetProperty("code", out JsonElement c))
                    {
                        code = c.ToString();
                    }
                }
                tcs.TrySetException(new DiscordIpcCommandException(errMsg, code));
            }
            else
            {
                tcs.TrySetResult(msg.Data ?? default);
            }
            return;
        }

        // Unsolicited event (subscription delivery)
        if (!string.IsNullOrEmpty(msg.Evt) && msg.Evt != "READY")
        {
            JsonElement data = msg.Data ?? default;
            RpcDebugLog.LogEvent(msg.Evt!, data.ValueKind == JsonValueKind.Undefined ? "(no data)" : data.GetRawText());
            EventReceived?.Invoke(this, new DiscordIpcEvent(msg.Evt!, data));
        }
    }

    private void FailPending(Exception ex)
    {
        foreach (var kv in _pending)
        {
            kv.Value.TrySetException(ex);
        }
        _pending.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        _receiveCts?.Cancel();
        try
        {
            _pipe?.Dispose();
            if (_receiveTask is not null)
            {
                try
                {
                    await _receiveTask.ConfigureAwait(false);
                }
                catch
                {
                    // suppressed
                }
            }
        }
        finally
        {
            FailPending(new DiscordIpcCommandException("Disposed"));
            _receiveCts?.Dispose();
        }
    }
}

internal sealed record DiscordIpcEvent(string EventName, JsonElement Data);
