// ============================================================================
// Tests: ManagementProjectIsolationTests — pins the LOCKED
// architectural rule: Blazor components MUST consume the management
// REST API only, never reach directly into Core/Host services. This
// is what keeps the API as the public contract (future Angular UI,
// future EREMOS V2 embedding, future plugin SDK all bind to the
// same surface as the Blazor pages).
//
// Enforcement strategy: at runtime, walk the Management assembly's
// types; any Razor component (descendant of ComponentBase) that
// declares an [Inject] field whose type lives in an EdgeConnect
// Core or Host namespace is a violation.
//
// Allowed: HttpClient injection (the canonical "consume the API"
// pattern), ManagementOptions injection (UI needs to know its own
// config — the API is a layer above this).
// ============================================================================

using System;
using System.Linq;
using System.Reflection;
using ElpisEdgeConnect.Management;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class ManagementProjectIsolationTests
{
    [Fact]
    public void NoRazorComponent_DirectlyInjects_CoreOrHostServices()
    {
        var managementAssembly = typeof(ManagementHostingExtensions).Assembly;
        var componentTypes = managementAssembly
            .GetTypes()
            .Where(t => typeof(ComponentBase).IsAssignableFrom(t) && !t.IsAbstract);

        var violations = new System.Collections.Generic.List<string>();

        foreach (var component in componentTypes)
        {
            // [Inject] applies to public + non-public instance properties.
            var injectProps = component
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<InjectAttribute>() is not null);

            foreach (var p in injectProps)
            {
                var typeNamespace = p.PropertyType.Namespace ?? string.Empty;
                if (typeNamespace.StartsWith("ElpisEdgeConnect.Core", StringComparison.Ordinal)
                    || typeNamespace.StartsWith("ElpisEdgeConnect.Host", StringComparison.Ordinal))
                {
                    // Allow-list: only the management's own namespaces.
                    // ManagementOptions is the only exception — UI may
                    // know its own config; everything else routes through
                    // the API.
                    violations.Add(
                        $"{component.FullName}.{p.Name} injects {p.PropertyType.FullName} " +
                        $"from {typeNamespace} — violates the Razor↔Core isolation rule.");
                }
            }
        }

        violations.Should().BeEmpty(
            "Razor components MUST consume the management REST API only. " +
            "Found direct injections from EdgeConnect.Core / EdgeConnect.Host:\n" +
            string.Join("\n", violations));
    }
}
