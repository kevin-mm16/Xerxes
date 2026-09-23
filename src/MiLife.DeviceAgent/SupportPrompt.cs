using MiLife.DeviceContracts;

namespace MiLife.DeviceAgent;

internal sealed class SupportPrompt : Form
{
    private readonly System.Windows.Forms.Timer expiry = new() { Interval = 1000 };
    public SupportPrompt(SupportCommand command)
    {
        Text = "MiLife IT support approval"; ClientSize = new Size(620, 400); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); MinimizeBox = false; MaximizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.Controls.Add(new Label { Text = $"IT administrator {command.RequestedBy} requests the PowerShell command below. It runs with your Windows permissions and sends its output to IT. Approve only if you recognize this support request.", Dock = DockStyle.Fill });
        layout.Controls.Add(new TextBox { Text = command.Script, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font("Consolas", 10) });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var decline = new Button { Text = "Decline", DialogResult = DialogResult.Cancel, AutoSize = true };
        var approve = new Button { Text = "Approve command", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(decline); buttons.Controls.Add(approve); layout.Controls.Add(buttons); Controls.Add(layout); CancelButton = decline;
        // No default approval button. Closing the window, expiry or no response never approves.
        expiry.Tick += (_, _) => { if (DateTime.UtcNow >= command.ExpiresAtUtc) { DialogResult = DialogResult.Cancel; Close(); } };
        expiry.Start();
    }
    protected override void Dispose(bool disposing) { if (disposing) expiry.Dispose(); base.Dispose(disposing); }
}
