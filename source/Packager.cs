using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using s3pi.Interfaces;
using s3pi.Package;
using ScriptResource;

namespace Sims3ModPackager
{
    class Program
    {
        const uint TYPE_NAMEMAP = 0x0166038C;
        const uint TYPE_S3SA    = 0x073FAA07;
        const uint TYPE_XML     = 0x0333406C;

        static void Main(string[] args)
        {
            if (args.Length < 5)
            {
                Console.WriteLine("Usage: Packager.exe <outPackagePath> <dllPath> <xmlPath> <s3saName> <xmlFullName>");
                Console.WriteLine("  s3saName    = assembly name,          e.g. S3IO");
                Console.WriteLine("  xmlFullName = fully qualified class,  e.g. S3IO.ModEntry");
                return;
            }

            string outPackagePath = args[0];
            string dllPath        = args[1];
            string xmlPath        = args[2];
            string s3saName       = args[3]; // e.g. "S3IO"        — keep original casing
            string xmlFullName    = args[4]; // e.g. "S3IO.ModEntry" — keep original casing

            try
            {
                IPackage package = Package.NewPackage(0);

                // --- S3SA (compiled dll) ---
                ulong s3saInstance = HashFNV64(s3saName.ToLowerInvariant());
                IResourceKey s3saKey = new TGIBlock(0, null, TYPE_S3SA, 0, s3saInstance);

                byte[] dllBytes = File.ReadAllBytes(dllPath);
                MemoryStream dllStream = new MemoryStream(dllBytes);
                ScriptResource.ScriptResource s3saRes = new ScriptResource.ScriptResource(0, null);
                BinaryReader dllReader = new BinaryReader(dllStream);
                s3saRes.Assembly = dllReader;
                package.AddResource(s3saKey, s3saRes.Stream, true);
                Console.WriteLine("Added S3SA: {0} => Instance {1:X16}", s3saName, s3saInstance);

                // --- Tuning XML ---
                ulong xmlInstance = HashFNV64(xmlFullName.ToLowerInvariant());
                IResourceKey xmlKey = new TGIBlock(0, null, TYPE_XML, 0, xmlInstance);

                byte[] xmlBytes = File.ReadAllBytes(xmlPath);
                MemoryStream xmlStream = new MemoryStream(xmlBytes);
                package.AddResource(xmlKey, xmlStream, true);
                Console.WriteLine("Added XML:  {0} => Instance {1:X16}", xmlFullName, xmlInstance);

                // --- Name Map (0x0166038C) ---
                // Maps instance IDs -> resource name strings (proper casing).
                // S3PE shows these in the Name column. The game uses them to bind
                // XML tuning resources to class names and to identify S3SA assemblies.
                Dictionary<ulong, string> names = new Dictionary<ulong, string>();
                names[s3saInstance] = s3saName;
                names[xmlInstance]  = xmlFullName;

                byte[] nameMapBytes = BuildNameMap(names);
                IResourceKey nameMapKey = new TGIBlock(0, null, TYPE_NAMEMAP, 0, 0);
                MemoryStream nmStream = new MemoryStream(nameMapBytes);
                package.AddResource(nameMapKey, nmStream, true);
                Console.WriteLine("Added NameMap with {0} entries.", names.Count);

                // --- Compression & Save ---
                // All streams must remain open until SaveAs completes
                foreach (IResourceIndexEntry item in package.GetResourceList)
                {
                    item.Compressed = (ushort)((item.Filesize != item.Memsize) ? 0xFFFF : 0x0000);
                }

                package.SaveAs(outPackagePath);
                Package.ClosePackage(0, package);

                // Now safe to close streams
                dllReader.Close();
                dllStream.Close();
                xmlStream.Close();
                nmStream.Close();

                Console.WriteLine("Successfully built package: {0}", outPackagePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.ToString());
            }
        }

        // Serialize a name map in the Sims 3 NameMap binary format:
        //   uint32  version (1)
        //   int32   count
        //   repeated count times:
        //     uint64  instance ID
        //     int32   string length (number of characters)
        //     char[]  name characters (UTF-8/ASCII)
        static byte[] BuildNameMap(Dictionary<ulong, string> entries)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write((uint)1); // Version: 1
                w.Write((int)entries.Count); // Count
                foreach (KeyValuePair<ulong, string> kv in entries)
                {
                    w.Write(kv.Key);
                    w.Write((int)kv.Value.Length);
                    w.Write(kv.Value.ToCharArray());
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        // FNV-1 64-bit hash — pass lowercased input
        static ulong HashFNV64(string s)
        {
            ulong hash = 0xcbf29ce484222325;
            foreach (char c in s)
            {
                hash *= 0x100000001b3;
                hash ^= (byte)c;
            }
            return hash;
        }
    }
}
