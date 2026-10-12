using System.Diagnostics;
using System.Net;
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
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 300_000 };
    private readonly System.Windows.Forms.Timer supportTimer = new() { Interval = 30_000 };
    private readonly Label status = new();
    private readonly TextBox employeeName = new() { MaxLength = 120, AccessibleName = "Your full name" };
    private readonly TextBox phoneNumber = new() { MaxLength = 32, AccessibleName = "Your phone number" };
    private readonly Button register = new();
    private readonly PictureBox logo = new();
    private readonly Label heading = new();
    private readonly Panel formPanel = new();
    private readonly PrivacyConsentPanel privacyPanel = new();
    private readonly Label notice = new();
    private bool processingAction;
    private Task? collection;
    private bool sending, supporting, exiting, registering;
    private bool Registered => profile.Enabled && profile.ManagementNoticeVersion == "1.2" && !string.IsNullOrWhiteSpace(profile.EmployeeName);
    private bool HasCurrentConsent => profile.PrivacyNoticeVersion == AgentPolicy.PrivacyNoticeVersion && profile.PrivacyAcceptedAtUtc is not null;

    public AgentForm(CollectorSettings settings, bool background)
    {
        Text = "MiLife | PC check-in"; ClientSize = new Size(500, 600); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 10); BackColor = Color.White; ForeColor = Color.FromArgb(16, 30, 64);
        logs = new LocalFiles(AgentPolicy.AgentVersion, store.DirectoryPath);
        profile = store.Load() ?? new AgentProfile(); api = new AgentApi(settings, logs); employeeName.Text = profile.EmployeeName; phoneNumber.Text = profile.PhoneNumber;

        using (var logoStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("MiLifeLogo")!)
        using (var image = Image.FromStream(logoStream)) logo.Image = new Bitmap(image);
        logo.SetBounds(125, 20, 250, 93); logo.SizeMode = PictureBoxSizeMode.Zoom; logo.TabStop = false; Controls.Add(logo);

        heading.SetBounds(40, 132, 420, 48); heading.TextAlign = ContentAlignment.MiddleCenter;
        heading.Font = new Font("Segoe UI", 18, FontStyle.Bold); Controls.Add(heading);

        privacyPanel.SetBounds(40, 192, 420, 354); privacyPanel.Accepted += (_, _) => AcceptPrivacyNotice();
        Controls.Add(privacyPanel);

        formPanel.SetBounds(40, 205, 420, 190);
        formPanel.Controls.Add(new Label { Text = "Your full name", Bounds = new Rectangle(0, 0, 420, 22), Font = new Font("Segoe UI", 10, FontStyle.Bold) });
        employeeName.SetBounds(0, 26, 420, 32); employeeName.Font = new Font("Segoe UI", 12); employeeName.BorderStyle = BorderStyle.FixedSingle;
        formPanel.Controls.Add(new Label { Text = "Phone number", Bounds = new Rectangle(0, 66, 420, 22), Font = new Font("Segoe UI", 10, FontStyle.Bold) });
        phoneNumber.SetBounds(0, 92, 420, 32); phoneNumber.Font = new Font("Segoe UI", 12); phoneNumber.BorderStyle = BorderStyle.FixedSingle;
        register.Text = "Check in"; register.SetBounds(0, 142, 420, 44); register.BackColor = Color.FromArgb(0, 183, 179);
        register.ForeColor = Color.FromArgb(16, 30, 64); register.FlatStyle = FlatStyle.Flat; register.FlatAppearance.BorderSize = 0;
        register.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        formPanel.Controls.Add(employeeName); formPanel.Controls.Add(phoneNumber); formPanel.Controls.Add(register); Controls.Add(formPanel); AcceptButton = register;

        status.SetBounds(40, 342, 420, 48); status.TextAlign = ContentAlignment.MiddleCenter;
        status.ForeColor = Color.FromArgb(83, 99, 123); Controls.Add(status);
        notice.Text = "The agent runs quietly in the background after check-in.";
        notice.SetBounds(40, 397, 420, 42); notice.Font = new Font("Segoe UI", 9);
        notice.ForeColor = Color.FromArgb(105, 117, 135); notice.TextAlign = ContentAlignment.MiddleCenter; Controls.Add(notice);

        register.Click += async (_, _) => await RegisterAsync();
        timer.Tick += async (_, _) => await HeartbeatAsync(); supportTimer.Tick += async (_, _) => await SupportAsync();

        Shown += async (_, _) =>
        {
            if (await HandleActionAsync()) return;
            if (Registered)
            {
                if (!InstallCurrentVersion()) return;
                Hide(); timer.Start(); supportTimer.Start(); await HeartbeatAsync();
                return;
            }

            store.Save(profile);
            if (HasCurrentConsent) BeginRegistration(); else ShowPrivacyNotice();
        };
        FormClosing += (_, e) =>
        {
            if (registering && !exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
            if (Registered && !exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
    }

    private void ShowPrivacyNotice()
    {
        ClientSize = new Size(500, 600); heading.Text = "Before you continue";
        privacyPanel.Visible = true; formPanel.Visible = false; status.Visible = false; notice.Visible = false;
    }

    private void AcceptPrivacyNotice()
    {
        profile.PrivacyNoticeVersion = AgentPolicy.PrivacyNoticeVersion;
        profile.PrivacyAcceptedAtUtc = DateTime.UtcNow;
        store.Save(profile);
        BeginRegistration();
    }

    private void BeginRegistration()
    {
        ClientSize = new Size(500, 470); heading.Text = "Check in your PC with us";
        privacyPanel.Visible = false; formPanel.Visible = true; status.Visible = true; notice.Visible = true;
        status.SetBounds(40, 402, 420, 48); status.Text = ""; employeeName.Enabled = true; phoneNumber.Enabled = true;
        register.Visible = true; register.Enabled = true; AcceptButton = register; employeeName.Focus();
        collection ??= CollectAsync();
    }

    private void ShowRegistered()
    {
        ClientSize = new Size(500, 430); privacyPanel.Visible = false; formPanel.Visible = false;
        heading.Text = "PC checked in"; status.Visible = true; status.SetBounds(40, 215, 420, 60);
        status.Text = $"Registered to {profile.EmployeeName}."; notice.Visible = true; notice.SetBounds(40, 285, 420, 44);
    }

    private bool InstallCurrentVersion()
    {
        var source = Environment.ProcessPath!;
        var installed = Path.Combine(store.DirectoryPath, "MiLifeDeviceAgent.exe");
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(installed), StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            store.EnableStartup();
            var start = new ProcessStartInfo(installed) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--background"); start.ArgumentList.Add("--cleanup-source"); start.ArgumentList.Add(source);
            if (ProfileStore.AcceptanceTest) start.ArgumentList.Add("--acceptance-test");
            Process.Start(start); ExitAgent(); return false;
        }
        catch (Exception ex)
        {
            logs.Log("UpgradeFailed", ex.GetType().Name); status.Visible = true;
            status.Text = "Could not update the installed agent. Close it and try again."; return false;
        }
    }

    private async Task CollectAsync()
    {
        try { await api.CollectAsync(profile, store); }
        catch (Exception ex) { logs.Log("CollectionFailed", ex.GetType().Name); }
    }

    private void ShowStatus() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    private async Task RegisterAsync()
    {
        if (!HasCurrentConsent) { ShowPrivacyNotice(); return; }
        var name = employeeName.Text.Trim();
        var phone = phoneNumber.Text.Trim();
        if (name.Length == 0 || name.Any(char.IsControl)) { status.Text = "Please enter your full name."; employeeName.Focus(); return; }
        if (phone.Count(char.IsDigit) is < 7 or > 20 || phone.Any(c => !char.IsDigit(c) && c is not ('+' or ' ' or '-' or '(' or ')'))) { status.Text = "Please enter a valid phone number."; phoneNumber.Focus(); return; }
        registering = true; register.Enabled = false; employeeName.Enabled = false; phoneNumber.Enabled = false; status.Text = "Registering...";
        try
        {
            collection ??= CollectAsync(); await collection;
            profile.EmployeeName = name; profile.PhoneNumber = phone; profile.ManagementNoticeVersion = "1.2"; store.Save(profile);
            await api.EnableAsync(profile, store);
            store.EnableStartup(); profile.Enabled = true; store.Save(profile);
            heading.Text = "Check-in successful"; formPanel.Visible = false; notice.Visible = false;
            status.SetBounds(40, 215, 420, 50); status.Text = "You're all set.";
            await Task.Delay(1800);
            if (InstallCurrentVersion()) { Hide(); timer.Start(); supportTimer.Start(); await HeartbeatAsync(); }
        }
        catch (Exception ex)
        {
            logs.Log("RegistrationFailed", ex.GetType().Name);
            status.Text = ex is InvalidOperationException ? ex.Message : "Registration could not finish. Please try again.";
            formPanel.Visible = true; notice.Visible = true; heading.Text = "Check in your PC with us";
            status.SetBounds(40, 402, 420, 48); register.Visible = true; register.Enabled = true; employeeName.Enabled = true; phoneNumber.Enabled = true;
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
            var response = await api.SendAsync(profile);
            if (response == HttpStatusCode.OK && HasCurrentConsent && profile.InventorySchemaVersion < 1)
            {
                try { await api.RefreshInventoryAsync(profile, store); }
                catch (Exception ex) { logs.Log("InventoryRefreshFailed", ex.GetType().Name); }
            }
            if (!Registered) return;
            if (response is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                if (await HandleActionAsync()) return;
                timer.Stop(); supportTimer.Stop(); profile.Enabled = false; store.DisableStartup(); store.Save(profile); ExitAgent();
            }
            else status.Text = response == HttpStatusCode.OK
                ? $"Registered to {profile.EmployeeName}. Last check-in {DateTime.Now:t}."
                : "Server unavailable. Retrying automatically.";
        }
        catch (Exception ex) { logs.Log("HeartbeatFailed", ex.GetType().Name); status.Text = "Offline. Retrying automatically."; }
        finally { sending = false; }
    }

    private async Task SupportAsync()
    {
        if (!Registered || supporting || sending) return;
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
                status.Text = "Support is disabled while this app is elevated."; return;
            }

            var runSilently = command.RunSilently && HasCurrentConsent;
            var approved = runSilently;
            if (!runSilently)
            {
                using var prompt = new SupportPrompt(command);
                approved = prompt.ShowDialog() == DialogResult.OK && Registered && DateTime.UtcNow < command.ExpiresAtUtc;
            }
            if (!await api.CommandUpdateAsync(profile, command.Id, "decision", new SupportDecision(profile.AgentId, approved)) || !approved) return;
            logs.Log("SupportStarted", command.Id + (runSilently ? ":Silent" : ":Approved"));

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

    private async Task<bool> HandleActionAsync()
    {
        if (processingAction) return true;
        if (supporting || registering || profile.SubmissionId == Guid.Empty) return false;
        processingAction = true;
        AgentActionReceipt? action = null;
        try
        {
            action = await api.PendingActionAsync(profile);
            if (action is null || action.Action is not ("Disable" or "Remove")) return false;
            if (!await api.ActionStatusAsync(profile, action.Id, "Acknowledged")) return false;
            timer.Stop(); supportTimer.Stop(); store.DisableStartup(); profile.Enabled = false; store.Save(profile);
            if (action.Action == "Disable")
            {
                await api.ActionStatusAsync(profile, action.Id, "Completed");
                ExitAgent(); return true;
            }
            profile.RemovalActionId = action.Id; profile.RemovalApiUrl = api.ActionStatusUrl(action.Id); store.Save(profile);
            await AgentUninstaller.StartAsync(store); ExitAgent(); return true;
        }
        catch (Exception ex)
        {
            logs.Log("ManagementActionFailed", ex.GetType().Name);
            if (action is not null)
            {
                try { await api.ActionStatusAsync(profile, action.Id, "Failed", "The agent could not finish the action. Contact IT."); } catch (Exception) { }
                status.Text = "This action could not finish. Please contact IT."; return true;
            }
            return false;
        }
        finally { processingAction = false; }
    }

    private void ExitAgent() { exiting = true; timer.Stop(); supportTimer.Stop(); Close(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); supportTimer.Dispose(); api.Dispose(); logo.Image?.Dispose(); }
        base.Dispose(disposing);
    }
}
