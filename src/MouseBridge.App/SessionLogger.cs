using System.IO;
using System.Threading.Channels;
namespace MouseBridge.App;
internal sealed class SessionLogger : IAsyncDisposable
{
    private readonly Channel<string> lines=Channel.CreateBounded<string>(new BoundedChannelOptions(4096){FullMode=BoundedChannelFullMode.DropOldest});
    private readonly Task writer;
    public string DirectoryPath{get;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MouseBridge","Logs");
    public SessionLogger()
    {
        writer=Task.Run(async()=>
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                await foreach(var line in lines.Reader.ReadAllAsync())
                    await File.AppendAllTextAsync(Path.Combine(DirectoryPath,$"mousebridge-{DateTime.Now:yyyyMMdd}.log"),line);
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        });
    }
    public void Write(string line)=>lines.Writer.TryWrite(line);
    public async ValueTask DisposeAsync(){lines.Writer.TryComplete();await writer;}
}
