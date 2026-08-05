// ============================================================================
// File: ConsoleStartupSequenceObserver.cs
// Purpose: Diagnostic observer that prints every startup / shutdown phase
//          to stderr the moment the host crosses it. Swapped in for the
//          production NullStartupSequenceObserver during soak runs so the
//          operator can see exactly which locked phase is blocking if the
//          host hangs.
//
//          The host calls OnStartupPhase IMMEDIATELY BEFORE doing the work
//          for that phase — if we print "about to enter X" and never see
//          the next "about to enter Y", X is where the hang lives.
// ============================================================================

using System;
using ElpisEdgeConnect.Host;

namespace ElpisEdgeConnect.Tools.ModbusSoakRunner;

internal sealed class ConsoleStartupSequenceObserver : IStartupSequenceObserver
{
    public void OnStartupPhase(StartupPhase phase)
    {
        Console.Error.WriteLine($"[soak]   startup → entering phase {phase}");
    }

    public void OnShutdownPhase(StartupPhase phase)
    {
        Console.Error.WriteLine($"[soak]   shutdown → entering phase {phase}");
    }
}
