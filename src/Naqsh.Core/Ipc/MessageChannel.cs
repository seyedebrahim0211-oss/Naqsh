using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json;

namespace Naqsh.Core.Ipc;

/// <summary>
/// Length-prefixed (int32 little-endian) UTF-8 JSON messages over any duplex stream (named pipe).
/// Sends are serialized with a semaphore so several threads may send safely.
/// </summary>
public sealed class MessageChannel : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Stream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public MessageChannel(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public static byte[] Encode(IpcMessage message)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (json.Length > IpcProtocol.MaxMessageBytes)
        {
            throw new InvalidOperationException("IPC message too large.");
        }

        var buf = new byte[json.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, json.Length);
        json.CopyTo(buf, 4);
        return buf;
    }

    public static IpcMessage Decode(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<IpcMessage>(json, JsonOptions)
        ?? throw new InvalidDataException("Null IPC message.");

    public async Task SendAsync(IpcMessage message, CancellationToken ct)
    {
        var buf = Encode(message);
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(buf, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Reads the next message, or null when the other side closed the stream.</summary>
    public async Task<IpcMessage?> ReceiveAsync(CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(header, ct).ConfigureAwait(false))
        {
            return null;
        }

        var len = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (len <= 0 || len > IpcProtocol.MaxMessageBytes)
        {
            throw new InvalidDataException($"Bad IPC message length {len}.");
        }

        var body = ArrayPool<byte>.Shared.Rent(len);
        try
        {
            if (!await ReadExactAsync(body.AsMemory(0, len), ct).ConfigureAwait(false))
            {
                return null;
            }

            return Decode(body.AsSpan(0, len));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(body);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }

    private async Task<bool> ReadExactAsync(Memory<byte> buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await _stream.ReadAsync(buffer[read..], ct).ConfigureAwait(false);
            if (n == 0)
            {
                return false;
            }

            read += n;
        }

        return true;
    }
}
