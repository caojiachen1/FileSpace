using System.Windows;
using FileSpace.Views;

namespace FileSpace.Services
{
    public class DialogService
    {
        private static DialogService? _instance;
        public static DialogService Instance => _instance ??= new DialogService();

        private DialogService() { }

        public bool ConfirmExtensionChange(string originalExt, string newExt)
        {
            var message = string.IsNullOrEmpty(newExt) 
                ? $"您即将移除文件扩展名 '{originalExt}'。\n\n这可能导致文件无法正常打开。确定要继续吗？"
                : $"您即将将文件扩展名从 '{originalExt}' 更改为 '{newExt}'。\n\n这可能导致文件无法正常打开。确定要继续吗？";

            var warningDialog = new WarningDialog(
                "扩展名更改警告",
                message,
                "继续更改",
                "取消")
            {
                Owner = Application.Current.MainWindow
            };

            return warningDialog.ShowDialog() == true;
        }

        public bool ConfirmDelete(int itemCount, bool permanent = false)
        {
            // Check if confirmation is enabled in settings
            var settings = SettingsService.Instance.Settings.FileOperationSettings;
            if (!settings.ConfirmDelete)
            {
                return true; // Skip confirmation if disabled
            }

            var title = permanent ? "确认永久删除" : "确认删除";
            var actionText = permanent ? "永久删除" : "删除";
            var warningText = permanent 
                ? "\n\n此操作无法撤销，文件将不会进入回收站。"
                : "\n\n文件将被移动到回收站。";

            var message = $"确定要{actionText}选中的 {itemCount} 个项目吗？{warningText}";

            var confirmDialog = new ConfirmationDialog(
                title,
                message,
                actionText,
                "取消")
            {
                Owner = Application.Current.MainWindow
            };

            return confirmDialog.ShowDialog() == true;
        }

        public bool ShowRenameDialog(string currentName, bool isDirectory, out string newName)
        {
            newName = string.Empty;

            try
            {
                var renameWindow = new RenameDialog(currentName, isDirectory)
                {
                    Owner = Application.Current.MainWindow
                };

                if (renameWindow.ShowDialog() == true)
                {
                    newName = renameWindow.NewName;
                    return !string.IsNullOrWhiteSpace(newName) && newName != currentName;
                }
            }
            catch
            {
                // Handle any dialog creation errors
            }

            return false;
        }
    }
}
