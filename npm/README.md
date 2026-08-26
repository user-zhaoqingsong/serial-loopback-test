# serial-loopback-test

Launch the Windows [Serial Loopback Test](https://github.com/user-zhaoqingsong/serial-loopback-test) application with npx.

```powershell
npx serial-loopback-test
```

The package downloads the v1.2.0 Windows EXE from the official GitHub Release, verifies its SHA-256 checksum, caches it under `%LOCALAPPDATA%\SerialLoopbackTest`, and starts the application.

## Requirements

- Windows 10 or Windows 11
- Node.js 16 or newer
- .NET Framework 4.8
- The driver for your serial-port adapter

## Options

```text
--download-only  Download and verify without launching
--print-path     Print the cached EXE path
--version        Print the package version
--help           Show help
```

Source code and issue tracking are available in the [GitHub repository](https://github.com/user-zhaoqingsong/serial-loopback-test).
