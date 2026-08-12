Partial-offline scenario: ATC_TOOLS is intentionally absent.

The probe endpoint (HTTPD_MCNINFO) and four others respond with the
same payloads as the "running" scenario; only ATC_TOOLS 404s. This
pins that the new adapter continues to harvest the available payloads
when a single non-probe endpoint fails — no short-circuit, no whole-
cycle abandonment, and the subset parity assertion against the legacy
oracle continues to hold.

See ParityTests.LegacyOracle_And_NewAdapter_Agree_OfflinePartial_
SingleEndpointMissing for the assertion.

NOTE: HTTPD_MCNINFO is the StartAsync probe endpoint per
BrotherHttpSourceAdapter.cs:188-216. Removing it would cause the
adapter to fail to start, which is a separate concern covered by
ParityTests.NewAdapter_OfflineEmpty_AllEndpoints404_StartAsyncThrows.
