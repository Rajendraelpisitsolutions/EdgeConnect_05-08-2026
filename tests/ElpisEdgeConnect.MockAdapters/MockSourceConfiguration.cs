// ============================================================================
// File: MockSourceConfiguration.cs
// Purpose: Concrete SourceConfiguration record for the mock source adapter.
//          The base SourceConfiguration is abstract, so the mock needs its
//          own derived record to be constructible from tests.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.2
// Milestone: D — phase 3.
// ============================================================================

using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.MockAdapters;

/// <summary>
/// Mock source adapter configuration. Carries no protocol-specific fields;
/// the mock's behavior is driven by the public mutators on
/// <see cref="MockSourceAdapter"/> rather than by configuration.
/// </summary>
public sealed record MockSourceConfiguration : SourceConfiguration
{
}
