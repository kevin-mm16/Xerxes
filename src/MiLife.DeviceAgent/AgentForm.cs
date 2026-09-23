using System.Net;
using System.Diagnostics;
using MiLife.DeviceCollector.Configuration;
using MiLife.DeviceCollector.Diagnostics;
using MiLife.DeviceContracts;

namespace MiLife.DeviceAgent;

public sealed class AgentForm : Form
{
    private readonly ProfileStore store = new();
    private readonly AgentProfile profile;
    private readonly AgentApi api;
    private readonly LocalFiles logs;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 300_000 };
    private readonly System.Windows.Forms.Timer supportTimer = new() { Interval = 30_000 };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(400, 0), Margin = new Padding(0, 14, 0, 12) };
    private readonly TextBox employeeName = new() { Width = 390, MaxLength = 120, AccessibleName = "Your full name" };
    private readonly Button register = new() { Text = "Register", AutoSize = true, Padding = new Padding(14, 5, 14, 5), BackColor = Color.FromArgb(22, 43, 77), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly PictureBox logo = new();
    private readonly Label heading = new();
    private readonly Panel formPanel = new();
    private readonly Label notice = new();
    private bool processingAction;
    private Task? collection;
    private bool sending, supporting, exiting, registering;
    private bool Registered => profile.Enabled && profile.ManagementNoticeVersion == "1.2" && !string.IsNullOrWhiteSpace(profile.EmployeeName);

    public AgentForm(CollectorSettings settings, bool background)
    {
        Text = "miLife | PC check-in"; ClientSize = new Size(472, 440); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 10); BackColor = Color.White; ForeColor = Color.FromArgb(16, 30, 64);
        logs = new LocalFiles("1.3.1", store.DirectoryPath);
        profile = store.Load() ?? new AgentProfile(); api = new AgentApi(settings, logs); employeeName.Text = profile.EmployeeName;
        using (var logoStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("MiLifeLogo")!)
        using (var image = Image.FromStream(logoStream)) logo.Image = new Bitmap(image);
        logo.SetBounds(111, 24, 250, 93); logo.SizeMode = PictureBoxSizeMode.Zoom; logo.TabStop = false; Controls.Add(logo);
        heading.Text = "Check in your PC with us"; heading.SetBounds(30, 143, 412, 36); heading.TextAlign = ContentAlignment.MiddleCenter;
        heading.Font = new Font("Segoe UI", 18, FontStyle.Bold); Controls.Add(heading);
        formPanel.SetBounds(40, 201, 392, 124);
        formPanel.Controls.Add(new Label { Text = "Your full name", Bounds = new Rectangle(0, 0, 392, 22), Font = new Font("Segoe UI", 10, FontStyle.Bold) });
        employeeName.SetBounds(0, 26, 392, 32); employeeName.Font = new Font("Segoe UI", 12); employeeName.BorderStyle = BorderStyle.FixedSingle;
        register.Text = "Check in"; register.AutoSize = false; register.SetBounds(0, 76, 392, 44); register.BackColor = Color.FromArgb(0, 183, 179);
        register.ForeColor = Color.FromArgb(16, 30, 64); register.FlatAppearance.BorderSize = 0; register.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        formPanel.Controls.Add(employeeName); formPanel.Controls.Add(register); Controls.Add(formPanel); AcceptButton = register;
        status.AutoSize = false; status.SetBounds(40, 334, 392, 42); status.TextAlign = ContentAlignment.MiddleCenter; status.ForeColor = Color.FromArgb(83, 99, 123); Controls.Add(status);
        notice.Text = "IT manages this PC's inventory, availability and permitted location. Support needs approval.";
        notice.SetBounds(40, 382, 392, 42); notice.Font = new Font("Segoe UI", 9); notice.ForeColor = Color.FromArgb(105, 117, 135); notice.TextAlign = ContentAlignment.MiddleCenter; Controls.Add(notice);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show status", null, (_, _) => ShowStatus());
        menu.Items.Add("Send heartbeat now", null, async (_, _) => await HeartbeatAsync());
        menu.Items.Add("Disable agent", null, async (_, _) => await DisableAsync());
        menu.Items.Add("Exit until next sign-in", null, (_, _) => ExitAgent());
        tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "MiLife company device agent", ContextMenuStrip = menu, Visible = Registered };
        tray.DoubleClick += (_, _) => ShowStatus(); register.Click += async (_, _) => await RegisterAsync();
        timer.Tick += async (_, _) => await HeartbeatAsync(); supportTimer.Tick += async (_, _) => await SupportAsync();
        Shown += async (_, _) =>
        {
            if (await HandleActionAsync()) return;
            if (Registered)
            {
                // Running a newer download for an already registered user must update the installed copy too.
                var source = Environment.ProcessPath!;
                var installed = Path.Combine(store.DirectoryPath, "MiLifeDeviceAgent.exe");
                if (!Path.GetFullPath(source).Equals(Path.GetFullPath(installed), StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        store.EnableStartup();
                        var start = new ProcessStartInfo(installed) { UseShellExecute = false, CreateNoWindow = true };
                        start.ArgumentList.Add("--background"); start.ArgumentList.Add("--cleanup-source"); start.ArgumentList.Add(source);
                        if (ProfileStore.AcceptanceTest) start.ArgumentList.Add("--acceptance-test");
                        Process.Start(start); ExitAgent(); return;
                    }
                    catch (Exception ex) { logs.Log("UpgradeFailed", ex.GetType().Name); status.Text = "Could not update the installed agent. Close it and try again."; return; }
                }
                employeeName.Enabled = false; register.Visible = false; tray.Visible = true;
                if (background) Hide(); timer.Start(); supportTimer.Start(); await HeartbeatAsync();
            }
            else
            {
                status.Text = "";
                store.Save(profile); collection = CollectAsync();
            }
        };
        FormClosing += (_, e) =>
        {
            if (registering && !exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
            if (Registered && !exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
    }
    private async Task CollectAsync()
    {
        try { await api.CollectAsync(profile, store); }
        catch (Exception ex) { logs.Log("CollectionFailed", ex.GetType().Name); }
    }
    private void ShowStatus() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private async Task RegisterAsync()
    {
        var name = employeeName.Text.Trim();
        if (name.Length == 0 || name.Any(char.IsControl)) { status.Text = "Please enter your full name."; employeeName.Focus(); return; }
        registering = true; register.Enabled = false; employeeName.Enabled = false; status.Text = "Registering...";
        try
        {
            if (collection is not null) await collection;
            profile.EmployeeName = name; profile.ManagementNoticeVersion = "1.2"; store.Save(profile);
            await api.EnableAsync(profile, store);
            store.EnableStartup(); profile.Enabled = true; store.Save(profile);
            tray.Visible = true; heading.Text = "✓ Check-in successful"; formPanel.Visible = false; notice.Visible = false; status.SetBounds(40, 205, 392, 50); status.Text = "You're all set.";
            await Task.Delay(1800);
            var source = Environment.ProcessPath!;
            var target = Path.Combine(store.DirectoryPath, "MiLifeDeviceAgent.exe");
            if (!Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                var start = new ProcessStartInfo(target) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--background"); start.ArgumentList.Add("--cleanup-source"); start.ArgumentList.Add(source);
                if (ProfileStore.AcceptanceTest) start.ArgumentList.Add("--acceptance-test");
                Process.Start(start); ExitAgent();
            }
            else { Hide(); timer.Start(); supportTimer.Start(); await HeartbeatAsync(); }
        }
        catch (Exception ex)
        {
            logs.Log("RegistrationFailed", ex.GetType().Name);
            status.Text = ex is InvalidOperationException ? ex.Message : "Registration could not finish. Please try again.";
            formPanel.Visible = true; notice.Visible = true; heading.Text = "Check in your PC with us"; status.SetBounds(40, 334, 392, 42); register.Visible = true; register.Enabled = true; employeeName.Enabled = true;
        }
        finally { registering = false; }
    }
    private async Task HeartbeatAsync()
    {
        if (!Registered || sending || supporting) return;
        if (await HandleActionAsync()) return;
        sending = true;
        try
        {
            var result = await api.SendAsync(profile);
            if (!Registered) return;
            if (result is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                if (await HandleActionAsync()) return;
                timer.Stop(); supportTimer.Stop(); profile.Enabled = false; store.DisableStartup(); store.Save(profile); tray.Visible = false;
                formPanel.Visible = true; notice.Visible = true; heading.Text = "Check in your PC with us";
                status.SetBounds(40, 326, 392, 55); register.Visible = true; register.Enabled = true; employeeName.Enabled = true;
                status.Text = "IT has disabled this agent. Ask IT to restore access, then click Check in."; ShowStatus();
            }
            else status.Text = result == HttpStatusCode.OK ? $"Registered to {profile.EmployeeName}. Last check-in {DateTime.Now:t}." : "Server unavailable. Retrying automatically.";
        }
        catch (Exception ex) { logs.Log("HeartbeatFailed", ex.GetType().Name); status.Text = "Offline. Retrying automatically."; }
        finally { sending = false; }
    }
    private async Task SupportAsync()
    {
        if (!Registered || supporting) return;
        if (await HandleActionAsync()) return;
        supporting = true;
        try
        {
            var command = await api.NextCommandAsync(profile);
            if (command is null || !Registered || command.ExpiresAtUtc <= DateTime.UtcNow) return;
            var principal = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
            if (principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            {
                await api.CommandUpdateAsync(profile, command.Id, "decision", new SupportDecision(profile.AgentId, false));
                status.Text = "Support is disabled while this app is elevated. Restart it normally."; ShowStatus(); return;
            }
            using var prompt = new SupportPrompt(command);
            var approved = prompt.ShowDialog() == DialogResult.OK && Registered && DateTime.UtcNow < command.ExpiresAtUtc;
            if (!await api.CommandUpdateAsync(profile, command.Id, "decision", new SupportDecision(profile.AgentId, approved)) || !approved) return;
            tray.ShowBalloonTip(3000, "MiLife IT support", "Running your approved support command.", ToolTipIcon.Info);
            SupportResult result;
            try
            {
                var execution = await LocalPowerShell.RunAsync(command.Script, 60);
                result = new(profile.AgentId, execution.Output, execution.ExitCode, execution.TimedOut);
            }
            catch (Exception ex) { result = new(profile.AgentId, "PowerShell failed: " + ex.GetType().Name, null, false); }
            await api.CommandUpdateAsync(profile, command.Id, "result", result);
        }
        catch (Exception ex) { logs.Log("SupportFailed", ex.GetType().Name); }
        finally { supporting = false; }
    }
    private async Task DisableAsync()
    {
        if (!profile.Enabled) return;
        timer.Stop(); supportTimer.Stop();
        try { store.DisableStartup(); profile.Enabled = false; store.Save(profile); tray.Visible = false; }
        catch (Exception ex) { logs.Log("DisableFailed", ex.GetType().Name); status.Text = "Unable to disable startup. Contact IT."; return; }
        try { await api.SendAsync(profile, disable: true); } catch (Exception ex) { logs.Log("DisableNotificationFailed", ex.GetType().Name); }
        status.Text = "Agent disabled."; formPanel.Visible = true; notice.Visible = true; heading.Text = "Check in your PC with us"; status.SetBounds(40, 334, 392, 42); register.Visible = true; register.Enabled = true; employeeName.Enabled = true; ShowStatus();
    }
    private async Task<bool> HandleActionAsync()
    {
        if (processingAction) return true;
        if (supporting || registering || profile.SubmissionId == Guid.Empty) return false;
        processingAction = true;
        AgentActionReceipt? action = null;
        try
        {
            action = await api.PendingActionAsync(profile);
            if (action is null) return false;
            if (action.Action is not ("Disable" or "Remove")) return false;
            if (!await api.ActionStatusAsync(profile, action.Id, "Acknowledged")) return false;
            timer.Stop(); supportTimer.Stop(); store.DisableStartup(); profile.Enabled = false; store.Save(profile); tray.Visible = false;
            if (action.Action == "Disable")
            {
                await api.ActionStatusAsync(profile, action.Id, "Completed");
                ExitAgent(); return true;
            }
            profile.RemovalActionId = action.Id; profile.RemovalApiUrl = api.ActionStatusUrl(action.Id); store.Save(profile);
            await AgentUninstaller.StartAsync(store);
            ExitAgent(); return true;
        }
        catch (Exception ex)
        {
            logs.Log("ManagementActionFailed", ex.GetType().Name);
            if (action is not null)
            {
                try { await api.ActionStatusAsync(profile, action.Id, "Failed", "The agent could not finish the action. Contact IT."); } catch (Exception) { }
                status.Text = "This action could not finish. Please contact IT."; ShowStatus(); return true;
            }
            return false;
        }
        finally { processingAction = false; }
    }
    private void ExitAgent() { exiting = true; timer.Stop(); supportTimer.Stop(); Close(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); supportTimer.Dispose(); tray.Dispose(); api.Dispose(); logo.Image?.Dispose(); }
        base.Dispose(disposing);
    }
}
