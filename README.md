# S3IO

Native I/O bridge for The Sims 3. Provides file and directory operations to script mods.

## Why S3IO?

### TL;DR

The Sims 3 runtime is sandboxed and does not include `System.IO`. Previous community solutions relied on a "delay_ms" parameter that often requires fine tuning per machine. 

### In more detail...

The Sims 3 executes script mods inside a heavily sandboxed Mono runtime where `System.IO` and `System.Net` are disabled. Previous community solutions to this problem relied on fixed timing delays to synchronize a native component with the game process. This worked on the hardware of the era, but modern CPUs (Intel Alder Lake and newer hybrid architectures, fast NVMe storage, etc.) change the game's startup timing enough that the fixed delay either fires too early (crash) or too late (timeout). Users are forced to hand-tune millisecond values by trial and error, and what works on one machine breaks on another.

S3IO eliminates timing dependence entirely. Instead of waiting a fixed delay, the native side continuously scans for a known signature (`S3IO_IPC`) that the managed side writes to a shared memory buffer. It doesn't matter whether the game loads in 10 seconds or 60 — the handshake completes whenever both sides are ready. No configuration file, no delay tuning, no per-machine adjustment.

S3IO is also fully self-contained (one `.package` + one `.asi`, no external processes or launcher utilities) and supports binary data, not just text.

## Architecture

Two components:

- **Managed C# (`S3IO.dll` / `S3IO.package`)** — allocates a 16 MB shared memory buffer using `Marshal.AllocHGlobal` and writes an `S3IO_IPC` signature header. Exposes `ModIO.File.*`, `ModIO.Directory.*`, and `ModIO.System.*` to other mods. Yields cooperatively via `Simulator.Sleep` while waiting for the native side.
- **Native C++ (`S3IO.asi`)** — loaded into the game process via an ASI loader (`ddraw.dll`). Scans process memory for the `S3IO_IPC` signature, then monitors the shared buffer for commands and executes them using Win32 APIs.

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

### Add S3IO as a compile-time reference and you're good to go:


```/r:"path\to\S3IO.dll"```
```
using S3IO;

// Read a file
string text = ModIO.File.ReadAllText(@"C:\path\to\file.txt");

// Write a file
ModIO.File.WriteAllText(@"C:\path\to\file.txt", "Hello world");
```

S3IO initializes itself — just call `ModIO.File.*`, `ModIO.Directory.*`, or `ModIO.System.*` and it handles the rest.

**Important**: Do not copy S3IO's ModIO or FunctionTask classes into your own mod. Reference S3IO.dll as a shared dependency. Copying them creates a second IPC buffer that the native side can't reliably distinguish from the real one.

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

Open `source\build_s3io.ps1` and set the paths at the top of the file to match your system:

| Variable | What it points to |
|---|---|
| `$S3IO_DIR` | This repo's root (where `source/` and `mod/` live) |
| `$S3PI_DLL_DIR` | [S3PI](https://sourceforge.net/projects/sims3tools/) library DLLs (`s3pi.Interfaces.dll`, `s3pi.Package.dll`, etc.) |
| `$GAME_REFS_DIR` | Sims 3 reference DLLs extracted from the game (`mscorlib.dll`, `SimIFace.dll`, `ScriptCore.dll`, etc.) |
| `$GAME_BIN_DIR` | Your Sims 3 `Game\Bin` directory |
| `$USER_MODS_DIR` | Your Sims 3 `Mods\Packages` directory |

Then run:

```powershell
.\source\build_s3io.ps1
```

This compiles the C# mod and C++ ASI plugin, packages into `S3IO.package`, deploys to the game directories, and clears the script cache. Requires the .NET Framework 4.x C# compiler (`csc.exe`) and MSVC Build Tools for the native side.
