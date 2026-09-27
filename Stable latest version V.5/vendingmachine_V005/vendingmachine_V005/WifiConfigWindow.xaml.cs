using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using ManagedNativeWifi; // 패키지 정상 참조

namespace vendingmachine_V005
{
    public class WifiItem
    {
        public string Ssid { get; set; } = "";
        public string Signal { get; set; } = "";
        public string Channel { get; set; } = "-";
    }

    public partial class WifiConfigWindow : Window
    {
        public ObservableCollection<WifiItem> WifiList { get; set; } = new();

        public WifiConfigWindow()
        {
            InitializeComponent();
            dgWifiList.ItemsSource = WifiList;
            ScanNetworks();
        }

        private void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            ScanNetworks();
        }

        private void ScanNetworks()
        {
            try
            {
                WifiList.Clear();

                // ManagedNativeWifi 패키지의 올바른 정적 클래스 호출
                var networks = NativeWifi.EnumerateAvailableNetworks();

                foreach (var net in networks)
                {
                    string ssid = net.Ssid.ToString();
                    if (string.IsNullOrWhiteSpace(ssid)) continue;

                    string signal = $"{net.SignalQuality}%";

                    if (!WifiList.Any(w => w.Ssid == ssid))
                    {
                        // new 식 및 컬렉션 초기화 단순화 적용
                        WifiList.Add(new()
                        {
                            Ssid = ssid,
                            Signal = signal,
                            Channel = "-"
                        });
                    }
                }

                if (WifiList.Count > 0)
                {
                    dgWifiList.SelectedIndex = 0;
                }
                else
                {
                    MessageBox.Show("검색된 와이파이가 없습니다. 무선랜 장치를 확인해주세요.", "검색 결과 없음");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("와이파이 스캔 중 오류 발생: " + ex.Message, "오류");
            }
        }

        private void BtnWifiKeypad_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content != null)
            {
                txtWifiPassword.Password += btn.Content.ToString();
            }
        }

        private void BtnWifiKeypadClear_Click(object sender, RoutedEventArgs e)
        {
            txtWifiPassword.Password = string.Empty;
        }

        private void BtnWifiKeypadBack_Click(object sender, RoutedEventArgs e)
        {
            if (txtWifiPassword.Password.Length > 0)
            {
                txtWifiPassword.Password = txtWifiPassword.Password[..^1];
            }
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (dgWifiList.SelectedItem is not WifiItem selectedWifi)
            {
                MessageBox.Show("목록에서 연결할 네트워크를 선택해주세요.");
                return;
            }

            string ssid = selectedWifi.Ssid;
            string password = txtWifiPassword.Password;

            try
            {
                string profileXml = $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{ssid}</name>
    <SSIDConfig>
        <SSID>
            <name>{ssid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <security>
        <authEncryption>
            <authentication>WPA2PSK</authentication>
            <encryption>AES</encryption>
            <useOneX>false</useOneX>
        </authEncryption>
        <sharedKey>
            <keyType>passPhrase</keyType>
            <protected>false</protected>
            <keyString>{password}</keyString>
        </sharedKey>
    </security>
</WLANProfile>";

                string tempFile = Path.Combine(Path.GetTempPath(), "temp_wifi.xml");
                File.WriteAllText(tempFile, profileXml, System.Text.Encoding.UTF8);

                RunNetsh($@"wlan add profile filename=""{tempFile}""");
                RunNetsh($@"wlan connect name=""{ssid}""");

                if (File.Exists(tempFile)) File.Delete(tempFile);

                MessageBox.Show($"[{ssid}] 네트워크 연결을 요청했습니다.\n잠시 후 인터넷 상태를 확인해 주세요.", "연결 요청 성공", MessageBoxButton.OK, MessageBoxImage.Information);
                txtWifiPassword.Password = string.Empty;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("와이파이 연결 오류: " + ex.Message, "연결 실패");
            }
        }

        private static void RunNetsh(string arguments)
        {
            ProcessStartInfo psi = new()
            {
                FileName = "netsh",
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process? p = Process.Start(psi);
            p?.WaitForExit();
        }
    }
}