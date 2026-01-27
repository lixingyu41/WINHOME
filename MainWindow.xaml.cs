using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace WINHOME
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _initialPositioned;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            IsVisibleChanged += MainWindow_IsVisibleChanged;
            KeyDown += Window_KeyDown;
        }

        // 点击 X 或 Alt+F4 会触发 Closing，拦截为隐藏而非退出
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 初次加载时设置初始位置与大小（只执行一次）
            EnsureInitialPositionAndSize();
        }

        private void EnsureInitialPositionAndSize()
        {
            if (_initialPositioned) return;

            // 使用主显示器（primary screen）的可工作区
            var work = SystemParameters.WorkArea;

            double screenW = work.Width;
            double screenH = work.Height;

            // 默认尺寸：宽 = 屏幕60%，高 = 屏幕50%
            double targetW = Math.Round(screenW * 0.6);
            double targetH = Math.Round(screenH * 0.5);

            // 最大不超过屏幕90%
            double maxW = Math.Round(screenW * 0.9);
            double maxH = Math.Round(screenH * 0.9);

            if (targetW > maxW) targetW = maxW;
            if (targetH > maxH) targetH = maxH;

            // 应用尺寸
            this.Width = targetW;
            this.Height = targetH;

            // 将窗口中心对齐到屏幕中心
            this.Left = work.Left + (work.Width - this.Width) / 2.0;
            this.Top = work.Top + (work.Height - this.Height) / 2.0;

            _initialPositioned = true;

            // 激活窗口以便接收键盘焦点
            try
            {
                this.Activate();
            }
            catch { }
        }

        private void MainWindow_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            // 只在首次可见时确保初始位置（若后续更改则不复原）
            if (IsVisible && !_initialPositioned)
            {
                EnsureInitialPositionAndSize();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Hide();
                e.Handled = true;
            }
        }
    }
}
