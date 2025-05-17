using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace PublishTool
{
    /// <summary>
    /// SftpWindow.xaml 的交互逻辑
    /// </summary>
    public partial class SftpWindow : Window
    {
        private SftpClient _sftpClient;
        private string _currentPath = "/";
        private ServerConfig _serverConfig;

        public SftpWindow(ServerConfig serverConfig)
        {
            InitializeComponent();
            _serverConfig = serverConfig;
            ConnectAndLoad();
        }

        private void ConnectAndLoad()
        {
            try
            {
                _sftpClient = new SftpClient(_serverConfig.ServerIP, _serverConfig.Username, _serverConfig.Password);
                _sftpClient.Connect();
                LoadFiles(_currentPath);
            }
            catch (Exception ex)
            {
                HandyControl.Controls.MessageBox.Show("连接SFTP失败: " + ex.Message);
                Close();
            }
        }
        public static string ConvertToLinuxPath(string windowsPath)
        {
            return windowsPath.Replace(@"\\", "/").Replace(@"\", "/");
        }
        private void LoadFiles(string path)
        {
            path = ConvertToLinuxPath(path);
            _currentPath = path;
            txtPath.Text = path;
            var items = new List<FileItem>();
            foreach (var entry in _sftpClient.ListDirectory(path))
            {
                if (entry.Name == "." || entry.Name == "..") continue;
                items.Add(new FileItem
                {
                    Name = entry.Name,
                    FullPath = entry.FullName,
                    IsDirectory = entry.IsDirectory,
                    LastWriteTime = entry.LastWriteTime,
                    Size = entry.IsDirectory ? 0 : entry.Length
                });
            }
            lvFiles.ItemsSource = items.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Name).ToList();
        }

        private void lvFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lvFiles.SelectedItem is FileItem item && item.IsDirectory)
            {
                LoadFiles(item.FullPath);
            }
        }

        private void BtnUp_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPath == "/") return;
            var parent = System.IO.Path.GetDirectoryName(_currentPath.TrimEnd('/'));
            if (string.IsNullOrEmpty(parent)) parent = "/";
            LoadFiles(parent);
        }

        private void BtnGo_Click(object sender, RoutedEventArgs e)
        {
            if (_sftpClient.Exists(txtPath.Text))
                LoadFiles(ConvertToLinuxPath( txtPath.Text));
        }

        // 右键菜单：新建文件夹
        private void MenuCreateDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("请输入新文件夹名称：", "");
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
            {
                var newDir = _currentPath.TrimEnd('/') + "/" + dlg.InputText;
                _sftpClient.CreateDirectory(newDir);
                LoadFiles(_currentPath);
            }
        }

        // 右键菜单：重命名
        private void MenuRename_Click(object sender, RoutedEventArgs e)
        {
            if (lvFiles.SelectedItem is FileItem item)
            {
                var dlg = new InputDialog("请输入新名称：", item.Name);
                dlg.Owner = this;
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
                {
                    var newPath = _currentPath.TrimEnd('/') + "/" + dlg.InputText;
                    _sftpClient.RenameFile(item.FullPath, newPath);
                    LoadFiles(_currentPath);
                }
            }
        }

        // 右键菜单：删除
        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            if (lvFiles.SelectedItem is FileItem item)
            {
                if (HandyControl.Controls.MessageBox.Show($"确定要删除“{item.Name}”吗？", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    if (item.IsDirectory)
                        _sftpClient.DeleteDirectory(item.FullPath);
                    else
                        _sftpClient.DeleteFile(item.FullPath);
                    LoadFiles(_currentPath);
                }
            }
        }

        // 右键菜单：上传文件
        private void MenuUploadFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            if (dlg.ShowDialog() == true)
            {
                var fileName = System.IO.Path.GetFileName(dlg.FileName);
                using (var fs = new FileStream(dlg.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    _sftpClient.UploadFile(fs, _currentPath.TrimEnd('/') + "/" + fileName, true);
                }
                LoadFiles(_currentPath);
            }
        }

        // 右键菜单：下载文件
        private void MenuDownloadFile_Click(object sender, RoutedEventArgs e)
        {
            if (lvFiles.SelectedItem is FileItem item && !item.IsDirectory)
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { FileName = item.Name };
                if (dlg.ShowDialog() == true)
                {
                    using (var fs = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write))
                    {
                        _sftpClient.DownloadFile(item.FullPath, fs);
                    }
                }
            }
        }
    }
}
