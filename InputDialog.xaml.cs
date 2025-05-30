﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PublishTool
{
    /// <summary>
    /// InputDialog.xaml 的交互逻辑
    /// </summary>
    public partial class InputDialog : Window
    {
        public string InputText => txtInput.Text;

        public InputDialog(string message, string defaultText = "")
        {
            InitializeComponent();
            this.Title = message;
            lblMessage.Text = message;
            txtInput.Text = defaultText;
            txtInput.Focus();
            txtInput.SelectAll();
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
