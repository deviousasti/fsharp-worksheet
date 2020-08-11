using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FsWorksheet
{
    public class WorksheetServer : IDisposable
    {
        public WorksheetServer(string pipename, string filePath)
        {
            Pipename = pipename;
            FilePath = filePath;
        }

        public string Pipename { get; }

        public string FilePath { get; }
        public Process Process { get; private set; }

        public void Start()
        {
            var root = Path.GetDirectoryName(typeof(WorksheetServer).Assembly.Location);
            var target = Path.Combine(root, @"server\netcoreapp3.0\FsWorksheetServer.exe");
            this.Process = Process.Start(target, $"\"{Pipename}\" \"{FilePath}\"");
        }

        public void Dispose()
        {
            Process?.Close();
            Process?.Dispose();
        }
    }
}
