# Third-party notices

Elpis EdgeConnect bundles or links the following third-party components. This
file records the attribution and license obligations that ship with the product.

## libplctag (EtherNet/IP source adapter)

- **Component:** `libplctag` native library + `libplctag.NET` managed wrapper
  (`libplctag` 1.5.2, `libplctag.NativeImport`).
- **Used by:** `ElpisEdgeConnect.Sources.EthernetIp` (Allen-Bradley EtherNet/IP
  source adapter).
- **License:** The `libplctag.NET` wrapper is MPL-2.0. The native `libplctag`
  C library is dual-licensed **MPL-2.0 OR LGPL-2.1+**.
- **Obligation:** MPL-2.0 is file-level copyleft. EdgeConnect links the library
  without modifying any libplctag source file, so no source disclosure is
  triggered. The native binary remains separately replaceable (shipped via the
  `libplctag.NativeImport` package and auto-extracted at first use). This notice
  satisfies the MPL-2.0 attribution requirement.
- **Source:** <https://github.com/libplctag/libplctag.NET> and
  <https://github.com/libplctag/libplctag>.

> Reference: multi-protocol pilot expansion plan v2.1 §3.6 (licensing — GO).
