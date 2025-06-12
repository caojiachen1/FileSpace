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
- **Colorful icons**: Rich, colorful file type icons for better visual distinction
- **Responsive layout**: Supports window resizing
- **Color-coded navigation**: Intuitive color scheme for different UI elements

## 🚀 Installation

Requires .NET 8.0 Runtime and Windows 10 or higher.

```bash
git clone https://github.com/caojiachen1/FileSpace
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
- **Up**: Enter parent directory
- **Refresh**: Reload current directory contents

### Folder Size
- Select a folder to view basic info in preview panel
- System automatically calculates folder size in background
- Progress information displayed during calculation
- Results are cached for improved performance

## 📋 TODO List

### 🚀 Priority Features (Core Functionality)

#### 📁 File Operations
- ✅ **Copy/Cut/Paste**: Basic file operations with clipboard support (Ctrl+C, Ctrl+X, Ctrl+V)
- ✅ **Delete Files/Folders**: Support both recycle bin (Delete) and permanent deletion (Shift+Delete)
- ✅ **Rename**: In-place file/folder renaming functionality (F2)
- ✅ **Multi-File Selection**: Ctrl/Shift multi-selection support
- ✅ **Batch Operations**: Bulk copy, delete, move operations with progress tracking
- ✅ **Selection Tools**: Select all (Ctrl+A), invert selection, and clear selection
- ⏳ **Create New Folder**: Directory creation with input validation
- ⏳ **File Drag & Drop**: Support drag and drop for move/copy operations

### 🎯 Multi-Selection & Batch Operations ✅
- **Multi-File Selection**: Full Ctrl/Shift multi-selection support
- **Batch Copy/Cut/Paste**: Copy or move multiple files simultaneously
- **Batch Delete**: Delete multiple files to recycle bin or permanently
- **Selection Utilities**: 
  - Select All (Ctrl+A)
  - Invert Selection
  - Visual selection count in toolbar and context menu
- **Progress Tracking**: Real-time progress for batch operations
- **Smart UI**: Toolbar buttons and context menu adapt to selection count

### ⌨️ Keyboard Shortcuts ✅
- **Ctrl+C**: Copy selected files
- **Ctrl+X**: Cut selected files  
- **Ctrl+V**: Paste files
- **Delete**: Delete selected files to recycle bin
- **Shift+Delete**: Permanently delete selected files
- **F2**: Rename selected file
- **Ctrl+A**: Select all files
- **Escape**: Cancel current operation/close dialog
- **Backspace**: Navigate to parent directory

### 🎨 UI/UX Enhancements

#### 📋 View Modes
- ⏳ **Icon View**: Large icon display with customizable sizes
- ✅ **List Details**: Customizable columns (name, size, date, type)
- ⏳ **Thumbnail View**: Image file thumbnail previews
- ✅ **Tree View**: Hierarchical folder structure

#### 🖼️ Layout & Interface
- ⏳ **Resizable Panels**: Drag to adjust panel sizes
- ⏳ **Layout Persistence**: Remember user layout preferences
- ⏳ **Fullscreen Mode**: Hide toolbars for maximum viewing area
- ✅ **Responsive Design**: Adapt to different window sizes

### ⚙️ Advanced Features

#### 🖱️ Context & Interaction
- ✅ **Right-Click Context Menu**: Comprehensive context operations
- ✅ **Keyboard Shortcuts**: Standard file manager hotkeys
- ⏳ **File Properties Dialog**: Detailed file information and editing
- ✅ **Quick Actions Toolbar**: Customizable quick access buttons
- ✅ **Explorer Integration**: "View in Explorer" context menu option

#### ⭐ Bookmarks & Navigation
- ⏳ **Favorite Paths**: Bookmark frequently used directories
- ⏳ **Quick Access**: Recent folders and files
- ⏳ **Workspaces**: Save different working environments
- ✅ **Breadcrumb Navigation**: Click-to-navigate path bar

#### 👁️ Enhanced Preview
- ✅ **File Preview Panel**: Preview files without opening
- ⏳ **Image Gallery Mode**: Navigate through images
- ✅ **Text File Preview**: Syntax highlighting for code files
- ⏳ **Video/Audio Preview**: Media file thumbnails and info

### 🔧 System Integration

#### 🪟 Windows Integration
- ⏳ **Explorer Integration**: Option to set as default file manager
- ⏳ **System Tray**: Minimize to system tray
- ⏳ **Startup Options**: Auto-start with Windows
- ⏳ **Shell Extensions**: Right-click menu in Windows Explorer

### 🚀 Performance & Technical

#### ⚡ Performance Optimization
- ⏳ **Virtual Scrolling**: Handle large directories efficiently
- ✅ **Caching System**: Cache file info and thumbnails
- ✅ **Background Threading**: Move heavy operations to background
- ✅ **Memory Management**: Optimize memory usage for large file sets

#### 💾 Data Persistence
- ⏳ **Settings Storage**: Save user preferences and settings
- ⏳ **History Tracking**: Access and search history
- ⏳ **Session Recovery**: Restore tabs and state on startup
- ⏳ **Configuration Export/Import**: Backup and share settings

#### 🧩 Extensibility
- ⏳ **Plugin System**: Support for third-party extensions
- ⏳ **Theme System**: Switchable UI themes and color schemes
- ⏳ **Custom Toolbar**: User-configurable toolbar buttons
- ⏳ **Script Integration**: PowerShell/batch script integration

### 📊 Utility Tools

#### 📈 File Analysis
- ✅ **Disk Usage Analyzer**: Visual disk space usage analysis
- ⏳ **Duplicate File Finder**: Find and clean duplicate files
- ⏳ **File Type Statistics**: Analyze file type distribution
- ⏳ **Large File Finder**: Locate space-consuming files

#### 🔄 Batch Processing
- ⏳ **Batch Rename**: Rule-based bulk file renaming
- ⏳ **Format Conversion**: Image and document format conversion
- ⏳ **File Synchronization**: Folder sync functionality
- ✅ **Bulk Operations**: Mass file operations with progress tracking

### 🛡️ Security & Reliability

#### 🔒 Error Handling & Recovery
- ⏳ **Crash Recovery**: Recover from unexpected shutdowns
- ⏳ **Operation Undo**: Undo file operations (when possible)
- ⏳ **Backup Mechanism**: Auto-backup before destructive operations
- ✅ **Error Logging**: Comprehensive error logging and reporting

#### 🔐 Security Features
- ⏳ **File Permissions**: View and edit file permissions
- ⏳ **Secure Delete**: Secure file deletion options
- ⏳ **Checksum Verification**: File integrity checking
- ⏳ **Access Control**: Restrict access to sensitive operations

### 🌟 Future Enhancements

#### 🌐 Advanced Features
- ⏳ **Cloud Integration**: Support for cloud storage services
- ⏳ **Remote File Access**: FTP/SFTP/SSH support
- ⏳ **Archive Support**: Built-in zip/rar handling
- ⏳ **Version Control**: Basic Git integration for developers
- ⏳ **File Comparison**: Compare files and directories
- ⏳ **Network Drive Management**: Enhanced network location handling

## 🙏 Acknowledgments

Special thanks to the following projects that made FileSpace possible:

- **[Magika-CSharp](https://github.com/mkht/Magika-CSharp)** - File type detection library

---

**Legend:**
- ✅ Completed
- ⏳ In Progress / Partially Implemented
- ❌ Not Started / Blocked
- 🚀 High Priority
- ⭐ Medium Priority  
- 🌟 Future/Low Priority
