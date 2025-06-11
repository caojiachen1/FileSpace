using System;
using System.Threading;
using System.Threading.Tasks;
using magika;

namespace FileSpace.Utils
{
    public static class MagikaDetector
    {
        public static async Task<string> DetectFileTypeAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var magika = new Magika();
                    var res = magika.IdentifyPath(filePath);
                    
                    // Don't show probability for unknown file types
                    if (res.output.ct_label.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        return "unknown";
                    }
                    
                    return $"{res.output.ct_label} ({res.output.score:P1})";
                }, cancellationToken);
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
