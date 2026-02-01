using System;
using System.Threading;
using System.Threading.Tasks;
using magika;

namespace FileSpace.Utils
{
    public static class MagikaDetector
    {
        private static readonly Lazy<Magika> _magikaInstance = new(() => new Magika());
        private static readonly SemaphoreSlim _semaphore = new(1, 1);

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
                        var res = _magikaInstance.Value.IdentifyPath(filePath);
                        
                        if (res.output.ct_label.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                        {
                            return "unknown";
                        }
                        
                        return $"{res.output.ct_label} ({res.output.score:P1})";
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
                        var res = _magikaInstance.Value.IdentifyPath(filePath);
                        var label = res.output.ct_label.ToLower();
                        
                        if (label.StartsWith("text") || 
                            label == "json" || 
                            label == "xml" || 
                            label == "ini" || 
                            label == "markdown" || 
                            label == "yaml" ||
                            label == "javascript" ||
                            label == "python" ||
                            label == "c" ||
                            label == "cpp" ||
                            label == "java" ||
                            label == "csharp" ||
                            label == "bash" ||
                            label == "powershell" ||
                            label.Contains("code") ||
                            label.Contains("script"))
                        {
                            return FilePreviewType.Text;
                        }

                        if (label == "csv") return FilePreviewType.Csv;
                        if (label == "html") return FilePreviewType.Html;

                        if (label.StartsWith("image")) return FilePreviewType.Image;
                        if (label.StartsWith("video")) return FilePreviewType.Video;
                        if (label.StartsWith("audio")) return FilePreviewType.Audio;
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
