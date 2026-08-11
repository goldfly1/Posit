// Shipping — File I/O portal caps
// Auto-bound to Dafny stub: file-io
// DO NOT invent new structure. This file only inlays function behind pre-cut portals.

using System.IO;
using System.Threading.Tasks;
using _module;

namespace Shipping
{
    public static partial class FileIO
    {
        // Portal: ReadFile(path) returns (content: string)
        public static string ReadFile(string path)
        {
            return File.ReadAllText(path);
        }

        // Portal: WriteFile(path, content)
        public static void WriteFile(string path, string content)
        {
            File.WriteAllText(path, content);
        }

        // Portal: AppendFile(path, content)
        public static void AppendFile(string path, string content)
        {
            File.AppendAllText(path, content);
        }

        // Portal: ReadLines(path) returns (lines: seq<string>)
        public static string[] ReadLines(string path)
        {
            return File.ReadAllLines(path);
        }

        // Portal: WriteLines(path, lines)
        public static void WriteLines(string path, string[] lines)
        {
            File.WriteAllLines(path, lines);
        }

        // Portal: FileExists(path) returns (found: bool)
        public static bool FileExists(string path)
        {
            return File.Exists(path);
        }
    }
}