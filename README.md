# S3IO

Native I/O bridge for The Sims 3. Bypasses the game's stripped .NET 2.0 Mono sandbox to provide file and directory operations to script mods.

## Architecture

Two components:

- **C# mod (`S3IO.dll`)** — allocates a 16MB shared memory buffer with an IPC protocol. Exposes `ModIO.File.*` and `ModIO.Directory.*` to other mods.
- **C++ ASI plugin (`S3IO.asi`)** — injected into the game process, scans for the shared buffer, and executes Win32 file/directory operations on behalf of the managed side.

Communication uses cooperative yielding (`Simulator.Sleep`) with a status byte state machine. No threads, no locks.

## Supported operations

- `File.Exists`, `ReadAllBytes/Text`, `WriteAllBytes/Text`, `AppendAllBytes/Text`, `Delete`
- `Directory.Exists`, `Create`, `Delete`, `GetFiles`, `GetDirectories`
- `System.GetDocumentsPath`

## Build

```powershell
.\build_s3io.ps1
```

Compiles the C# mod, C++ ASI plugin, packages, and deploys.
