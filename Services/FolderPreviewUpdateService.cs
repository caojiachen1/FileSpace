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
            if (previewContent is Panel panel)
            {
                var grids = new List<Grid>();
                FindGridsInPanel(panel, grids);
                
                foreach (var grid in grids)
                {
                    if (grid.Children.Count >= 2 && 
                        grid.Children[0] is TextBlock labelBlock && 
                        grid.Children[1] is TextBlock valueBlock)
                    {
                        var labelText = labelBlock.Text ?? "";
                        var valueText = valueBlock.Text ?? "";

                        // 1. Update File count row
                        if (labelText.Contains("包含文件"))
                        {
                            valueBlock.Text = $"{sizeInfo.FileCount:N0} 个";
                        }
                        // 2. Update Directory count row
                        else if (labelText.Contains("包含目录") || labelText.Contains("包含文件夹"))
                        {
                            valueBlock.Text = $"{sizeInfo.DirectoryCount:N0} 个";
                        }
                        // 3. Update Size row
                        else if (labelText.StartsWith("大小") || labelText.StartsWith("总大小") || 
                            valueText.Contains("正在计算") || 
                            valueText.Contains("正在后台计算") || 
                            valueText.Contains("准备计算"))
                        {
                            if (!string.IsNullOrEmpty(sizeInfo.Error))
                            {
                                valueBlock.Text = $"计算失败: {sizeInfo.Error}";
                            }
                            else
                            {
                                valueBlock.Text = sizeInfo.FormattedSize;
                            }
                        }
                        // 4. Update Status row if exists
                        else if (labelText.Contains("计算状态"))
                        {
                            if (sizeInfo.InaccessibleItems > 0)
                            {
                                valueBlock.Text = $"无法访问 {sizeInfo.InaccessibleItems} 个项目";
                            }
                            else
                            {
                                valueBlock.Text = "";
                            }
                        }
                    }
                }
            }
        }

        private void FindGridsInPanel(Panel panel, List<Grid> grids)
        {
            foreach (var child in panel.Children)
            {
                if (child is Grid grid)
                {
                    grids.Add(grid);
                }
                
                if (child is Panel childPanel)
                {
                    FindGridsInPanel(childPanel, grids);
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
