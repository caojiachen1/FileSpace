using System.Windows.Media.Imaging;

namespace FileSpace.Models
{
    /// <summary>
    /// Shell "新建" 菜单项数据模型
    /// </summary>
    public class ShellNewEntry
    {
        /// <summary>
        /// 文件扩展名，如 ".txt"
        /// </summary>
        public required string Extension { get; set; }

        /// <summary>
        /// 模板文件路径（相对路径或绝对路径）
        /// </summary>
        public string? TemplatePath { get; set; }

        /// <summary>
        /// 自定义创建命令
        /// </summary>
        public string? Command { get; set; }

        /// <summary>
        /// 内嵌初始数据
        /// </summary>
        public byte[]? Data { get; set; }

        /// <summary>
        /// 是否为空文件类型（NullFile）
        /// </summary>
        public bool IsNullFile { get; set; }

        /// <summary>
        /// 显示名称（如 "文本文档"）
        /// </summary>
        public required string DisplayName { get; set; }

        /// <summary>
        /// 文件类型图标
        /// </summary>
        public BitmapSource? Icon { get; set; }

        /// <summary>
        /// ProgId（程序标识符）
        /// </summary>
        public string? ProgId { get; set; }

        /// <summary>
        /// 获取默认的新文件名（不含路径）
        /// </summary>
        public string GetDefaultFileName()
        {
            return Extension.ToLowerInvariant() switch
            {
                ".txt" => "新建文本文档.txt",
                ".docx" => "新建 Microsoft Word 文档.docx",
                ".doc" => "新建 Microsoft Word 文档.doc",
                ".xlsx" => "新建 Microsoft Excel 工作表.xlsx",
                ".xls" => "新建 Microsoft Excel 工作表.xls",
                ".pptx" => "新建 Microsoft PowerPoint 演示文稿.pptx",
                ".ppt" => "新建 Microsoft PowerPoint 演示文稿.ppt",
                ".rtf" => "新建 RTF 文档.rtf",
                ".zip" => "新建压缩(zipped)文件夹.zip",
                ".bmp" => "新建位图图像.bmp",
                ".lnk" => "新建快捷方式.lnk",
                _ => $"新建 {DisplayName}{Extension}"
            };
        }

        public override string ToString() => $"{DisplayName} ({Extension})";
    }
}
