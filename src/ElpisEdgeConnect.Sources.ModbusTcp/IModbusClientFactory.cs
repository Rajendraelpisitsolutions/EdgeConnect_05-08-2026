// ============================================================================
// File: IModbusClientFactory.cs
// Purpose: Selects the concrete IModbusClient transport for a Modbus source
//          based on its configured encapsulation. This is the single seam where
//          TCP vs RTU-over-TCP vs serial-RTU is chosen; the adapter, connection
//          manager, and transaction executor stay transport-agnostic.
// Reference: docs/sessions/2026-06-24-modbus-rtu-support-plan-v1.md
// ============================================================================

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>Builds the transport client appropriate to a source's encapsulation.</summary>
internal interface IModbusClientFactory
{
    /// <summary>Create an <see cref="IModbusClient"/> for the given config.</summary>
    IModbusClient Create(ModbusTcpSourceConfiguration config);
}

/// <summary>
/// Default factory: native TCP → <see cref="FluentModbusClient"/>; RTU-over-TCP
/// and serial RTU → <see cref="FluentModbusRtuClient"/>.
/// </summary>
internal sealed class FluentModbusClientFactory : IModbusClientFactory
{
    /// <inheritdoc/>
    public IModbusClient Create(ModbusTcpSourceConfiguration config)
    {
        System.ArgumentNullException.ThrowIfNull(config);
        return config.Encapsulation switch
        {
            ModbusEncapsulation.Tcp => new FluentModbusClient(),
            ModbusEncapsulation.RtuOverTcp => new FluentModbusRtuClient(RtuTransportMode.Tcp, config),
            ModbusEncapsulation.SerialRtu => new FluentModbusRtuClient(RtuTransportMode.Serial, config),
            _ => throw new ModbusFatalException(
                ModbusErrors.ConfigInvalid,
                $"Unsupported Modbus encapsulation '{config.Encapsulation}'."),
        };
    }
}
