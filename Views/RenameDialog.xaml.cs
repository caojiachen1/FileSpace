using System.IO;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace FileSpace.Views
{
    public partial class RenameDialog : FluentWindow
    {
        public string NewName { get; set; }
        private readonly bool _isDirectory;
        private readonly string _originalName;
        private readonly string _originalExtension;

        public RenameDialog(string currentName, bool isDirectory)
        {
            InitializeComponent();
            _isDirectory = isDirectory;
            _originalName = currentName;
            
            if (isDirectory)
            {
                NewName = currentName;
                _originalExtension = string.Empty;
            }
            else
            {
                // For files, show the full name including extension
                NewName = currentName;
                _originalExtension = Path.GetExtension(currentName);
            }
            
            DataContext = this;
            Loaded += (s, e) => {
                NameTextBox.Focus();
                
                if (!isDirectory && !string.IsNullOrEmpty(_originalExtension))
                {
                    // Select only the filename part without extension
                    var nameWithoutExtension = Path.GetFileNameWithoutExtension(currentName);
                    NameTextBox.Select(0, nameWithoutExtension.Length);
                }
                else
                {
                    // For directories or files without extensions, select all
                    NameTextBox.SelectAll();
                }
            };
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateNameAndConfirmChanges())
            {
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void NameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    if (ValidateNameAndConfirmChanges())
                    {
                        DialogResult = true;
                        Close();
                    }
                    e.Handled = true;
                    break;
                case Key.Escape:
                    DialogResult = false;
                    Close();
                    e.Handled = true;
                    break;
            }
        }

        private bool ValidateNameAndConfirmChanges()
        {
            if (!ValidateName())
                return false;

            // Check if extension was changed for files
            if (!_isDirectory && !string.IsNullOrEmpty(_originalExtension))
            {
                var newExtension = Path.GetExtension(NewName);
                
                if (!string.Equals(_originalExtension, newExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return ConfirmExtensionChange(_originalExtension, newExtension);
                }
            }

            return true;
        }

        private bool ConfirmExtensionChange(string originalExt, string newExt)
        {
            var message = string.IsNullOrEmpty(newExt) 
                ? $"您即将移除文件扩展名 '{originalExt}'。\n\n这可能导致：\n• 文件无法正常打开\n• 系统无法识别文件类型\n• 程序关联丢失\n\n确定要继续吗？"
                : $"您即将将文件扩展名从 '{originalExt}' 更改为 '{newExt}'。\n\n这可能导致：\n• 文件无法正常打开\n• 系统无法识别文件类型\n• 程序关联改变\n\n确定要继续吗？";

            var warningDialog = new WarningDialog(
                "扩展名更改警告",
                message,
                "继续更改",
                "取消")
            {
                Owner = this
            };

            return warningDialog.ShowDialog() == true;
        }

        private bool ValidateName()
        {
            if (string.IsNullOrWhiteSpace(NewName))
            {
                ShowError("名称不能为空");
                return false;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            if (NewName.IndexOfAny(invalidChars) >= 0)
            {
                ShowError("名称包含无效字符");
                return false;
            }

            if (NewName.Length > 255)
            {
                ShowError("名称过长");
                return false;
            }

            // Check for reserved names (only check the filename part without extension)
            var nameToCheck = _isDirectory ? NewName : Path.GetFileNameWithoutExtension(NewName);
            var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reservedNames.Contains(nameToCheck.ToUpperInvariant()))
            {
                ShowError("此名称为系统保留名称");
                return false;
            }

            HideError();
            return true;
        }

        private void ShowError(string message)
        {
            ErrorTextBlock.Text = message;
            ErrorTextBlock.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            ErrorTextBlock.Visibility = Visibility.Collapsed;
        }
    }
}
