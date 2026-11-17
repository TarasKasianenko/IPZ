using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace IPZ.Tracking
{
    public static class TrackerLauncher
    {
        const int ControlPort = 5056;

        static Process process;

        public static bool IsLaunching
        {
            get
            {
                try
                {
                    return process != null && !process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Launch()
        {
            if (IsTrackerRunning())
                return;

            var folder = FindTrackerFolder();
            if (folder == null)
            {
                UnityEngine.Debug.LogWarning("TrackerLauncher: Tracker folder not found, start the tracker manually.");
                return;
            }

            try
            {
                process = Process.Start(CreateStartInfo(folder));
                Application.quitting += Stop;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"TrackerLauncher: {e.Message}");
            }
        }

        static bool IsTrackerRunning()
        {
            try
            {
                using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, ControlPort));
                return false;
            }
            catch (SocketException)
            {
                return true;
            }
        }

        static string FindTrackerFolder()
        {
            var root = Path.GetDirectoryName(Application.dataPath);
            foreach (var candidate in new[] { root, Path.GetDirectoryName(root) })
            {
                if (candidate == null)
                    continue;
                var folder = Path.Combine(candidate, "Tracker");
                if (File.Exists(Path.Combine(folder, "tracker.py")))
                    return folder;
            }
            return null;
        }

        static ProcessStartInfo CreateStartInfo(string folder)
        {
            bool windows = Application.platform == RuntimePlatform.WindowsEditor
                           || Application.platform == RuntimePlatform.WindowsPlayer;
            var python = windows
                ? Path.Combine(folder, ".venv", "Scripts", "python.exe")
                : Path.Combine(folder, ".venv", "bin", "python");

            if (File.Exists(python))
                return new ProcessStartInfo(python, "tracker.py --hide")
                {
                    WorkingDirectory = folder,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

            if (windows)
                return new ProcessStartInfo(Path.Combine(folder, "run.cmd"), "--hide")
                {
                    WorkingDirectory = folder,
                    UseShellExecute = true,
                };

            return new ProcessStartInfo("/bin/bash", "-c \"exec ./run.sh --hide > tracker.log 2>&1\"")
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }

        static void Stop()
        {
            Application.quitting -= Stop;
            if (process == null)
                return;
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (InvalidOperationException) { }
            process.Dispose();
            process = null;
        }
    }
}
