using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FileSpace.ViewModels;
using FileSpace.Utils;
using magika;

namespace FileSpace.Services
{
    public class PreviewService
    {
        private static readonly Lazy<PreviewService> _instance = new(() => new PreviewService());
        public static PreviewService Instance => _instance.Value;

        private PreviewService() { }

        public async Task<object?> GeneratePreviewAsync(FileItemViewModel file, CancellationToken cancellationToken)
        {
            if (file == null) return null;

            if (file.IsDirectory)
            {
                return await GenerateDirectoryPreviewAsync(file, cancellationToken);
            }

            return await GenerateFilePreviewAsync(file, cancellationToken);
        }

        private async Task<StackPanel> GenerateFilePreviewAsync(FileItemViewModel file, CancellationToken cancellationToken)
        {
            string extension = Path.GetExtension(file.FullPath).ToLower();
            long fileSize = file.Size;

            // Check file size limits
            if (FileUtils.IsTextFile(extension) && fileSize > 10 * 1024 * 1024)
            {
                return PreviewUIHelper.CreateInfoPanel("文件过大", "文本文件超过10MB，无法预览");
            }

            if (FileUtils.IsImageFile(extension) && fileSize > 50 * 1024 * 1024)
            {
                return PreviewUIHelper.CreateInfoPanel("文件过大", "图片文件超过50MB，无法预览");
            }

            var fileType = FilePreviewUtils.DetermineFileType(extension);
            return await GenerateFileInfoAndPreviewAsync(file, fileType, cancellationToken);
        }

