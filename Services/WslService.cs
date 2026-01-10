using System.Diagnostics;
using System.Text;

namespace FileSpace.Services
{
    public class WslService
    {
        private static readonly Lazy<WslService> _instance = new(() => new WslService());
        public static WslService Instance => _instance.Value;

        private WslService() { }

        public async Task<List<(string Name, string Path)>> GetDistributionsAsync()
        {
            var distros = new List<(string Name, string Path)>();
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "wsl",
                    Arguments = "--list --quiet",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.Unicode
                };

                using var process = Process.Start(processStartInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var name = line.Trim();
                            if (!string.IsNullOrEmpty(name))
                            {
                                // Path format: \\wsl$\DistroName
                                var path = $"\\\\wsl$\\{name}"; 
                                distros.Add((name, path));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching WSL distributions: {ex.Message}");
            }

            return distros;
        }

        public bool IsWslInstalled()
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "wsl",
                    Arguments = "--status",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(processStartInfo);
                process?.WaitForExit();
                return process?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
