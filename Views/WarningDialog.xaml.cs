using System.Windows;
using Wpf.Ui.Controls;

namespace FileSpace.Views
{
    public partial class WarningDialog : FluentWindow
    {
        public string WindowTitle { get; set; } = "警告";
        public string WarningTitle { get; set; } = "警告";
        public string WarningMessage { get; set; } = "";
        public string ConfirmButtonText { get; set; } = "确定";
        public string CancelButtonText { get; set; } = "取消";

        public WarningDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        public WarningDialog(string title, string message, string confirmText = "确定", string cancelText = "取消") : this()
        {
            WarningTitle = title;
            WarningMessage = message;
            ConfirmButtonText = confirmText;
            CancelButtonText = cancelText;
            WindowTitle = title;
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
