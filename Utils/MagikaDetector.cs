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
    }
}
