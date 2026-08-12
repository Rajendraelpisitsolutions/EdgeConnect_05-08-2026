// ============================================================================
// File: TagTable.cs
// Purpose: The simulator's tag catalog — maps controller tag names (symbolic
//          addresses) to SimulatedTags. Allen-Bradley tag names are
//          case-insensitive, so lookups are ordinal-ignore-case.
// ============================================================================

namespace ElpisEdgeConnect.Tools.EthernetIpSimulator;

/// <summary>The set of tags the simulated controller exposes for reading.</summary>
internal sealed class TagTable
{
    private readonly Dictionary<string, SimulatedTag> _tags = new(StringComparer.OrdinalIgnoreCase);

    public void Add(SimulatedTag tag) => _tags[tag.Name] = tag;

    public bool TryGet(string name, out SimulatedTag tag) => _tags.TryGetValue(name, out tag!);

    public IReadOnlyCollection<SimulatedTag> All => _tags.Values;

    public int Count => _tags.Count;
}
