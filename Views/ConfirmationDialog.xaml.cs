using System.Windows;

namespace FileSpace.Views
{
    public partial class ConfirmationDialog : Wpf.Ui.Controls.FluentWindow
    {
        public new string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string ConfirmButtonText { get; set; } = "确定";
        public string CancelButtonText { get; set; } = "取消";

        public ConfirmationDialog(string title, string message, string confirmText = "确定", string cancelText = "取消")
        {
            InitializeComponent();
            
            Title = title;
            Message = message;
            ConfirmButtonText = confirmText;
            CancelButtonText = cancelText;
            
            DataContext = this;
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
