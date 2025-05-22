using HandyControl.Tools;
using Renci.SshNet;
using Renci.SshNet.Messages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using Ude;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
using DragDropEffects = System.Windows.DragDropEffects;

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

        //sftp连接状态检查器
        private bool _lastSftpConnected;
        private DispatcherTimer _sftpStatusTimer;

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
                var needReConnect=false;
                if (_selectedServer == null)
                {
                    needReConnect = true;
                }
                else
                {
                    if (value != null && value.ServerIP != _selectedServer.ServerIP && value.Username != _selectedServer.Username && value.Password != _selectedServer.Password)
                    {
                        needReConnect = true;
                    }
                }
                _selectedServer = value;
                OnPropertyChanged(nameof(SelectedServer));
                if (needReConnect)
                {
                    ConnectAndLoad();
                }
            }
        }
        private void DisconnectSftp()
        {
            CurrentPath = "/";
            this.SftpDirAndFiles.Clear();
            if (_sftpClient!= null)
            {
                _sftpClient.Disconnect();
                UpdateSftpConnected();
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
        private ObservableCollection<FilrOrDir> _selectedFiles = new ObservableCollection<FilrOrDir>();
        public ObservableCollection<FilrOrDir> SelectedFiles
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

            _sftpStatusTimer = new DispatcherTimer();
            _sftpStatusTimer.Interval = TimeSpan.FromSeconds(1); // 每秒检查一次
            _sftpStatusTimer.Tick += SftpStatusTimer_Tick;
            _sftpStatusTimer.Start();
        }
        private void SftpStatusTimer_Tick(object sender, EventArgs e)
        {
            bool current = SftpConnected;
            if (current != _lastSftpConnected)
            {
                OnPropertyChanged(nameof(SftpConnected));
                _lastSftpConnected = current;
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
                Title= "请选择文件(可多选)"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                foreach (var file in dialog.FileNames)
                {
                    if (!SelectedFiles.Any(o => o.Path == file&&o.Type=="File"))
                    {
                        SelectedFiles.Add(new FilrOrDir() { Path = file, Type = "File" });
                    }
                }
            }
        }


        private void BtnSelectDirs_Click(object sender, RoutedEventArgs e)
        {
            //var folderDialog = new FolderBrowserDialog();
            //// 选择文件夹
            //if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            //{
            //    SelectedFiles.Add(new FilrOrDir { Path = folderDialog.SelectedPath, Type = "文件夹" });
            //}
            var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
            {
                Multiselect = true,
                Description = "请选择文件夹(可多选)",
                UseDescriptionForTitle = true,
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var folder in dialog.SelectedPaths)
                {
                    if (!SelectedFiles.Any(o => o.Path == folder && o.Type == "Dir"))
                    {
                        SelectedFiles.Add(new FilrOrDir { Path = folder, Type = "Dir" });
                    }
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
                var result = HandyControl.Controls.MessageBox.Show(message, caption, button, icon);
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
                var newRemotePath = System.IO.Path.Combine(remotePath, System.IO.Path.GetFileName(dir));
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
                    // 获取服务上已有服务的运行路径
                    var oldExePath = GetServiceExecutablePath(SelectedServer.ServiceName, sshClient);
                    if (oldExePath != null)
                    {
                        var nowExePath = Path.Combine(SelectedServer.RemotePath, SelectedServer.ExeName).Replace("\\", "/");
                        if (!string.Equals(oldExePath.Replace("\\", "/"), nowExePath, StringComparison.OrdinalIgnoreCase))
                        {
                            // 如果路径不一致，提示用户是否删除服务
                            var message = $"服务器已有服务 {SelectedServer.ServiceName} 的运行路径与本地配置不一致。\n服务器当前运行路径: {oldExePath}\n本次配置运行路径: {nowExePath}\n是否删除服务器原有服务并重新安装新服务？";
                            var caption = "警告";
                            var button = MessageBoxButton.YesNo;
                            var icon = MessageBoxImage.Warning;
                            var result = HandyControl.Controls.MessageBox.Show(message, caption, button, icon);
                            if (result == MessageBoxResult.Yes)
                            {
                                var deleteServiceCommand = $"sc delete {SelectedServer.ServiceName}";
                                var deleteResult = sshClient.RunCommand(deleteServiceCommand);
                                _worker.ReportProgress(0, $"$ {deleteServiceCommand}\n{deleteResult.Result}");
                                serviceExists = false; // 标记服务已被删除
                            }
                            else
                            {
                                throw new Exception("⚠️ 服务部署终止!请重新确认配置!");
                            }
                        }
                    }

                    // 如果服务存在，停止服务
                    var stopServiceCommand = $"sc stop {SelectedServer.ServiceName}";
                    var stopResult = sshClient.RunCommand(stopServiceCommand);
                    _worker.ReportProgress(0, $"$ {stopServiceCommand}\n{stopResult.Result}");
                    var rrr = WaitForServiceToStop(sshClient, SelectedServer.ServiceName);
                    if (!rrr)//没有成功停止服务
                    {
                        throw new Exception("⚠️ 服务停止失败!无法继续部署!");
                    }
                }

                // 上传文件
                if (IsFullCheck)//全量
                {
                    UploadDirectory(client, SelectedServer.LocalPath, SelectedServer.RemotePath);
                }
                else//部分上传
                {
                    foreach (var fileOrDir in SelectedFiles)
                    {
                        //UploadFile(client, file,SelectedServer.RemotePath);
                        if (fileOrDir.Type == "File") // 如果是文件
                        {
                            UploadFile(client, fileOrDir.Path, SelectedServer.RemotePath);
                        }
                        else // 如果是文件夹
                        {
                            UploadDirectory(client, fileOrDir.Path, Path.Combine(SelectedServer.RemotePath, Path.GetFileName(fileOrDir.Path)));
                        }
                    }
                }

                if (serviceExists)
                {
                    // 如果服务存在，重新启动服务
                    var startServiceCommand = $"sc start {SelectedServer.ServiceName}";
                    var startResult = sshClient.RunCommand(startServiceCommand);
                    _worker.ReportProgress(0, $"$ {startServiceCommand}\n{startResult.Result}");
                    WaitForServiceToStart(sshClient, SelectedServer.ServiceName);
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
                    WaitForServiceToStart(sshClient, SelectedServer.ServiceName);
                }

                sshClient.Disconnect();
            }
        }
        //等待停下, true表示成功停止/或者不存在, false表示超时
        private bool WaitForServiceToStop(SshClient sshClient, string serviceName)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            sshClient.ConnectionInfo.Encoding = Encoding.GetEncoding("GBK");
            var stopresult = false;
            //循环等待服务停止, 超时时间为10秒
            var timeout = 10;
            var waitTime = 0;
            while (true)
            {
                // 检查服务状态
                var checkServiceCommand = $"sc query {serviceName}";
                var result = sshClient.RunCommand(checkServiceCommand);

                if (result.Result.Contains("STOPPED"))
                {
                    Log($"✅ 服务 {serviceName} 已停止");
                    stopresult = true;
                    break;
                }
                else if (result.Result.Contains("1060"))
                {
                    Log($"⚠️ 服务 {serviceName} 未安装");
                    stopresult = true;
                    break;
                }
                else if (result.Result.Contains("1062"))
                {
                    Log($"⚠️ 服务{SelectedServer.ServiceName} 未启动!");
                    stopresult = true;
                }
                else
                {
                    Log($"⏳ 等待服务 {serviceName} 停止...");
                    System.Threading.Thread.Sleep(1000); // 等待1秒后重试
                    waitTime += 1;
                    if (waitTime >= timeout)
                    {
                        Log($"⚠️ 服务 {serviceName} 停止超时");
                        break;
                    }
                }
            }
            return stopresult;
        }

        private bool WaitForServiceToStart(SshClient sshClient, string serviceName)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            sshClient.ConnectionInfo.Encoding = Encoding.GetEncoding("GBK");
            var startresult = false;
            //循环等待服务启动, 超时时间为10秒
            var timeout = 10;
            var waitTime = 0;
            while (true)
            {
                // 检查服务状态
                var checkServiceCommand = $"sc query {serviceName}";
                var result = sshClient.RunCommand(checkServiceCommand);

                if (result.Result.Contains("RUNNING"))
                {
                    Log($"✅ 服务 {serviceName} 已启动");
                    startresult = true;
                    break;
                }
                else if (result.Result.Contains("1060"))
                {
                    Log($"⚠️ 服务 {serviceName} 未安装");
                    startresult = true;
                    break;
                }
                else if (result.Result.Contains("1056"))
                {
                    Log($"⚠️ 服务 {SelectedServer.ServiceName} 已在运行中");
                }
                else
                {
                    Log($"⏳ 等待服务 {serviceName} 启动中...");
                    System.Threading.Thread.Sleep(1000); // 等待1秒后重试
                    waitTime += 1;
                    if (waitTime >= timeout)
                    {
                        Log($"⚠️ 服务 {serviceName} 启动超时");
                        break;
                    }
                }
            }
            return startresult;
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
            //密码无法绑定, 单独处理
            if (SelectedServer!=null)
            {
                txtPassword.Password = SelectedServer.Password;
            }
        }

        private void BtnAddServer_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("请输入新配置名:");
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
            {
                if (Servers.Any(o => o.Name == dlg.InputText))
                {
                    Log("⚠️ 该配置名称已存在，请重新输入!");
                    return;
                }
                var newServer = new ServerConfig
                {
                    Name = dlg.InputText
                };
                Servers.Add(newServer);
                Log($"✅ 已成功添加发版配置: {newServer.Name}");
            }
        }
        //复制一份配置
        private void BtnCopyServer_Click(object sender, RoutedEventArgs e)
        {
            var newServer = new ServerConfig
            {
                Name = SelectedServer.Name+"_"+DateTime.Now.ToString("yyyyMMddHHmmss"),
                ServerIP = SelectedServer.ServerIP,
                Username = SelectedServer.Username,
                Password = SelectedServer.Password,
                ExeName = SelectedServer.ExeName,
                ServiceName = SelectedServer.ServiceName,
                LocalPath = SelectedServer.LocalPath,
                RemotePath = SelectedServer.RemotePath,
            };
            Servers.Add(newServer);
            SelectedServer = newServer;
            Log($"✅ 已成功复制发版配置: {newServer.Name}");
            Log($"✅ 已切换至新复制的配置: {newServer.Name}");
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
            using (var sshClient = new SshClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password))
            {
                sshClient.Connect();
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                sshClient.ConnectionInfo.Encoding = Encoding.GetEncoding("GBK");
                WaitForServiceToStart(sshClient, SelectedServer.ServiceName);
                sshClient.Disconnect();
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
            if (this.SelectedServer!=null)
            {
                this.SelectedServer.Password = (sender as PasswordBox).Password;
            }
            
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
        private async void BtnStartService_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedServer == null)
            {
                Log("⚠️ 请先选择一个服务器配置!");
                return;
            }

            try
            {
                await Task.Run(() => {
                    using (var sshClient = new SshClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password))
                    {
                        sshClient.Connect();
                        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                        sshClient.ConnectionInfo.Encoding = Encoding.GetEncoding("GBK");
                        // 启动服务
                        var startServiceCommand = $"sc start {SelectedServer.ServiceName}";
                        var result = sshClient.RunCommand(startServiceCommand);
                        Log($"$ {startServiceCommand}\n{result.Result}");
                       
                        WaitForServiceToStart(sshClient, SelectedServer.ServiceName);
                        sshClient.Disconnect();
                    }
                });
                
            }
            catch (Exception ex)
            {
                Log($"❌ 启动服务时出错: {ex.Message}");
            }
        }
        //停止服务
        private async void BtnStopService_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedServer == null)
            {
                Log("⚠️ 请先选择一个服务器配置!");
                return;
            }

            try
            {
                await Task.Run(() => {
                    using (var sshClient = new SshClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password))
                    {
                        sshClient.Connect();
                        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                        sshClient.ConnectionInfo.Encoding = Encoding.GetEncoding("GBK");
                        // 停止服务
                        var stopServiceCommand = $"sc stop {SelectedServer.ServiceName}";
                        var result = sshClient.RunCommand(stopServiceCommand);
                        Log($"$ {stopServiceCommand}\n{result.Result}");
                        
                        WaitForServiceToStop(sshClient, SelectedServer.ServiceName);
                        sshClient.Disconnect();
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"❌ 停止服务时出错: {ex.Message}");
            }
        }
        //移除服务
        private async void BtnRemoveService_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedServer == null)
            {
                Log("⚠️ 请先选择一个服务器配置!");
                return;
            }

            try
            {
                await Task.Run(() => {
                    using (var sshClient = new SshClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password))
                    {
                        sshClient.Connect();
                        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                        sshClient.ConnectionInfo.Encoding = Encoding.GetEncoding("GBK");
                        // 停止服务
                        var stopServiceCommand = $"sc stop {SelectedServer.ServiceName}";
                        var stopResult = sshClient.RunCommand(stopServiceCommand);
                        Log($"$ {stopServiceCommand}\n{stopResult.Result}");

                        // 删除服务
                        var deleteServiceCommand = $"sc delete {SelectedServer.ServiceName}";
                        var deleteResult = sshClient.RunCommand(deleteServiceCommand);
                        Log($"$ {deleteServiceCommand}\n{deleteResult.Result}");

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
                });
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
                    //Log($"$ {listServicesCommand}\n{result.Result}");

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
            if (lstFiles.SelectedItem is FilrOrDir selectedFile)
            {
                // 从绑定的集合中移除该项
                var files = DataContext as dynamic; // 假设 DataContext 是绑定的 ViewModel
                files?.SelectedFiles?.Remove(selectedFile);
            }
        }

        private void BtnClearFileList_Click(object sender, RoutedEventArgs e)
        {
            SelectedFiles.Clear();
        }
        
        private ObservableCollection<FileItem> _sftpDirAndFiles = new ObservableCollection<FileItem>();
        public ObservableCollection<FileItem> SftpDirAndFiles
        {
            get => _sftpDirAndFiles;
            set
            {
                _sftpDirAndFiles = value;
                OnPropertyChanged(nameof(SftpDirAndFiles));
            }
        }

        private FileItem _selectedDirAndFiles;
        public FileItem SelectedDirAndFiles
        {
            get => _selectedDirAndFiles;
            set
            {
                _selectedDirAndFiles = value;
                OnPropertyChanged(nameof(SelectedDirAndFiles));
            }
        }

        private string _currentPath = "/";
        public string CurrentPath
        {
            get => _currentPath;
            set
            {
                _currentPath = value;
                OnPropertyChanged(nameof(CurrentPath));
            }
        }

        private void LoadFiles(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (_sftpClient != null && _sftpClient.IsConnected)
            {
                path = ConvertToLinuxPath(path);
                CurrentPath = path;
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
                SftpDirAndFiles = new ObservableCollection<FileItem>(items.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Name));
                UpdateSftpHeaderSortIcon();
                SortSftpList(_sftpSortColumn, _sftpSortDirection);
            }
        }


        private void BtnUp_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentPath == "/") return;
            var parent = System.IO.Path.GetDirectoryName(CurrentPath.TrimEnd('/'));
            if (string.IsNullOrEmpty(parent)) parent = "/";
            CurrentPath = ConvertToLinuxPath(parent);
            LoadFiles(parent);
        }

        private void BtnGo_Click(object sender, RoutedEventArgs e)
        {
            if (_sftpClient!=null&&_sftpClient.IsConnected)
            {
                if (_sftpClient.Exists(CurrentPath))
                    LoadFiles(CurrentPath);
            }
            
        }

        private void SftpLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtSftpLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                txtSftpLog.ScrollToEnd();
            });
        }

        private void DelSftpLog_Click(object sender, RoutedEventArgs e)
        {
            txtSftpLog.Clear();
        }

        // 右键菜单：新建文件夹
        private void MenuCreateDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("请输入新文件夹名称：", "");
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
            {
                var newDir = CurrentPath.TrimEnd('/') + "/" + dlg.InputText;
                try
                {
                    _sftpClient.CreateDirectory(newDir);
                    SftpLog($"📁 新建文件夹: {newDir}");
                    LoadFiles(CurrentPath);
                }
                catch (Exception ex)
                {
                    SftpLog($"❌ 新建文件夹失败: {ex.Message}");
                }
            }
        }

        // 右键菜单：重命名
        private void MenuRename_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("请输入新名称：", SelectedDirAndFiles.Name);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
            {
                var newPath = CurrentPath.TrimEnd('/') + "/" + dlg.InputText;
                try
                {
                    _sftpClient.RenameFile(SelectedDirAndFiles.FullPath, newPath);
                    SftpLog($"✏️ 重命名: {SelectedDirAndFiles.FullPath} → {newPath}");
                    LoadFiles(CurrentPath);
                }
                catch (Exception ex)
                {
                    SftpLog($"❌ 重命名失败: {ex.Message}");
                }
            }
        }

        // 右键菜单：删除
        private async void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = lvFiles.SelectedItems.Cast<FileItem>().ToList();
            if (selectedItems.Count == 0)
            {
                SftpLog("⚠️ 请先选择要删除的文件或文件夹！");
                return;
            }

            if (HandyControl.Controls.MessageBox.Show(
                $"确定要删除选中的 {selectedItems.Count} 个文件/文件夹吗？",
                "确认", MessageBoxButton.YesNo, icon: MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            sftpProgressBar.Visibility = Visibility.Visible;
            sftpProgressBar.Value = 0;
            sftpProgressBar.Maximum = 100;

            int total = selectedItems.Count;
            await Task.Run(() =>
            {
                for (int i = 0; i < total; i++)
                {
                    var item = selectedItems[i];
                    try
                    {
                        if (item.IsDirectory)
                            DeleteDirectoryRecursive(_sftpClient, item.FullPath);
                        else
                            _sftpClient.DeleteFile(item.FullPath);

                        Dispatcher.Invoke(() =>
                        {
                            int percent = (int)((i + 1) * 100.0 / total);
                            sftpProgressBar.Value = percent;
                            SftpLog($"🗑️ 已删除: {item.Name}");
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            SftpLog($"❌ 删除失败: {item.Name} - {ex.Message}");
                        });
                    }
                }
            });

            await Task.Delay(300);
            sftpProgressBar.Visibility = Visibility.Collapsed;
            LoadFiles(CurrentPath);
        }
        private void DeleteDirectoryRecursive(SftpClient client, string remoteDir)
        {
            foreach (var entry in client.ListDirectory(remoteDir))
            {
                if (entry.Name == "." || entry.Name == "..") continue;
                if (entry.IsDirectory)
                {
                    DeleteDirectoryRecursive(client, entry.FullName);
                }
                else
                {
                    client.DeleteFile(entry.FullName);
                }
            }
            client.DeleteDirectory(remoteDir);
        }
        // 右键菜单：上传文件
        private async void MenuUploadFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                int total = dlg.FileNames.Length;
                if (total == 0) return;

                sftpProgressBar.Visibility = Visibility.Visible;
                sftpProgressBar.Value = 0;
                sftpProgressBar.Maximum = 100;

                await Task.Run(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        var filePath = dlg.FileNames[i];
                        var fileName = System.IO.Path.GetFileName(filePath);
                        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            _sftpClient.UploadFile(fs, CurrentPath.TrimEnd('/') + "/" + fileName, true);
                        }
                        // 进度更新需在UI线程
                        Dispatcher.Invoke(() =>
                        {
                            int percent = (int)((i + 1) * 100.0 / total);
                            sftpProgressBar.Value = percent;
                            SftpLog($"⬆️ 已上传: {fileName}");
                        });
                    }
                });

                await Task.Delay(300);
                sftpProgressBar.Visibility = Visibility.Collapsed;
                LoadFiles(CurrentPath);
            }
        }

        // 右键菜单：下载文件
        private async void MenuDownloadFile_Click(object sender, RoutedEventArgs e)
        {
            if (!SelectedDirAndFiles.IsDirectory)
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { FileName = SelectedDirAndFiles.Name };
                if (dlg.ShowDialog() == true)
                {
                    sftpProgressBar.Visibility = Visibility.Visible;
                    sftpProgressBar.Value = 0;
                    sftpProgressBar.Maximum = 100;

                    await Task.Run(() =>
                    {
                        using (var fs = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write))
                        {
                            _sftpClient.DownloadFile(SelectedDirAndFiles.FullPath, fs);
                        }
                    });

                    sftpProgressBar.Value = 100;
                    await Task.Delay(300);
                    sftpProgressBar.Visibility = Visibility.Collapsed;
                    SftpLog($"✅ 文件已下载到: {dlg.FileName}");
                }
            }
            else
            {
                var folderDlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "请选择本地保存文件夹"
                };
                if (folderDlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string localRoot = System.IO.Path.Combine(folderDlg.SelectedPath, SelectedDirAndFiles.Name);

                    int totalFiles = CountFiles(_sftpClient, SelectedDirAndFiles.FullPath);
                    if (totalFiles == 0)
                    {
                        SftpLog("⚠️ 文件夹为空，无需下载。");
                        return;
                    }

                    sftpProgressBar.Visibility = Visibility.Visible;
                    sftpProgressBar.Value = 0;
                    sftpProgressBar.Maximum = 100;
                    int current = 0;

                    var progress = new Progress<int>(v =>
                    {
                        int percent = (int)((double)v / totalFiles * 100);
                        sftpProgressBar.Value = percent;
                    });

                    await Task.Run(() =>
                    {
                        DownloadDirectoryAsync(_sftpClient, SelectedDirAndFiles.FullPath, localRoot, ref current, totalFiles, progress);
                    });

                    sftpProgressBar.Value = 100;
                    await Task.Delay(300);
                    sftpProgressBar.Visibility = Visibility.Collapsed;
                    SftpLog($"✅ 文件夹已下载到: {localRoot}");
                }
            }
        }
        // 拖拽进入时改变鼠标样式
        private void lvFiles_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        // 拖拽释放时上传文件
        private async void lvFiles_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;

            // 统计文件和文件夹数量
            int totalFiles = files.SelectMany(path =>
                Directory.Exists(path) ? Directory.GetFiles(path, "*", SearchOption.AllDirectories) : new[] { path }
            ).Count();

            if (totalFiles == 0)
            {
                SftpLog("⚠️ 没有可上传的文件。");
                return;
            }

            sftpProgressBar.Visibility = Visibility.Visible;
            sftpProgressBar.Value = 0;
            sftpProgressBar.Maximum = 100;
            int current = 0;

            await Task.Run(() =>
            {
                foreach (var path in files)
                {
                    if (File.Exists(path))
                    {
                        // 单文件
                        try
                        {
                            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                string remoteFile = CurrentPath.TrimEnd('/') + "/" + Path.GetFileName(path);
                                _sftpClient.UploadFile(fs, remoteFile, true);
                            }
                            current++;
                            Dispatcher.Invoke(() =>
                            {
                                int percent = (int)((double)current / totalFiles * 100);
                                sftpProgressBar.Value = percent;
                                SftpLog($"⬆️ 已上传: {Path.GetFileName(path)}");
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                SftpLog($"❌ 上传失败: {Path.GetFileName(path)} - {ex.Message}");
                            });
                        }
                    }
                    else if (Directory.Exists(path))
                    {
                        // 文件夹
                        current = UploadDirectoryWithProgress(_sftpClient, path, CurrentPath.TrimEnd('/') + "/" + Path.GetFileName(path), current, totalFiles);
                    }
                }
            });

            sftpProgressBar.Value = 100;
            await Task.Delay(300);
            sftpProgressBar.Visibility = Visibility.Collapsed;
            SftpLog("✅ 拖拽上传完成。");
            LoadFiles(CurrentPath);
        }
        private int CountFiles(SftpClient client, string remotePath)
        {
            int count = 0;
            foreach (var entry in client.ListDirectory(remotePath))
            {
                if (entry.Name == "." || entry.Name == "..") continue;
                if (entry.IsDirectory)
                    count += CountFiles(client, entry.FullName);
                else
                    count++;
            }
            return count;
        }

        private void DownloadDirectoryAsync(SftpClient client, string remotePath, string localPath, ref int current, int total, IProgress<int> progress)
        {
            if (!Directory.Exists(localPath))
                Directory.CreateDirectory(localPath);

            foreach (var entry in client.ListDirectory(remotePath))
            {
                if (entry.Name == "." || entry.Name == "..") continue;
                string localFilePath = System.IO.Path.Combine(localPath, entry.Name);
                if (entry.IsDirectory)
                {
                    DownloadDirectoryAsync(client, entry.FullName, localFilePath, ref current, total, progress);
                }
                else
                {
                    using (var fs = new FileStream(localFilePath, FileMode.Create, FileAccess.Write))
                    {
                        client.DownloadFile(entry.FullName, fs);
                    }
                    current++;
                    progress.Report(current);
                }
            }
        }
        private void ConnectAndLoad()
        {
            try
            {
                CurrentPath = "/";
                if (_sftpClient != null)
                {
                    _sftpClient.Disconnect();
                    UpdateSftpConnected();
                }
                _sftpClient = new SftpClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password);
                _sftpClient.Connect();
                UpdateSftpConnected();
                SftpLog($"🆗 已连接到 {SelectedServer.ServerIP}。");
                LoadFiles(CurrentPath);
            }
            catch (Exception ex)
            {
                SftpLog($"❌ 连接失败: {ex.Message}");
                SftpDirAndFiles.Clear();
            }
        }
        // 支持的文本文件扩展名
        private static readonly string[] TextFileExtensions = {
            // 常规文本
            ".txt", ".log", ".md", ".me", ".readme", ".rst", ".out", ".lst", ".srt", ".vtt",
            // 配置/数据
            ".json", ".xml", ".csv", ".tsv", ".yaml", ".yml", ".ini", ".conf", ".cfg", ".config", ".env", ".properties", ".toml", ".manifest", ".lock",
            // 脚本/代码
            ".bat", ".cmd", ".ps1", ".sh", ".py", ".js", ".ts", ".css", ".scss", ".less", ".html", ".htm", ".php", ".asp", ".aspx", ".jsp", ".java",
            ".c", ".cpp", ".h", ".hpp", ".cs", ".vb", ".go", ".rs", ".swift", ".kt", ".dart", ".sql", ".pl", ".rb", ".lua",
            // 版本/构建/工程
            ".gitignore", ".gitattributes", ".editorconfig", ".makefile", ".mak", ".dockerfile", ".npmrc", ".yarnrc", ".gradle", ".pro", ".props", ".targets",
            ".sln", ".csproj", ".vbproj", ".vcxproj", ".cmake", ".am", ".ac", ".m4", ".rc", ".def", ".prj", ".pubxml", ".pubxml.user"
        };

        // 判断是否为文本文件
        private bool IsTextFile(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLower();
            return TextFileExtensions.Contains(ext);
        }

        private async void lvFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedDirAndFiles!=null)
            {
                if (SelectedDirAndFiles.IsDirectory)
                {
                    LoadFiles(SelectedDirAndFiles.FullPath);
                }
                else
                {
                    // 判断是否为文本文件
                    if (IsTextFile(SelectedDirAndFiles.Name))
                    {
                        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_" + SelectedDirAndFiles.Name);

                        try
                        {
                            // 下载到本地临时文件
                            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                            {
                                await Task.Run(() => _sftpClient.DownloadFile(SelectedDirAndFiles.FullPath, fs));
                            }

                            // 用本机默认编辑器打开
                            var process = Process.Start(new ProcessStartInfo
                            {
                                FileName = tempFile,
                                UseShellExecute = true
                            });

                            // 等待编辑器关闭
                            await Task.Run(() => process.WaitForExit());

                            // 弹窗询问用户是否保存到服务器
                            var saveResult = HandyControl.Controls.MessageBox.Show(
                                $"是否将编辑后的文件保存回服务器？\n\n文件名: {SelectedDirAndFiles.Name}",
                                "保存到服务器", MessageBoxButton.YesNo, MessageBoxImage.Question);

                            if (saveResult == MessageBoxResult.Yes)
                            {
                                // 编辑完成后上传回服务器
                                using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read))
                                {
                                    await Task.Run(() => _sftpClient.UploadFile(fs, SelectedDirAndFiles.FullPath, true));
                                }
                                SftpLog($"✅ 文件已编辑并上传: {SelectedDirAndFiles.Name}");
                            }
                            else
                            {
                                SftpLog($"ℹ️ 文件已关闭: {SelectedDirAndFiles.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            SftpLog($"❌ 编辑或上传文件失败: {ex.Message}");
                        }
                        finally
                        {
                            // 删除本地临时文件
                            try { File.Delete(tempFile); } catch { }
                        }
                    }
                    else
                    {
                        SftpLog("⚠️ 暂不支持此类型文件的本地编辑。");
                    }
                }
            }
        }
        //递归统计文件数
        private int CountLocalFiles(string folder)
        {
            int count = 0;
            foreach (var file in Directory.GetFiles(folder))
                count++;
            foreach (var dir in Directory.GetDirectories(folder))
                count += CountLocalFiles(dir);
            return count;
        }
        private async void MenuUploadDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "请选择要上传的文件夹"
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string localFolder = dlg.SelectedPath;
                string remoteFolder = CurrentPath.TrimEnd('/') + "/" + Path.GetFileName(localFolder);

                // 统计总文件数
                int totalFiles = CountLocalFiles(localFolder);
                if (totalFiles == 0)
                {
                    SftpLog("⚠️ 文件夹为空，无需上传。");
                    return;
                }

                sftpProgressBar.Visibility = Visibility.Visible;
                sftpProgressBar.Value = 0;
                sftpProgressBar.Maximum = 100;
                int current = 0;

                await Task.Run(() =>
                {
                    UploadDirectoryWithProgress(_sftpClient, localFolder, remoteFolder, current, totalFiles);
                });

                sftpProgressBar.Value = 100;
                await Task.Delay(300);
                sftpProgressBar.Visibility = Visibility.Collapsed;
                SftpLog($"✅ 文件夹已上传到: {remoteFolder}");
                LoadFiles(CurrentPath);
            }
        }
        private int UploadDirectoryWithProgress(SftpClient client, string localPath, string remotePath, int current, int total)
        {
            // 创建远程目录（如果不存在）
            if (!client.Exists(ConvertToLinuxPath(remotePath)))
            {
                client.CreateDirectory(ConvertToLinuxPath(remotePath));
            }

            foreach (var file in Directory.GetFiles(localPath))
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    string remoteFile = remotePath.TrimEnd('/') + "/" + Path.GetFileName(file);
                    client.UploadFile(fs, ConvertToLinuxPath(remoteFile), true);
                }
                current++;
                int percent = (int)((double)current / total * 100);
                Dispatcher.Invoke(() =>
                {
                    sftpProgressBar.Value = percent;
                    SftpLog($"⬆️ 已上传: {ConvertToLinuxPath(Path.Combine(remotePath, Path.GetFileName(file)))}");
                });
            }

            foreach (var dir in Directory.GetDirectories(localPath))
            {
                string newRemotePath = remotePath.TrimEnd('/') + "/" + Path.GetFileName(dir);
                current = UploadDirectoryWithProgress(client, dir, newRemotePath, current, total);
            }
            return current;
        }
        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            // 重新加载当前路径
            LoadFiles(CurrentPath);
        }

        private void MenuRefresh_Click(object sender, RoutedEventArgs e)
        {
            // 重新加载当前路径
            LoadFiles(CurrentPath);
        }
        private string _sftpSortColumn = "Name";
        private ListSortDirection _sftpSortDirection = ListSortDirection.Ascending;
        private void lvFiles_GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader header && header.Column != null)
            {
                string sortBy = null;
                if (header.Column.Header.ToString().Contains("名称"))
                    sortBy = "Name";
                else if (header.Column.Header.ToString().Contains("修改时间"))
                    sortBy = "LastWriteTime";
                else if (header.Column.Header.ToString().Contains("文件大小"))
                    sortBy = "Size";
                else
                    return;

                ListSortDirection direction = ListSortDirection.Ascending;
                if (_sftpSortColumn == sortBy && _sftpSortDirection == ListSortDirection.Ascending)
                    direction = ListSortDirection.Descending;

                _sftpSortColumn = sortBy;
                _sftpSortDirection = direction;
                SortSftpList(_sftpSortColumn, _sftpSortDirection);
                UpdateSftpHeaderSortIcon();
            }
        }

        public bool SftpConnected
        {
            get => _sftpClient != null && _sftpClient.IsConnected;
        }
        // 在连接/断开后调用
        private void UpdateSftpConnected()
        {
            OnPropertyChanged(nameof(SftpConnected));
        }

        private void UpdateSftpHeaderSortIcon()
        {
            foreach (var col in ((GridView)lvFiles.View).Columns)
            {
                string header = col.Header as string;
                if (header == null) continue;

                if ((_sftpSortColumn == "Name" && header.Contains("名称")) ||
                    (_sftpSortColumn == "LastWriteTime" && header.Contains("修改时间")) ||
                    (_sftpSortColumn == "Size" && header.Contains("文件大小")))
                {
                    string arrow = _sftpSortDirection == ListSortDirection.Ascending ? " ▲" : " ▼";
                    if (!header.EndsWith("▲") && !header.EndsWith("▼"))
                        col.Header = header.TrimEnd(' ', '▲', '▼') + arrow;
                    else
                        col.Header = header.TrimEnd(' ', '▲', '▼') + arrow;
                }
                else
                {
                    // 去掉箭头
                    col.Header = header.TrimEnd(' ', '▲', '▼');
                }
            }
        }
        // 排序实现
        private void SortSftpList(string sortBy, ListSortDirection direction)
        {
            ICollectionView dataView = CollectionViewSource.GetDefaultView(lvFiles.ItemsSource);
            if (dataView != null)
            {
                dataView.SortDescriptions.Clear();
                dataView.SortDescriptions.Add(new SortDescription(sortBy, direction));
                dataView.Refresh();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (_sftpClient!=null)
            {
                if (_sftpClient.IsConnected)
                {
                    _sftpClient.Disconnect();
                    UpdateSftpConnected();
                }
                _sftpClient.Dispose();
            }
            _sftpStatusTimer.Stop();
        }

        private async void BtnSystemSSH_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedServer == null || string.IsNullOrEmpty(SelectedServer.ServerIP) ||
        string.IsNullOrEmpty(SelectedServer.Username) || string.IsNullOrEmpty(SelectedServer.Password))
            {
                HandyControl.Controls.MessageBox.Show("请先选择有效的服务器配置，并填写用户名和密码。", "提醒", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
            string privateKeyPath = Path.Combine(sshDir, "id_rsa");
            string publicKeyPath = Path.Combine(sshDir, "id_rsa.pub");

            try
            {
                // 1. 检查并生成密钥对
                if (!File.Exists(privateKeyPath) || !File.Exists(publicKeyPath))
                {
                    Directory.CreateDirectory(sshDir);
                    var psi = new ProcessStartInfo
                    {
                        FileName = "ssh-keygen",
                        Arguments = $"-t rsa -b 2048 -N \"\" -f \"{privateKeyPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var proc = Process.Start(psi);
                    proc.WaitForExit();
                    if (!File.Exists(publicKeyPath))
                    {
                        HandyControl.Controls.MessageBox.Show("密钥生成失败，请检查ssh-keygen是否可用。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // 2. 读取本地公钥
                string publicKey = File.ReadAllText(publicKeyPath).Trim();

                // 3. 上传公钥到Windows服务器（PowerShell方式，防止重复）
                await Task.Run(() =>
                {
                    using (var client = new SshClient(SelectedServer.ServerIP, SelectedServer.Username, SelectedServer.Password))
                    {
                        client.Connect();

                        // 确保.ssh目录存在
                        client.RunCommand("powershell -Command \"if (!(Test-Path $env:USERPROFILE\\.ssh)) { New-Item -ItemType Directory -Path $env:USERPROFILE\\.ssh }\"");

                        // 检查并追加公钥（防止重复）
                        string escapedKey = publicKey.Replace("'", "''");
                        string checkAndAddCmd =
                            $"powershell -Command \"if ((Test-Path $env:USERPROFILE\\.ssh\\authorized_keys) -and " +
                            $"(Get-Content $env:USERPROFILE\\.ssh\\authorized_keys | Select-String -Pattern '{escapedKey}')) " +
                            $"{{}} else {{ Add-Content -Path $env:USERPROFILE\\.ssh\\authorized_keys -Value '{escapedKey}' }}\"";
                        client.RunCommand(checkAndAddCmd);

                        client.Disconnect();
                    }
                });

                // 4. 自动打开本地终端并连接服务器
                string sshCmd = $"ssh {SelectedServer.Username}@{SelectedServer.ServerIP}";
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k {sshCmd}",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    HandyControl.Controls.MessageBox.Show($"自动打开终端失败: {ex.Message}", "提醒", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                HandyControl.Controls.MessageBox.Show($"自动配置免密登录失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    public class FilrOrDir : INotifyPropertyChanged
    {
        private string _type;
        public string Type
        {
            get => _type;
            set
            {
                _type = value;
                OnPropertyChanged(nameof(Type));
            }
        }
        private string _path;
        public string Path
        {
            get => _path;
            set
            {
                _path = value;
                OnPropertyChanged(nameof(Path));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
