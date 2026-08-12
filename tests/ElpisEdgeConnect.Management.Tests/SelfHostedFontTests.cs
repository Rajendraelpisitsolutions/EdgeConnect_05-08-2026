// ============================================================================
// Tests: SelfHostedFontTests — pins the Studio's typeface as SELF-HOSTED.
//
// site.css used to open with:
//     @import url('https://fonts.googleapis.com/css2?family=Inter...');
//
// EdgeConnect is an on-premises gateway bound to 127.0.0.1 on factory-floor
// PCs, which are routinely air-gapped, and the operator's browser is usually on
// that same locked-down machine. A remote @import cannot resolve there: every
// page load spent a DNS lookup and TCP connect that failed before falling back
// to the next font in the stack, and hardened networks black-hole the request
// rather than refusing it, so the page stalls instead of degrading. It is also
// a third-party call out of an industrial product, which a customer's security
// review is entitled to refuse outright.
//
// This is exactly the kind of line a copy-pasted snippet reintroduces six
// months later — from a design tool, a CSS reset, an icon-font "quick fix" —
// and nothing else in the suite would notice, because the Studio renders fine
// on the developer's connected laptop. It only fails on the customer's
// air-gapped cell, where nobody is running tests.
//
// The invariant is therefore deliberately broader than "no Google Fonts": NO
// external URL of any kind in site.css. A self-hosted product stylesheet has no
// business reaching off-box for anything, whichever CDN is fashionable.
//
// Reference: site.css §"Typeface (self-hosted)".
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class SelfHostedFontTests
{
    /// <summary>
    /// Repository root, walked up from THIS FILE's compile-time path rather than
    /// from the test binary. The binary can legitimately be built outside the
    /// tree (the Studio holds a lock on bin\Debug, so the documented workaround
    /// is <c>-p:BaseOutputPath=&lt;temp&gt;</c>), at which point walking up from
    /// AppContext.BaseDirectory finds nothing. The source path is fixed at
    /// compile time and is always inside the repo.
    /// </summary>
    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ElpisEdgeConnect.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must be able to locate the repository root");
        return dir!.FullName;
    }

    private static string WwwRoot() => Path.Combine(
        RepoRoot(), "src", "ElpisEdgeConnect.Management", "wwwroot");

    private static string SiteCssPath() => Path.Combine(WwwRoot(), "css", "site.css");

    /// <summary>
    /// site.css with /* ... */ comments removed. The prose in this file names
    /// fonts.googleapis.com on purpose — as a "do not reintroduce this" warning
    /// — so a naive substring search over the raw text would flag the very
    /// comment documenting the fix. Assertions run against declarations only.
    /// </summary>
    private static string SiteCssWithoutComments() =>
        Regex.Replace(File.ReadAllText(SiteCssPath()), @"/\*.*?\*/", " ", RegexOptions.Singleline);

    [Fact]
    public void SiteCss_ContainsNoExternalFontImport()
    {
        var css = SiteCssWithoutComments();

        css.Should().NotContain("@import",
            "site.css must not pull any stylesheet at page load — a remote @import is " +
            "unreachable on an air-gapped plant network and blocks first paint while it fails");

        foreach (var host in new[] { "fonts.googleapis.com", "fonts.gstatic.com" })
        {
            css.Should().NotContain(host,
                $"the Studio must never fetch its typeface from {host}; Inter is " +
                "self-hosted under wwwroot/fonts/");
        }
    }

    [Fact]
    public void SiteCss_ReferencesNoOffBoxUrl()
    {
        var css = SiteCssWithoutComments();

        // Absolute (http://, https://) and protocol-relative (//host/...) URLs alike.
        var external = Regex.Matches(css, @"url\(\s*['""]?\s*(?:https?:)?//[^)]*", RegexOptions.IgnoreCase)
            .Select(m => m.Value.Trim())
            .ToList();

        external.Should().BeEmpty(
            "every asset site.css references must ship with the product and resolve on a " +
            "machine with no route to the internet. Found off-box reference(s):\n" +
            string.Join("\n", external));
    }

    [Fact]
    public void SiteCss_FontFaceSources_ResolveToFilesThatExist()
    {
        var css = SiteCssWithoutComments();
        var cssDir = Path.GetDirectoryName(SiteCssPath())!;

        var sources = Regex.Matches(css, @"url\(\s*['""]?([^'""()]+\.woff2?)['""]?\s*\)")
            .Select(m => m.Groups[1].Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        sources.Should().NotBeEmpty(
            "site.css is expected to declare @font-face rules for the self-hosted typeface; " +
            "if the font was intentionally dropped for the system stack, delete this test " +
            "rather than leaving it asserting nothing");

        var missing = new List<string>();
        foreach (var src in sources)
        {
            // @font-face URLs are relative to the stylesheet, not to wwwroot.
            var resolved = Path.GetFullPath(Path.Combine(cssDir, src.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(resolved))
            {
                missing.Add($"{src} -> {resolved}");
            }
        }

        missing.Should().BeEmpty(
            "a @font-face pointing at a file that is not in the repo is worse than the remote " +
            "@import it replaced: it fails silently on every machine, not just offline ones. " +
            "Missing:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void BundledFonts_ShipTheirOpenFontLicence()
    {
        var fontsDir = Path.Combine(WwwRoot(), "fonts");

        var fontFiles = Directory.Exists(fontsDir)
            ? Directory.GetFiles(fontsDir, "*.woff2")
            : Array.Empty<string>();

        if (fontFiles.Length == 0)
        {
            // Nothing vendored — no licence obligation to enforce.
            return;
        }

        var licence = Path.Combine(fontsDir, "OFL.txt");
        File.Exists(licence).Should().BeTrue(
            "Inter is SIL Open Font License 1.1. The OFL permits bundling the font with a " +
            "product, but REQUIRES the licence text to be distributed with the font files. " +
            $"Vendored {fontFiles.Length} .woff2 file(s) into {fontsDir} with no OFL.txt " +
            "beside them, which makes shipping the product a licence violation.");

        var text = File.ReadAllText(licence);
        text.Should().Contain("SIL Open Font License");
        text.Should().Contain("Version 1.1");
    }

    [Fact]
    public void NoManagementSourceFile_ReferencesAGoogleFontHost()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var extensions = new[] { "*.css", "*.razor", "*.html", "*.cshtml" };

        var offenders = new List<string>();

        foreach (var pattern in extensions)
        {
            foreach (var file in Directory.EnumerateFiles(src, pattern, SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                var text = File.ReadAllText(file);

                // Match the hosts only in URL position ("//host"), so prose that names
                // them — such as the warning comment in site.css — does not trip this.
                if (Regex.IsMatch(text, @"//fonts\.(googleapis|gstatic)\.com", RegexOptions.IgnoreCase))
                {
                    offenders.Add(Path.GetRelativePath(RepoRoot(), file));
                }
            }
        }

        offenders.Should().BeEmpty(
            "no shipped asset may fetch a webfont from Google at runtime. Offending file(s):\n" +
            string.Join("\n", offenders));
    }
}
