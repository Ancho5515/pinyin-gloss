using System.Buffers.Binary;
using System.Text.Json;

namespace PinyinGloss.Core;
public static class PipeProtocol
{
    public const int MaxFrameBytes = 64 * 1024;
    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4];
        if (await stream.ReadAsync(header.AsMemory(0, 1), token) == 0) return default;
        await stream.ReadExactlyAsync(header.AsMemory(1, 3), token);
        var count = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (count <= 0 || count > MaxFrameBytes) throw new InvalidDataException("IPC frame too large or invalid");
        byte[] body = new byte[count]; await stream.ReadExactlyAsync(body, token);
        return JsonSerializer.Deserialize<T>(body, SettingsJson.Options) ?? throw new InvalidDataException("Empty IPC message");
    }
    public static async Task PumpReaderAsync(Stream stream, Action<ReaderMessage> receive, CancellationToken token)
    {
        try
        {
            while (await ReadAsync<ReaderMessage>(stream, token) is { } message)
            {
                if (message.ProtocolVersion != 3) throw new InvalidDataException("Unsupported IPC version");
                receive(message);
            }
        }
        finally { receive(new(ReaderMessageKind.Hidden)); }
    }
    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken token)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, SettingsJson.Options);
        if (body.Length > MaxFrameBytes) throw new InvalidDataException("IPC frame too large");
        byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, token); await stream.WriteAsync(body, token); await stream.FlushAsync(token);
    }
}
