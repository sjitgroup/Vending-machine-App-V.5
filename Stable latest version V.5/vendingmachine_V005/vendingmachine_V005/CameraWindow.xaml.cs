using System;
using System.Windows;
using System.Windows.Threading;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace vendingmachine_V005
{
    public partial class CameraWindow : System.Windows.Window
    {
        private VideoCapture? capture;
        private DispatcherTimer? timer;

        public CameraWindow()
        {
            InitializeComponent();
            StartCamera();
        }

        private void StartCamera()
        {
            try
            {
                capture = new VideoCapture(0);
                if (!capture.IsOpened())
                {
                    MessageBox.Show("카메라를 찾을 수 없습니다.", "에러", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(33)
                };
                timer.Tick += Timer_Tick;
                timer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"카메라 초기화 오류: {ex.Message}");
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (capture != null && capture.IsOpened())
            {
                using var frame = new Mat();
                capture.Read(frame);
                if (!frame.Empty())
                {
                    WebCamImage.Source = BitmapSourceConverter.ToBitmapSource(frame);
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            timer?.Stop();
            if (capture != null)
            {
                capture.Release();
                capture.Dispose();
            }
        }
    }
}