        private async Task<StackPanel> GenerateFileInfoAndPreviewAsync(FileItemViewModel file, FilePreviewType fileType, CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(file.FullPath);
            var panel = new StackPanel();

            // Add common file information
            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"文件名: {fileInfo.Name}"));
            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"完整路径: {fileInfo.FullName}"));
            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"文件大小: {FileUtils.FormatFileSize(fileInfo.Length)}"));
            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"文件类型: {file.Type}"));
            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"创建时间: {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}"));
            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"修改时间: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}"));

            // Add file type specific information
            await AddFileTypeSpecificInfoAsync(panel, fileInfo, fileType, cancellationToken);

            var aiDetectionBlock = PreviewUIHelper.CreateInfoTextBlock("AI检测文件类型: 正在检测...");
            panel.Children.Add(aiDetectionBlock);
            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock(""));

            // Add preview content
            await AddPreviewContentAsync(panel, fileInfo, fileType, cancellationToken);

            // Start AI detection asynchronously
            _ = Task.Run(async () =>
            {
                var aiResult = await MagikaDetector.DetectFileTypeAsync(fileInfo.FullName, cancellationToken);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        aiDetectionBlock.Text = $"AI检测文件类型: {aiResult}";
                    }
                });
            }, cancellationToken);

            return panel;
        }

        private async Task AddFileTypeSpecificInfoAsync(StackPanel panel, FileInfo fileInfo, FilePreviewType fileType, CancellationToken cancellationToken)
        {
            switch (fileType)
            {
                case FilePreviewType.Text:
                    var encoding = FileUtils.DetectEncoding(fileInfo.FullName);
                    panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"编码: {encoding.EncodingName}"));
                    break;

                case FilePreviewType.Image:
                    try
                    {
                        var imageInfo = await FilePreviewUtils.GetImageInfoAsync(fileInfo.FullName, cancellationToken);
                        if (imageInfo != null)
                        {
                            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"图片尺寸: {imageInfo.Value.Width} × {imageInfo.Value.Height} 像素"));
                        }
                    }
                    catch { }
                    break;

                case FilePreviewType.Csv:
                    try
                    {
                        var lines = await File.ReadAllLinesAsync(fileInfo.FullName, cancellationToken);
                        panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"总行数: {lines.Length:N0}"));
                    }
                    catch { }
                    break;

                case FilePreviewType.General:
                    panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"访问时间: {fileInfo.LastAccessTime:yyyy-MM-dd HH:mm:ss}"));
                    panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"属性: {fileInfo.Attributes}"));
                    break;
            }
        }

        private async Task AddPreviewContentAsync(StackPanel panel, FileInfo fileInfo, FilePreviewType fileType, CancellationToken cancellationToken)
        {
            var previewHeader = PreviewUIHelper.CreateInfoTextBlock(FilePreviewUtils.GetPreviewHeaderText(fileType));
            previewHeader.FontWeight = FontWeights.Bold;
            panel.Children.Add(previewHeader);

            switch (fileType)
            {
                case FilePreviewType.Text:
                    await PreviewUIHelper.AddTextPreviewAsync(panel, fileInfo, cancellationToken);
                    break;

                case FilePreviewType.Html:
                    await PreviewUIHelper.AddHtmlPreviewAsync(panel, fileInfo, cancellationToken);
                    break;

                case FilePreviewType.Image:
                    await PreviewUIHelper.AddImagePreviewAsync(panel, fileInfo, cancellationToken);
                    break;

                case FilePreviewType.Csv:
                    await PreviewUIHelper.AddCsvPreviewAsync(panel, fileInfo, cancellationToken);
                    break;

                case FilePreviewType.Pdf:
                    PreviewUIHelper.AddPdfPreview(panel);
                    break;

                case FilePreviewType.General:
                    panel.Children.Remove(previewHeader);
                    break;
            }
        }

        private async Task<StackPanel> GenerateDirectoryPreviewAsync(FileItemViewModel file, CancellationToken cancellationToken)
        {
            var dirInfo = new DirectoryInfo(file.FullPath);
            var panel = new StackPanel();

            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"文件夹: {dirInfo.Name}"));
            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"完整路径: {dirInfo.FullName}"));
            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"创建时间: {dirInfo.CreationTime:yyyy-MM-dd HH:mm:ss}"));
            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock($"修改时间: {dirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}"));
            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock(""));

            // Quick directory count
            var quickSummary = await Task.Run(() =>
            {
                int fileCount = 0;
                int dirCount = 0;

                try
                {
                    foreach (var f in dirInfo.EnumerateFiles())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        fileCount++;
                    }

                    foreach (var d in dirInfo.EnumerateDirectories())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        dirCount++;
                    }
                }
                catch (UnauthorizedAccessException) { }

                return new { FileCount = fileCount, DirCount = dirCount };
            }, cancellationToken);

            // Create fixed positions for content info that will be updated in place
            var fileCountBlock = PreviewUIHelper.CreateInfoTextBlock($"总共包含文件: {quickSummary.FileCount:N0} 个");
            var dirCountBlock = PreviewUIHelper.CreateInfoTextBlock($"直接包含文件夹: {quickSummary.DirCount:N0} 个");
            
            panel.Children.Add(fileCountBlock);
            panel.Children.Add(dirCountBlock);
            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock(""));

            // Add size calculation section
            AddSizeCalculationSection(panel, dirInfo.FullName, fileCountBlock, dirCountBlock);

            return panel;
        }

        private void AddSizeCalculationSection(StackPanel panel, string folderPath, System.Windows.Controls.TextBlock fileCountBlock, System.Windows.Controls.TextBlock dirCountBlock)
        {
            var sizeHeaderBlock = PreviewUIHelper.CreateInfoTextBlock("总大小计算:");
            sizeHeaderBlock.FontWeight = FontWeights.Bold;
            panel.Children.Add(sizeHeaderBlock);

            var backgroundCalculator = BackgroundFolderSizeCalculator.Instance;
            var cachedSize = backgroundCalculator.GetCachedSize(folderPath);
            var isActiveCalculation = backgroundCalculator.IsCalculationActive(folderPath);

            var sizeStatusBlock = PreviewUIHelper.CreateInfoTextBlock("");
            var progressBlock = PreviewUIHelper.CreateInfoTextBlock("");

            if (cachedSize != null && !string.IsNullOrEmpty(cachedSize.Error))
            {
                sizeStatusBlock.Text = $"计算失败: {cachedSize.Error}";
                // Keep original quick count display when calculation fails
            }
            else if (cachedSize != null && cachedSize.IsCalculationComplete)
            {
                sizeStatusBlock.Text = $"总大小: {cachedSize.FormattedSize}";
                // Update the existing blocks in place with accurate counts
                fileCountBlock.Text = $"总共包含文件: {cachedSize.FileCount:N0} 个";
                dirCountBlock.Text = $"直接包含文件夹: {cachedSize.DirectoryCount:N0} 个";
                if (cachedSize.InaccessibleItems > 0)
                {
                    progressBlock.Text = $"无法访问 {cachedSize.InaccessibleItems} 个项目";
                }
            }
            else if (isActiveCalculation)
            {
                sizeStatusBlock.Text = "正在后台计算...";
            }
            else
            {
                sizeStatusBlock.Text = "准备计算...";
                backgroundCalculator.QueueFolderSizeCalculation(folderPath,
                    new { PreviewPanel = panel, StatusBlock = sizeStatusBlock, ProgressBlock = progressBlock, FileCountBlock = fileCountBlock, DirCountBlock = dirCountBlock });
            }

            panel.Children.Add(sizeStatusBlock);
            panel.Children.Add(progressBlock);
        }
    }
}
