Intentionally empty scenario folder.

The Brother HTTP test server serves files named after the six Brother
endpoints (HTTPD_MCNINFO.txt, MNTP_CYCLETIME.txt, MNTP_WKCNTR.txt,
ATC_TOOLS.txt, ALARM_CURALMLIST.txt, MNTP_MAINTNOTICE.txt). Because
none of those files exist here, every GET returns 404 — which exercises
the adapter's fail-fast behaviour when the CNC is fully unreachable.

The probe endpoint is HTTPD_MCNINFO (see BrotherHttpSourceAdapter.cs
StartAsync ~line 188-216); when that 404s, StartAsync deliberately
throws InvalidOperationException with a BrotherErrors.HttpUnreachable
last-error record. See ParityTests.NewAdapter_OfflineEmpty_All
Endpoints404_StartAsyncThrows for the assertion.

This README file is only present so the directory survives the csproj
Content glob (Parity\Samples\**\*.txt) into the build output. The test
server never matches its name to any real Brother endpoint.
