using System.Runtime.InteropServices;
using System.IO;
using FileSpace.Models;
using FileSpace.Utils;
using Wpf.Ui.Controls;

namespace FileSpace.Services
{
    public class RecycleBinService
    {
        private static readonly Lazy<RecycleBinService> _instance = new(() => new RecycleBinService());
        public static RecycleBinService Instance => _instance.Value;

        private RecycleBinService() { }

        public async Task<List<FileItemModel>> GetRecycleBinItemsAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var result = new List<FileItemModel>();
                dynamic? shell = null;
                dynamic? recycleFolder = null;
                dynamic? items = null;

                try
                {
                    var shellType = Type.GetTypeFromProgID("Shell.Application");
                    if (shellType == null)
                    {
                        return result;
                    }

                    shell = Activator.CreateInstance(shellType);
                    recycleFolder = shell?.NameSpace(10); // 回收站
                    if (recycleFolder == null)
                    {
                        return result;
                    }

                    items = recycleFolder.Items();
                    if (items == null)
                    {
                        return result;
                    }
                    int count = items?.Count ?? 0;

                    for (int i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        dynamic? item = null;
                        try
                        {
                            item = items.Item(i);
                            if (item == null)
                            {
                                continue;
                            }

                            string name = Convert.ToString(item.Name) ?? "未知项目";
                            string deletedFrom = Convert.ToString(GetExtendedProperty(item, "System.Recycle.DeletedFrom")) ?? string.Empty;
                            string dateDeletedRaw = Convert.ToString(GetExtendedProperty(item, "System.Recycle.DateDeleted")) ?? string.Empty;
                            string sizeRaw = Convert.ToString(GetExtendedProperty(item, "System.Size")) ?? "0";
                            string parsingPath = Convert.ToString(item.Path) ?? name;

                            DateTime deletedAt = ParseDateTime(dateDeletedRaw);
                            long size = ParseLong(sizeRaw);
                            string fullPath = parsingPath;

                            string extension = Path.GetExtension(name);
                            bool isDirectory = string.IsNullOrEmpty(extension);

                            var model = new FileItemModel
                            {
                                Name = name,
                                FullPath = fullPath,
                                IsDirectory = isDirectory,
                                Size = isDirectory ? 0 : size,
                                Type = isDirectory ? "文件夹" : FileSystemService.GetFileTypePublic(extension),
                                ModifiedDateTime = deletedAt == DateTime.MinValue ? DateTime.Now : deletedAt,
                                ModifiedTime = deletedAt == DateTime.MinValue ? string.Empty : deletedAt.ToString("yyyy-MM-dd HH:mm"),
                                CreationDateTime = deletedAt == DateTime.MinValue ? DateTime.Now : deletedAt,
                                CreationTime = deletedAt == DateTime.MinValue ? string.Empty : deletedAt.ToString("yyyy-MM-dd HH:mm"),
                                Icon = isDirectory ? SymbolRegular.Folder24 : FileSystemService.GetFileIconPublic(extension),
                                IconColor = isDirectory ? "#FFE6A23C" : FileSystemService.GetFileIconColorPublic(extension)
                            };

                            model.Thumbnail = ThumbnailUtils.GetThumbnail(parsingPath, 32, 32)
                                ?? (isDirectory
                                    ? IconCacheService.Instance.GetFolderIcon()
                                    : IconCacheService.Instance.GetIcon(parsingPath, false));

                            result.Add(model);
                        }
                        catch
                        {
                            // 忽略单个条目异常，继续加载其他项目
                        }
                        finally
                        {
                            TryReleaseCom(item);
                        }
                    }
                }
                finally
                {
                    TryReleaseCom(items);
                    TryReleaseCom(recycleFolder);
                    TryReleaseCom(shell);
                }

                return result;
            }, cancellationToken);
        }

        public async Task<bool> RestoreAllItemsAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                dynamic? shell = null;
                dynamic? recycleFolder = null;
                dynamic? items = null;
                var snapshots = new List<dynamic>();

                try
                {
                    var shellType = Type.GetTypeFromProgID("Shell.Application");
                    if (shellType == null)
                    {
                        return false;
                    }

                    shell = Activator.CreateInstance(shellType);
                    recycleFolder = shell?.NameSpace(10);
                    if (recycleFolder == null)
                    {
                        return false;
                    }

                    items = recycleFolder.Items();
                    if (items == null)
                    {
                        return false;
                    }
                    int count = items?.Count ?? 0;

                    for (int i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var item = items.Item(i);
                        if (item != null)
                        {
                            snapshots.Add(item);
                        }
                    }

                    foreach (var item in snapshots)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!TryInvokeRestoreVerb(item))
                        {
                            // 如果找不到动词，继续下一个，避免整批中断
                            continue;
                        }
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    foreach (var item in snapshots)
                    {
                        TryReleaseCom(item);
                    }

                    TryReleaseCom(items);
                    TryReleaseCom(recycleFolder);
                    TryReleaseCom(shell);
                }
            }, cancellationToken);
        }

        public async Task<bool> EmptyRecycleBinAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 保留系统确认对话框行为，与资源管理器接近
                    int hr = Win32Api.SHEmptyRecycleBin(IntPtr.Zero, null, Win32Api.SHERB_NOSOUND);
                    return hr == 0;
                }
                catch
                {
                    return false;
                }
            });
        }

        private static object? GetExtendedProperty(dynamic shellItem, string propertyName)
        {
            if (shellItem == null)
            {
                return null;
            }

            try
            {
                return shellItem.ExtendedProperty(propertyName);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryInvokeRestoreVerb(dynamic item)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                var verbs = item.Verbs();
                int count = verbs?.Count ?? 0;
                for (int i = 0; i < count; i++)
                {
                    dynamic? verb = verbs.Item(i);
                    string name = (Convert.ToString(verb?.Name) ?? string.Empty)
                        .Replace("&", string.Empty)
                        .Trim();

                    if (name.Contains("还原", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Restore", StringComparison.OrdinalIgnoreCase))
                    {
                        if (verb != null)
                        {
                            verb.DoIt();
                        }
                        TryReleaseCom(verb);
                        TryReleaseCom(verbs);
                        return true;
                    }

                    TryReleaseCom(verb);
                }

                TryReleaseCom(verbs);
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static DateTime ParseDateTime(string raw)
        {
            if (DateTime.TryParse(raw, out var value))
            {
                return value;
            }

            return DateTime.MinValue;
        }

        private static long ParseLong(string raw)
        {
            if (long.TryParse(raw, out var value))
            {
                return value;
            }

            return 0;
        }

        private static void TryReleaseCom(object? obj)
        {
            if (obj == null)
            {
                return;
            }

            try
            {
                if (Marshal.IsComObject(obj))
                {
                    Marshal.ReleaseComObject(obj);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}