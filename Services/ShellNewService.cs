using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using FileSpace.Models;
using Microsoft.Win32;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;

namespace FileSpace.Services
{
    /// <summary>
    /// Shell 新建文件服务 - 负责读取注册表 ShellNew 配置并创建新文件
    /// </summary>
    public class ShellNewService
    {
        private static readonly Lazy<ShellNewService> _instance = new(() => new ShellNewService());
        public static ShellNewService Instance => _instance.Value;

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, uint cchOutBuf, IntPtr ppvReserved);

        private List<ShellNewEntry>? _cachedEntries;
        private DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

        private ShellNewService() { }

        /// <summary>
        /// 获取所有可用的 ShellNew 条目
        /// </summary>
        /// <param name="forceRefresh">强制刷新缓存</param>
        public List<ShellNewEntry> GetShellNewEntries(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedEntries != null && DateTime.Now - _cacheTime < CacheExpiry)
            {
                return _cachedEntries;
            }

            _cachedEntries = LoadShellNewEntriesFromRegistry();
            _cacheTime = DateTime.Now;
            return _cachedEntries;
        }

        /// <summary>
        /// 在指定目录创建新文件
        /// </summary>
        /// <param name="entry">ShellNew 条目</param>
        /// <param name="targetDirectory">目标目录</param>
        /// <returns>创建的文件完整路径，失败返回 null</returns>
        public string? CreateNewFile(ShellNewEntry entry, string targetDirectory)
        {
            if (string.IsNullOrEmpty(targetDirectory) || !Directory.Exists(targetDirectory))
            {
                return null;
            }

            try
            {
                // 生成唯一文件名
                var baseName = entry.GetDefaultFileName();
                var newPath = GetUniqueFilePath(targetDirectory, baseName);

                // 按优先级创建文件：Command > FileName(Template) > Data > NullFile
                if (!string.IsNullOrEmpty(entry.Command))
                {
                    return CreateFileWithCommand(entry.Command, newPath, targetDirectory);
                }

                if (!string.IsNullOrEmpty(entry.TemplatePath))
                {
                    return CreateFileFromTemplate(entry.TemplatePath, newPath);
                }

                if (entry.Data != null && entry.Data.Length > 0)
                {
                    return CreateFileWithData(newPath, entry.Data);
                }

                // NullFile 或默认情况：创建空文件（特殊处理 .zip）
                return CreateEmptyFile(newPath, entry.Extension);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"创建新文件失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 使用 Shell API 创建新文件（支持管理员权限和自动重命名）
        /// </summary>
        public async Task<string?> CreateNewFileWithShellAsync(ShellNewEntry entry, string targetDirectory, Window? ownerWindow = null)
        {
            if (string.IsNullOrEmpty(targetDirectory) || !Directory.Exists(targetDirectory))
            {
                return null;
            }

            try
            {
                var baseName = entry.GetDefaultFileName();
                var newPath = GetUniqueFilePath(targetDirectory, baseName);

                // 对于有 Command 的类型，直接执行命令
                if (!string.IsNullOrEmpty(entry.Command))
                {
                    return await Task.Run(() => CreateFileWithCommand(entry.Command, newPath, targetDirectory));
                }

                // 使用 Shell 文件操作
                return await Task.Run(() =>
                {
                    try
                    {
                        // 先创建临时文件
                        string? tempPath = null;

                        if (!string.IsNullOrEmpty(entry.TemplatePath) && File.Exists(entry.TemplatePath))
                        {
                            tempPath = entry.TemplatePath;
                        }
                        else
                        {
                            // 创建临时源文件
                            tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + entry.Extension);
                            
                            if (entry.Data != null && entry.Data.Length > 0)
                            {
                                File.WriteAllBytes(tempPath, entry.Data);
                            }
                            else
                            {
                                CreateEmptyFile(tempPath, entry.Extension);
                            }
                        }

                        // 如果使用了临时文件，复制到目标位置
                        if (tempPath != entry.TemplatePath)
                        {
                            File.Move(tempPath, newPath);
                        }
                        else
                        {
                            File.Copy(tempPath, newPath, false);
                        }

                        // 如果有 Data 且使用了模板，还需要写入 Data
                        if (entry.Data != null && entry.Data.Length > 0 && !string.IsNullOrEmpty(entry.TemplatePath))
                        {
                            File.WriteAllBytes(newPath, entry.Data);
                        }

                        return newPath;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Shell 文件操作失败: {ex.Message}");
                        return null;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"创建新文件失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public void ClearCache()
        {
            _cachedEntries = null;
            _cacheTime = DateTime.MinValue;
        }

        #region Private Methods

        /// <summary>
        /// 从注册表加载 ShellNew 条目
        /// </summary>
        private List<ShellNewEntry> LoadShellNewEntriesFromRegistry()
        {
            var entries = new List<ShellNewEntry>();

            try
            {
                using var classesRoot = Registry.ClassesRoot;
                var subKeyNames = classesRoot.GetSubKeyNames();

                foreach (var ext in subKeyNames.Where(k => k.StartsWith(".")))
                {
                    try
                    {
                        var entry = TryLoadShellNewEntry(classesRoot, ext);
                        if (entry != null)
                        {
                            entries.Add(entry);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"处理扩展名 {ext} 失败: {ex.Message}");
                    }
                }

                // 按显示名称排序并去重
                entries = entries
                    .GroupBy(e => e.Extension.ToLowerInvariant())
                    .Select(g => g.First())
                    .OrderBy(e => e.DisplayName)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"从注册表加载 ShellNew 条目失败: {ex.Message}");
            }

            return entries;
        }

        /// <summary>
        /// 尝试加载单个 ShellNew 条目
        /// </summary>
        private ShellNewEntry? TryLoadShellNewEntry(RegistryKey classesRoot, string extension)
        {
            using var extKey = classesRoot.OpenSubKey(extension);
            if (extKey == null) return null;

            string? progId = extKey.GetValue("") as string;
            RegistryKey? shellNewKey = null;

            try
            {
                // 按优先级查找 ShellNew 键
                // 1. 直接在扩展名下查找
                shellNewKey = extKey.OpenSubKey("ShellNew");

                // 2. 在扩展名下的 progId 子键下查找 (Office 风格: .docx\Word.Document.12\ShellNew)
                if (shellNewKey == null && !string.IsNullOrEmpty(progId))
                {
                    shellNewKey = extKey.OpenSubKey($"{progId}\\ShellNew");
                }

                // 3. 在 progId 根键下查找 (Zip 风格: CompressedFolder\ShellNew)
                if (shellNewKey == null && !string.IsNullOrEmpty(progId))
                {
                    shellNewKey = classesRoot.OpenSubKey($"{progId}\\ShellNew");
                }

                if (shellNewKey == null) return null;

                // 检查是否被配置为隐藏
                var handler = shellNewKey.GetValue("Handler") as string;
                var iconPath = shellNewKey.GetValue("IconPath") as string;
                
                // 获取创建方式
                bool isNullFile = shellNewKey.GetValue("NullFile") != null;
                var data = GetDataValue(shellNewKey);
                var fileName = shellNewKey.GetValue("FileName") as string;
                var command = shellNewKey.GetValue("Command") as string;

                // 如果既没有 NullFile、Data、FileName 也没有 Command，可能不是有效的 ShellNew 条目
                // 但某些类型（如 .lnk）可能只有 Handler
                if (!isNullFile && data == null && string.IsNullOrEmpty(fileName) && 
                    string.IsNullOrEmpty(command) && string.IsNullOrEmpty(handler))
                {
                    return null;
                }

                // 解析模板文件路径
                string? templatePath = null;
                if (!string.IsNullOrEmpty(fileName))
                {
                    templatePath = ResolveTemplatePath(fileName);
                }

                // 获取显示名称
                var displayName = GetDisplayName(classesRoot, extKey, extension, progId);

                // 获取图标
                var icon = GetExtensionIcon(extension);

                return new ShellNewEntry
                {
                    Extension = extension,
                    DisplayName = displayName,
                    ProgId = progId,
                    IsNullFile = isNullFile,
                    Data = data,
                    TemplatePath = templatePath,
                    Command = command,
                    Icon = icon
                };
            }
            finally
            {
                shellNewKey?.Dispose();
            }
        }

        /// <summary>
        /// 获取 Data 值（可能是字节数组或字符串）
        /// </summary>
        private byte[]? GetDataValue(RegistryKey shellNewKey)
        {
            var data = shellNewKey.GetValue("Data");
            
            if (data is byte[] bytes)
            {
                return bytes;
            }
            
            if (data is string str && !string.IsNullOrEmpty(str))
            {
                return Encoding.UTF8.GetBytes(str);
            }

            return null;
        }

        /// <summary>
        /// 解析模板文件路径
        /// </summary>
        private string? ResolveTemplatePath(string fileName)
        {
            // 如果是绝对路径，直接返回
            if (Path.IsPathRooted(fileName))
            {
                return File.Exists(fileName) ? fileName : null;
            }

            // 尝试在 ShellNew 模板目录查找
            var searchPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Templates), fileName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonTemplates), fileName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ShellNew", fileName)
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取文件类型显示名称
        /// </summary>
        private string GetDisplayName(RegistryKey classesRoot, RegistryKey extKey, string extension, string? progId)
        {
            try
            {
                // 1. 尝试从 ProgId 获取 FriendlyTypeName
                if (!string.IsNullOrEmpty(progId))
                {
                    using var progIdKey = classesRoot.OpenSubKey(progId);
                    if (progIdKey != null)
                    {
                        var friendlyName = progIdKey.GetValue("FriendlyTypeName") as string;
                        if (!string.IsNullOrEmpty(friendlyName))
                        {
                            var resolved = ResolveIndirectString(friendlyName);
                            if (!string.IsNullOrEmpty(resolved))
                            {
                                return resolved;
                            }
                        }

                        // 2. 尝试获取默认值
                        var defaultValue = progIdKey.GetValue("") as string;
                        if (!string.IsNullOrEmpty(defaultValue))
                        {
                            return defaultValue;
                        }
                    }
                }

                // 3. 尝试从扩展名直接获取
                var extDefault = extKey.GetValue("") as string;
                if (!string.IsNullOrEmpty(extDefault) && extDefault != progId)
                {
                    return extDefault;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取显示名称失败: {ex.Message}");
            }

            // 默认使用扩展名
            return extension.TrimStart('.').ToUpperInvariant() + " 文件";
        }

        /// <summary>
        /// 解析间接字符串（如 @shell32.dll,-101）
        /// </summary>
        private string? ResolveIndirectString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            if (!value.StartsWith("@"))
            {
                return value;
            }

            try
            {
                var sb = new StringBuilder(512);
                if (SHLoadIndirectString(value, sb, (uint)sb.Capacity, IntPtr.Zero) == 0)
                {
                    return sb.ToString();
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 获取扩展名的图标
        /// </summary>
        private BitmapSource? GetExtensionIcon(string extension)
        {
            try
            {
                var shFileInfo = new SHFILEINFO();
                var flags = SHGFI.SHGFI_ICON | SHGFI.SHGFI_SMALLICON | SHGFI.SHGFI_USEFILEATTRIBUTES;

                var result = SHGetFileInfo(
                    extension,
                    FileAttributes.Normal,
                    ref shFileInfo,
                    Marshal.SizeOf<SHFILEINFO>(),
                    flags);

                if (result != IntPtr.Zero && !shFileInfo.hIcon.IsNull)
                {
                    var imageSource = Imaging.CreateBitmapSourceFromHIcon(
                        (IntPtr)shFileInfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    imageSource.Freeze();
                    User32.DestroyIcon(shFileInfo.hIcon);

                    return imageSource;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取图标失败: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 获取唯一文件路径（自动处理重名）
        /// </summary>
        private string GetUniqueFilePath(string directory, string baseName)
        {
            var path = Path.Combine(directory, baseName);

            if (!File.Exists(path))
            {
                return path;
            }

            var nameWithoutExt = Path.GetFileNameWithoutExtension(baseName);
            var extension = Path.GetExtension(baseName);
            int counter = 1;

            while (File.Exists(path))
            {
                path = Path.Combine(directory, $"{nameWithoutExt} ({counter}){extension}");
                counter++;
            }

            return path;
        }

        /// <summary>
        /// 使用命令创建文件
        /// </summary>
        private string? CreateFileWithCommand(string command, string newPath, string targetDirectory)
        {
            try
            {
                // 替换命令中的占位符
                var processedCommand = command
                    .Replace("%1", $"\"{newPath}\"")
                    .Replace("%L", $"\"{newPath}\"")
                    .Replace("%V", $"\"{targetDirectory}\"");

                // 解析命令和参数
                string fileName;
                string arguments;

                if (processedCommand.StartsWith("\""))
                {
                    var endQuote = processedCommand.IndexOf('"', 1);
                    if (endQuote > 0)
                    {
                        fileName = processedCommand[1..endQuote];
                        arguments = processedCommand[(endQuote + 1)..].Trim();
                    }
                    else
                    {
                        fileName = processedCommand;
                        arguments = "";
                    }
                }
                else
                {
                    var spaceIndex = processedCommand.IndexOf(' ');
                    if (spaceIndex > 0)
                    {
                        fileName = processedCommand[..spaceIndex];
                        arguments = processedCommand[(spaceIndex + 1)..].Trim();
                    }
                    else
                    {
                        fileName = processedCommand;
                        arguments = "";
                    }
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = targetDirectory,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                using var process = Process.Start(startInfo);
                
                // 对于某些程序，可能需要等待文件创建完成
                // 但不要无限等待，给用户控制权
                process?.WaitForExit(5000);

                // 检查文件是否已创建
                if (File.Exists(newPath))
                {
                    return newPath;
                }

                // 命令可能创建了不同名称的文件，返回期望的路径
                return newPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"执行命令创建文件失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从模板创建文件
        /// </summary>
        private string? CreateFileFromTemplate(string templatePath, string newPath)
        {
            try
            {
                if (File.Exists(templatePath))
                {
                    File.Copy(templatePath, newPath, false);
                    return newPath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"从模板创建文件失败: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 使用数据创建文件
        /// </summary>
        private string? CreateFileWithData(string newPath, byte[] data)
        {
            try
            {
                File.WriteAllBytes(newPath, data);
                return newPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"写入数据创建文件失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 创建空文件（特殊处理某些类型）
        /// </summary>
        private string? CreateEmptyFile(string newPath, string extension)
        {
            try
            {
                // ZIP 文件需要有效的空 ZIP 结构
                if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    // 有效的空 ZIP 文件结构（End of Central Directory Record）
                    byte[] emptyZip = [0x50, 0x4B, 0x05, 0x06, 0x00, 0x00, 0x00, 0x00, 
                                       0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
                                       0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
                    File.WriteAllBytes(newPath, emptyZip);
                    return newPath;
                }

                // RTF 文件需要基本结构
                if (extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllText(newPath, @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset0 Arial;}}{\colortbl ;}\viewkind4\uc1\pard\lang2052\f0\fs20\par}");
                    return newPath;
                }

                // 默认创建空文件
                File.WriteAllBytes(newPath, []);
                return newPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"创建空文件失败: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}
