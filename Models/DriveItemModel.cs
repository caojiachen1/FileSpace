using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace FileSpace.Models
{
    public partial class DriveItemModel : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _driveLetter = string.Empty;

        [ObservableProperty]
        private string _driveFormat = string.Empty;

        [ObservableProperty]
        private DriveType _driveType;

        [ObservableProperty]
        private long _totalSize;

        [ObservableProperty]
        private long _availableFreeSpace;

        [ObservableProperty]
        private double _percentUsed;

        [ObservableProperty]
        private SymbolRegular _icon;

        [ObservableProperty]
        private ImageSource? _thumbnail;

        [ObservableProperty]
        private string? _customDescription;

        public string Description
        {
            get
            {
                if (!string.IsNullOrEmpty(CustomDescription)) return CustomDescription;
                if (TotalSize <= 0) return "Unknown";
                return $"{FormatSize(AvailableFreeSpace)} 可用，共 {FormatSize(TotalSize)}";
            }
        }
        
        // Helper to format size
        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (number >= 1024 && counter < suffixes.Length - 1)
            {
                number = number / 1024;
                counter++;
            }
            string format = counter == 4 ? "{0:n2} {1}" : "{0:n1} {1}";
            return string.Format(format, number, suffixes[counter]);
        }
    }
}
