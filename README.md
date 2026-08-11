# S3IO - Sims 3 Native I/O Bridge Framework

S3IO is a high-performance, native-managed IPC bridge designed to bypass the stripped `.NET 2.0` Mono runtime sandbox limitations in **The Sims 3**. It enables Pure Scripting Mods to execute file system I/O, directory management, networking, and process execution from within the game engine.

## Architecture

- **C# Gameplay Mod (`S3IO.dll`)**: Compiles as a .NET 2.0 gameplay assembly. Allocates a 16MB unmanaged memory buffer (`Marshal.AllocHGlobal`) with the magic signature `"S3IO_IPC"`.
- **C++ Native Plugin (`S3IO.asi`)**: A 32-bit x86 ASI plugin injected into `The Sims 3\Game\Bin`. Scans private process memory (`MEM_PRIVATE`) for `"S3IO_IPC"`, establishes an IPC handshake, and processes Win32 requests.

## File Structure

- `S3IO.cs` - C# managed ModIO wrapper class
- `S3IO.cpp` - C++ native ASI IPC engine
- `ModEntry.cs` - Mod entry point & world load event handler
- `S3IO.ModEntry.xml` - Mod tuning XML instantiator
- `AssemblyInfo.cs` - Assembly attributes (`[assembly: Tunable]`)
- `Packager.cs` - Headless .package packer utility
- `build_s3io.ps1` - PowerShell build, packaging, and deployment script

## Building & Deploying

Run the build script from PowerShell:
```powershell
powershell -ExecutionPolicy Bypass -File .\build_s3io.ps1
```

The script automatically:
1. Compiles `Packager.exe`
2. Compiles `S3IO.dll` (.NET 2.0 Mono)
3. Compiles `S3IO.asi` (32-bit x86 MSVC)
4. Packages `S3IO.package`
5. Deploys `S3IO.asi` to `The Sims 3\Game\Bin` and `S3IO.package` to `Mods\Packages`
6. Clears `scriptCache.package`
