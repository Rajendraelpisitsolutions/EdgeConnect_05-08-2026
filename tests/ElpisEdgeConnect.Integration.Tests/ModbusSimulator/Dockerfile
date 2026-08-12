# syntax=docker/dockerfile:1
# ============================================================================
# Modbus TCP simulator used by the Phase 3 F1 integration tests.
#
# Builds a tiny pymodbus-based TCP server that returns deterministic coils /
# discrete inputs / holding registers / input registers. The test fixture
# (ModbusTcpSimulatorFixture) builds this image and spins a container for the
# duration of the test class.
#
# Build:   docker build -t elpis-modbus-sim:test .
# Run:     docker run --rm -p 5020:5020 elpis-modbus-sim:test
# ============================================================================
FROM python:3.12-slim

WORKDIR /app

# pymodbus is pinned to the 3.6/3.7 line — pymodbus 3.8 deprecated and 3.9+
# renamed `ModbusSlaveContext` to `ModbusDeviceContext`, breaking the sim's
# imports. Bump the upper bound only after porting server.py to the new API.
RUN pip install --no-cache-dir "pymodbus>=3.6,<3.8"

COPY server.py /app/server.py

# Default port matches the MODBUS_PORT env var in server.py.
ENV MODBUS_PORT=5020
EXPOSE 5020

ENTRYPOINT ["python", "-u", "/app/server.py"]
