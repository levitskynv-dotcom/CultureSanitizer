using System;
using System.IO;
using TaleWorlds.Library;
using HarmonyLib;

namespace CultureSanitizer
{
    public static class ModLog
    {
        private const string LogTag = "[CultureSanitizer]";
        private static readonly object Lock = new object();

        public static void Log(string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {LogTag} {message}";

            try { Debug.Print(line); } catch { }

            try
            {
                lock (Lock)
                {
                    string dllDir = Path.GetDirectoryName(typeof(ModLog).Assembly.Location) ?? ".";
                    string path = Path.GetFullPath(Path.Combine(dllDir, "..", "..", "CultureSanitizer.log"));
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}