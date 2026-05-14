using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordHass.Discord;

internal enum DiscordOpCode
{
    Handshake = 0,
    Frame = 1,
    Close = 2,
    Ping = 3,
    Pong = 4,
}

internal readonly record struct DiscordIpcFrame(DiscordOpCode Op, string Json);

internal static class DiscordIpcProtocol
{
    public static async Task WriteFrameAsync(Stream stream, DiscordIpcFrame frame, CancellationToken ct)
    {
        byte[] payload = Encoding.UTF8.GetBytes(frame.Json);
        byte[] header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), (int)frame.Op);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), payload.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        }
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<DiscordIpcFrame> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[8];
        await ReadExactlyAsync(stream, header, ct).ConfigureAwait(false);
        int op = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        int len = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if (len < 0 || len > 16 * 1024 * 1024)
        {
            throw new InvalidDataException($"Bogus IPC frame length: {len}");
        }
        byte[] payload = len == 0 ? Array.Empty<byte>() : new byte[len];
        if (len > 0)
        {
            await ReadExactlyAsync(stream, payload, ct).ConfigureAwait(false);
        }
        return new DiscordIpcFrame((DiscordOpCode)op, Encoding.UTF8.GetString(payload));
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Discord IPC pipe closed");
            }
            offset += read;
        }
    }
}
