using System.Buffers.Binary;
using System.Text;
using MouseBridge.Core;
using Xunit;
namespace MouseBridge.Tests;
public class ClipboardTests
{
    private static ClipboardSnapshot RoundTrip(Guid id,ClipboardFlow flow,ClipboardSnapshot snapshot)
    {
        var frames=ClipboardWire.Encode(id,flow,snapshot);var assembler=new ClipboardAssembler();assembler.Begin(frames[0].Payload);
        foreach(var frame in frames.Skip(1).SkipLast(1)){Assert.True(assembler.Append(frame.Payload));Assert.True(Protocol.Encode(frame).Length<=Protocol.MaxPayload+5);}
        return assembler.Finish(frames[^1].Payload)!;
    }
    [Theory] [InlineData("")] [InlineData("hello")] [InlineData("한글 가나다 ABC 😀")] [InlineData("line1\r\nline2\t\"quotes\"")]
    public void ExactTextRoundTrip(string text)
    {foreach(var flow in new[]{ClipboardFlow.ToSub,ClipboardFlow.ToMain})Assert.Equal(text,RoundTrip(Guid.NewGuid(),flow,new(ClipboardPayloadKind.Text,text)).Text);}
    [Fact] public void UnicodeMaySpanChunkBoundaries()
    {string text=string.Concat(Enumerable.Repeat("한😀",6000));Assert.Equal(text,RoundTrip(Guid.NewGuid(),ClipboardFlow.ToMain,new(ClipboardPayloadKind.Text,text)).Text);}
    [Fact] public void MaximumTextFitsBoundedFrames()
    {string text=new('a',ClipboardWire.MaxBytes);Assert.Equal(text,RoundTrip(Guid.NewGuid(),ClipboardFlow.ToSub,new(ClipboardPayloadKind.Text,text)).Text);}
    [Fact] public void OversizedOrInvalidTextRejected()
    {
        Assert.Throws<InvalidDataException>(()=>ClipboardWire.Encode(Guid.NewGuid(),ClipboardFlow.ToSub,new(ClipboardPayloadKind.Text,new string('a',ClipboardWire.MaxBytes+1))));
        Assert.Throws<EncoderFallbackException>(()=>ClipboardWire.Encode(Guid.NewGuid(),ClipboardFlow.ToSub,new(ClipboardPayloadKind.Text,"\uD800")));
    }
    [Theory] [InlineData(-1,1)] [InlineData(262145,1)] [InlineData(1,0)] [InlineData(0,9)]
    public void RejectsInvalidMetadata(int size,int kind)
    {
        var begin=ClipboardWire.Encode(Guid.NewGuid(),ClipboardFlow.ToSub,new(ClipboardPayloadKind.Text,"a"))[0].Payload;
        begin[17]=(byte)kind;BinaryPrimitives.WriteInt32LittleEndian(begin.AsSpan(18),size);
        Assert.Throws<InvalidDataException>(()=>new ClipboardAssembler().Begin(begin));
    }
    [Fact] public void RejectsOutOfOrderDuplicateAndIncompleteData()
    {
        var frames=ClipboardWire.Encode(Guid.NewGuid(),ClipboardFlow.ToSub,new(ClipboardPayloadKind.Text,new string('a',9000)));var a=new ClipboardAssembler();a.Begin(frames[0].Payload);
        Assert.Throws<InvalidDataException>(()=>a.Append(frames[2].Payload));a.Append(frames[1].Payload);
        Assert.Throws<InvalidDataException>(()=>a.Append(frames[1].Payload));Assert.Throws<InvalidDataException>(()=>a.Finish(frames[^1].Payload));
    }
    [Fact] public void OtherSessionAndDirectionCannotCompleteTransfer()
    {
        Guid id=Guid.NewGuid();var original=ClipboardWire.Encode(id,ClipboardFlow.ToMain,new(ClipboardPayloadKind.Text,"a"));
        var wrong=ClipboardWire.Encode(Guid.NewGuid(),ClipboardFlow.ToMain,new(ClipboardPayloadKind.Text,"b"));
        var direction=ClipboardWire.Encode(id,ClipboardFlow.ToSub,new(ClipboardPayloadKind.Text,"c"));var a=new ClipboardAssembler();a.Begin(original[0].Payload);
        Assert.False(a.Append(wrong[1].Payload));Assert.Null(a.Finish(direction[^1].Payload));
        a.Append(original[1].Payload);Assert.Equal("a",a.Finish(original[^1].Payload)!.Text);Assert.Null(a.Finish(original[^1].Payload));
    }
    [Fact] public void InvalidUtf8NeverBecomesClipboardText()
    {
        var frames=ClipboardWire.Encode(Guid.NewGuid(),ClipboardFlow.ToSub,new(ClipboardPayloadKind.Text,"a"));frames[1].Payload[21]=255;
        var a=new ClipboardAssembler();a.Begin(frames[0].Payload);a.Append(frames[1].Payload);Assert.Throws<DecoderFallbackException>(()=>a.Finish(frames[^1].Payload));
    }
    [Theory] [InlineData(ClipboardPayloadKind.NoText)] [InlineData(ClipboardPayloadKind.Unavailable)]
    public void MissingTextIsNotAnEmptyTextReplacement(ClipboardPayloadKind kind)
    {var result=RoundTrip(Guid.NewGuid(),ClipboardFlow.ToMain,new(kind));Assert.Equal(kind,result.Kind);Assert.Null(result.Text);}
    [Fact] public async Task FinalCopyKeyUpPrecedesReturnRequest()
    {
        Guid id=Guid.NewGuid();var q=new OutboundBuffer();
        var input=new Message(MessageKind.InputBatch,InputWire.Encode(id,[new(InputActionKind.Key,67,46,1)]));
        var request=new Message(MessageKind.ControlReturn,InputWire.Session(id));q.TryWrite(input);q.TryWrite(request);
        Assert.Same(input,await q.ReadAsync(default));Assert.Same(request,await q.ReadAsync(default));
    }
}
