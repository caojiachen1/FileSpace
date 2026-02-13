using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using Vanara.Windows.Shell;

namespace FileSpace.Utils
{
    public static class FilePreviewUtils
    {
        private const int MaxImageInfoRetryCount = 6;
        private const int InitialRetryDelayMs = 120;
        private const int ErrorSharingViolation = 32;
        private const int ErrorLockViolation = 33;

        public static FilePreviewType DetermineFileType(string extension)
        {
            return extension.ToLower() switch
            {
                ".txt" or ".log" or ".cs" or ".xml" or ".json" or ".config" or ".ini" or ".md" or ".yaml" or ".yml" => FilePreviewType.Text,
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".ico" => FilePreviewType.Image,
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".mpeg" or ".m4v" or ".3gp" => FilePreviewType.Video,
                ".mp3" or ".wav" or ".wma" or ".flac" or ".m4a" or ".ogg" or ".aac" or ".m4r" => FilePreviewType.Audio,
                ".pdf" => FilePreviewType.Pdf,
                ".html" or ".htm" => FilePreviewType.Html,
                ".csv" => FilePreviewType.Csv,
                _ => FilePreviewType.General
            };
        }

        public static string GetPreviewHeaderText(FilePreviewType fileType)
        {
            return fileType switch
            {
                FilePreviewType.Text => "文件预览:",
                FilePreviewType.Image => "图片预览:",
                FilePreviewType.Video => "视频信息:",
                FilePreviewType.Audio => "音频信息:",
                FilePreviewType.Html => "HTML 源代码预览:",
                FilePreviewType.Csv => "CSV 文件预览:",
                FilePreviewType.Pdf => "PDF 预览信息:",
                _ => "预览:"
            };
        }

        public static string GetFileTypeDisplayName(FilePreviewType fileType)
        {
            return fileType switch
            {
                FilePreviewType.Text => "文本",
                FilePreviewType.Image => "图片",
                FilePreviewType.Video => "视频",
                FilePreviewType.Audio => "音频",
                FilePreviewType.Html => "HTML",
                FilePreviewType.Csv => "CSV",
                FilePreviewType.Pdf => "PDF",
                _ => "文件"
            };
        }

        public static string GetPreviewStatus(FilePreviewType fileType, FileInfo fileInfo)
        {
            return fileType switch
            {
                FilePreviewType.Text => "文本预览",
                FilePreviewType.Image => "图片预览",
                FilePreviewType.Video => "视频信息",
                FilePreviewType.Audio => "音频信息",
                FilePreviewType.Html => "HTML 源代码",
                FilePreviewType.Csv => "CSV 预览",
                FilePreviewType.Pdf => "PDF 文档信息",
                _ => "文件信息"
            };
        }

        public static async Task<(double Width, double Height)?> GetImageInfoAsync(string filePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0) return null;

            for (int attempt = 0; attempt <= MaxImageInfoRetryCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0)
                    {
                        return null;
                    }

                    var frame = decoder.Frames[0];
                    return (frame.Width, frame.Height);
                }
                catch (IOException ex) when (IsTransientFileLock(ex) && attempt < MaxImageInfoRetryCount)
                {
                    var delay = InitialRetryDelayMs * (attempt + 1);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static bool IsTransientFileLock(IOException ex)
        {
            var nativeCode = ex.HResult & 0xFFFF;
            return nativeCode == ErrorSharingViolation || nativeCode == ErrorLockViolation;
        }

        public static Dictionary<string, string> GetShellMediaInfo(string filePath)
        {
            var result = new Dictionary<string, string>();
            try
            {
                using var shellItem = new ShellItem(filePath);
                
                // 帧速率 (System.Video.FrameRate returns milliframes/sec)
                var frameRateValue = shellItem.Properties["System.Video.FrameRate"];
                if (frameRateValue != null)
                {
                    double fps = Convert.ToDouble(frameRateValue) / 1000.0;
                    if (fps > 0) result["FrameRate"] = fps.ToString("F2") + " 帧/秒";
                }

                // 帧宽度/高度
                var widthValue = shellItem.Properties["System.Video.FrameWidth"];
                if (widthValue != null) result["Width"] = widthValue.ToString() ?? "";
                var heightValue = shellItem.Properties["System.Video.FrameHeight"];
                if (heightValue != null) result["Height"] = heightValue.ToString() ?? "";

                // 数据速率 (System.Video.DataRate in bps)
                var dataRateValue = shellItem.Properties["System.Video.EncodingBitrate"];
                if (dataRateValue != null)
                {
                    double kbps = Convert.ToDouble(dataRateValue) / 1000.0;
                    if (kbps > 0) result["DataBitrate"] = kbps.ToString("F0") + " kbps";
                }

                // 总比特率 (System.Video.TotalBitrate in bps)
                var totalBitrateValue = shellItem.Properties["System.Video.TotalBitrate"];
                if (totalBitrateValue != null)
                {
                    double kbps = Convert.ToDouble(totalBitrateValue) / 1000.0;
                    if (kbps > 0) result["TotalBitrate"] = kbps.ToString("F0") + " kbps";
                }
                
                // 时长 (System.Media.Duration in 100-ns ticks)
                var durationValue = shellItem.Properties["System.Media.Duration"];
                if (durationValue != null)
                {
                    long ticks = Convert.ToInt64(durationValue);
                    var ts = TimeSpan.FromTicks(ticks);
                    result["Duration"] = ts.ToString(@"hh\:mm\:ss");
                }

                // 音频比特率 (System.Audio.EncodingBitrate)
                var audioBitrateValue = shellItem.Properties["System.Audio.EncodingBitrate"];
                if (audioBitrateValue != null)
                {
                    double kbps = Convert.ToDouble(audioBitrateValue) / 1000.0;
                    if (kbps > 0) result["AudioBitrate"] = kbps.ToString("F0") + " kbps";
                }
            }
            catch { }
            return result;
        }
    }

    public enum FilePreviewType
    {
        Text,
        Image,
        Video,
        Audio,
        Pdf,
        Html,
        Csv,
        General
    }
}
