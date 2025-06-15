using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using FileSpace.ViewModels;
using FileSpace.Models;

namespace FileSpace.Services
{
    public class FolderPreviewUpdateService
    {
        public void UpdateDirectoryPreviewWithSize(object? previewContent, FolderSizeInfo sizeInfo)
        {
            if (previewContent is StackPanel panel)
            {
                // Find and update the size status blocks
                foreach (var child in panel.Children.OfType<TextBlock>())
                {
                    if (child.Text.StartsWith("总大小:") || child.Text.StartsWith("正在后台计算") || child.Text.StartsWith("准备计算"))
                    {
                        if (!string.IsNullOrEmpty(sizeInfo.Error))
                        {
                            child.Text = $"计算失败: {sizeInfo.Error}";
                            // Don't update file/folder counts if calculation failed
                        }
                        else
                        {
                            child.Text = $"总大小: {sizeInfo.FormattedSize}";

                            // Update the file and folder count blocks in their original positions
                            foreach (var contentChild in panel.Children.OfType<TextBlock>())
                            {
                                if (contentChild.Text.StartsWith("直接包含文件:"))
                                {
                                    contentChild.Text = $"总共包含文件: {sizeInfo.FileCount:N0} 个";
                                }
                                else if (contentChild.Text.StartsWith("直接包含文件夹:"))
                                {
                                    contentChild.Text = $"直接包含文件夹: {sizeInfo.DirectoryCount:N0} 个";
                                }
                            }

                            // Update or add inaccessible items info
                            var progressBlock = panel.Children.OfType<TextBlock>()
                                .LastOrDefault(tb => !tb.Text.StartsWith("直接包含") && !tb.Text.StartsWith("总大小") && !tb.Text.StartsWith("文件夹") && !tb.Text.StartsWith("完整路径") && !tb.Text.StartsWith("创建时间") && !tb.Text.StartsWith("修改时间") && !string.IsNullOrEmpty(tb.Text));
                            
                            if (progressBlock != null && sizeInfo.InaccessibleItems > 0)
                            {
                                progressBlock.Text = $"无法访问 {sizeInfo.InaccessibleItems} 个项目";
                            }
                            else if (progressBlock != null)
                            {
                                progressBlock.Text = "";
                            }
                        }
                        break;
                    }
                }
            }
        }

        public void UpdateDirectoryTreeItemSize(ObservableCollection<DirectoryItemModel> directoryTree, string folderPath, FolderSizeInfo sizeInfo)
        {
            // Update directory tree items recursively
            UpdateDirectoryTreeItemSizeRecursive(directoryTree, folderPath, sizeInfo);
        }

        private void UpdateDirectoryTreeItemSizeRecursive(ObservableCollection<DirectoryItemModel> items, string folderPath, FolderSizeInfo sizeInfo)
        {
            foreach (var item in items)
            {
                if (item.FullPath == folderPath)
                {
                    item.SizeInfo = sizeInfo;
                    if (!string.IsNullOrEmpty(sizeInfo.Error))
                    {
                        item.SizeText = "计算失败";
                    }
                    else
                    {
                        item.SizeText = sizeInfo.FormattedSize;
                    }
                    item.IsSizeCalculating = false;
                    return;
                }

                if (item.SubDirectories.Any())
                {
                    UpdateDirectoryTreeItemSizeRecursive(item.SubDirectories, folderPath, sizeInfo);
                }
            }
        }

        public string FormatSizeCalculationProgress(string currentPath, int processedFiles)
        {
            if (!string.IsNullOrEmpty(currentPath) && currentPath.Length > 60)
            {
                currentPath = $"...{currentPath.Substring(currentPath.Length - 50)}";
            }
            
            // Format large numbers better
            string fileCountText = processedFiles > 10000 ? 
                $"{processedFiles / 1000}K+" : 
                processedFiles.ToString("N0");
            
            return $"正在扫描: {Path.GetFileName(currentPath)} ({fileCountText} 文件)";
        }
    }
}
