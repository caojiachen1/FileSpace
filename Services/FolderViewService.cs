using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace FileSpace.Services
{
    /// <summary>
    /// 管理文件夹视图模式和排序设置的持久化服务，使用 SQLite 存储
    /// </summary>
    public class FolderViewService
    {
        private static readonly Lazy<FolderViewService> _instance = new(() => new FolderViewService());
        public static FolderViewService Instance => _instance.Value;

        private readonly string _dbPath;
        private readonly string _connectionString;

        private FolderViewService()
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSpace");
            Directory.CreateDirectory(appDataPath);
            _dbPath = Path.Combine(appDataPath, "folder_settings.db");
            _connectionString = $"Data Source={_dbPath}";

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = 
            @"
                CREATE TABLE IF NOT EXISTS FolderSettings (
                    Path TEXT PRIMARY KEY,
                    ViewMode TEXT,
                    SortMode TEXT,
                    SortAscending INTEGER
                );
            ";
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// 获取文件夹设置
        /// </summary>
        public (string? ViewMode, string? SortMode, bool? SortAscending) GetFolderSettings(string path)
        {
            if (string.IsNullOrEmpty(path)) return (null, null, null);

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT ViewMode, SortMode, SortAscending FROM FolderSettings WHERE Path = $path";
                command.Parameters.AddWithValue("$path", path);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    return (
                        reader.IsDBNull(0) ? null : reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetInt32(2) != 0
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取文件夹设置失败: {ex.Message}");
            }

            return (null, null, null);
        }

        /// <summary>
        /// 获取指定文件夹的视图模式，如果没有记录则返回 null
        /// </summary>
        public string? GetViewMode(string path)
        {
            return GetFolderSettings(path).ViewMode;
        }

        /// <summary>
        /// 保存指定文件夹的视图模式
        /// </summary>
        public void SetViewMode(string path, string viewMode)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(viewMode)) return;

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = 
                @"
                    INSERT INTO FolderSettings (Path, ViewMode) 
                    VALUES ($path, $viewMode)
                    ON CONFLICT(Path) DO UPDATE SET ViewMode = $viewMode;
                ";
                command.Parameters.AddWithValue("$path", path);
                command.Parameters.AddWithValue("$viewMode", viewMode);

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存文件夹视图模式失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存指定文件夹的排序设置
        /// </summary>
        public void SetSortSettings(string path, string sortMode, bool sortAscending)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(sortMode)) return;

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = 
                @"
                    INSERT INTO FolderSettings (Path, SortMode, SortAscending) 
                    VALUES ($path, $sortMode, $sortAscending)
                    ON CONFLICT(Path) DO UPDATE SET SortMode = $sortMode, SortAscending = $sortAscending;
                ";
                command.Parameters.AddWithValue("$path", path);
                command.Parameters.AddWithValue("$sortMode", sortMode);
                command.Parameters.AddWithValue("$sortAscending", sortAscending ? 1 : 0);

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存文件夹排序设置失败: {ex.Message}");
            }
        }
    }
}
