// ============================================================================
// File: TemplateSubstitutionEngineTests.cs
// Purpose: Unit coverage for the substitution engine that powers
//          BulkSourceMergeService. These are the v3.1 §2 + §10 hostile-value
//          and structural-error tests.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Management.Api.BulkSourceMerge;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class TemplateSubstitutionEngineTests
{
    // ── Sample template fragment matching the chip-3 Fanuc Sources[0] shape.
    //    deviceId appears 3 times: as DeviceId, in InstanceId derivation,
    //    and at the top of a derived computed field for the test.
    private const string SourceTemplate = """
        {
          "InstanceId": "{{ instanceId }}",
          "DeviceId": "{{ deviceId }}",
          "DeviceName": "{{ deviceName }}",
          "Enabled": {{ enabled }},
          "Connection": { "host": "{{ host }}" }
        }
        """;

    private static TemplateSubstitutionEngine BuildEngine() => new(new[]
    {
        new PlaceholderSpec { Name = "instanceId", Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "deviceId",   Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "deviceName", Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "enabled",    Position = PlaceholderPosition.RawBoolean,  ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "host",       Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
    });

    private static Dictionary<string, string> HappyValues() => new()
    {
        ["instanceId"] = "cnc-007-source",
        ["deviceId"]   = "cnc-007",
        ["deviceName"] = "Lathe-Bay-7",
        ["enabled"]    = "true",
        ["host"]       = "192.168.10.27",
    };

    [Fact]
    public void Render_HappyPath_ProducesValidJsonRoundTripping()
    {
        var engine = BuildEngine();
        var rendered = engine.Render(SourceTemplate, HappyValues());

        rendered.Should().Contain("\"InstanceId\": \"cnc-007-source\"");
        rendered.Should().Contain("\"DeviceId\": \"cnc-007\"");
        rendered.Should().Contain("\"Enabled\": true");
    }

    [Fact]
    public void Render_DeviceNameWithQuotesBackslashesBraces_PreservesLiteral()
    {
        // v3.1 §2 hostile-value test. The CSV value must round-trip through
        // JsonSerializer.Deserialize back to the original literal.
        var engine = BuildEngine();
        var values = HappyValues();
        values["deviceName"] = @"Mill ""A"" \ {{bad}}";

        var rendered = engine.Render(SourceTemplate, values);
        var parsed = System.Text.Json.JsonDocument.Parse(rendered);
        var deviceName = parsed.RootElement.GetProperty("DeviceName").GetString();

        deviceName.Should().Be(@"Mill ""A"" \ {{bad}}");
    }

    [Fact]
    public void Render_DeviceNameWithControlChar_PreservesLiteral()
    {
        var engine = BuildEngine();
        var values = HappyValues();
        values["deviceName"] = "Tab\there\nnewline";

        var rendered = engine.Render(SourceTemplate, values);
        var parsed = System.Text.Json.JsonDocument.Parse(rendered);
        var deviceName = parsed.RootElement.GetProperty("DeviceName").GetString();

        deviceName.Should().Be("Tab\there\nnewline");
    }

    [Fact]
    public void Render_EnabledNotExactlyTrueOrFalse_Throws()
    {
        var engine = BuildEngine();
        var values = HappyValues();
        values["enabled"] = "TRUE";

        var act = () => engine.Render(SourceTemplate, values);

        act.Should().Throw<TemplateSubstitutionException>()
           .Which.ErrorCode.Should().Be(BulkSourceMergeErrorCode.EnabledValueInvalid);
    }

    [Fact]
    public void Render_EnabledEmpty_Throws()
    {
        var engine = BuildEngine();
        var values = HappyValues();
        values["enabled"] = "";

        var act = () => engine.Render(SourceTemplate, values);

        act.Should().Throw<TemplateSubstitutionException>()
           .Which.ErrorCode.Should().Be(BulkSourceMergeErrorCode.EnabledValueInvalid);
    }

    [Fact]
    public void Render_HostWithQuoteInjection_DoesNotBreakJson()
    {
        var engine = BuildEngine();
        var values = HappyValues();
        values["host"] = @"1.2.3.4"";""port"":99";

        var rendered = engine.Render(SourceTemplate, values);
        var act = () => System.Text.Json.JsonDocument.Parse(rendered);

        act.Should().NotThrow("operator-supplied quotes must be escaped, never break out of the JSON context");
    }

    [Fact]
    public void Render_TemplateMissingExpectedPlaceholder_Throws()
    {
        var engine = BuildEngine();
        var broken = SourceTemplate.Replace("{{ deviceId }}", "fixed-id");

        var act = () => engine.Render(broken, HappyValues());

        act.Should().Throw<TemplateSubstitutionException>()
           .Which.ErrorCode.Should().Be(BulkSourceMergeErrorCode.TemplateSubstitutionCountMismatch);
    }

    [Fact]
    public void Render_TemplateHasUnknownMarker_Throws()
    {
        var engine = BuildEngine();
        var broken = SourceTemplate + "\n// trailing {{ rogue }} marker";

        var act = () => engine.Render(broken, HappyValues());

        act.Should().Throw<TemplateSubstitutionException>()
           .Which.ErrorCode.Should().Be(BulkSourceMergeErrorCode.TemplateResidualMarker);
    }

    [Fact]
    public void Render_MissingValueForRegisteredPlaceholder_Throws()
    {
        var engine = BuildEngine();
        var values = HappyValues();
        values.Remove("deviceName");

        var act = () => engine.Render(SourceTemplate, values);

        act.Should().Throw<TemplateSubstitutionException>()
           .Which.ErrorCode.Should().Be(BulkSourceMergeErrorCode.TemplateSubstitutionCountMismatch);
    }

    [Fact]
    public void Render_PlaceholderAppearsTwiceWhenExpectedOnce_Throws()
    {
        // Mirror v3.1 §2 expected-count guard.
        var engine = BuildEngine();
        var brokenTemplate = SourceTemplate + "  // {{ deviceId }} duplicated";

        var act = () => engine.Render(brokenTemplate, HappyValues());

        act.Should().Throw<TemplateSubstitutionException>()
           .Which.ErrorCode.Should().Be(BulkSourceMergeErrorCode.TemplateSubstitutionCountMismatch);
    }
}
