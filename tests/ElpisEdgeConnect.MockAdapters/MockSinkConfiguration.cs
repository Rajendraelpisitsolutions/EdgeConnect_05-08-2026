// ============================================================================
// File: MockSinkConfiguration.cs
// Purpose: Concrete SinkConfiguration record for the mock sink adapter.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.3
// Milestone: D — phase 3.
// ============================================================================

using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.MockAdapters;

/// <summary>
/// Mock sink adapter configuration. Carries no protocol-specific fields;
/// the mock's behavior is driven by the public mutators on
/// <see cref="MockSinkAdapter"/>.
/// </summary>
public sealed record MockSinkConfiguration : SinkConfiguration
{
}
