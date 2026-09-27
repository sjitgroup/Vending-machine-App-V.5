using System;
using System.IO;
using System.Windows;

namespace vendingmachine_V005
{
    public partial class CardManagerWindow : Window
    {
        private readonly string statusFilePath = @"C:\VendingMachine\Data\card_status.json";

        public CardManagerWindow()
        {
            InitializeComponent();
        }

        private void RegisterCard_Click(object sender, RoutedEventArgs e)
        {
            string cardNumber = CardNumberInput.Text.Trim();
            if (string.IsNullOrEmpty(cardNumber))
            {
                MessageBox.Show("카드 번호를 입력해주세요.", "경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string? dir = Path.GetDirectoryName(statusFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string jsonData = $"{{\"CardNumber\": \"{cardNumber}\", \"Status\": \"Registered\", \"Time\": \"{DateTime.Now}\"}}";
                File.WriteAllText(statusFilePath, jsonData);

                MessageBox.Show("카드가 성공적으로 인증되었습니다! 자판기 화면에서 결제를 진행하세요.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"인증 중 오류 발생: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}