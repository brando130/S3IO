# S3IO

Native I/O bridge for The Sims 3. Bypasses the game's stripped .NET 2.0 Mono sandbox to provide file and directory operations to script mods.

## Why S3IO?

The Sims 3 executes script mods inside a heavily sandboxed Mono runtime where `System.IO` and `System.Net` are disabled. Previous community solutions to this problem relied on fixed timing delays to synchronize a native component with the game process — the native side would wait a hardcoded number of milliseconds (e.g. 30 seconds) before attempting to locate the managed side's data in memory. This worked on the hardware of the era, but modern CPUs (Intel Alder Lake and newer hybrid architectures, fast NVMe storage, etc.) change the game's startup timing enough that the fixed delay either fires too early (crash) or too late (timeout). Users are forced to hand-tune millisecond values by trial and error, and what works on one machine breaks on another.

S3IO eliminates timing dependence entirely. Instead of waiting a fixed delay, the native side continuously scans for a known signature (`S3IO_IPC`) that the managed side writes to a shared memory buffer. It doesn't matter whether the game loads in 10 seconds or 60 — the handshake completes whenever both sides are ready. No configuration file, no delay tuning, no per-machine adjustment.

S3IO is also fully self-contained (one `.package` + one `.asi`, no external processes or launcher utilities) and supports binary data, not just text.

## Architecture

Two components:

- **C# mod (`S3IO.dll` / `S3IO.package`)** — allocates a 16 MB shared memory buffer using `Marshal.AllocHGlobal` and writes an `S3IO_IPC` signature header. Exposes `ModIO.File.*`, `ModIO.Directory.*`, and `ModIO.System.*` to other mods. Yields cooperatively via `Simulator.Sleep` while waiting for the native side.
- **C++ ASI plugin (`S3IO.asi`)** — loaded into the game process via an ASI loader (`ddraw.dll`). Scans process memory for the `S3IO_IPC` signature, then monitors the shared buffer for commands and executes them using Win32 APIs.

Communication uses a status-byte state machine with cooperative yielding on the managed side and a polling loop on the native side. No OS-level locks are held across yields.

## Getting Started

### Requirements

- An ASI loader in your game's `Bin` directory (`ddraw.dll` — the same one used by other ASI mods like the Smooth Patch).
- The Sims 3 with any combination of expansion/stuff packs.

### Installation

1. Copy `S3IO.asi` to your game's `Bin` directory (e.g. `C:\Games\The Sims 3\Game\Bin\`).
2. Copy `S3IO.package` to your Mods folder (e.g. `Documents\Electronic Arts\The Sims 3\Mods\Packages\`).
3. Delete `scriptCache.package` from your Sims 3 user directory if it exists.
4. Launch the game. S3IO initializes automatically — no configuration needed.

### Using S3IO in Your Mod

Reference `S3IO.dll` at compile time. Do **not** bundle your own copy of `ModIO` or `FunctionTask` — use S3IO as a shared dependency.

```
/r:"path\to\S3IO.dll"
```

Add `using S3IO;` to your source files, then call `ModIO.File.*`, `ModIO.Directory.*`, or `ModIO.System.*`. Initialization is handled by S3IO's own static constructor — do not call `ModIO.Initialize()` from your mod.

## API Reference

All methods yield cooperatively when called from a yielding context (inside `Task.Simulate()`, `Interaction.Run()`, or a `FunctionTask` delegate). They are safe to call from any simulator context — non-yielding contexts use a spin-wait fallback.

### ModIO.File

```csharp
// Check if a file exists at the given path.
bool exists = ModIO.File.Exists(@"C:\path\to\file.txt");

// Read an entire file as a byte array.
byte[] data = ModIO.File.ReadAllBytes(@"C:\path\to\file.bin");

// Read an entire file as a UTF-8 string.
string text = ModIO.File.ReadAllText(@"C:\path\to\file.txt");

// Write a byte array to a file (creates or overwrites).
// Parent directories are created automatically.
ModIO.File.WriteAllBytes(@"C:\path\to\file.bin", data);

// Write a UTF-8 string to a file (creates or overwrites).
ModIO.File.WriteAllText(@"C:\path\to\file.txt", "Hello from The Sims 3");

// Append a byte array to a file (creates if missing).
ModIO.File.AppendAllBytes(@"C:\path\to\log.bin", data);

// Append a UTF-8 string to a file (creates if missing).
ModIO.File.AppendAllText(@"C:\path\to\log.txt", "New log entry\n");

// Delete a file.
ModIO.File.Delete(@"C:\path\to\file.txt");
```

### ModIO.Directory

```csharp
// Check if a directory exists.
bool exists = ModIO.Directory.Exists(@"C:\path\to\folder");

// Create a directory.
ModIO.Directory.Create(@"C:\path\to\folder");

// Delete a directory (non-recursive — must be empty).
ModIO.Directory.Delete(@"C:\path\to\folder", false);

// Delete a directory and all its contents (recursive).
ModIO.Directory.Delete(@"C:\path\to\folder", true);

// List all files in a directory (returns filenames, not full paths).
List<string> files = ModIO.Directory.GetFiles(@"C:\path\to\folder");

// List all subdirectories in a directory (returns names, not full paths).
List<string> dirs = ModIO.Directory.GetDirectories(@"C:\path\to\folder");
```

### ModIO.System

```csharp
// Get the current user's Documents folder path.
// Returns null if the path cannot be determined.
string docs = ModIO.System.GetDocumentsPath();
// e.g. "C:\Users\YourName\Documents"
```

### Return Values

| Method | Returns |
|---|---|
| `File.Exists` | `true` if the file exists, `false` otherwise |
| `File.ReadAllBytes` | `byte[]` on success, `null` on failure |
| `File.ReadAllText` | `string` on success, `null` on failure |
| `File.WriteAllBytes` | `true` on success, `false` on failure |
| `File.WriteAllText` | `true` on success, `false` on failure |
| `File.AppendAllBytes` | `true` on success, `false` on failure |
| `File.AppendAllText` | `true` on success, `false` on failure |
| `File.Delete` | `true` on success, `false` on failure |
| `Directory.Exists` | `true` if the directory exists, `false` otherwise |
| `Directory.Create` | `true` on success, `false` on failure |
| `Directory.Delete` | `true` on success, `false` on failure |
| `Directory.GetFiles` | `List<string>` of filenames (empty list on failure) |
| `Directory.GetDirectories` | `List<string>` of directory names (empty list on failure) |
| `System.GetDocumentsPath` | `string` path on success, `null` on failure |

## Build From Source (Optional)

```powershell
.\build_s3io.ps1
```

Compiles the C# mod, C++ ASI plugin, packages into `S3IO.package`, deploys to the game directories, and clears the script cache.
