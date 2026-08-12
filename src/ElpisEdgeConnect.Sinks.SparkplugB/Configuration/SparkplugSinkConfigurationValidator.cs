// ============================================================================
// File: Configuration/SparkplugSinkConfigurationValidator.cs
// Purpose: Semantic validation of a SparkplugSinkConfiguration, returning a
//          Core ValidationResult (never throwing on invalid input). Keeps the
//          facade thin and lets the rules be unit-tested directly. This is the
//          adapter-local last-chance validation; the route-level and
//          cross-destination validation is K4 (plan v3 §3.5, §9 slice 1).
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §9.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Configuration;

/// <summary>
/// Validates a <see cref="SparkplugSinkConfiguration"/>. All rules are collected;
/// the first issue is surfaced through <see cref="ValidationResult"/> (which
/// carries one code/message/path per result, matching the MQTT sink).
/// </summary>
public static class SparkplugSinkConfigurationValidator
{
    private static readonly char[] TopicReservedChars = ['/', '+', '#', '\0'];

    /// <summary>
    /// Validate a sink configuration. A configuration of the wrong type fails
    /// with <see cref="SparkplugErrors.ConfigWrongType"/>.
    /// </summary>
    /// <param name="config">The configuration to validate.</param>
    /// <returns>A success result, or a failure carrying the first issue found.</returns>
    public static ValidationResult Validate(SinkConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config is not SparkplugSinkConfiguration cfg)
        {
            return ValidationResult.Failure(
                SparkplugErrors.ConfigWrongType,
                $"Expected SparkplugSinkConfiguration, got {config.GetType().Name}.",
                "config");
        }

        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(cfg.BrokerHost))
        {
            issues.Add(Issue(SparkplugErrors.ConfigMissingBrokerHost, "BrokerHost is required.", "BrokerHost"));
        }

        // Omitted (null) is valid — the TLS-appropriate default applies; a PRESENT value must be in range.
        if (cfg.BrokerPort is { } port and (< 1 or > 65535))
        {
            issues.Add(Issue(SparkplugErrors.ConfigInvalidBrokerPort,
                $"BrokerPort must be 1..65535, got {port}.", "BrokerPort"));
        }

        if (!IsTopicSafe(cfg.GroupId))
        {
            issues.Add(Issue(SparkplugErrors.ConfigInvalidGroupId,
                "GroupId must be non-empty and must not contain '/', '+', '#', or NUL.", "GroupId"));
        }

        if (!IsTopicSafe(cfg.EdgeNodeId))
        {
            issues.Add(Issue(SparkplugErrors.ConfigInvalidEdgeNodeId,
                "EdgeNodeId must be non-empty and must not contain '/', '+', '#', or NUL.", "EdgeNodeId"));
        }

        if (cfg.KeepAliveSeconds is < 1 or > 65535)
        {
            issues.Add(Issue(SparkplugErrors.ConfigInvalidKeepAlive,
                $"KeepAliveSeconds must be 1..65535 (MQTT 3.1.1), got {cfg.KeepAliveSeconds}.", "KeepAliveSeconds"));
        }

        // A present client id must be non-blank; omit it (null) to auto-generate.
        if (cfg.ClientId is not null && string.IsNullOrWhiteSpace(cfg.ClientId))
        {
            issues.Add(Issue(SparkplugErrors.ConfigInvalidClientId,
                "ClientId, when present, must be non-blank (omit it to auto-generate).", "ClientId"));
        }

        if (cfg.TransportRecoveryMaxAttempts < 1)
        {
            issues.Add(Issue(SparkplugErrors.ConfigInvalidRecoveryBudget,
                $"TransportRecoveryMaxAttempts must be >= 1, got {cfg.TransportRecoveryMaxAttempts}.",
                "TransportRecoveryMaxAttempts"));
        }

        if (cfg.TransportRecoveryInitialDelayMs < 1)
        {
            issues.Add(Issue(SparkplugErrors.ConfigInvalidRecoveryBudget,
                $"TransportRecoveryInitialDelayMs must be >= 1, got {cfg.TransportRecoveryInitialDelayMs}.",
                "TransportRecoveryInitialDelayMs"));
        }

        if (cfg.TransportRecoveryMaxDelayMs < cfg.TransportRecoveryInitialDelayMs)
        {
            issues.Add(Issue(SparkplugErrors.ConfigInvalidRecoveryBudget,
                $"TransportRecoveryMaxDelayMs ({cfg.TransportRecoveryMaxDelayMs}) must be >= " +
                $"TransportRecoveryInitialDelayMs ({cfg.TransportRecoveryInitialDelayMs}).",
                "TransportRecoveryMaxDelayMs"));
        }

        var hasUser = !string.IsNullOrEmpty(cfg.Username);
        var hasPass = !string.IsNullOrEmpty(cfg.Password);
        if (hasUser != hasPass)
        {
            issues.Add(Issue(SparkplugErrors.ConfigAuthIncomplete,
                "Username and Password must both be set or both be empty.",
                hasUser ? "Password" : "Username"));
        }

        if (issues.Count == 0)
        {
            return ValidationResult.Success();
        }

        var first = issues[0];
        return ValidationResult.Failure(first.Code, first.Message, first.Path);
    }

    private static bool IsTopicSafe(string value) =>
        !string.IsNullOrEmpty(value) && value.IndexOfAny(TopicReservedChars) < 0;

    private static ValidationIssue Issue(string code, string message, string path) =>
        new() { Code = code, Message = message, Path = path };
}
