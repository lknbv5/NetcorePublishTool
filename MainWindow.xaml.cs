using HandyControl.Tools;
using Renci.SshNet;
using Renci.SshNet.Messages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Ude;
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
        private string _configPath;
        public string ConfigPath
        {
            get => _configPath;
            set
            {
                _configPath = value;
                OnPropertyChanged(nameof(ConfigPath));
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
            ConfigHelper.Instance.SetWindowDefaultStyle();
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
            ConfigPath = System.IO.Path.Combine(exePath, "config.json");
            //判断是否存在, 存在则加载
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                try
                {
                    var loadedServers = System.Text.Json.JsonSerializer.Deserialize<List<ServerConfig>>(json);
                    Servers.Clear();
                    foreach (var server in loadedServers)
                    {
                        Servers.Add(server);
                    }
                    Log("✅ 配置已加载");
                }
                catch (Exception ex)
                {
                    Log("⚠️ 配置加载失败,请检查配置文件格式!");
                }
                
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
            //progressBar.Value = 0;

            if (e.Error != null)
            {
                Log($"❌ 部署失败: {e.Error.Message}");
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

            //如果IsFullCheck=true, 弹窗提醒是否全量发布
            if (IsFullCheck)
            {
                var message = "是否全量发布？\n\n全量发布将会覆盖远程服务器上所有的文件, 包括配置文件,可能会导致未知问题发生，请确认！";
                var caption = "警告";
                var button = MessageBoxButton.YesNo;
                var icon = MessageBoxImage.Warning;
                var result = System.Windows.MessageBox.Show(message, caption, button, icon);
                if (result == MessageBoxResult.No)
                {
                    return;
                }
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
            //    Dispatcher.Invoke(() => Log($"❌ 错误: {ex.Message}"));
                e.Result = null;
                throw;
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
                //判断远程目录是否存在，不存在则创建
                if (!client.Exists("/" + ConvertToLinuxPath(newRemotePath)))
                {
                    client.CreateDirectory("/" + ConvertToLinuxPath(newRemotePath));
                }
                UploadDirectory(client, dir,newRemotePath);
            }
        }

        private void UploadFile(SftpClient client, string localFile, string remotePath)
        {
            using (var fileStream = File.OpenRead(localFile))
            {
                var remoteFileName =Path.Combine(remotePath, Path.GetFileName(localFile));
                //判断远程目录是否存在，不存在则创建
                if (!client.Exists("/" + ConvertToLinuxPath(Path.GetDirectoryName(remoteFileName))))
                {
                    client.CreateDirectory("/" + ConvertToLinuxPath(Path.GetDirectoryName(remoteFileName)));
                }
                client.UploadFile(fileStream, "/" + ConvertToLinuxPath( remoteFileName));
                _worker.ReportProgress(0, $"⬆️ 已上传: {remoteFileName}");
            }
        }
        /// <summary>
        /// 获取服务最终执行的exe路径
        /// </summary>
        /// <param name="serviceName"></param>
        /// <param name="sshClient"></param>
        /// <returns></returns>
        private string GetServiceExecutablePath(string serviceName, SshClient sshClient)
        {
            // 使用 PowerShell 查询注册表中的 Application 键
            var command = $"powershell -Command \"Get-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\{serviceName}\\Parameters' -Name Application | Select-Object -ExpandProperty Application\"";
            var result = sshClient.RunCommand(command);

            if (!string.IsNullOrWhiteSpace(result.Result))
            {
                return result.Result.Trim();
            }

            return null;
        }
        private void InstallWindowsService(SftpClient client)
        {
            using (var sshClient = new SshClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password))
            {
                sshClient.Connect();
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                sshClient.ConnectionInfo.Encoding = Encoding.GetEncoding("GBK");
                // 检查服务是否存在
                var checkServiceCommand = $"sc query {SelectedServer.ServiceName}";
                var checkResult = sshClient.RunCommand(checkServiceCommand);

                bool serviceExists = checkResult.Result.Contains("SERVICE_NAME");

                if (serviceExists)
                {
                    // 获取服务的运行路径
                    var currentExePath = GetServiceExecutablePath(SelectedServer.ServiceName, sshClient);
                    if (currentExePath != null)
                    {
                        var expectedExePath = Path.Combine(SelectedServer.RemotePath, SelectedServer.ExeName).Replace("\\", "/");
                        if (!string.Equals(currentExePath.Replace("\\", "/"), expectedExePath, StringComparison.OrdinalIgnoreCase))
                        {
                            // 如果路径不一致，提示用户是否删除服务
                            var message = $"服务 {SelectedServer.ServiceName} 的运行路径与本地配置不一致。\n当前路径: {currentExePath}\n预期路径: {expectedExePath}\n是否删除并重新安装服务？";
                            var caption = "警告";
                            var button = MessageBoxButton.YesNo;
                            var icon = MessageBoxImage.Warning;
                            var result = System.Windows.MessageBox.Show(message, caption, button, icon);
                            if (result == MessageBoxResult.Yes)
                            {
                                var deleteServiceCommand = $"sc delete {SelectedServer.ServiceName}";
                                var deleteResult = sshClient.RunCommand(deleteServiceCommand);
                                _worker.ReportProgress(0, $"$ {deleteServiceCommand}\n{deleteResult.Result}");
                                serviceExists = false; // 标记服务已被删除
                            }
                        }
                    }

                    // 如果服务存在，停止服务
                    var stopServiceCommand = $"sc stop {SelectedServer.ServiceName}";
                    var stopResult = sshClient.RunCommand(stopServiceCommand);
                    _worker.ReportProgress(0, $"$ {stopServiceCommand}\n{stopResult.Result}");
                    WaitForServiceToStop(sshClient, SelectedServer.ServiceName);
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
                    var startServiceCommand = $"chcp 65001 & sc start {SelectedServer.ServiceName}";
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

        private void WaitForServiceToStop(SshClient sshClient, string serviceName)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            sshClient.ConnectionInfo.Encoding = Encoding.GetEncoding("GBK");
            while (true)
            {
                // 检查服务状态
                var checkServiceCommand = $"sc query {serviceName}";
                var result = sshClient.RunCommand(checkServiceCommand);

                if (result.Result.Contains("STOPPED"))
                {
                    Log($"✅ 服务 {serviceName} 已停止");
                    break;
                }
                else if (result.Result.Contains("1060"))
                {
                    Log($"⚠️ 服务 {serviceName} 未安装");
                    break;
                }
                else
                {
                    Log($"⏳ 等待服务 {serviceName} 停止...");
                    System.Threading.Thread.Sleep(1000); // 等待1秒后重试
                }
            }
        }

        private string DetectEncoding(byte[] data)
        {
            CharsetDetector detector = new CharsetDetector();
            detector.Feed(data, 0, data.Length);
            detector.DataEnd();

            if (detector.Charset != null)
            {
                return detector.Charset; // 返回检测到的编码名称
            }
            else
            {
                return "未知编码";
            }
        }

        //编码转换
        private string ConvertEncoding(string str, string targetEncoding)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 将字符串转换为字节数组
            var bytes = Encoding.Default.GetBytes(str);
            // 自动检测源编码
            var detectedEncodingName = DetectEncoding(bytes);
            var sourceEnc = Encoding.GetEncoding(detectedEncodingName);

            // 转换为目标编码
            var targetEnc = Encoding.GetEncoding(targetEncoding);
            return targetEnc.GetString(bytes);
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
            //判断是否选择了配置文件
            if (ConfigPath == null)
            {
                Log("⚠️ 请先选择配置文件!");
                return;
            }
            var json = File.ReadAllText(ConfigPath);
            var loadedServers = System.Text.Json.JsonSerializer.Deserialize<List<ServerConfig>>(json);
            Servers.Clear();
            foreach (var server in loadedServers)
            {
                Servers.Add(server);
            }
            Log("✅ 配置已加载");
        }

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            //判断是否选择了配置文件
            if (ConfigPath == null)
            {
                Log("⚠️ 请先选择配置文件!");
                return;
            }
            var json = System.Text.Json.JsonSerializer.Serialize(Servers, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(ConfigPath, json);
            Log("✅ 配置已保存");
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
        //检查服务运行状态
        private void BtnCheckService_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedServer == null)
            {
                Log("⚠️ 请先选择一个服务器配置!");
                return;
            }

            try
            {
                using (var sshClient = new SshClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password))
                {
                    sshClient.Connect();
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                    sshClient.ConnectionInfo.Encoding = Encoding.GetEncoding("GBK");
                    // 检查服务状态
                    var checkServiceCommand = $"sc query {SelectedServer.ServiceName}";
                    var result = sshClient.RunCommand(checkServiceCommand);
                    Log($"$ {checkServiceCommand}\n{result.Result}");

                    if (result.Result.Contains("RUNNING"))
                    {
                        Log($"✅ 服务 {SelectedServer.ServiceName} 正在运行");
                    }
                    else if (result.Result.Contains("STOPPED"))
                    {
                        Log($"⏹ 服务 {SelectedServer.ServiceName} 已停止");
                    }
                    else if (result.Result.Contains("1060"))
                    {
                        Log($"⚠️ 服务 {SelectedServer.ServiceName} 未安装");
                    }
                    else
                    {
                        Log($"⚠️ 无法确定服务 {SelectedServer.ServiceName} 的状态");
                    }

                    sshClient.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Log($"❌ 检查服务状态时出错: {ex.Message}");
            }
        }
        //开启服务
        private void BtnStartService_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedServer == null)
            {
                Log("⚠️ 请先选择一个服务器配置!");
                return;
            }

            try
            {
                using (var sshClient = new SshClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password))
                {
                    sshClient.Connect();
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                    sshClient.ConnectionInfo.Encoding = Encoding.GetEncoding("GBK");
                    // 启动服务
                    var startServiceCommand = $"sc start {SelectedServer.ServiceName}";
                    var result = sshClient.RunCommand(startServiceCommand);
                    Log( $"$ {startServiceCommand}\n{result.Result}");

                    if (result.Result.Contains("RUNNING"))
                    {
                        Log($"✅ 服务 {SelectedServer.ServiceName} 已成功启动");
                    }
                    else if (result.Result.Contains("1060"))
                    {
                        Log($"⚠️ 服务 {SelectedServer.ServiceName} 未安装");
                    }
                    else
                    {
                        Log($"⚠️ 无法启动服务 {SelectedServer.ServiceName}: {result.Result}");
                    }

                    sshClient.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Log($"❌ 启动服务时出错: {ex.Message}");
            }
        }
        //停止服务
        private void BtnStopService_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedServer == null)
            {
                Log("⚠️ 请先选择一个服务器配置!");
                return;
            }

            try
            {
                using (var sshClient = new SshClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password))
                {
                    sshClient.Connect();
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                    sshClient.ConnectionInfo.Encoding = Encoding.GetEncoding("GBK");
                    // 停止服务
                    var stopServiceCommand = $"sc stop {SelectedServer.ServiceName}";
                    var result = sshClient.RunCommand(stopServiceCommand);

                    if (result.Result.Contains("STOPPED"))
                    {
                        Log($"✅ 服务 {SelectedServer.ServiceName} 已成功停止");
                    }
                    else if (result.Result.Contains("1062"))
                    {
                        Log($"⚠️ 服务{SelectedServer.ServiceName} 未启动!");
                    }
                    else if (result.Result.Contains("1060"))
                    {
                        Log($"⚠️ 服务 {SelectedServer.ServiceName} 未安装!");
                    }
                    else
                    {
                        Log($"⚠️ 无法停止服务 {SelectedServer.ServiceName}: {result.Result}");
                    }

                        sshClient.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Log($"❌ 停止服务时出错: {ex.Message}");
            }
        }
        //移除服务
        private void BtnRemoveService_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedServer == null)
            {
                Log("⚠️ 请先选择一个服务器配置!");
                return;
            }

            try
            {
                using (var sshClient = new SshClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password))
                {
                    sshClient.Connect();
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                    sshClient.ConnectionInfo.Encoding = Encoding.GetEncoding("GBK");
                    // 停止服务
                    var stopServiceCommand = $"sc stop {SelectedServer.ServiceName}";
                    var stopResult = sshClient.RunCommand(stopServiceCommand);
                    Log( $"$ {stopServiceCommand}\n{stopResult.Result}");

                    // 删除服务
                    var deleteServiceCommand = $"sc delete {SelectedServer.ServiceName}";
                    var deleteResult = sshClient.RunCommand(deleteServiceCommand);
                    Log( $"$ {deleteServiceCommand}\n{deleteResult.Result}");

                    if (deleteResult.Result.Contains("成功") || deleteResult.Result.Contains("SUCCESS") || deleteResult.Result.Contains("DeleteService"))
                    {
                        Log($"✅ 服务 {SelectedServer.ServiceName} 已成功移除!");
                    }
                    else if (deleteResult.Result.Contains("1060"))
                    {
                        Log($"⚠️ 服务 {SelectedServer.ServiceName} 未安装!");
                    }
                    else
                    {
                        Log($"⚠️ 无法移除服务 {SelectedServer.ServiceName}: {deleteResult.Result}");
                    }

                    sshClient.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Log($"❌ 移除服务时出错: {ex.Message}");
            }
        }
        //查看服务器, 以API_开头的服务列表
        private void BtnCheckServiceList_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedServer == null)
            {
                Log("⚠️ 请先选择一个服务器配置!");
                return;
            }

            try
            {
                using (var sshClient = new SshClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password))
                {
                    sshClient.Connect();
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                    sshClient.ConnectionInfo.Encoding = Encoding.GetEncoding("GBK");
                    // 获取所有服务并筛选以 "API_" 开头的服务
                    var listServicesCommand = "sc query state= all";
                    var result = sshClient.RunCommand(listServicesCommand);
                    Log($"$ {listServicesCommand}\n{result.Result}");

                    if (!string.IsNullOrWhiteSpace(result.Result))
                    {
                        Log("✅ 以下是以 'API_' 开头的服务列表:");
                        var services = result.Result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                                    .Where(line => line.Trim().Contains("SERVICE_NAME: API_"));
                        foreach (var service in services)
                        {
                            Log(service.Trim());
                        }
                    }
                    else
                    {
                        Log("⚠️ 未找到任何服务!");
                    }

                    sshClient.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Log($"❌ 获取服务列表时出错: {ex.Message}");
            }
        }
        //配置文件浏览
        private void BtnConfigBrowseLocal_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "配置文件 (*.json)|*.json",
                Title = "加载发版配置",
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
            {
                txtConfigPath.Text = dialog.FileName;
            }
        }

        private void lstFiles_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 获取双击的项
            if (lstFiles.SelectedItem is string selectedFile)
            {
                // 从绑定的集合中移除该项
                var files = DataContext as dynamic; // 假设 DataContext 是绑定的 ViewModel
                files?.SelectedFiles?.Remove(selectedFile);
            }
        }
    }
}
