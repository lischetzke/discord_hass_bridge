using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscordHass.Discord;
using Xunit;

namespace DiscordHass.Tests;

public class DiscordIpcProtocolTests
{
    [Fact]
    public async Task RoundTrip_HandshakeFrame()
    {
        DiscordIpcFrame original = new(DiscordOpCode.Handshake, "{\"v\":1,\"client_id\":\"123\"}");
        using MemoryStream ms = new();
        await DiscordIpcProtocol.WriteFrameAsync(ms, original, CancellationToken.None);
        ms.Position = 0;
        DiscordIpcFrame roundTripped = await DiscordIpcProtocol.ReadFrameAsync(ms, CancellationToken.None);
        Assert.Equal(original.Op, roundTripped.Op);
        Assert.Equal(original.Json, roundTripped.Json);
    }

    [Theory]
    [InlineData(1, "{\"cmd\":\"AUTHORIZE\",\"args\":{\"client_id\":\"123\"}}")]
    [InlineData(3, "{}")]
    [InlineData(2, "{\"code\":1000,\"message\":\"bye\"}")]
    [InlineData(4, "")]
    public async Task RoundTrip_VariousOpcodes(int opValue, string json)
    {
        DiscordOpCode op = (DiscordOpCode)opValue;
        DiscordIpcFrame original = new(op, json);
        using MemoryStream ms = new();
        await DiscordIpcProtocol.WriteFrameAsync(ms, original, CancellationToken.None);
        ms.Position = 0;
        DiscordIpcFrame roundTripped = await DiscordIpcProtocol.ReadFrameAsync(ms, CancellationToken.None);
        Assert.Equal(op, roundTripped.Op);
        Assert.Equal(json, roundTripped.Json);
    }

    [Fact]
    public async Task ReadFrame_TruncatedPayload_Throws()
    {
        // Header says 100 bytes but stream contains only 10
        byte[] header = new byte[8];
        // op = 1 (Frame), length = 100
        header[0] = 1;
        header[4] = 100;
        byte[] partialPayload = new byte[10];
        using MemoryStream ms = new();
        ms.Write(header, 0, header.Length);
        ms.Write(partialPayload, 0, partialPayload.Length);
        ms.Position = 0;
        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            DiscordIpcProtocol.ReadFrameAsync(ms, CancellationToken.None));
    }
}
