using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileSpace.Utils
{
    public static class FilePreviewUtils
    {
        public static FilePreviewType DetermineFileType(string extension)
        {
            return extension.ToLower() switch
            {
                ".txt" or ".log" or ".cs" or ".xml" or ".json" or ".config" or ".ini" or ".md" or ".yaml" or ".yml" => FilePreviewType.Text,
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".ico" => FilePreviewType.Image,
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
                FilePreviewType.Html => "HTML 源代码",
                FilePreviewType.Csv => "CSV 预览",
                FilePreviewType.Pdf => "PDF 文档信息",
                _ => "文件信息"
            };
        }

        public static async Task<(double Width, double Height)?> GetImageInfoAsync(string filePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(filePath);
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        return (bitmap.Width, bitmap.Height);
                    }
                    catch
                    {
                        return ((double, double)?)null;
                    }
                });
            }, cancellationToken);
        }
    }

    public enum FilePreviewType
    {
        Text,
        Image,
        Pdf,
        Html,
        Csv,
        General
    }
}
