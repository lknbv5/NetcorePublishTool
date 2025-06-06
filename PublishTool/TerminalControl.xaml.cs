using Renci.SshNet;
using Renci.SshNet.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace PublishTool
{
    public partial class TerminalControl : UserControl
    {
        private SshClient _sshClient;
        private ShellStream _shellStream;
        public ServerConfig ServerConfig;

        public TerminalControl()
        {
            InitializeComponent();
        }
        //加载完成, 自动连接
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (ServerConfig!= null)
            {
                BtnConnect_Click(sender, e);
            }
        }
        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_sshClient != null && _sshClient.IsConnected)
            {
                AppendTerminalText("Already connected to SSH server.\n");
                return;
            }

            try
            {
                AppendTerminalText($"Connecting to {ServerConfig.ServerIP}...\n");
                //btnConnect.IsEnabled = false;
                //txtStatus.Text = "Connecting...";

                await Task.Run(() =>
                {
                    _sshClient = new SshClient(ServerConfig.ServerIP, 22, ServerConfig.Username, ServerConfig.Password);
                    _sshClient.Connect();

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // 创建 ShellStream 时禁用回显
                        _shellStream = _sshClient.CreateShellStream("vt100", 80, 24, 800, 600, 1024,
                            new Dictionary<TerminalModes, uint> { { TerminalModes.ECHO, 0 } });

                        AppendTerminalText($"Connected to {ServerConfig.ServerIP} as {ServerConfig.Username}\n");

                        // 设置终端
                        //_shellStream.WriteLine("stty -echo");
                        //_shellStream.Flush();

                        //btnConnect.IsEnabled = false;
                        //btnDisconnect.IsEnabled = true;
                        txtCommand.IsEnabled = true;
                        btnSend.IsEnabled = true;
                        //txtStatus.Text = $"Connected to {ServerConfig.ServerIP}";
                        txtCommand.Focus();
                    });

                    // 开始读取输出
                    ReadShellStream();
                });
            }
            catch (Exception ex)
            {
                AppendTerminalText($"Connection failed: {ex.Message}\n");
                //btnConnect.IsEnabled = true;
                //btnDisconnect.IsEnabled = false;
                //txtStatus.Text = "Connection failed";
            }
        }

        private void ReadShellStream()
        {
            byte[] buffer = new byte[4096];
            while (_sshClient != null && _sshClient.IsConnected && _shellStream != null && _shellStream.CanRead)
            {
                try
                {
                    int bytesRead = _shellStream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string output = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        AppendTerminalText(output);
                    }
                }
                catch (Exception ex)
                {
                    AppendTerminalText($"Error reading stream: {ex.Message}\n");
                    break;
                }
            }
        }

        private void SendCommand()
        {
            if (_sshClient == null || !_sshClient.IsConnected || _shellStream == null)
            {
                AppendTerminalText("Not connected to SSH server.\n");
                return;
            }

            string command = txtCommand.Text.Trim();
            if (string.IsNullOrEmpty(command))
            {
                return;
            }
            // 记录本次命令
            _lastCommand = command;

            // 显示用户输入的命令
            AppendTerminalInputText($"> {command}\n");

            // 发送命令
            _shellStream.WriteLine(command);
            _shellStream.Flush();

            // 清空输入框
            txtCommand.Clear();
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();
        }

        private void Disconnect()
        {
            try
            {
                if (_sshClient != null && _sshClient.IsConnected)
                {
                    _sshClient.Disconnect();
                }
            }
            catch (Exception ex)
            {
                AppendTerminalText($"Error disconnecting: {ex.Message}\n");
            }
            finally
            {
                _sshClient?.Dispose();
                _sshClient = null;
                _shellStream = null;

                AppendTerminalText("Disconnected from SSH server.\n");
                //btnConnect.IsEnabled = true;
                //btnDisconnect.IsEnabled = false;
                txtCommand.IsEnabled = false;
                btnSend.IsEnabled = false;
                //txtStatus.Text = "Disconnected";
            }
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            SendCommand();
        }

        private void TxtCommand_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendCommand();
            }
        }
        private string _lastCommand = null;

        private string FilterAnsiEscapeSequences(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // 匹配 ANSI 转义序列的正则表达式
            string pattern = @"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07]*(\x07|\x1B\\)|\x1B[@-Z\\-_]";
            return Regex.Replace(input, pattern, string.Empty);
        }
        private void AppendTerminalInputText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                txtTerminal.AppendText(text);
                txtTerminal.ScrollToEnd();
            });
        }

        private void AppendTerminalText(string text)
        {
            // 过滤 ANSI 转义序列
            string filteredText = FilterAnsiEscapeSequences(text);

            // 过滤回显的命令（仅过滤一次）
            if (!string.IsNullOrEmpty(_lastCommand) && filteredText.Contains(_lastCommand))
            {
                _lastCommand = null;
                return;
            }
            Dispatcher.Invoke(() =>
            {
                txtTerminal.AppendText(filteredText);
                txtTerminal.ScrollToEnd();
            });
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            txtTerminal.Clear();
        }
    }
}