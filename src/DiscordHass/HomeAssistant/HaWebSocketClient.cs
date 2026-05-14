using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordHass.HomeAssistant;

internal sealed class HaWebSocketClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Uri _wsUri;
    private readonly string _accessToken;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private int _nextId = 1;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public event EventHandler? Disconnected;

    public HaWebSocketClient(string baseUrl, string accessToken)
    {
        _wsUri = BuildWebSocketUri(baseUrl);
        _accessToken = accessToken;
    }

    public async Task ConnectAndAuthenticateAsync(CancellationToken ct)
    {
        ClientWebSocket socket = new();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        await socket.ConnectAsync(_wsUri, ct).ConfigureAwait(false);

        HaServerMessage authRequired = await ReceiveMessageAsync(socket, ct).ConfigureAwait(false);
        if (authRequired.Type != "auth_required")
        {
            throw new HaCommandException($"Expected auth_required, got {authRequired.Type}");
        }

        await SendRawAsync(socket, new { type = "auth", access_token = _accessToken }, ct).ConfigureAwait(false);

        HaServerMessage authResult = await ReceiveMessageAsync(socket, ct).ConfigureAwait(false);
        if (authResult.Type == "auth_invalid")
        {
            socket.Dispose();
            throw new HaCommandException(authResult.Message ?? "Authentication failed", "auth_invalid");
        }
        if (authResult.Type != "auth_ok")
        {
            socket.Dispose();
            throw new HaCommandException($"Expected auth_ok, got {authResult.Type}");
        }

        _socket = socket;
        _receiveCts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));
    }

    public async Task<JsonElement> SendCommandAsync(object payload, CancellationToken ct)
    {
        ClientWebSocket socket = _socket ?? throw new InvalidOperationException("Not connected");

        int id = Interlocked.Increment(ref _nextId);
        TaskCompletionSource<JsonElement> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        object envelope = MergeWithId(payload, id);
        try
        {
            await SendRawAsync(socket, envelope, ct).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }

        using CancellationTokenRegistration reg = ct.Register(() => tcs.TrySetCanceled(ct));
        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _receiveCts?.Cancel();
        try
        {
            if (_socket is { State: WebSocketState.Open })
            {
                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
                try
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // best effort
                }
            }
            _socket?.Dispose();
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
            FailPending(new HaCommandException("Disposed"));
            _receiveCts?.Dispose();
            _sendLock.Dispose();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        ClientWebSocket socket = _socket!;
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                HaServerMessage msg = await ReceiveMessageAsync(socket, ct).ConfigureAwait(false);
                DispatchMessage(msg);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            FailPending(new HaCommandException($"Receive loop terminated: {ex.Message}"));
        }
        finally
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DispatchMessage(HaServerMessage msg)
    {
        if (msg.Type != "result" || msg.Id is null)
        {
            return;
        }

        if (!_pending.TryRemove(msg.Id.Value, out TaskCompletionSource<JsonElement>? tcs))
        {
            return;
        }

        if (msg.Success == true)
        {
            tcs.TrySetResult(msg.Result ?? default);
        }
        else
        {
            string errMessage = msg.Error?.Message ?? "Unknown HA error";
            tcs.TrySetException(new HaCommandException(errMessage, msg.Error?.Code));
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

    private async Task SendRawAsync(ClientWebSocket socket, object payload, CancellationToken ct)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async Task<HaServerMessage> ReceiveMessageAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using MemoryStream ms = new();
        byte[] buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException("Connection closed by server");
            }
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        ms.Position = 0;
        HaServerMessage? msg = JsonSerializer.Deserialize<HaServerMessage>(ms.GetBuffer().AsSpan(0, (int)ms.Length), JsonOptions);
        return msg ?? throw new HaCommandException("Failed to deserialize HA message");
    }

    private static object MergeWithId(object payload, int id)
    {
        // Serialize the payload to a dictionary so we can inject the id field.
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        using JsonDocument doc = JsonDocument.Parse(json);
        var dict = new System.Collections.Generic.Dictionary<string, object?>();
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = JsonElementToObject(prop.Value);
        }
        dict["id"] = id;
        return dict;
    }

    private static object? JsonElementToObject(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out long l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => JsonSerializer.Deserialize<object>(el.GetRawText(), JsonOptions),
        };
    }

    private static Uri BuildWebSocketUri(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Home Assistant base URL is required", nameof(baseUrl));
        }

        UriBuilder b = new(baseUrl.TrimEnd('/') + "/api/websocket");
        b.Scheme = b.Scheme switch
        {
            "http" => "ws",
            "https" => "wss",
            "ws" => "ws",
            "wss" => "wss",
            _ => throw new ArgumentException($"Unsupported scheme '{b.Scheme}' in base URL"),
        };
        return b.Uri;
    }
}
