# FileSpace - Modern File Manager

A modern file manager built with WPF, featuring an intuitive interface and powerful file preview capabilities.

## 🚀 Features

### 📁 File Browsing
- **Three-panel layout**: Directory tree, file list, file preview
- **Smart navigation**: Back/forward/up directory navigation
- **Address bar**: Direct path input for quick navigation
- **Live refresh**: Dynamic file and directory updates

### 👁️ File Preview
- **Text files**: .txt, .cs, .xml, .json, .md and more
- **Images**: .jpg, .png, .gif, .bmp, .webp and more
- **HTML files**: Source code preview
- **CSV files**: Table data preview
- **PDF files**: File info display (opens externally)
- **Directory info**: Folder statistics and size calculation

### 📊 Folder Size Calculation
- **Background calculation**: Multi-threaded async directory size calculation
- **Progress display**: Real-time calculation progress
- **Smart caching**: Avoids redundant calculations
- **Access control**: Graceful handling of permission restrictions

### 🎨 Modern Interface
- **Fluent Design**: Modern design based on WPF-UI
- **Dark theme**: Default dark theme for comfortable viewing
- **Responsive layout**: Supports window resizing
- **Icon system**: Rich file type icons

## 🚀 Installation

Requires .NET 8.0 Runtime and Windows 10 or higher.

```bash
git clone <repository-url>
cd FileSpace
dotnet run
```

## 📖 Usage

### Basic Operations
1. **Browse files**: Select folders in the left tree, view files in center panel
2. **Preview files**: Click files to view preview in right panel
3. **Open files**: Double-click to open with default program
4. **Enter directories**: Double-click folders to navigate

### Navigation
- **Back**: Return to previous directory
- **Forward**: Go to next directory
- **Up**: Enter parent directory
- **Refresh**: Reload current directory contents

### Folder Size
- Select a folder to view basic info in preview panel
- System automatically calculates folder size in background
- Progress information displayed during calculation
- Results are cached for improved performance
