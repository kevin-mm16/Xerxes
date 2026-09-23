namespace MiLife.DeviceAgent;

internal sealed class PrivacyConsentPanel : Panel
{
    public event EventHandler? Accepted;

    public PrivacyConsentPanel()
    {
        var terms = new RichTextBox
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(35, 51, 77),
            Font = new Font("Segoe UI", 9.5f),
            DetectUrls = false,
            TabStop = true,
            Text = """
                COMPANY DEVICE MANAGEMENT NOTICE

                MiLife IT collects the information needed to manage this company PC:

                • Your name and Windows username
                • Computer hardware, Windows version and network adapter details
                • A background availability heartbeat
                • Location supplied by Windows when location services permit it

                IT SUPPORT

                The agent may run non-administrator PowerShell diagnostics requested by MiLife IT. Approved background commands can run without another pop-up and return their output to IT. Commands use your signed-in Windows permissions; they do not gain administrator access or bypass Windows security.

                The agent starts when you sign in and continues until IT disables or removes it. Device inventory, command activity and support results are retained by MiLife for company-device administration.

                Continue only on a MiLife company PC assigned to you.
                """
        };
        terms.SetBounds(0, 0, 420, 245);

        var agreement = new CheckBox
        {
            Text = "I understand and accept this notice.",
            AutoSize = false,
            Bounds = new Rectangle(0, 258, 420, 42),
            ForeColor = Color.FromArgb(35, 51, 77)
        };
        var accept = new Button
        {
            Text = "Accept and continue",
            Enabled = false,
            Bounds = new Rectangle(0, 310, 420, 44),
            BackColor = Color.FromArgb(0, 183, 179),
            ForeColor = Color.FromArgb(16, 30, 64),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        };
        accept.FlatAppearance.BorderSize = 0;
        agreement.CheckedChanged += (_, _) => accept.Enabled = agreement.Checked;
        accept.Click += (_, _) => Accepted?.Invoke(this, EventArgs.Empty);
        Controls.Add(terms); Controls.Add(agreement); Controls.Add(accept);
    }
}
