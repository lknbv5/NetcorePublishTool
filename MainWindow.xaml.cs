using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace PublishTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private SftpClient _sftpClient;
        private BackgroundWorker _worker;
        private bool _isDeploying;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // 服务器配置集合
        private ObservableCollection<ServerConfig> _servers = new ObservableCollection<ServerConfig>();
        public ObservableCollection<ServerConfig> Servers
        {
            get => _servers;
            set
            {
                _servers = value;
                OnPropertyChanged(nameof(Servers));
            }
        }

        private ServerConfig _selectedServer;
        public ServerConfig SelectedServer
        {
            get => _selectedServer;
            set
            {
                _selectedServer = value;
                OnPropertyChanged(nameof(SelectedServer));
            }
        }

        private bool _isFullCheck;
        public bool IsFullCheck
        {
            get => _isFullCheck;
            set
            {
                _isFullCheck = value;
                OnPropertyChanged(nameof(IsFullCheck));
            }
        }

        // 选择的文件列表
        private ObservableCollection<string> _selectedFiles = new ObservableCollection<string>();
        public ObservableCollection<string> SelectedFiles
        {
            get => _selectedFiles;
            set
            {
                _selectedFiles = value;
                OnPropertyChanged(nameof(SelectedFiles));
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            IsFullCheck = true;
            // 初始化后台工作线程
            _worker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            _worker.DoWork += Worker_DoWork;
            _worker.ProgressChanged += Worker_ProgressChanged;
            _worker.RunWorkerCompleted += Worker_RunWorkerCompleted;

            //默认加载exe同文件夹下的配置文件
            var exePath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var configPath = System.IO.Path.Combine(exePath, "config.json");
            //判断是否存在, 存在则加载
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var loadedServers = System.Text.Json.JsonSerializer.Deserialize<List<ServerConfig>>(json);
                Servers.Clear();
                foreach (var server in loadedServers)
                {
                    Servers.Add(server);
                }
                Log("✅ 配置已加载");
            }

            if (Servers.Count>0)
            {
                SelectedServer=Servers[0];
            }
        }
        // 进度更新处理函数
        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // 更新进度条
            progressBar.Value = e.ProgressPercentage;

            // 显示附加信息
            if (e.UserState != null)
            {
                Dispatcher.Invoke(() =>
                {
                    txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {e.UserState}\n");
                    txtLog.ScrollToEnd();
                });
            }
        }

        // 任务完成处理函数
        private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            _isDeploying = false;
            BtnDeploy.IsEnabled = true;
            BtnStop.IsEnabled = false;
            progressBar.Value = 0;

            if (e.Error != null)
            {
                Log($"⚠️ 部署失败: {e.Error.Message}");
            }
            else if (e.Cancelled)
            {
                Log("⏹ 操作已取消");
            }
            else
            {
                Log("✅ 部署完成");
            }
        }
        private void BtnBrowseLocal_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog()) // 修复命名空间问题
            {
                if (dialog.ShowDialog() ==System.Windows.Forms.DialogResult.OK) // 修复对正确枚举的引用
                {
                    txtLocalPath.Text = dialog.SelectedPath;
                }
            }
        }

        private void BtnSelectFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.OpenFileDialog()
            {
                Multiselect = true,
                InitialDirectory = txtLocalPath.Text,
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SelectedFiles.Clear();
                foreach (var file in dialog.FileNames)
                {
                    SelectedFiles.Add(file);
                }
            }
        }

        private void BtnDeploy_Click(object sender, RoutedEventArgs e)
        {
            //判断信息是否完整
            if (string.IsNullOrEmpty(SelectedServer.ServerIP) ||
                string.IsNullOrEmpty(SelectedServer.Username) ||
                string.IsNullOrEmpty(SelectedServer.Password) ||
                string.IsNullOrEmpty(SelectedServer.LocalPath) ||
                string.IsNullOrEmpty(SelectedServer.RemotePath) ||
                string.IsNullOrEmpty(SelectedServer.ServiceName) ||
                string.IsNullOrEmpty(SelectedServer.ExeName))
            {
                Log("⚠️ 请填写完整的配置信息!");
                return;
            }

            if(!IsFullCheck && SelectedFiles.Count == 0)
            {
                Log("⚠️ 请选择要发布的文件!");
                return;
            }

            if (!_isDeploying)
            {
                _isDeploying = true;
                _worker.RunWorkerAsync();
                ((System.Windows.Controls.Button)sender).IsEnabled = false;
                BtnStop.IsEnabled = true;
            }
        }
        public static string ConvertToLinuxPath(string windowsPath)
        {
            return windowsPath.Replace(@"\\", "/").Replace(@"\","/");
        }
        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                // 建立SSH连接
                using (var client = new SftpClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password))
                {
                    client.Connect();
                    // 安装Windows服务
                    InstallWindowsService(client);
                    client.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Log($"❌ 错误: {ex.Message}"));
            }
        }

        private void UploadDirectory(SftpClient client, string localPath, string remotePath)
        {
            foreach (var file in Directory.GetFiles(localPath))
            {
                UploadFile(client, file, remotePath);
            }

            foreach (var dir in Directory.GetDirectories(localPath))
            {
                var newRemotePath = Path.Combine(remotePath, Path.GetFileName(dir));
                client.CreateDirectory("/"+newRemotePath);
                UploadDirectory(client, dir,newRemotePath);
            }
        }

        private void UploadFile(SftpClient client, string localFile, string remotePath)
        {
            using (var fileStream = File.OpenRead(localFile))
            {
                var remoteFileName =Path.Combine(remotePath, Path.GetFileName(localFile));
                client.UploadFile(fileStream, "/" + ConvertToLinuxPath( remoteFileName));
                _worker.ReportProgress(0, $"⬆️ 已上传: {remoteFileName}");
            }
        }

        private void InstallWindowsService(SftpClient client)
        {
            using (var sshClient = new SshClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password))
            {
                sshClient.Connect();

                // 检查服务是否存在
                var checkServiceCommand = $"sc query {SelectedServer.ServiceName}";
                var checkResult = sshClient.RunCommand(checkServiceCommand);

                bool serviceExists = checkResult.Result.Contains("SERVICE_NAME");

                if (serviceExists)
                {
                    // 如果服务存在，停止服务
                    var stopServiceCommand = $"sc stop {SelectedServer.ServiceName}";
                    var stopResult = sshClient.RunCommand(stopServiceCommand);
                    _worker.ReportProgress(0, $"$ {stopServiceCommand}\n{stopResult.Result}");
                }

                // 上传文件
                if (IsFullCheck)
                {
                    UploadDirectory(client, SelectedServer.LocalPath, SelectedServer.RemotePath);
                }
                else
                {
                    foreach (var file in SelectedFiles)
                    {
                        UploadFile(client, file,SelectedServer.RemotePath);
                    }
                }

                if (serviceExists)
                {
                    // 如果服务存在，重新启动服务
                    var startServiceCommand = $"sc start {SelectedServer.ServiceName}";
                    var startResult = sshClient.RunCommand(startServiceCommand);
                    _worker.ReportProgress(0, $"$ {startServiceCommand}\n{startResult.Result}");
                }
                else
                {
                    // 如果服务不存在，安装并启动服务
                    var installCommands = new[]
                    {
                        $"nssm install {SelectedServer.ServiceName} {Path.Combine(SelectedServer.RemotePath, SelectedServer.ExeName)}",
                        $"nssm set {SelectedServer.ServiceName} AppDirectory {SelectedServer.RemotePath}",
                        $"nssm start {SelectedServer.ServiceName}"
                    };

                    foreach (var cmd in installCommands)
                    {
                        var result = sshClient.RunCommand(cmd);
                        _worker.ReportProgress(0, $"$ {cmd}\n{result.Result}");
                    }
                }

                sshClient.Disconnect();
            }
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                txtLog.ScrollToEnd();
            });
        }

        private void CmbServers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbServers.SelectedItem is ServerConfig selectedServer)
            {
                txtName.Text = selectedServer.Name;
                txtServerIP.Text = selectedServer.ServerIP;
                txtUserName.Text = selectedServer.Username;
                txtPassword.Password = selectedServer.Password;
                txtExeName.Text = selectedServer.ExeName;
                txtServiceName.Text = selectedServer.ServiceName;
                txtLocalPath.Text = selectedServer.LocalPath;
                txtRemotePath.Text = selectedServer.RemotePath;
            }
        }

        private void BtnAddServer_Click(object sender, RoutedEventArgs e)
        {
            if (Servers.Any(o=>o.Name==txtName.Text))
            {
                Log("⚠️ 该配置名称已存在，请重新输入!");
                return;
            }
            var newServer = new ServerConfig
            {
                Name = txtName.Text,
                ServerIP = txtServerIP.Text,
                Username = txtUserName.Text,
                Password = txtPassword.Password,
                LocalPath = txtLocalPath.Text,
                RemotePath = txtRemotePath.Text,
                ServiceName = txtServiceName.Text,
                ExeName = txtExeName.Text
            };

            Servers.Add(newServer);
            Log($"✅ 已成功添加发版配置: {newServer.Name}");
        }

        private void BtnLoadConfig_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "配置文件 (*.json)|*.json",
                Title = "加载发版配置"
            };

            if (dialog.ShowDialog() == true)
            {
                var json = File.ReadAllText(dialog.FileName);
                var loadedServers = System.Text.Json.JsonSerializer.Deserialize<List<ServerConfig>>(json);
                Servers.Clear();
                foreach (var server in loadedServers)
                {
                    Servers.Add(server);
                }
                Log("✅ 配置已加载");
            }
        }

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "配置文件 (*.json)|*.json",
                Title = "保存发版配置"
            };

            if (dialog.ShowDialog() == true)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(Servers);
                File.WriteAllText(dialog.FileName, json);
                Log("✅ 配置已保存");
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (_worker.IsBusy)
            {
                _worker.CancelAsync();
                Log("⏹ 停止操作请求已发送");
            }
        }

        private void ClearConfigTxt()
        {
            txtName.Text = "";
            txtServerIP.Text = "";
            txtUserName.Text = "";
            txtPassword.Password = "";
            txtExeName.Text = "";
            txtServiceName.Text = "";
            txtLocalPath.Text = "";
            txtRemotePath.Text = "";    
        }
        private void BtnRemoveServer_Click(object sender, RoutedEventArgs e)
        {
            if (cmbServers.SelectedItem is ServerConfig selectedServer)
            {
                Servers.Remove(selectedServer);
                ClearConfigTxt();
                Log($"✅ 已移除发版配置: {selectedServer.Name}");
            }
            else
            {
                Log("⚠️ 请先选择要移除的配置");
            }
        }
        /// <summary>
        /// 清空日志
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DelLog_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Clear();
        }

        private void txtPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            this.SelectedServer.Password = (sender as PasswordBox).Password;
        }
    }
}
