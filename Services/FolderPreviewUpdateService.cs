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
                // Find all Grid children that are property rows
                var grids = panel.Children.OfType<Grid>().ToList();
                
                // Find the size status row (总大小:)
                foreach (var grid in grids)
                {
                    if (grid.Children.Count >= 2 && grid.Children[1] is TextBlock valueBlock)
                    {
                        var text = valueBlock.Text;
                        // Match any size-related status
                        if (text.StartsWith("准备计算") || text.StartsWith("正在后台计算") || text.StartsWith("总大小:"))
                        {
                            if (!string.IsNullOrEmpty(sizeInfo.Error))
                            {
                                valueBlock.Text = $"计算失败: {sizeInfo.Error}";
                            }
                            else
                            {
                                valueBlock.Text = sizeInfo.FormattedSize;
                            }
                            break;
                        }
                    }
                }

                // Update file and folder counts if calculation succeeded
                if (string.IsNullOrEmpty(sizeInfo.Error))
                {
                    foreach (var grid in grids)
                    {
                        if (grid.Children.Count >= 2)
                        {
                            if (grid.Children[0] is TextBlock labelBlock && grid.Children[1] is TextBlock valueBlock2)
                            {
                                if (labelBlock.Text == "直接包含文件:")
                                {
                                    valueBlock2.Text = $"{sizeInfo.FileCount:N0} 个";
                                }
                                else if (labelBlock.Text == "直接包含文件夹:")
                                {
                                    valueBlock2.Text = $"{sizeInfo.DirectoryCount:N0} 个";
                                }
                                else if (labelBlock.Text == "计算状态:")
                                {
                                    if (sizeInfo.InaccessibleItems > 0)
                                    {
                                        valueBlock2.Text = $"无法访问 {sizeInfo.InaccessibleItems} 个项目";
                                    }
                                    else
                                    {
                                        valueBlock2.Text = "";
                                    }
                                }
                            }
                        }
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
