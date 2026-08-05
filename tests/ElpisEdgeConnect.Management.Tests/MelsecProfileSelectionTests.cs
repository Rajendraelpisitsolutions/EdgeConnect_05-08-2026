// ============================================================================
// Tests: A-2O profile selection at the Management layer — wizard-model
// profile-aware validation (device set + radix follow the selected profile)
// and probe profile resolution (absent = Modern; explicit values must resolve
// to an operator-selectable registry profile). The IqF acceptance assertions
// are self-adjusting on the registry gate bit so the O-3 flip does not rewrite
// them.
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sources.Melsec;
using ElpisEdgeConnect.Sources.Melsec.Profiles;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class MelsecProfileSelectionTests
{
    private static MelsecTagWizardRow Row(string address, string datatype = "Int16") =>
        new() { Name = "t", Address = address, Datatype = datatype, ScanRateMs = 1000 };

    // ─── Wizard: profile-aware per-tag validation ─────────────────────────

    [Fact]
    public void ValidateTag_iqf_rejects_ZR_with_device_not_implemented()
    {
        var issues = MelsecSourceWizardModel.ValidateTag(Row("ZR100"), MelsecProfiles.IqF);

        issues.Should().ContainSingle(i => i.Code == MelsecAddressParser.DeviceNotImplemented);
    }

    [Fact]
    public void ValidateTag_iqf_accepts_octal_X10_and_rejects_X18()
    {
        MelsecSourceWizardModel.ValidateTag(Row("X10", "Bool"), MelsecProfiles.IqF).Should().BeEmpty();

        var issues = MelsecSourceWizardModel.ValidateTag(Row("X18", "Bool"), MelsecProfiles.IqF);
        issues.Should().ContainSingle(i => i.Code == MelsecAddressParser.InvalidAddress
            && i.Message.Contains("octal"));
    }

    [Fact]
    public void ValidateTag_legacy_overload_stays_Modern()
    {
        // ZR is valid on Modern; the parameterless overload must not change.
        MelsecSourceWizardModel.ValidateTag(Row("ZR100")).Should().BeEmpty();
    }

    // ─── Wizard: model-level profile gate ─────────────────────────────────

    [Fact]
    public void Model_rejects_unknown_profile_string()
    {
        var model = new MelsecSourceWizardModel
        {
            InstanceId = "m1", Host = "10.0.0.5", Port = 5007, DeviceProfile = "Banana",
        };

        var result = model.Validate();

        result.Errors.Should().Contain(e => e.Code == "MELSEC.CONFIG_PROFILE_NOT_IMPLEMENTED");
    }

    [Fact]
    public void Model_IqF_acceptance_follows_the_operator_selectable_gate()
    {
        var model = new MelsecSourceWizardModel
        {
            InstanceId = "m1", Host = "10.0.0.5", Port = 5007, DeviceProfile = "IqF",
        };

        var hasProfileError = model.Validate().Errors
            .Any(e => e.Code == "MELSEC.CONFIG_PROFILE_NOT_IMPLEMENTED");

        hasProfileError.Should().Be(!MelsecProfiles.IqF.IsOperatorSelectable);
    }

    [Fact]
    public void Model_tag_validation_follows_the_selected_profile_when_available()
    {
        // With IqF selected, a ZR tag must be flagged — but only once the profile
        // itself is selectable (before the flip, ResolvedProfile falls back to
        // Modern and the profile-level error is the blocker instead).
        var model = new MelsecSourceWizardModel
        {
            InstanceId = "m1", Host = "10.0.0.5", Port = 5007, DeviceProfile = "IqF",
        };
        model.Tags.Add(Row("ZR100"));

        var errors = model.Validate().Errors;

        if (MelsecProfiles.IqF.IsOperatorSelectable)
        {
            errors.Should().Contain(e => e.Code == MelsecAddressParser.DeviceNotImplemented);
        }
        else
        {
            errors.Should().Contain(e => e.Code == "MELSEC.CONFIG_PROFILE_NOT_IMPLEMENTED");
        }
    }

    [Fact]
    public void SelectableProfiles_always_contains_Modern()
    {
        MelsecSourceWizardModel.SelectableProfiles.Should().Contain(p => p.Key == MelsecDeviceProfile.Modern);
    }

    // ─── Probe: profile resolution ────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Probe_absent_profile_defaults_to_Modern(string? text)
    {
        MelsecProbeService.TryResolveProfile(text, out var profile, out _).Should().BeTrue();

        profile.Should().BeSameAs(MelsecProfiles.Modern);
    }

    [Fact]
    public void Probe_explicit_Modern_resolves()
    {
        MelsecProbeService.TryResolveProfile("Modern", out var profile, out _).Should().BeTrue();
        profile.Key.Should().Be(MelsecDeviceProfile.Modern);
    }

    [Fact]
    public void Probe_unknown_profile_fails_typed()
    {
        MelsecProbeService.TryResolveProfile("banana", out _, out var error).Should().BeFalse();
        error.Should().Contain("not available");
    }

    [Fact]
    public void Probe_IqF_resolution_follows_the_operator_selectable_gate()
    {
        var ok = MelsecProbeService.TryResolveProfile("IqF", out var profile, out _);

        ok.Should().Be(MelsecProfiles.IqF.IsOperatorSelectable);
        if (ok) profile.Key.Should().Be(MelsecDeviceProfile.IqF);
    }
}
