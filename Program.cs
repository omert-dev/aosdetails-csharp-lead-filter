using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CsvHelper;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System;

public record LeadRow(
    DateTime TimestampUtc,
    string Source,
    string FromName,
    string FromEmail,
    string Title,
    string Message,
    string Url,
    string City,
    decimal? Price,
    double Score,
    bool Qualified
);

public class ImapConfig {
    public string Host { get; set; } = "imap.gmail.com";
    public int Port { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Folder { get; set; } = "INBOX";
    public int SearchLookbackDays { get; set; } = 3;
}

public class SmtpConfig {
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string To { get; set; } = "";
}

public class ProvidersConfig {
    public System.Collections.Generic.List<string> SubjectMustContain { get; set; } = new();
}

public class AppConfig {
    public ImapConfig Imap { get; set; } = new();
    public SmtpConfig Smtp { get; set; } = new();
    public string CsvPath { get; set; } = "leads.csv";
    public double ScoreThreshold { get; set; } = 0.65;
    public System.Collections.Generic.List<string> Cities { get; set; } = new();
    public System.Collections.Generic.List<string> Keywords { get; set; } = new();
    public System.Collections.Generic.List<string> HotIntent { get; set; } = new();
    public ProvidersConfig Providers { get; set; } = new();
}

public static class Program
{
    public static async Task Main()
    {
        var appCfg = LoadConfig();
        Console.WriteLine("LeadInboxAgent starting…");
        Console.WriteLine($"IMAP host: {appCfg.Imap.Host}, folder: {appCfg.Imap.Folder}, lookback: {appCfg.Imap.SearchLookbackDays}d");
        Console.WriteLine($"CSV path: {appCfg.CsvPath}");
        Console.WriteLine();

        var processed = LoadProcessedSet();
        var newCount = await FetchAndProcessAsync(appCfg, processed);
        SaveProcessedSet(processed);

        Console.WriteLine($"\nDone. New leads processed: {newCount}");
    }

    // ---------- Config & state ----------
    static AppConfig LoadConfig() {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables(); // supports IMAP__PASSWORD, SMTP__PASSWORD overrides
        var cfg = builder.Build();
        var appCfg = new AppConfig();
        cfg.Bind(appCfg);
        return appCfg;
    }

    static System.Collections.Generic.HashSet<string> LoadProcessedSet() {
        var path = "processed.json";
        try {
            if (File.Exists(path)) {
                var json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(json) ?? new();
                return new System.Collections.Generic.HashSet<string>(list);
            }
        } catch { /* ignore */ }
        return new System.Collections.Generic.HashSet<string>();
    }

