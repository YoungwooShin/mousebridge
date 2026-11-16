using System.Buffers.Binary;
namespace MouseBridge.Core;
public enum MessageKind : byte { Text = 1, Binary = 2, Ping = 3, Pong = 4, ControlStart = 10, ControlGrant = 11, ControlStop = 12, InputBatch = 13, Heartbeat = 14, HeartbeatAck = 15, LinkPulse = 16, LinkPulseAck = 17, ClipboardBegin = 20, ClipboardChunk = 21, ClipboardEnd = 22, ControlReturn = 23, EdgeEnter = 24, EdgeReturn = 25, EdgeConfigure = 26 }
public record Message(MessageKind Kind, byte[] Payload);
public interface IMessageTransport : IAsyncDisposable
{
    Task SendAsync(Message message, CancellationToken ct);
    Task<Message?> ReceiveAsync(CancellationToken ct);
}
// Wire: 4-byte big-endian length (kind + payload), 1-byte kind, payload.
public static class Protocol
{
    public const int MaxPayload = 65536;
    public static byte[] Encode(Message m)
    {
        if (!Enum.IsDefined(m.Kind) || m.Payload.Length > MaxPayload) throw new InvalidDataException("Invalid message");
        var bytes = new byte[5 + m.Payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(bytes, m.Payload.Length + 1);
        bytes[4] = (byte)m.Kind; m.Payload.CopyTo(bytes, 5); return bytes;
    }
    public static async Task<Message?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (await stream.ReadAsync(header.AsMemory(0, 1), ct) == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), ct);
        int length = GetBodyLength(header);
        var body = new byte[length]; await stream.ReadExactlyAsync(body, ct);
        return DecodeBody(body);
    }
    public static int GetBodyLength(ReadOnlySpan<byte> header)
    {
        if (header.Length != 4) throw new InvalidDataException("Invalid frame header");
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 1 || length > MaxPayload + 1) throw new InvalidDataException("Frame length rejected");
        return length;
    }
    public static Message DecodeBody(byte[] body)
    {
        if (body.Length < 1 || body.Length > MaxPayload + 1) throw new InvalidDataException("Invalid frame body");
        var kind = (MessageKind)body[0];
        if (!Enum.IsDefined(kind)) throw new InvalidDataException("Unknown message kind");
        return new Message(kind, body[1..]);
    }
}
public sealed class StreamTransport(Stream input, Stream output) : IMessageTransport
{
    private readonly SemaphoreSlim gate = new(1);
    public async Task SendAsync(Message m, CancellationToken ct)
    {
        var bytes = Protocol.Encode(m); await gate.WaitAsync(ct);
        try { await output.WriteAsync(bytes, ct); await output.FlushAsync(ct); }
        finally { gate.Release(); }
    }
    public Task<Message?> ReceiveAsync(CancellationToken ct) => Protocol.ReadAsync(input, ct);
    public async ValueTask DisposeAsync() { await input.DisposeAsync(); if (!ReferenceEquals(input, output)) await output.DisposeAsync(); }
}
