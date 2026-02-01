using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using System.Buffers;
using MagikaNet;

namespace FileSpace.Utils
{
    public static class MagikaDetector
    {
        private static readonly Lazy<MagikaClient> _magikaInstance = new(() => new MagikaClient());
        private static readonly SemaphoreSlim _semaphore = new(1, 1);

        private static byte[] ReadFileBytes(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var length = (int)Math.Min(fs.Length, 1024 * 1024); // 最多读取 1MB
            
            var pool = ArrayPool<byte>.Shared;
            var buffer = pool.Rent(length);
            try
            {
                int totalRead = 0;
                while (totalRead < length)
                {
                    int read = fs.Read(buffer, totalRead, length - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
                
                // Magika 需要精确大小的数组，所以如果没读满或者租借的数组太大，得复制一份
                // 或者我们可以修改 DetectBytesJson 接口（如果支持 offset/count）
                // 但 MagikaNet 2.0 只接受 byte[] arr，内部用 arr.Length
                var result = new byte[totalRead];
                Buffer.BlockCopy(buffer, 0, result, 0, totalRead);
                return result;
            }
            finally
            {
                pool.Return(buffer);
            }
        }

        public static async Task<string> DetectFileTypeAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await Task.Run(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!File.Exists(filePath)) return "文件不存在";

                        var buffer = ReadFileBytes(filePath);
                        var json = _magikaInstance.Value.DetectBytesJson(buffer);
                        
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        
                        string label = "unknown";
                        string description = "unknown";

                        if (root.TryGetProperty("value", out var value) && value.TryGetProperty("output", out var output))
                        {
                            if (output.TryGetProperty("label", out var l)) label = l.GetString() ?? "unknown";
                            if (output.TryGetProperty("description", out var d)) description = d.GetString() ?? "unknown";
                        }
                        else if (root.TryGetProperty("output", out var directOutput))
                        {
                            if (directOutput.TryGetProperty("label", out var l)) label = l.GetString() ?? "unknown";
                            if (directOutput.TryGetProperty("description", out var d)) description = d.GetString() ?? "unknown";
                        }

                        if (label.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                        {
                            return "unknown";
                        }
                        
                        return description != "unknown" ? $"{label} ({description})" : label;
                    }, cancellationToken);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                return "检测已取消";
            }
            catch (Exception)
            {
                return "检测失败";
            }
        }

        public static async Task<bool> IsTextFileAsync(string filePath, CancellationToken cancellationToken)
        {
            var type = await GetFilePreviewTypeAsync(filePath, cancellationToken);
            return type == FilePreviewType.Text || type == FilePreviewType.Html || type == FilePreviewType.Csv;
        }

        public static async Task<FilePreviewType> GetFilePreviewTypeAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await Task.Run(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!File.Exists(filePath)) return FilePreviewType.General;

                        var buffer = ReadFileBytes(filePath);
                        var json = _magikaInstance.Value.DetectBytesJson(buffer);
                        
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        string label = "";
                        string group = "";
                        bool isText = false;

                        if (root.TryGetProperty("value", out var value) && value.TryGetProperty("output", out var output))
                        {
                            label = output.TryGetProperty("label", out var l) ? l.GetString()?.ToLower() ?? "" : "";
                            group = output.TryGetProperty("group", out var g) ? g.GetString()?.ToLower() ?? "" : "";
                            isText = output.TryGetProperty("is_text", out var t) && t.GetBoolean();
                        }
                        else if (root.TryGetProperty("output", out var directOutput))
                        {
                            label = directOutput.TryGetProperty("label", out var l) ? l.GetString()?.ToLower() ?? "" : "";
                            group = directOutput.TryGetProperty("group", out var g) ? g.GetString()?.ToLower() ?? "" : "";
                            isText = directOutput.TryGetProperty("is_text", out var t) && t.GetBoolean();
                        }

                        if (label == "csv") return FilePreviewType.Csv;
                        if (label == "html") return FilePreviewType.Html;
                        if (isText) return FilePreviewType.Text;

                        if (group == "image") return FilePreviewType.Image;
                        if (group == "video") return FilePreviewType.Video;
                        if (group == "audio") return FilePreviewType.Audio;
                        if (label == "pdf") return FilePreviewType.Pdf;

                        return FilePreviewType.General;
                    }, cancellationToken);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch
            {
                return FilePreviewType.General;
            }
        }
    }
}
