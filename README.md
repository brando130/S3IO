# S3IO - Sims 3 Native I/O Bridge Framework

S3IO is a high-performance, native-managed IPC bridge designed to bypass the stripped `.NET 2.0` Mono runtime sandbox limitations in **The Sims 3**. It enables Pure Scripting Mods to execute file system I/O and directory management from within the game engine.

## Architecture

- **C# Gameplay Mod (`S3IO.dll`)**: Compiles as a .NET 2.0 gameplay assembly. Allocates a 16MB+4 byte unmanaged memory buffer (`Marshal.AllocHGlobal`) with the magic signature `"S3IO_IPC"`. Communicates with the native side through a shared buffer protocol using cooperative yielding (`Simulator.Sleep`) to avoid blocking the game thread.
- **C++ Native Plugin (`S3IO.asi`)**: A 32-bit x86 ASI plugin injected into `The Sims 3\Game\Bin`. Scans private process memory (`MEM_PRIVATE`, `PAGE_READWRITE`) for `"S3IO_IPC"`, establishes an IPC handshake, and processes Win32 file/directory requests.

### Shared Buffer Layout

```
Offset  Size   Field
0-7     8B     Magic signature: "S3IO_IPC"
8       1B     Status byte (handshake/command state machine)
9-10    2B     Command code (ushort)
11-14   4B     Payload size (int) / Response size (int)
15+     ~16MB  Payload data (command-specific)
```

### Status State Machine

| Value | Name               | Meaning |
|------:|--------------------|---------|
| 0     | `STATUS_IDLE`      | C++ side connected, ready for commands |
| 1     | `STATUS_CS_WRITING`| C# is writing a command (or buffer not yet discovered) |
| 2     | `STATUS_READY`     | Command written, waiting for C++ pickup |
| 3     | `STATUS_CPP_PROCESSING` | C++ is executing the command |
| 4     | `STATUS_DONE`      | Command complete, response available |

### IPC Exclusion

`SendCommand` uses a cooperative boolean flag (`sIpcBusy`), not `lock{}`, to prevent concurrent calls. This avoids deadlocks when yielding via `Simulator.Sleep()` — see the modding manual section on cooperative scheduling.

## Supported Operations

### File Operations
- **File.Exists** — Check if a file exists
- **File.ReadAllBytes / ReadAllText** — Read file contents
- **File.WriteAllBytes / WriteAllText** — Write file (creates parent directories automatically)
- **File.AppendAllBytes / AppendAllText** — Append to file (creates parent directories automatically)
- **File.Delete** — Delete a file

### Directory Operations
- **Directory.Exists** — Check if a directory exists
- **Directory.Create** — Create a directory
- **Directory.Delete** — Delete a directory (with optional recursive flag)
- **Directory.GetFiles** — List files in a directory
- **Directory.GetDirectories** — List subdirectories in a directory

### System Operations
- **System.GetDocumentsPath** — Get the user's Documents folder path

## File Structure

- `S3IO.cs` — C# managed `ModIO` wrapper class (the public API)
- `S3IO.cpp` — C++ native ASI IPC engine
- `ModEntry.cs` — Mod entry point, world load handler, and `FunctionTask` implementation
- `S3IO.ModEntry.xml` — Mod tuning XML instantiator
- `AssemblyInfo.cs` — Assembly attributes (`[assembly: Tunable]`)
- `Packager.cs` — Headless `.package` packer utility (S3SA + XML + NameMap)
- `DumpXML.cs` — Diagnostic tool to dump XML and S3SA entries from a `.package`
- `build_s3io.ps1` — PowerShell build, packaging, and deployment script

## Building & Deploying

Run the build script from PowerShell:
```powershell
powershell -ExecutionPolicy Bypass -File .\build_s3io.ps1
```

The script automatically:
1. Compiles `Packager.exe` (using S3PI libraries)
2. Compiles `S3IO.dll` (.NET 2.0 Mono, `/nostdlib /unsafe`)
3. Compiles `S3IO.asi` (32-bit x86 MSVC via `vcvars32.bat`)
4. Packages `S3IO.package` (S3SA assembly + tuning XML + NameMap)
5. Deploys `S3IO.asi` to `The Sims 3\Game\Bin`, `S3IO.package` to `Mods\Packages`, and clears `scriptCache.package`
