using System;
using System.IO;
using System.Text;
using s3pi.Interfaces;
using s3pi.Package;

class DumpXML
{
    const uint TYPE_XML = 0x0333406C;
    const uint TYPE_S3SA = 0x073FAA07;

    static void Main(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("Usage: DumpXML.exe <package>"); return; }
        IPackage pkg = Package.OpenPackage(0, args[0], false);
        foreach (IResourceIndexEntry entry in pkg.GetResourceList)
        {
            if (entry.ResourceType == TYPE_XML)
            {
                Console.WriteLine("=== XML TGI: {0:X8}/{1:X8}/{2:X16} ===", entry.ResourceType, entry.ResourceGroup, entry.Instance);
                Stream s = pkg.GetResource(entry);
                byte[] buf = new byte[s.Length];
                s.Read(buf, 0, buf.Length);
                Console.WriteLine(Encoding.UTF8.GetString(buf));
            }
            else if (entry.ResourceType == TYPE_S3SA)
            {
                Console.WriteLine("=== S3SA Instance: {0:X16} ===", entry.Instance);
            }
        }
        Package.ClosePackage(0, pkg);
    }
}