    static void SaveProcessedSet(System.Collections.Generic.HashSet<string> set) {
        var json = JsonSerializer.Serialize(set.ToList(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText("processed.json", json);
    }

    // ---------- Core ----------
    static async Task<int> FetchAndProcessAsync(AppConfig cfg, System.Collections.Generic.HashSet<string> processed) {
        using var client = new ImapClient();
        var imap = cfg.Imap;

        var secure = imap.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(imap.Host, imap.Port, secure);
        await client.AuthenticateAsync(imap.Username, imap.Password);

        var folder = await client.GetFolderAsync(imap.Folder);
        await folder.OpenAsync(FolderAccess.ReadOnly);

        var since = DateTime.UtcNow.AddDays(-Math.Max(1, imap.SearchLookbackDays));
        var uids = await folder.SearchAsync(SearchQuery.DeliveredAfter(since));

        int newLeads = 0;
        foreach (var uid in uids) {
            var msg = await folder.GetMessageAsync(uid);

            if (!SubjectMatches(msg.Subject, cfg.Providers.SubjectMustContain)) continue;

            var id = msg.MessageId ?? $"{uid}";
            if (processed.Contains(id)) continue;

            var bodyText = GetBestBodyText(msg);
            var from = msg.From?.Mailboxes?.FirstOrDefault();
            string fromName = from?.Name ?? "";
            string fromEmail = from?.Address ?? "";

            var source = DetectSource(msg.Subject);
            var title = msg.Subject ?? "";
            var url = ExtractFirstUrl(bodyText) ?? "";
            var price = TryParsePrice(bodyText);
            var city = TryFindCity(bodyText, cfg.Cities);

            var score = ScoreLead(bodyText, cfg.Keywords, cfg.HotIntent, cfg.Cities, price, out var tags);
            var qualified = score >= cfg.ScoreThreshold;

            var compactMsg = Compact(bodyText, 500);
            var row = new LeadRow(
                TimestampUtc: DateTime.UtcNow,
                Source: source,
                FromName: fromName,
                FromEmail: fromEmail,
                Title: title,
                Message: compactMsg,
                Url: url,
                City: city,
                Price: price,
                Score: score,
                Qualified: qualified
            );

            AppendCsv(cfg.CsvPath, row);
            processed.Add(id);
            newLeads++;

            Console.WriteLine($"[{(qualified ? "QUAL" : "LOG")}] {source} | {fromName} <{fromEmail}> | {Math.Round(score,2)} | {title}");

            if (cfg.Smtp.Enabled && qualified) {
                await NotifyAsync(cfg.Smtp, row, tags);
            }
        }

        await client.DisconnectAsync(true);
        return newLeads;
    }

    // ---------- Helpers ----------
    static bool SubjectMatches(string? subject, System.Collections.Generic.List<string> needles) {
        if (string.IsNullOrWhiteSpace(subject)) return false;
        foreach (var s in needles) {
            if (subject.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    static string DetectSource(string? subject) {
        var s = subject?.ToLowerInvariant() ?? "";
        if (s.Contains("offerup")) return "OfferUp";
        if (s.Contains("marketplace") || s.Contains("facebook")) return "Facebook";
        return "Email";
    }

    static string GetBestBodyText(MimeMessage msg) {
        string? text = msg.TextBody;
        if (string.IsNullOrWhiteSpace(text)) {
            var html = msg.HtmlBody ?? msg.Body?.ToString();
            text = StripHtml(html ?? "");
        }
        return WebUtility.HtmlDecode(text ?? "").Trim();
    }

    static string StripHtml(string html) {
        var noTags = Regex.Replace(html, "<.*?>", " ");
        return Regex.Replace(noTags, "\\s+", " ").Trim();
    }

    static decimal? TryParsePrice(string body) {
        var m = Regex.Match(body, @"(?:\$|price[:\s]*)(\d{2,5})", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(body, @"\b(\d{2,5})\s?(?:usd|dollars?)\b", RegexOptions.IgnoreCase);
        if (m.Success && decimal.TryParse(m.Groups[1].Value, out var d)) return d;
        return null;
    }

    static string? ExtractFirstUrl(string body) {
        var m = Regex.Match(body, @"https?://\S+");
        if (m.Success) return m.Value.TrimEnd('.', ')', ']', '}', ',');
        return null;
    }

    static string TryFindCity(string body, System.Collections.Generic.List<string> cities) {
        var lower = body.ToLowerInvariant();
        foreach (var c in cities) {
            if (Regex.IsMatch(lower, $@"\b{Regex.Escape(c.ToLower())}\b")) return c;
        }
        return "";
    }

    static string Compact(string s, int max) {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        return s.Substring(0, max) + "…";
    }

    static double ScoreLead(string body, System.Collections.Generic.IEnumerable<string> keywords, System.Collections.Generic.IEnumerable<string> hotIntent, System.Collections.Generic.IEnumerable<string> cities, decimal? price, out System.Collections.Generic.List<string> tags) {
        tags = new();
        if (string.IsNullOrWhiteSpace(body)) return 0;

        var txt = body.ToLowerInvariant();
        double score = 0;

        foreach (var k in keywords) {
            if (txt.Contains(k.ToLowerInvariant())) { score += 0.08; tags.Add($"kw:{k}"); }
        }
        foreach (var w in hotIntent) {
            if (txt.Contains(w.ToLowerInvariant())) { score += 0.10; tags.Add($"intent:{w}"); }
        }
        foreach (var c in cities) {
            if (Regex.IsMatch(txt, $@"\b{Regex.Escape(c.ToLower())}\b")) { score += 0.06; tags.Add($"city:{c}"); }
        }
        if (price is >= 50 and <= 2000) { score += 0.10; tags.Add("price:ok"); }

        if (Regex.IsMatch(txt, @"\bfree\b|cheapest|lowest|follow\s*me|promo|discount code|spam", RegexOptions.IgnoreCase)) score -= 0.15;

        return Math.Max(0, Math.Min(1, score));
    }

    static void AppendCsv(string path, LeadRow row) {
        var fileExists = File.Exists(path) && new FileInfo(path).Length > 0;
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: !fileExists));
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        if (!fileExists) csv.WriteHeader<LeadRow>();
        csv.NextRecord();
        csv.WriteRecord(row);
        csv.NextRecord();
    }

    static async Task NotifyAsync(SmtpConfig smtp, LeadRow row, System.Collections.Generic.List<string> tags) {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("LeadInboxAgent", smtp.Username));
        msg.To.Add(new MailboxAddress("Me", smtp.To));
        msg.Subject = $"Qualified lead {Math.Round(row.Score,2)} – {row.Source}: {row.Title}";

        var body = $@"
Source: {row.Source}
From: {row.FromName} <{row.FromEmail}>
City: {row.City}
Price: {(row.Price?.ToString() ?? "n/a")}
Score: {Math.Round(row.Score,2)} ({string.Join(", ", tags)})

Message:
{row.Message}

URL: {row.Url}
Logged at: {row.TimestampUtc:O}
";
        msg.Body = new TextPart("plain") { Text = body.Trim() };

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host, smtp.Port, smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect);
        await client.AuthenticateAsync(smtp.Username, smtp.Password);
        await client.SendAsync(msg);
        await client.DisconnectAsync(true);
    }
}
