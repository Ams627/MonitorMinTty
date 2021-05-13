using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MonitorMinTty
{
    class Program
    {
        static Regex gitDirRegex = new Regex(@"(?<=gitdir:\s+)[^\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static Regex gitHeadRegex = new Regex(@"(?<=ref:\s+refs/heads/)[^\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        [DllImport("user32.dll")]
        static extern int SetWindowText(IntPtr hWnd, string text);
        private static void Main(string[] args)
        {
            try
            {
                var bashToMinTtyMapping = new Dictionary<int, (int mintPid, IntPtr mintHandle)>();
                for (; ; )
                {
                    var processes = Process.GetProcessesByName("mintty");
                    var regex = new Regex(@"(?<=bashHello:\s*)[0-9]+", RegexOptions.Compiled);
                    foreach (var minttyProcess in processes)
                    {
                        var match = regex.Match(minttyProcess.MainWindowTitle);
                        if (match.Success && match.Value.All(char.IsDigit))
                        {
                            var bashPid = int.Parse(match.Value);
                            if (!bashToMinTtyMapping.TryGetValue(bashPid, out var minttyInfo))
                            {
                                bashToMinTtyMapping.Add(bashPid, (minttyProcess.Id, minttyProcess.MainWindowHandle));
                            }
                        }
                    }

                    foreach (var entry in bashToMinTtyMapping)
                    {
                        var dir = ProcessUtilities.GetCurrentDirectory(entry.Key);
                        if (Directory.Exists(dir))
                        {
                            Console.WriteLine("setting dir");
                            var gitBranch = GetGitBranch(dir);
                            if (gitBranch != null)
                            {
                                if (bashToMinTtyMapping.TryGetValue(entry.Key, out var minttyInfo))
                                {
                                    SetWindowText(minttyInfo.mintHandle, gitBranch);
                                }
                            }
                        }
                    }
                        
                    System.Threading.Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                var fullname = System.Reflection.Assembly.GetEntryAssembly().Location;
                var progname = Path.GetFileNameWithoutExtension(fullname);
                Console.Error.WriteLine($"{progname} Error: {ex.Message}");
            }

        }

        enum GitStatus
        {
            NotFound,
            Directory,
            File
        }


        private static string GetGitBranch(string dir)
        {
            var current = dir;
            var gitStatus = GitStatus.NotFound;
            do
            {
                if (File.Exists(Path.Combine(current, ".git")))
                {
                    gitStatus = GitStatus.File;
                    break;
                }
                if (Directory.Exists(Path.Combine(current, ".git")))
                {
                    gitStatus = GitStatus.Directory;
                    break;
                }
                var dirInfo = new DirectoryInfo(current);
                var parent = dirInfo.Parent;
                if (parent == null)
                {
                    break;
                }
                current = parent.FullName;
            } while (true);

            if (gitStatus == GitStatus.NotFound)
            {
                return dir;
            }

            if (gitStatus == GitStatus.Directory)
            {
                var headFile = Path.Combine(current, ".git", "HEAD");
                var text = File.ReadLines(headFile).First().Trim();
                var match = gitHeadRegex.Match(text);
                if (match.Success) return match.Value;
            }
            else if (gitStatus == GitStatus.File)
            {
                var gitFile = Path.Combine(current, ".git");
                var text = File.ReadAllText(gitFile);
                var match = gitDirRegex.Match(text);
                if (match.Success)
                {
                    var headFile = Path.Combine(match.Value, "HEAD");
                    var text2 = File.ReadAllText(headFile);
                    var match2 = gitHeadRegex.Match(text2);
                    if (match2.Success) return match2.Value;
                }
            }

            return dir;
        }
    }
}
