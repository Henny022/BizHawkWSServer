using System;
using System.Drawing;
using System.Threading.Channels;
using System.Windows.Forms;
using BizHawk.Client.EmuHawk;
using WSServer;

namespace de.henny022.WSServerTool;

using BizHawk.Client.Common;

[ExternalTool("WSServer",
    LoadAssemblyFiles = new[] { "System.Threading.Tasks.Extensions.dll", "System.Threading.Channels.dll" })]
public sealed class WSServerForm : ToolFormBase, IExternalToolForm
{
    protected override string WindowTitleStatic => "WSServer";

    private ChannelReader<byte[]> rx;
    private ChannelWriter<byte[]> tx;

    public ApiContainer? _maybeAPIContainer { get; set; }

    private ApiContainer APIs
        => _maybeAPIContainer!;

    public WSServerForm()
    {
        ClientSize = new Size(480, 320);

        (rx, tx) = WSServer.WSServer.Spawn("127.0.0.1", 8090);
        Console.WriteLine("server spawned");

        SuspendLayout();
        Controls.Add(new Label { AutoSize = true, Text = "Server running at ws://localhost:8090" });
        ResumeLayout(performLayout: false);
        PerformLayout();
    }

    protected override void UpdateAfter()
    {
        if (rx.TryRead(out var binary))
        {
            switch (binary[0] & 0xc0)
            {
                case 0x00:
                    var x = APIs.Memory.ReadU32(0x0300118D);
                    Console.WriteLine("x: {0}", x);
                    var t = APIs.Memory.ReadByteRange(0x0300118D, 4);
                    tx.TryWrite(new byte[] { 0x00, t[0], t[1], t[2], t[3] });
                    break;
                case 0x40:
                    break;
                case 0x80:
                    break;
                case 0xc0:
                    break;
                default:
                    Console.WriteLine("you are an idiot henny");
                    break;
            }
        }
    }
}