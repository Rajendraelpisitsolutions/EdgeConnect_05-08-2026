// ============================================================================
// File: Api/LicensePurchaseService.cs
// Purpose: Backs the "Buy License" dialog. Serves Elpis contact details and
//          accepts a customer enquiry, emailing it to Elpis. Delivery uses
//          SMTP when configured (EDGECONNECT_SMTP_*), otherwise returns a
//          mailto: URL so the operator's mail client composes the message to
//          Elpis — so the enquiry always reaches Elpis one way or another.
// ============================================================================

using System;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Elpis contact details shown in the Buy License dialog.</summary>
public sealed record LicenseContactDto
{
    /// <summary>Sales / enquiry email address.</summary>
    public required string Email { get; init; }

    /// <summary>Sales / contact phone number.</summary>
    public required string Phone { get; init; }

    /// <summary>Company website URL.</summary>
    public required string Website { get; init; }
}

/// <summary>Customer enquiry submitted from the Buy License dialog.</summary>
public sealed record LicensePurchaseRequest
{
    /// <summary>Contact person's name (required).</summary>
    public string? CustomerName { get; init; }

    /// <summary>Company / organisation name.</summary>
    public string? Company { get; init; }

    /// <summary>Reply-to email address (required).</summary>
    public string? Email { get; init; }

    /// <summary>Contact phone number.</summary>
    public string? Phone { get; init; }

    /// <summary>Requested license type / edition (optional): Starter / Professional / Enterprise.</summary>
    public string? LicenseType { get; init; }

    /// <summary>True when this is a renewal / upgrade of an existing (expired) license.</summary>
    public bool IsUpgrade { get; init; }

    /// <summary>Free-text message / requirements.</summary>
    public string? Message { get; init; }
}

/// <summary>Outcome of a Buy License submission.</summary>
public sealed record LicensePurchaseResult
{
    /// <summary>True when the enquiry was emailed server-side via SMTP.</summary>
    public required bool Sent { get; init; }

    /// <summary>
    /// A <c>mailto:</c> URL the UI should open when <see cref="Sent"/> is false
    /// (SMTP not configured / failed), so the operator's mail client sends it.
    /// </summary>
    public string? MailtoUrl { get; init; }

    /// <summary>Validation or delivery error, when applicable.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Serves Elpis contact details and handles Buy License enquiries. Singleton.
/// </summary>
public sealed class LicensePurchaseService
{
    private readonly ILogger<LicensePurchaseService> _logger;
    private readonly LicenseContactDto _contact;
    private readonly ElpisEdgeConnect.Core.Licensing.ILicenseManager _license;

    /// <summary>Construct the service with the configured contact details.</summary>
    public LicensePurchaseService(
        ILogger<LicensePurchaseService> logger,
        string salesEmail,
        string salesPhone,
        string companyWebsite,
        ElpisEdgeConnect.Core.Licensing.ILicenseManager license)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(license);
        _logger = logger;
        _license = license;
        _contact = new LicenseContactDto
        {
            Email = salesEmail ?? string.Empty,
            Phone = salesPhone ?? string.Empty,
            Website = companyWebsite ?? string.Empty,
        };
    }

    /// <summary>
    /// The gateway's current license edition for the upgrade email — the loaded
    /// edition even when expired (<c>Current</c> survives expiry), or a demo
    /// label when no license is loaded.
    /// </summary>
    private string CurrentLicenseLabel() =>
        _license.Current?.Edition.ToString() ?? "Demo (no active license)";

    /// <summary>The Elpis contact details for the dialog.</summary>
    public LicenseContactDto GetContact() => _contact;

