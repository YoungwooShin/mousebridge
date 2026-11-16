using MouseBridge.Core;
using Xunit;
namespace MouseBridge.Tests;
public class ProtocolTests
{
    [Fact] public async Task ConsecutiveMessagesSurviveFragmentation()
    {
        var messages = Enumerable.Range(0, 100).Select(i => new Message(MessageKind.Binary, new byte[] { (byte)i, 0, 255 })).ToArray();
        using var stream = new Fragmented(messages.SelectMany(Protocol.Encode).ToArray());
        foreach (var expected in messages) { var actual = await Protocol.ReadAsync(stream, default); Assert.Equal(expected.Kind, actual!.Kind); Assert.Equal(expected.Payload, actual.Payload); }
        Assert.Null(await Protocol.ReadAsync(stream, default));
    }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(65538)]
    public async Task RejectsInvalidLengths(int n)
    {
        var header = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, n);
        await Assert.ThrowsAsync<InvalidDataException>(() => Protocol.ReadAsync(new MemoryStream(header), default));
    }
    [Fact] public async Task RejectsTruncatedBody() => await Assert.ThrowsAsync<EndOfStreamException>(() => Protocol.ReadAsync(new MemoryStream(new byte[] {0,0,0,3,1}), default));
    [Fact] public async Task RejectsTruncatedHeader() => await Assert.ThrowsAsync<EndOfStreamException>(() => Protocol.ReadAsync(new MemoryStream(new byte[] {0,0}), default));
    [Fact] public async Task RejectsUnknownKind() => await Assert.ThrowsAsync<InvalidDataException>(() => Protocol.ReadAsync(new MemoryStream(new byte[] {0,0,0,1,99}), default));
    [Fact] public void RejectsOversizedPayload() => Assert.Throws<InvalidDataException>(() => Protocol.Encode(new(MessageKind.Binary, new byte[65537])));
    [Fact] public async Task SupportsMaximumPayload()
    { var data = new byte[65536]; Random.Shared.NextBytes(data); var m = await Protocol.ReadAsync(new MemoryStream(Protocol.Encode(new(MessageKind.Binary, data))), default); Assert.Equal(data, m!.Payload); }
    [Fact] public async Task CancellationIsObserved()
    { using var ct = new CancellationTokenSource(); ct.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Protocol.ReadAsync(new MemoryStream(new byte[4]), ct.Token)); }
    private sealed class Fragmented(byte[] data) : MemoryStream(data)
    { public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], ct); }
}
