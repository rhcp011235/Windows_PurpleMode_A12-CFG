# Device CFG Tool

Windows utility for device recovery, DIAG boot, syscfg read/write, and erase workflows.
The source in this repository is maintained as an independent .NET implementation.

## Build

```sh
dotnet build DeviceCfgTool.csproj
```

Create a self-contained Windows x64 publish directory with:

```sh
dotnet publish DeviceCfgTool.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o package/win-x64
```

The packaged GUI executable is:

```text
package/win-x64/DeviceCfgTool.exe
```

## Features

- Query connected recovery/DFU device values through the bundled recovery client.
- Install or repair the Windows USB driver binding required by the supported workflows.
- Boot supported A7-A11 and A12-A13 devices into DIAG mode.
- Run DIAG erase and DFU erase flows.
- Scan DIAG serial ports, read syscfg values, and write SrNm/WMac/BMac fields.

## Runtime Files

At runtime the application resolves helper tools from `files/` beside the executable.
The repository includes redistributable runtime components under `payload/files/`; MSBuild
copies that tree into `files/` during build and publish.

Generated publish output is intentionally ignored by git. Rebuild it locally with the
publish command above when you need a distributable Windows directory.
