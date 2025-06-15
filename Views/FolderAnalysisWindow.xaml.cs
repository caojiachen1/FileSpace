using Wpf.Ui.Controls;
using FileSpace.ViewModels;

namespace FileSpace.Views
{
    public partial class FolderAnalysisWindow : FluentWindow
    {
        public FolderAnalysisWindow(FolderAnalysisViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
