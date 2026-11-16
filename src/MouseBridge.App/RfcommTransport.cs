using System.IO;
using MouseBridge.Core;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
namespace MouseBridge.App;
// Explicit native partial reads: never wait to fill an adapter's read-ahead buffer.
internal sealed class RfcommTransport : IMessageTransport
{
    private readonly DataReader reader;
    private readonly DataWriter writer;
    private readonly SemaphoreSlim sendGate = new(1);
    public RfcommTransport(StreamSocket socket)
    {
        reader = new(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        writer = new(socket.OutputStream);
    }
    public async Task SendAsync(Message message, CancellationToken ct)
    {
        byte[] frame = Protocol.Encode(message);
        await sendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            writer.WriteBytes(frame);
            uint stored = await writer.StoreAsync().AsTask(ct).ConfigureAwait(false);
            if (stored != frame.Length) throw new IOException("Incomplete RFCOMM write");
            if (!await writer.FlushAsync().AsTask(ct).ConfigureAwait(false)) throw new IOException("RFCOMM flush failed");
        }
        finally { sendGate.Release(); }
    }
    private async Task<byte[]?> ReadExact(int length, bool allowCleanEof, CancellationToken ct)
    {
        var bytes = new byte[length]; int offset = 0;
        while (offset < length)
        {
            uint loaded = await reader.LoadAsync((uint)(length - offset)).AsTask(ct).ConfigureAwait(false);
            if (loaded == 0)
            {
                if (offset == 0 && allowCleanEof) return null;
                throw new EndOfStreamException("Truncated RFCOMM frame");
            }
            var chunk = new byte[loaded]; reader.ReadBytes(chunk);
            chunk.CopyTo(bytes, offset); offset += (int)loaded;
        }
        return bytes;
    }
    public async Task<Message?> ReceiveAsync(CancellationToken ct)
    {
        var header = await ReadExact(4, true, ct).ConfigureAwait(false);
        if (header == null) return null;
        var body = await ReadExact(Protocol.GetBodyLength(header), false, ct).ConfigureAwait(false);
        return Protocol.DecodeBody(body!);
    }
    public ValueTask DisposeAsync()
    {
        try { reader.DetachStream(); } finally { reader.Dispose(); }
        try { writer.DetachStream(); } finally { writer.Dispose(); }
        return ValueTask.CompletedTask;
    }
}
