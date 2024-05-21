using System.Drawing;
using System.Windows.Forms;
using BizHawk.Client.EmuHawk;
using WSServer;

namespace de.henny022.WSServerTool;

using BizHawk.Client.Common;

[ExternalTool("WSServer", LoadAssemblyFiles = new[] { "ExternalTools/System.Threading.Channels.dll" })]
public sealed class WSServerForm : ToolFormBase, IExternalToolForm
{
    protected override string WindowTitleStatic => "WSServer";

    public WSServerForm()
    {
        ClientSize = new Size(480, 320);

        var (rx, tx) = WSServer.WSServer.Spawn("localhost", 8090);

        SuspendLayout();
        Controls.Add(new Label { AutoSize = true, Text = "Server running at ws://localhost:8090" });
        ResumeLayout(performLayout: false);
        PerformLayout();
    }
}