    /// <summary>
    /// Validate and deliver a purchase enquiry. Sends via SMTP when configured,
    /// otherwise returns a <c>mailto:</c> URL for the operator's mail client.
    /// </summary>
    public async Task<LicensePurchaseResult> SubmitAsync(LicensePurchaseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.CustomerName) || string.IsNullOrWhiteSpace(request.Email))
        {
            return new LicensePurchaseResult { Sent = false, Error = "Name and email are required." };
        }

        var enquirySubject = request.IsUpgrade
            ? "Edge Connect License Upgrade/Renewal Purchase Request"
            : "New License Purchase Request for Elpis EdgeConnect";
        // Owner enquiry: HTML for SMTP/Outlook (bold headings + bullets, matching
        // the client emails), plus a plain-text version for the rare mailto fallback.
        var enquiryHtml = BuildOwnerHtml(request);
        var enquiryText = BuildOwnerText(request);
        var thankYouSubject = request.IsUpgrade
            ? "We've Received Your License Upgrade Request"
            : "We've Received Your License Purchase Request";
        // Both client acknowledgements are formatted HTML (bold headings, bullet
        // lists, and mailto:/tel: hyperlinks in the signature).
        var thankYouBody = request.IsUpgrade
            ? BuildUpgradeThankYouHtml(request)
            : BuildThankYouBody(request);
        var thankYouIsHtml = true;
        var clientEmail = request.Email!.Trim();

        // 1) SMTP (if configured) — enquiry to Elpis + thank-you to the client.
        var smtpHost = Environment.GetEnvironmentVariable("EDGECONNECT_SMTP_HOST");
        if (!string.IsNullOrWhiteSpace(smtpHost))
        {
            try
            {
                await SendViaSmtpAsync(smtpHost, _contact.Email, enquirySubject, enquiryHtml, clientEmail, cancellationToken, isHtml: true);
                await SendViaSmtpAsync(smtpHost, clientEmail, thankYouSubject, thankYouBody, _contact.Email, cancellationToken, thankYouIsHtml);
                _logger.LogInformation("License enquiry (Elpis) + thank-you ({Client}) emailed via SMTP.", clientEmail);
                return new LicensePurchaseResult { Sent = true };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMTP delivery of the license enquiry failed; falling back to mailto.");
                return new LicensePurchaseResult
                {
                    Sent = false,
                    MailtoUrl = BuildMailto(enquirySubject, enquiryText),
                    Error = "Automatic email failed; opening your mail client instead.",
                };
            }
        }

        // 2) No SMTP — send via the local Outlook profile (silent, no password /
        // popup): enquiry to Elpis, then a thank-you to the client.
        if (await SendViaOutlookAsync(_contact.Email, enquirySubject, enquiryHtml, isHtml: true).ConfigureAwait(false))
        {
            await SendViaOutlookAsync(clientEmail, thankYouSubject, thankYouBody, thankYouIsHtml).ConfigureAwait(false);
            _logger.LogInformation("License enquiry (Elpis) + thank-you ({Client}) sent via Outlook.", clientEmail);
            return new LicensePurchaseResult { Sent = true };
        }

        // 3) Fallback — mailto for the enquiry so it still reaches Elpis.
        _logger.LogInformation("SMTP and Outlook both unavailable; returning mailto for the license enquiry.");
        return new LicensePurchaseResult { Sent = false, MailtoUrl = BuildMailto(enquirySubject, enquiryText) };
    }

    /// <summary>
    /// Maximum time to wait for an Outlook COM send before giving up and falling
    /// back to mailto. Bounds the request so a slow / offline / security-prompted
    /// Outlook can't hang the HTTP call (which the client aborts at ~100s).
    /// </summary>
    private static readonly TimeSpan OutlookSendTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Send the enquiry through the machine's configured Outlook profile via COM —
    /// no SMTP credentials and no compose window. Runs on a dedicated STA thread
    /// (COM requirement). Returns false if Outlook isn't available (e.g. running as
    /// a service with no user profile) OR does not complete within
    /// <see cref="OutlookSendTimeout"/>, so the caller can fall back to mailto.
    /// </summary>
    private async Task<bool> SendViaOutlookAsync(string to, string subject, string body, bool isHtml = false)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        // A Windows service (LocalSystem) has no interactive session or user mail
        // profile, so Outlook COM cannot send. Skip it and fall straight through to
        // mailto instead of blocking for OutlookSendTimeout on a call that can't work.
        if (!Environment.UserInteractive)
        {
            _logger.LogInformation(
                "Not an interactive session (running as a service); skipping Outlook and using SMTP/mailto. " +
                "Configure EDGECONNECT_SMTP_HOST for automated delivery.");
            return false;
        }

        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                tcs.TrySetResult(TrySendViaOutlook(to, subject, body, isHtml));
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Outlook send failed: {Message}", ex.Message);
                tcs.TrySetResult(false);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Bound the wait so a stuck Outlook (starting up, offline, or awaiting a
        // "allow send" security prompt) can't hang the request. The STA thread is
        // a background thread; if it's still blocked it won't stop the process.
        var finished = await Task.WhenAny(tcs.Task, Task.Delay(OutlookSendTimeout)).ConfigureAwait(false);
        if (finished != tcs.Task)
        {
            _logger.LogWarning(
                "Outlook send did not complete within {Seconds}s (Outlook may be starting, offline, or " +
                "awaiting a security prompt); falling back to mailto.",
                OutlookSendTimeout.TotalSeconds);
            return false;
        }
        return await tcs.Task.ConfigureAwait(false);
    }

    private bool TrySendViaOutlook(string to, string subject, string body, bool isHtml = false)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var progId = Type.GetTypeFromProgID("Outlook.Application");
        if (progId is null)
        {
            return false; // Outlook not installed
        }

        object? app = null;
        object? mail = null;
        try
        {
            app = Activator.CreateInstance(progId);
            if (app is null)
            {
                return false;
            }

            var ci = CultureInfo.InvariantCulture;

            // mail = app.CreateItem(0)  // 0 = olMailItem
            mail = app.GetType().InvokeMember("CreateItem", BindingFlags.InvokeMethod, null, app, new object[] { 0 }, ci);
            if (mail is null)
            {
                return false;
            }

            var t = mail.GetType();
            t.InvokeMember("To", BindingFlags.SetProperty, null, mail, new object?[] { to }, ci);
            t.InvokeMember("Subject", BindingFlags.SetProperty, null, mail, new object?[] { subject }, ci);
            // HTMLBody for the formatted upgrade acknowledgement, Body for plain text.
            t.InvokeMember(isHtml ? "HTMLBody" : "Body", BindingFlags.SetProperty, null, mail, new object?[] { body }, ci);
            t.InvokeMember("Send", BindingFlags.InvokeMethod, null, mail, null, ci);
            return true;
        }
        finally
        {
            if (mail is not null)
            {
                Marshal.ReleaseComObject(mail);
            }
            if (app is not null)
            {
                Marshal.ReleaseComObject(app);
            }
        }
    }

    /// <summary>
    /// The owner enquiry as HTML — bold headings, a bulleted details list, and a
    /// hyperlinked company signature, matching the client acknowledgement emails.
    /// Sent with <c>IsBodyHtml = true</c>.
    /// </summary>
    private string BuildOwnerHtml(LicensePurchaseRequest r)
    {
        const string productName = "Elpis EdgeConnect";
        var approvalWord = r.IsUpgrade ? "upgrade" : "purchase";

        var sb = new StringBuilder();
        sb.Append("<html><body style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1a1a1a;line-height:1.5;\">");
        sb.Append("<p>Dear Team,</p>");
        sb.Append("<p>" + (r.IsUpgrade
            ? "A license upgrade / renewal request has been submitted for <strong>" + productName + "</strong>."
            : "A new license purchase request has been submitted for <strong>" + productName + "</strong>.") + "</p>");
        sb.Append("<p><strong>" + (r.IsUpgrade ? "Upgrade Details" : "Purchase Details") + "</strong></p>");
        sb.Append("<ul>");
        sb.Append("<li><strong>Product:</strong> " + productName + "</li>");
        sb.Append("<li><strong>Client Name:</strong> " + HtmlEscape(r.CustomerName) + "</li>");
        sb.Append("<li><strong>Client Email:</strong> " + HtmlEscape(r.Email) + "</li>");
        if (!string.IsNullOrWhiteSpace(r.Phone))
        {
            sb.Append("<li><strong>Client Phone:</strong> " + HtmlEscape(r.Phone) + "</li>");
        }
        if (!string.IsNullOrWhiteSpace(r.Company))
        {
            sb.Append("<li><strong>Company:</strong> " + HtmlEscape(r.Company) + "</li>");
        }
        if (r.IsUpgrade)
        {
            sb.Append("<li><strong>Current License:</strong> " + HtmlEscape(CurrentLicenseLabel()) + "</li>");
        }
        sb.Append("<li><strong>Requested License Type:</strong> "
            + HtmlEscape(string.IsNullOrWhiteSpace(r.LicenseType) ? "(not specified)" : r.LicenseType) + "</li>");
        sb.Append("<li><strong>Gateway ID:</strong> " + HtmlEscape(LocalGatewayId() ?? "(not established yet)") + "</li>");
        sb.Append("</ul>");
        if (!string.IsNullOrWhiteSpace(r.Message))
        {
            sb.Append("<p><strong>Message from client:</strong><br/>"
                + HtmlEscape(r.Message).Replace("\n", "<br/>") + "</p>");
        }
        sb.Append("<p>Please review this request and complete the next step in your licensing process. "
            + "Once the " + approvalWord + " is approved and the license is issued, Please review this request and proceed with the licensing process. Once the purchase has been approved, generate the license file and send it to the client.</p>");
        sb.Append("<p>Should you require any additional information, please contact the client directly "
            + "using the details provided above.</p>");
        sb.Append("<p>Thank you,<br/>" + CompanyDetailsHtml() + "</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Plain-text owner enquiry — used only for the mailto: fallback.</summary>
    private string BuildOwnerText(LicensePurchaseRequest r)
    {
        const string productName = "Elpis EdgeConnect";
        var approvalWord = r.IsUpgrade ? "upgrade" : "purchase";

        var sb = new StringBuilder();
        sb.AppendLine("Dear Team,");
        sb.AppendLine();
        sb.AppendLine(r.IsUpgrade
            ? "A license upgrade / renewal request has been submitted for " + productName + "."
            : "A new license purchase request has been submitted for " + productName + ".");
        sb.AppendLine();
        sb.AppendLine(r.IsUpgrade ? "Upgrade Details" : "Purchase Details");
        sb.AppendLine("• Product: " + productName);
        sb.AppendLine("• Client Name: " + r.CustomerName);
        sb.AppendLine("• Client Email: " + r.Email);
        if (!string.IsNullOrWhiteSpace(r.Phone))
        {
            sb.AppendLine("• Client Phone: " + r.Phone);
        }
        if (!string.IsNullOrWhiteSpace(r.Company))
        {
            sb.AppendLine("• Company: " + r.Company);
        }
        if (r.IsUpgrade)
        {
            sb.AppendLine("• Current License: " + CurrentLicenseLabel());
        }
        sb.AppendLine("• Requested License Type: "
            + (string.IsNullOrWhiteSpace(r.LicenseType) ? "(not specified)" : r.LicenseType));
        // The gateway id is what the owner issues the license against (ADR-0036
        // binds a license to a single gateway id).
        sb.AppendLine("• Gateway ID: " + (LocalGatewayId() ?? "(not established yet)"));
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(r.Message))
        {
            sb.AppendLine("Message from client:");
            sb.AppendLine(r.Message);
            sb.AppendLine();
        }
        sb.AppendLine("Please review this request and complete the next step in your licensing process. "
            + "Once the " + approvalWord + " is approved and the license is issued, Please review this request and proceed with the licensing process. Once the purchase has been approved, generate the license file and send it to the client.");
        sb.AppendLine();
        sb.AppendLine("Should you require any additional information, please contact the client directly "
            + "using the details provided above.");
        sb.AppendLine();
        sb.AppendLine("Thank you,");
        sb.Append(CompanyDetails());
        return sb.ToString();
    }

    /// <summary>
    /// Professional signature block appended under the closing line of every
    /// enquiry email — company name, contact email, phone, and website. Sourced
    /// from configuration so one change updates both the owner and client mails.
    /// </summary>
    private string CompanyDetails()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Elpis IT Solutions");
        sb.AppendLine(_contact.Email);
        sb.AppendLine(_contact.Phone);
        sb.Append(_contact.Website);
        return sb.ToString();
    }

    /// <summary>This gateway's persisted identity id, or <c>null</c> if not yet established.</summary>
    private static string? LocalGatewayId() =>
        ElpisEdgeConnect.Host.FileSystemGatewayIdentity.TryReadPersisted(LicenseActivationService.ResolveIdentityPath());

    private string BuildMailto(string subject, string body) =>
        $"mailto:{_contact.Email}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";

    private async Task SendViaSmtpAsync(string host, string to, string subject, string body, string? replyTo, CancellationToken ct, bool isHtml = false)
    {
        var port = int.TryParse(
            Environment.GetEnvironmentVariable("EDGECONNECT_SMTP_PORT"),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 587;
        var user = Environment.GetEnvironmentVariable("EDGECONNECT_SMTP_USER");
        var password = Environment.GetEnvironmentVariable("EDGECONNECT_SMTP_PASSWORD");
        var from = Environment.GetEnvironmentVariable("EDGECONNECT_SMTP_FROM") ?? _contact.Email;
        var useSsl = !string.Equals(
            Environment.GetEnvironmentVariable("EDGECONNECT_SMTP_USESSL"), "false", StringComparison.OrdinalIgnoreCase);

        using var message = new MailMessage(from, to, subject, body) { IsBodyHtml = isHtml };
        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            message.ReplyToList.Add(new MailAddress(replyTo));
        }

        // Cap the SMTP timeout so an unreachable/slow host can't hang the request
        // near the client's ~100s limit; fall through to the next channel instead.
        using var client = new SmtpClient(host, port) { EnableSsl = useSsl, Timeout = 15000 };
        if (!string.IsNullOrWhiteSpace(user))
        {
            client.Credentials = new NetworkCredential(user, password);
        }

        await client.SendMailAsync(message, ct);
    }

    /// <summary>
    /// New-purchase acknowledgement emailed to the client. HTML with bold
    /// headings, bullet lists, and hyperlinked signature (sent with
    /// <c>IsBodyHtml = true</c>).
    /// </summary>
    private string BuildThankYouBody(LicensePurchaseRequest r)
    {
        const string productName = "Elpis EdgeConnect";

        var clientName = HtmlEscape(string.IsNullOrWhiteSpace(r.CustomerName) ? "Customer" : r.CustomerName);
        var requested = HtmlEscape(string.IsNullOrWhiteSpace(r.LicenseType) ? "(not specified)" : r.LicenseType);
        var gatewayId = HtmlEscape(LocalGatewayId() ?? "(not established yet)");

        return
            "<html><body style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1a1a1a;line-height:1.5;\">"
            + "<p>Dear " + clientName + ",<br/>"
            + "Thank you for choosing <strong>" + productName + "</strong>.<br/>"
            + "We have received your request to purchase a license for this product. "
            + "Your request is currently being processed.</p>"
            + "<p><strong>Request Details</strong></p>"
            + "<ul>"
            + "<li><strong>Product:</strong> " + productName + "</li>"
            + "<li><strong>Requested License:</strong> " + requested + "</li>"
            + "<li><strong>Request Gateway ID:</strong> " + gatewayId + "</li>"
            + "</ul>"
            + "<p><strong>What happens next?</strong></p>"
            + "<ul>"
            + "<li>Your purchase request will be reviewed.</li>"
            + "<li>If applicable, you will receive payment instructions or an invoice.</li>"
            + "<li>Once the payment is confirmed and the license is issued, your license will be activated.</li>"
            + "<li>You will receive a confirmation email with your license details and access instructions.</li>"
            + "</ul>"
            + "<p>If you have any questions, please contact our support team.<br/>"
            + "Thank you for your business.</p>"
            + "<p>Best regards,<br/>"
            + CompanyDetailsHtml()
            + "</p>"
            + "</body></html>";
    }

    /// <summary>
    /// Upgrade / renewal acknowledgement emailed to the client. HTML with bold
    /// headings, bullet lists, and hyperlinked signature (sent with
    /// <c>IsBodyHtml = true</c>).
    /// </summary>
    private string BuildUpgradeThankYouHtml(LicensePurchaseRequest r)
    {
        const string productName = "Elpis EdgeConnect";
        const string companyName = "Elpis IT Solutions";

        var clientName = HtmlEscape(string.IsNullOrWhiteSpace(r.CustomerName) ? "Customer" : r.CustomerName);
        var requestedUpgrade = HtmlEscape(string.IsNullOrWhiteSpace(r.LicenseType) ? "(not specified)" : r.LicenseType);
        var gatewayId = HtmlEscape(LocalGatewayId() ?? "(not established yet)");

        return
            "<html><body style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1a1a1a;line-height:1.5;\">"
            + "<p>Dear " + clientName + ",<br/>"
            + "Thank you for requesting an upgrade to your <strong>" + productName + "</strong> license.<br/>"
            + "We have received your license upgrade request and it is currently being processed.</p>"
            + "<p><strong>Request Details</strong></p>"
            + "<ul>"
            + "<li><strong>Product:</strong> " + productName + "</li>"
            + "<li><strong>Current License:</strong> " + HtmlEscape(CurrentLicenseLabel()) + "</li>"
            + "<li><strong>Requested Upgrade:</strong> " + requestedUpgrade + "</li>"
            + "<li><strong>Request Gateway ID:</strong> " + gatewayId + "</li>"
            + "</ul>"
            + "<p><strong>What happens next?</strong></p>"
            + "<ul>"
            + "<li>Your upgrade request will be reviewed.</li>"
            + "<li>If applicable, you will receive payment instructions or an invoice.</li>"
            + "<li>Once the payment is confirmed and the upgrade is approved, your upgraded license will be activated.</li>"
            + "<li>You will receive a confirmation email with your updated license details.</li>"
            + "</ul>"
            + "<p>If you have any questions, please contact our support team.<br/>"
            + "Thank you for choosing " + companyName + ".</p>"
            + "<p>Best regards,<br/>"
            + CompanyDetailsHtml()
            + "</p>"
            + "</body></html>";
    }

    /// <summary>
    /// HTML signature for the client emails — company name (bold) plus the
    /// contact email, phone, and website as mailto:/tel:/https hyperlinks.
    /// </summary>
    private string CompanyDetailsHtml()
    {
        var email = HtmlEscape(_contact.Email);
        var website = HtmlEscape(_contact.Website);

        var sb = new StringBuilder();
        sb.Append("<strong>Elpis IT Solutions</strong><br/>");
        sb.Append("<a href=\"mailto:" + email + "\">" + email + "</a><br/>");
        if (!string.IsNullOrWhiteSpace(_contact.Phone))
        {
            sb.Append("<a href=\"tel:" + HtmlEscape(TelHref(_contact.Phone)) + "\">"
                + HtmlEscape(_contact.Phone) + "</a><br/>");
        }
        sb.Append("<a href=\"" + website + "\">" + website + "</a>");
        return sb.ToString();
    }

    /// <summary>Reduce a display phone number to a tel: href (leading + and digits only).</summary>
    private static string TelHref(string phone)
    {
        var sb = new StringBuilder();
        foreach (var c in phone)
        {
            if (c == '+' || char.IsDigit(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>Escape user-supplied text so form input cannot inject markup.</summary>
    private static string HtmlEscape(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("&", "&amp;", StringComparison.Ordinal)
                   .Replace("<", "&lt;", StringComparison.Ordinal)
                   .Replace(">", "&gt;", StringComparison.Ordinal)
                   .Replace("\"", "&quot;", StringComparison.Ordinal);
}
