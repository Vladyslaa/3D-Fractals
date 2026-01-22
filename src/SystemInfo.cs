using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.DXGI;
using System.Management;

namespace _3D_Fractals.src
{
    static class SystemInfo
    {
        public static ISystemInfo _sysInfo = Create();

        public static float? TotalVram { get => _sysInfo.TotalVram; }

        public static float TotalMemory { get => _sysInfo.TotalPhysicalMemory; }

        public static string CpuName { get => _sysInfo.CpuName; }
        public static int PhysicalCpuCount { get => _sysInfo.PhysicalCpuCount; }
        public static readonly int LogicalCpuCount = Environment.ProcessorCount;

        public static readonly string OSName = Environment.OSVersion.ToString();
        public static readonly string OSArchitecture = RuntimeInformation.OSArchitecture.ToString();

        public static string GpuVendor = null;
        public static string GpuRenderer = null;
        public static string GpuVersion = null;

        public static ISystemInfo Create()
        {
            if (OperatingSystem.IsWindows()) return new WindowsSystemInfo();
            else if (OperatingSystem.IsLinux() && !OperatingSystem.IsAndroid()) return new LinuxSystemInfo();
            else if (OperatingSystem.IsMacOS()) return new MacSystemInfo();
            else throw new PlatformNotSupportedException();
        }
    }

    internal interface ISystemInfo
    {
        float TotalPhysicalMemory { get; } // in gigabytes

        public float? TotalVram { get; } // in gigabytes

        string CpuName { get; }
        int PhysicalCpuCount { get; }
    }

    sealed class WindowsSystemInfo : ISystemInfo
    {
        public float TotalPhysicalMemory { get; }

        public float? TotalVram { get; }

        public string CpuName { get; }
        public int PhysicalCpuCount { get; }

        public WindowsSystemInfo() 
        {
            MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(mem);
            if (GlobalMemoryStatusEx(ref mem)) TotalPhysicalMemory = (float)mem.ullTotalPhys / Core.Gb;

            TotalVram = GetVramInfo();

            var cpu = QueryCpuWindows();
            CpuName = cpu.name;
            PhysicalCpuCount = cpu.cores;
        }

#pragma warning disable CA1416 // Validate platform compatibility
        private static (string name, int cores, int logical) QueryCpuWindows()
        {
            using var searcher = new ManagementObjectSearcher( "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");

            foreach (ManagementObject mo in searcher.Get().Cast<ManagementObject>())
            {
                string name = mo["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                int cores = Convert.ToInt32(mo["NumberOfCores"]);
                int logical = Convert.ToInt32(mo["NumberOfLogicalProcessors"]);

                return (name, cores, logical);
            }

            return ("Unknown CPU", 0, Environment.ProcessorCount);
        }
#pragma warning restore CA1416 // Validate platform compatibility

        private unsafe ulong? GetVramInfo()
        {
            try
            {
                var dxgi = DXGI.GetApi(null);

                IDXGIFactory1* factory;
                Guid uuid = IDXGIFactory1.Guid;
                int hr = dxgi.CreateDXGIFactory1(&uuid, (void**)&factory);

                if (hr != 0) return null;

                ulong maxVram = 0;
                uint index = 0;
                IDXGIAdapter1* adapter;

                while (factory->EnumAdapters1(index, &adapter) == 0)
                {
                    AdapterDesc1 desc;
                    adapter->GetDesc1(&desc);

                    if ((desc.Flags & (uint)AdapterFlag.Software) == 0)
                    {
                        ulong currentVram = desc.DedicatedVideoMemory;
                        if (currentVram > maxVram) maxVram = currentVram;
                    }

                    adapter->Release();
                    index++;
                }

                factory->Release();
                dxgi.Dispose();

                return maxVram / Core.Gb;
            }
            catch
            {
                return null;
            }
        }

        #region Win32 P/Invoke

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        #endregion
    }

    sealed class LinuxSystemInfo : ISystemInfo
    {
        public float? TotalVram { get; }

        public float TotalPhysicalMemory { get; }

        public string CpuName { get; }
        public int PhysicalCpuCount { get; }

        public LinuxSystemInfo()
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:"))
                {
                    TotalPhysicalMemory = (float)ulong.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]) / Core.Mb;
                    break;
                }
            }

            try
            {
                foreach (var card in Directory.GetDirectories("/sys/class/drm/"))
                {
                    string path = Path.Combine(card, "device/mem_info_vram_total");
                    if (File.Exists(path))
                    {
                        ulong vramBytes = ulong.Parse(File.ReadAllText(path).Trim());
                        TotalVram = vramBytes / Core.Gb;
                        break;
                    }
                }
            }
            catch
            {
                TotalVram = null;
            }

            CpuName = "Unknown";
            int logicalCpuCount = 0;

            var physicalIds = new HashSet<string>();

            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name"))
                    CpuName = line.Split(':', 2)[1].Trim();

                else if (line.StartsWith("processor"))
                    logicalCpuCount++;

                else if (line.StartsWith("physical id"))
                    physicalIds.Add(line.Split(':', 2)[1].Trim());
            }

            PhysicalCpuCount = physicalIds.Count > 0 ? physicalIds.Count : logicalCpuCount;
        }
    }

    sealed class MacSystemInfo : ISystemInfo
    {
        public float? TotalVram { get; } = null;

        public float TotalPhysicalMemory { get; }

        public string CpuName { get; }
        public int PhysicalCpuCount { get; }

        public MacSystemInfo()
        {
            TotalPhysicalMemory = QueryUlong("hw.memsize") / (float)Core.Gb;

            CpuName = QueryString("machdep.cpu.brand_string");
            PhysicalCpuCount = (int)QueryUlong("hw.physicalcpu");
        }

        private static string QueryString(string key)
        {
            return RunSysctl(key);
        }

        private static ulong QueryUlong(string key)
        {
            return ulong.Parse(RunSysctl(key));
        }

        private static string RunSysctl(string key)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "sysctl",
                    Arguments = "-n " + key,
                    RedirectStandardOutput = true
                }
            };

            p.Start();
            string output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return output;
        }
    }
}