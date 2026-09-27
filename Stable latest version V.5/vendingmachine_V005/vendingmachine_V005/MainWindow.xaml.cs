using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ManagedNativeWifi;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Drawing.Color;

namespace vendingmachine_V005
{
    public class ProductItem
    {
        public string Name { get; set; } = string.Empty;
        public int Price { get; set; }
        public int Stock { get; set; } = 1;
        public int ServoChannel { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public string RotationDirection { get; set; } = "CW";
    }

    public class RegisteredCardItem
    {
        public string CardPin { get; set; } = string.Empty;
        public string RfidValue { get; set; } = string.Empty;
        public string CardNumber { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public long Balance { get; set; } = 100000000;
    }

    public class WifiNetwork
    {
        public string TypeIcon { get; set; } = "📡";
        public bool IsConnected { get; set; }
        public string Ssid { get; set; } = string.Empty;
        public string Band { get; set; } = string.Empty;
        public int Score { get; set; }
        public bool IsLocked { get; set; } = true;
        public bool HasKey { get; set; } = false;

        public string ActionText => IsConnected ? "해제" : "연결";
        public Brush ButtonColor => IsConnected ? Brushes.IndianRed : Brushes.SlateGray;

        public Brush BarColor
        {
            get
            {
                if (Score >= 70) return (Brush)new BrushConverter().ConvertFrom("#FF2ECC71")!;
                if (Score >= 40) return (Brush)new BrushConverter().ConvertFrom("#FFF1C40F")!;
                if (Score >= 20) return (Brush)new BrushConverter().ConvertFrom("#FFE67E22")!;
                return (Brush)new BrushConverter().ConvertFrom("#FFE74C3C")!;
            }
        }
    }

    public partial class MainWindow : Window
    {
        private DispatcherTimer? checkTimer;
        private SerialPort? arduinoPort;
        private SerialPort? printerPort;
        private readonly string statusFilePath = @"C:\VendingMachine\Data\card_status.json";

        private int cartCount = 0;
        private int cartTotalPrice = 0;
        private List<ProductItem> cartItems = [];
        private bool isCardAuthenticated = false;
        private bool isDebugModeActive = false;

        private string cardRegDirectory = @"C:\VendingMachine\CardData";
        private string sjvmstDirectory = @"C:\VendingMachine\SjvmstData";
        private string selectedProductImagePath = string.Empty;

        private readonly string logoFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "sjitgrouplogo.png");

        private ObservableCollection<ProductItem> productList = [];
        private static readonly HttpClient httpClient = new();

        private string t_StatusNormal = "🟢 정상 작동 중 (카드 인증 완료)";
        private string t_StatusAbnormal = "🔴 비정상 작동 중 (F12 관리자 로그인)";
        private string t_CartSummary = "장바구니 담긴 상품: {0}개 ({1:N0}원)";

        private string currentLang = "ko";
        private string latestRfidTag = string.Empty;
        private Window? rfidWaitWindow = null;

        public MainWindow()
        {
            InitializeComponent();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            LanguageSelector.SelectedIndex = 0;

            LoadAdminProductData();
            LoadRegisteredCardsData();
            LoadRealWifiNetworks();
            StartCardStatusChecking();
            LoadArduinoSampleCode();
            InitPathBoxes();
            InitServoPinSelector();

            PortComboBox.DropDownOpened += ComPortComboBox_DropDownOpened;
            PrinterPortComboBox.DropDownOpened += ComPortComboBox_DropDownOpened;

            RefreshComPorts(PortComboBox);
            RefreshComPorts(PrinterPortComboBox);
        }

        private void InitServoPinSelector()
        {
            ServoPinSelector.Items.Clear();
            List<int> reservedPins = [0, 1, 2, 3, 13, 49, 50, 51, 52, 53];
            for (int i = 4; i <= 48; i++)
            {
                if (!reservedPins.Contains(i))
                {
                    ServoPinSelector.Items.Add(new ComboBoxItem { Content = $"디지털 {i}", Foreground = Brushes.Black });
                }
            }
            if (ServoPinSelector.Items.Count > 0) ServoPinSelector.SelectedIndex = 0;
        }

        private void ComPortComboBox_DropDownOpened(object? sender, EventArgs e)
        {
            if (sender is ComboBox cb) RefreshComPorts(cb);
        }

        private void RefreshComPorts(ComboBox cb)
        {
            string currentSelection = cb.SelectedItem is ComboBoxItem item ? item.Content.ToString() ?? "" : "";
            cb.Items.Clear();
            string[] ports = SerialPort.GetPortNames();

            if (ports.Length == 0)
            {
                cb.Items.Add(new ComboBoxItem { Content = "COM 포트 없음", Foreground = Brushes.Black });
            }
            else
            {
                foreach (string port in ports)
                {
                    cb.Items.Add(new ComboBoxItem { Content = port, Foreground = Brushes.Black });
                }
            }

            bool found = false;
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (cb.Items[i] is ComboBoxItem cbi && cbi.Content.ToString() == currentSelection)
                {
                    cb.SelectedIndex = i;
                    found = true;
                    break;
                }
            }
            if (!found && cb.Items.Count > 0) cb.SelectedIndex = 0;
        }

        private static void PrintReceiptLogo(SerialPort port, string imagePath)
        {
            if (port == null || !port.IsOpen) return;
            if (!File.Exists(imagePath)) return;

            try
            {
                using Bitmap bmp = new(imagePath);
                int maxWidth = 384;
                int targetWidth = bmp.Width > maxWidth ? maxWidth : bmp.Width;
                targetWidth = (targetWidth / 8) * 8;
                int targetHeight = (int)((double)bmp.Height * targetWidth / bmp.Width);

                using Bitmap resized = new(bmp, new System.Drawing.Size(targetWidth, targetHeight));
                int xBytes = targetWidth / 8;
                int yPixels = targetHeight;

                port.Write([0x1B, 0x61, 0x01], 0, 3);

                byte[] header = [0x1D, 0x76, 0x30, 0x00,
                    (byte)(xBytes % 256), (byte)(xBytes / 256),
                    (byte)(yPixels % 256), (byte)(yPixels / 256)];
                port.Write(header, 0, header.Length);

                byte[] imageBytes = new byte[xBytes * yPixels];
                int idx = 0;
                for (int y = 0; y < yPixels; y++)
                {
                    for (int x = 0; x < xBytes; x++)
                    {
                        byte b = 0;
                        for (int k = 0; k < 8; k++)
                        {
                            Color c = resized.GetPixel(x * 8 + k, y);
                            int luminance = (int)(c.R * 0.3 + c.G * 0.59 + c.B * 0.11);
                            if (luminance < 128 && c.A > 128) b |= (byte)(1 << (7 - k));
                        }
                        imageBytes[idx++] = b;
                    }
                }

                port.Write(imageBytes, 0, imageBytes.Length);
                port.Write([0x1B, 0x61, 0x00, 0x0A, 0x0A], 0, 5);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"영수증 로고 출력 실패: {ex.Message}");
            }
        }

        private void InitPathBoxes()
        {
            CardFolderPathBox.Text = cardRegDirectory;
            SjvmstPathBox.Text = sjvmstDirectory;
        }

        private async void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageSelector == null) return;
            LanguageSelector.IsEnabled = false;
            int index = LanguageSelector.SelectedIndex;
            currentLang = index switch { 1 => "en", 2 => "hi", 3 => "ja", _ => "ko" };
            await ApplyLanguageTextsAsync();
            LanguageSelector.IsEnabled = true;
        }

        private async Task ApplyLanguageTextsAsync()
        {
            if (AppTitleText == null || SystemStatusText == null) return;
            if (currentLang == "ko")
            {
                AppTitleText.Text = "스마트 자판기 시스템 V.6";
                ProductListTitleText.Text = "판매 상품 목록";
                ViewCartButton.Content = "장바구니 보기";
                CheckoutButton.Content = "결제하기";
                t_StatusNormal = "🟢 정상 작동 중 (카드 인증 완료)";
                t_StatusAbnormal = "🔴 비정상 작동 중 (F12 관리자 로그인)";
                t_CartSummary = "장바구니 담긴 상품: {0}개 ({1:N0}원)";
            }
            else
            {
                AppTitleText.Text = "Smart Vending Machine V.6";
                ProductListTitleText.Text = "Product List";
                ViewCartButton.Content = "View Cart";
                CheckoutButton.Content = "Checkout";
                t_StatusNormal = "🟢 Normal Operation (Card Verified)";
                t_StatusAbnormal = "🔴 Abnormal Operation (F12 Login)";
                t_CartSummary = "Items in cart: {0} ({1:N0} KRW)";
            }
            UpdateCartSummaryUI();
            SystemStatusText.Text = isCardAuthenticated ? t_StatusNormal : t_StatusAbnormal;
        }

        private void UpdateCartSummaryUI()
        {
            if (CartSummaryText != null)
            {
                CartSummaryText.Text = string.Format(t_CartSummary, cartCount, cartTotalPrice);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F12 && CustomerPanel.Visibility == Visibility.Visible) CheckAdminPassword();
        }

        private void CheckAdminPassword()
        {
            PasswordBox pwdBox = new() { Width = 300, Height = 40, FontSize = 20, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 15) };
            WrapPanel keypadPanel = new() { Width = 440, HorizontalAlignment = HorizontalAlignment.Center };

            string[] keys = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0",
                             "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P",
                             "A", "S", "D", "F", "G", "H", "J", "K", "L",
                             "Z", "X", "C", "V", "B", "N", "M", "지우기", "OK"];

            foreach (string key in keys)
            {
                Button btn = new() { Content = key, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(3) };
                if (key == "지우기" || key == "OK")
                {
                    btn.Width = 85; btn.Height = 45;
                    btn.Background = key == "지우기" ? Brushes.IndianRed : Brushes.MediumSeaGreen;
                    btn.Foreground = Brushes.White;
                }
                else { btn.Width = 35; btn.Height = 40; }

                btn.Click += (s, args) =>
                {
                    if (key == "지우기") pwdBox.Password = string.Empty;
                    else if (key != "OK") pwdBox.Password += key;
                };
                keypadPanel.Children.Add(btn);
            }

            Window pwdWin = new()
            {
                Title = "관리자 키패드 인증",
                Width = 480,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Content = new StackPanel
                {
                    Margin = new Thickness(15),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = {
                        new TextBlock { Text = "관리자 비밀번호를 입력하세요", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,10), HorizontalAlignment = HorizontalAlignment.Center },
                        pwdBox,
                        keypadPanel
                    }
                }
            };

            foreach (var child in keypadPanel.Children)
            {
                if (child is Button b && b.Content?.ToString() == "OK")
                {
                    b.Click += (s, args) =>
                    {
                        if (pwdBox.Password == "1234")
                        {
                            pwdWin.Close();
                            CustomerPanel.Visibility = Visibility.Collapsed;
                            AdminPanel.Visibility = Visibility.Visible;
                            LoadRealWifiNetworks();
                            LoadRegisteredCardsData();
                        }
                        else
                        {
                            MessageBox.Show("비밀번호가 틀렸습니다.", "인증 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                            pwdBox.Password = string.Empty;
                        }
                    };
                }
            }
            pwdWin.ShowDialog();
        }

        private void AdminLogout_Click(object sender, RoutedEventArgs e)
        {
            AdminPanel.Visibility = Visibility.Collapsed;
            CustomerPanel.Visibility = Visibility.Visible;
        }

        private void BrowseProductImage_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new()
            {
                Title = "상품 이미지 선택",
                Filter = "이미지 파일 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
            };
            if (dlg.ShowDialog() == true)
            {
                selectedProductImagePath = dlg.FileName;
                if (InputProdImageBox != null) InputProdImageBox.Text = selectedProductImagePath;
            }
        }

        private void AddToCart_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string productName)
            {
                var product = productList.FirstOrDefault(p => p.Name == productName);
                if (product != null)
                {
                    if (product.Stock <= 0)
                    {
                        MessageBox.Show($"'{product.Name}' 상품은 현재 품절(재고 0개)입니다!", "품절", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (cartItems.Any(p => p.Name == productName))
                    {
                        MessageBox.Show($"이미 장바구니에 담긴 상품입니다.", "중복 담기 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    cartItems.Add(product);
                    cartCount++;
                    cartTotalPrice += product.Price;
                    UpdateCartSummaryUI();
                }
            }
        }

        private void ViewCart_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"현재 장바구니 담긴 상품 수: {cartCount}개, 총 금액: {cartTotalPrice:N0}원", "장바구니 보기", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static string MaskUserName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "***";
            name = name.Trim();
            if (name.Length == 1) return "*";
            if (name.Length == 2) return name[0] + "*";
            return name[0] + new string('*', name.Length - 2) + name[^1];
        }

        private void Checkout_Click(object sender, RoutedEventArgs e)
        {
            if (cartCount == 0)
            {
                MessageBox.Show("장바구니가 비어 있습니다.", "결제 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            rfidWaitWindow = new Window
            {
                Title = "결제 카드 태깅 대기",
                Width = 380,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = {
                        new TextBlock { Text = "💳 결제할 카드를 리더기에 대어 주세요.", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,10), HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = $"결제 예정 금액: {cartTotalPrice:N0}원", FontSize = 14, Foreground = Brushes.SeaGreen, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,15), HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = "카드가 인식되면 자동으로 PIN 입력창이 나타납니다.", FontSize = 12, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center }
                    }
                }
            };
            rfidWaitWindow.Closed += (s, args) => { rfidWaitWindow = null; };
            rfidWaitWindow.ShowDialog();
        }

        private void ProcessCardPayment(string rfidUid)
        {
            string cardFile = Path.Combine(cardRegDirectory, $"{rfidUid}.json");
            if (!File.Exists(cardFile))
            {
                MessageBox.Show("등록되지 않은 카드입니다.\n관리자 모드에서 카드를 먼저 등록해 주세요.", "결제 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string userName = "";
            string cardPin = "";
            string cardNumber = "";
            long currentBalance = 100000000;

            try
            {
                string json = File.ReadAllText(cardFile);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                userName = root.TryGetProperty("UserName", out var un) ? un.GetString() ?? "" : "";
                cardPin = root.TryGetProperty("CardPin", out var cp) ? cp.GetString() ?? "" : "";
                cardNumber = root.TryGetProperty("CardNumber", out var cn) ? cn.GetString() ?? "" : "";

                if (root.TryGetProperty("Balance", out var bal) && bal.ValueKind == JsonValueKind.Number)
                {
                    currentBalance = bal.GetInt64();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"카드 정보를 읽는 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string maskedName = MaskUserName(userName);

            PasswordBox pinBox = new() { Width = 200, Height = 35, FontSize = 18, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 3, Margin = new Thickness(0, 0, 0, 15) };
            WrapPanel keypadPanel = new() { Width = 230, HorizontalAlignment = HorizontalAlignment.Center };
            string[] keys = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "지우기", "0", "결제"];

            Window pinWin = new()
            {
                Title = "카드 PIN 입력 (키패드)",
                Width = 320,
                Height = 350,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Content = new StackPanel
                {
                    Margin = new Thickness(15),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = {
                        new TextBlock { Text = $"{maskedName}님의 카드 PIN 번호(3자리)를 입력하세요", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,10), HorizontalAlignment = HorizontalAlignment.Center },
                        pinBox,
                        keypadPanel
                    }
                }
            };

            foreach (string key in keys)
            {
                Button btn = new() { Content = key, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(3) };
                if (key == "지우기" || key == "결제")
                {
                    btn.Width = 65; btn.Height = 50;
                    btn.Background = key == "지우기" ? Brushes.IndianRed : Brushes.MediumSeaGreen;
                    btn.Foreground = Brushes.White;
                }
                else
                {
                    btn.Width = 65; btn.Height = 50;
                    btn.Background = Brushes.WhiteSmoke;
                }

                btn.Click += (s, args) =>
                {
                    if (key == "지우기") pinBox.Password = string.Empty;
                    else if (key != "결제")
                    {
                        if (pinBox.Password.Length < 3) pinBox.Password += key;
                    }
                    else if (key == "결제")
                    {
                        string enteredPin = pinBox.Password.Trim();
                        if (enteredPin == cardPin || isDebugModeActive)
                        {
                            if (currentBalance < cartTotalPrice)
                            {
                                MessageBox.Show($"잔액이 부족합니다.\n현재 잔액: {currentBalance:N0}원 / 결제 금액: {cartTotalPrice:N0}원", "결제 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }

                            long updatedBalance = currentBalance - cartTotalPrice;

                            try
                            {
                                var updatedCardData = new { UserName = userName, CardNumber = cardNumber, CardPin = cardPin, RfidValue = rfidUid, Balance = updatedBalance };
                                File.WriteAllText(cardFile, JsonSerializer.Serialize(updatedCardData));
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"잔액 업데이트 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                            }

                            pinWin.Close();
                            MessageBox.Show($"[{maskedName}]님, {cartTotalPrice:N0}원 결제가 완료되었습니다.\n상품이 배출됩니다!\n\n💳 남은 잔액: {updatedBalance:N0}원", "결제 성공", MessageBoxButton.OK, MessageBoxImage.Information);

                            // 1. 재고 소진 및 서보 구동 전송
                            try
                            {
                                if (arduinoPort?.IsOpen == true)
                                {
                                    foreach (var item in cartItems)
                                    {
                                        item.Stock = 0;
                                        arduinoPort.WriteLine($"RUN:{item.ServoChannel}:{item.RotationDirection}");
                                        Thread.Sleep(600);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"서보모터 구동 에러: {ex.Message}", "하드웨어 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }

                            // 2. 영수증 인쇄 (입력된 영문 상품명 바로 출력)
                            try
                            {
                                if (printerPort?.IsOpen == true)
                                {
                                    PrintReceiptLogo(printerPort, logoFilePath);
                                    printerPort.WriteLine("=== PAYMENT RECEIPT ===");
                                    printerPort.WriteLine("Payment completed successfully.");
                                    printerPort.WriteLine("--------------------------------");

                                    foreach (var item in cartItems)
                                    {
                                        printerPort.WriteLine($"{item.Name} : {item.Price:N0} KRW");
                                    }

                                    printerPort.WriteLine("--------------------------------");
                                    printerPort.WriteLine($"Total Amount: {cartTotalPrice:N0} KRW");
                                    printerPort.WriteLine($"Remaining Balance: {updatedBalance:N0} KRW");
                                    printerPort.WriteLine("Thank you for using our smart vending machine!\n\n\n");

                                    byte[] cutCommand = [0x1D, 0x56, 0x01];
                                    printerPort.Write(cutCommand, 0, cutCommand.Length);
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"영수증 출력 에러: {ex.Message}", "프린터 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }

                            cartCount = 0;
                            cartTotalPrice = 0;
                            cartItems.Clear();
                            UpdateCartSummaryUI();
                            LoadRegisteredCardsData();
                        }
                        else
                        {
                            MessageBox.Show("카드 PIN 번호가 일치하지 않습니다.", "결제 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                            pinBox.Password = string.Empty;
                        }
                    }
                };
                keypadPanel.Children.Add(btn);
            }
            pinWin.ShowDialog();
        }

        private void StartCardStatusChecking()
        {
            checkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            checkTimer.Tick += CheckCardStatus;
            checkTimer.Start();
        }

        private void CheckCardStatus(object? sender, EventArgs e)
        {
            try
            {
                if (File.Exists(statusFilePath))
                {
                    string jsonString = File.ReadAllText(statusFilePath);
                    if (jsonString.Contains("Registered"))
                    {
                        isCardAuthenticated = true;
                        if (SystemStatusText != null)
                        {
                            SystemStatusText.Text = t_StatusNormal;
                            SystemStatusText.Foreground = Brushes.LightGreen;
                        }
                    }
                }
            }
            catch (Exception) { }
        }

        private void LoadAdminProductData()
        {
            productList = [
                new ProductItem { Name = "Coca-Cola", Price = 1500, Stock = 1, ServoChannel = 9, ImagePath = "images/coca.png", RotationDirection = "CW" },
                new ProductItem { Name = "Chilsung Cider", Price = 1500, Stock = 1, ServoChannel = 10, ImagePath = "images/cider.png", RotationDirection = "CW" }
            ];

            if (AdminProductGrid != null) AdminProductGrid.ItemsSource = productList;
            if (CustomerProductItemsControl != null) CustomerProductItemsControl.ItemsSource = productList;
        }

        private void RegisterNewProduct_Click(object sender, RoutedEventArgs e)
        {
            string prodName = InputProdNameBox.Text.Trim();
            string priceStr = InputProdPriceBox.Text.Trim();
            string servoStr = InputProdServoBox.Text.Trim();
            string direction = "CW";

            if (InputProdDirectionCombo?.SelectedItem is ComboBoxItem dirItem && dirItem.Content.ToString()!.Contains("반시계"))
            {
                direction = "CCW";
            }

            if (string.IsNullOrEmpty(prodName) || string.IsNullOrEmpty(priceStr) || string.IsNullOrEmpty(servoStr))
            {
                MessageBox.Show("모든 상품 정보를 정확히 입력해 주세요.", "등록 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 💡 상품 이름이 영문/숫자/기호(ASCII)만 포함되었는지 검증 (한글 입력 차단)
            foreach (char c in prodName)
            {
                if (c > 127)
                {
                    MessageBox.Show("상품 이름은 영문(영어)으로만 입력해 주세요.", "입력 형식 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            if (int.TryParse(priceStr, out int price) && int.TryParse(servoStr, out int servoChannel))
            {
                string savedImagePath = selectedProductImagePath;
                if (!string.IsNullOrEmpty(selectedProductImagePath) && File.Exists(selectedProductImagePath))
                {
                    try
                    {
                        string imgDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images");
                        if (!Directory.Exists(imgDir)) Directory.CreateDirectory(imgDir);

                        string fileName = Path.GetFileName(selectedProductImagePath);
                        string destPath = Path.Combine(imgDir, fileName);
                        File.Copy(selectedProductImagePath, destPath, true);
                        savedImagePath = destPath;
                    }
                    catch { }
                }

                productList.Add(new ProductItem { Name = prodName, Price = price, Stock = 1, ServoChannel = servoChannel, ImagePath = savedImagePath, RotationDirection = direction });

                if (AdminProductGrid != null) AdminProductGrid.ItemsSource = null;
                if (AdminProductGrid != null) AdminProductGrid.ItemsSource = productList;
                if (CustomerProductItemsControl != null) CustomerProductItemsControl.ItemsSource = null;
                if (CustomerProductItemsControl != null) CustomerProductItemsControl.ItemsSource = productList;

                MessageBox.Show($"'{prodName}' 상품이 성공적으로 등록되었습니다. (재고 1개 / {direction})", "상품 등록 완료", MessageBoxButton.OK, MessageBoxImage.Information);

                InputProdNameBox.Text = "";
                InputProdPriceBox.Text = "";
                InputProdServoBox.Text = "";
                InputProdImageBox.Text = "";
                selectedProductImagePath = string.Empty;
            }
            else
            {
                MessageBox.Show("가격과 서보 핀 번호는 숫자만 입력할 수 있습니다.", "입력 형식 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadRegisteredCardsData()
        {
            ObservableCollection<RegisteredCardItem> cardList = [];
            try
            {
                if (Directory.Exists(cardRegDirectory))
                {
                    string[] files = Directory.GetFiles(cardRegDirectory, "*.json");
                    foreach (var file in files)
                    {
                        string json = File.ReadAllText(file);
                        using JsonDocument doc = JsonDocument.Parse(json);
                        JsonElement root = doc.RootElement;

                        string userName = root.TryGetProperty("UserName", out var un) ? un.GetString() ?? "" : "";
                        string cardNumber = root.TryGetProperty("CardNumber", out var cn) ? cn.GetString() ?? "" : "";
                        string cardPin = root.TryGetProperty("CardPin", out var cp) ? cp.GetString() ?? "" : "";
                        string rfidValue = root.TryGetProperty("RfidValue", out var rv) ? rv.GetString() ?? "" : "";
                        long balance = 100000000;
                        if (root.TryGetProperty("Balance", out var bal) && bal.ValueKind == JsonValueKind.Number)
                        {
                            balance = bal.GetInt64();
                        }

                        cardList.Add(new RegisteredCardItem
                        {
                            CardPin = string.IsNullOrEmpty(cardPin) ? "***" : cardPin,
                            RfidValue = rfidValue,
                            CardNumber = cardNumber,
                            UserName = userName,
                            Balance = balance
                        });
                    }
                }
            }
            catch (Exception) { }

            if (RegisteredCardGrid != null) RegisteredCardGrid.ItemsSource = cardList;
        }

        private void DeleteCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RegisteredCardItem item)
            {
                if (MessageBox.Show($"정말 '{item.UserName}'님의 카드를 삭제하시겠습니까?", "카드 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    try
                    {
                        string filePath = Path.Combine(cardRegDirectory, $"{item.RfidValue}.json");
                        if (File.Exists(filePath)) File.Delete(filePath);
                        LoadRegisteredCardsData();
                        MessageBox.Show("정상적으로 삭제되었습니다.", "삭제 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"삭제 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void EditCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RegisteredCardItem item)
            {
                TextBox nameBox = new TextBox { Text = item.UserName, Width = 200, Height = 30, Margin = new Thickness(0, 0, 0, 10), VerticalContentAlignment = VerticalAlignment.Center };
                TextBox numBox = new TextBox { Text = item.CardNumber, Width = 200, Height = 30, MaxLength = 7, Margin = new Thickness(0, 0, 0, 10), VerticalContentAlignment = VerticalAlignment.Center };
                PasswordBox pinBox = new PasswordBox { Width = 200, Height = 30, MaxLength = 3, Margin = new Thickness(0, 0, 0, 10), VerticalContentAlignment = VerticalAlignment.Center };
                TextBox balanceBox = new TextBox { Text = item.Balance.ToString(), Width = 200, Height = 30, Margin = new Thickness(0, 0, 0, 15), VerticalContentAlignment = VerticalAlignment.Center };

                Window editWin = new Window
                {
                    Title = "사용자 정보 수정",
                    Width = 300,
                    Height = 430,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(20),
                        Children = {
                            new TextBlock { Text = "사용자 이름", FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,5) }, nameBox,
                            new TextBlock { Text = "카드 번호 (7자리)", FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,5) }, numBox,
                            new TextBlock { Text = "새 PIN 번호 (3자리)", FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,5) }, pinBox,
                            new TextBlock { Text = "잔액 (원)", FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,5) }, balanceBox,
                            new Button { Content = "저장하기", Height = 35, Background = Brushes.MediumSeaGreen, Foreground = Brushes.White, FontWeight = FontWeights.Bold }
                        }
                    }
                };

                foreach (var child in ((StackPanel)editWin.Content).Children)
                {
                    if (child is Button saveBtn)
                    {
                        saveBtn.Click += (s, args) =>
                        {
                            string nName = nameBox.Text.Trim();
                            string nNum = numBox.Text.Trim();
                            string nPin = pinBox.Password.Trim();
                            string nBalanceStr = balanceBox.Text.Trim();

                            if (string.IsNullOrEmpty(nName) || nNum.Length != 7)
                            {
                                MessageBox.Show("이름을 입력해야 하며, 카드 번호는 반드시 7자리여야 합니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                            if (!string.IsNullOrEmpty(nPin) && nPin.Length != 3)
                            {
                                MessageBox.Show("PIN 번호는 반드시 3자리여야 합니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                            if (!long.TryParse(nBalanceStr, out long nBalance))
                            {
                                MessageBox.Show("잔액은 올바른 숫자로 입력해 주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }

                            string filePath = Path.Combine(cardRegDirectory, $"{item.RfidValue}.json");
                            if (File.Exists(filePath))
                            {
                                try
                                {
                                    string existingJson = File.ReadAllText(filePath);
                                    using JsonDocument doc = JsonDocument.Parse(existingJson);
                                    string oldPin = doc.RootElement.TryGetProperty("CardPin", out var cp) ? cp.GetString() ?? "" : "";
                                    string finalPin = string.IsNullOrEmpty(nPin) ? oldPin : nPin;

                                    var updatedObj = new { UserName = nName, CardNumber = nNum, CardPin = finalPin, RfidValue = item.RfidValue, Balance = nBalance };
                                    File.WriteAllText(filePath, JsonSerializer.Serialize(updatedObj));

                                    LoadRegisteredCardsData();
                                    MessageBox.Show("정보가 성공적으로 수정되었습니다.", "수정 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                                    editWin.Close();
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show($"수정 중 오류 발생: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                            }
                        };
                    }
                }
                editWin.ShowDialog();
            }
        }

        private void RegisterNewCard_Click(object sender, RoutedEventArgs e)
        {
            string userName = InputUserNameBox.Text.Trim();
            string cardNumber = InputCardNumberBox.Text.Trim();
            string cardPin = InputCardPinBox.Password.Trim();
            string rfidValue = InputRfidBox.Text.Trim();

            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(cardNumber) || string.IsNullOrEmpty(cardPin) || string.IsNullOrEmpty(rfidValue))
            {
                MessageBox.Show("모든 항목을 입력하고 RFID 스캔을 완료해 주세요.", "등록 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (cardNumber.Length != 7)
            {
                MessageBox.Show("카드번호는 반드시 7자리여야 합니다.", "등록 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (cardPin.Length != 3)
            {
                MessageBox.Show("카드 PIN 번호는 반드시 3자리여야 합니다.", "등록 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                if (!Directory.Exists(cardRegDirectory)) Directory.CreateDirectory(cardRegDirectory);

                string filePath = Path.Combine(cardRegDirectory, $"{rfidValue}.json");
                var cardObj = new { UserName = userName, CardNumber = cardNumber, CardPin = cardPin, RfidValue = rfidValue, Balance = 100000000L };
                File.WriteAllText(filePath, JsonSerializer.Serialize(cardObj));

                MessageBox.Show($"'{userName}'님의 카드가 정상적으로 등록되었습니다.\n(기초 잔액: 100,000,000원)", "등록 성공", MessageBoxButton.OK, MessageBoxImage.Information);

                InputUserNameBox.Text = "";
                InputCardNumberBox.Text = "";
                InputCardPinBox.Password = "";
                InputRfidBox.Text = "";
                LoadRegisteredCardsData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"카드 저장 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ArduinoConnect_Click(object sender, RoutedEventArgs e)
        {
            if (PortComboBox.SelectedItem is ComboBoxItem selectedPort)
            {
                string portName = selectedPort.Content.ToString() ?? "";
                if (!string.IsNullOrEmpty(portName) && portName != "COM 포트 없음")
                {
                    try
                    {
                        arduinoPort?.Close();
                        arduinoPort = new SerialPort(portName, 9600);
                        arduinoPort.DataReceived += ArduinoPort_DataReceived;
                        arduinoPort.Open();
                        MessageBox.Show($"아두이노 메가 포트({portName}) 연결 성공!", "통신 연결", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"포트 연결 실패: {ex.Message}", "연결 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void ArduinoPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (arduinoPort?.IsOpen != true) return;
                string line = arduinoPort.ReadLine().Trim();

                if (line.StartsWith("RFID_TAG:"))
                {
                    string rfidUid = line[9..].Trim();
                    latestRfidTag = rfidUid;

                    Dispatcher.Invoke(() =>
                    {
                        if (InputRfidBox != null) InputRfidBox.Text = rfidUid;
                        if (rfidWaitWindow != null)
                        {
                            rfidWaitWindow.Close();
                            rfidWaitWindow = null;
                            ProcessCardPayment(rfidUid);
                        }
                    });
                }
            }
            catch (Exception) { }
        }

        private void CardTab_Loaded(object sender, RoutedEventArgs e)
        {
            if (InputRfidBox != null && !string.IsNullOrEmpty(latestRfidTag))
            {
                InputRfidBox.Text = latestRfidTag;
            }
        }

        private void ServoRun_Click(object sender, RoutedEventArgs e)
        {
            if (ServoPinSelector.SelectedItem is ComboBoxItem selectedItem)
            {
                string pinText = selectedItem.Content.ToString() ?? "디지털 4";
                string pinNum = pinText.Replace("디지털 ", "").Trim();
                string direction = "CW";
                if (ServoDirectionSelector?.SelectedItem is ComboBoxItem dirItem && dirItem.Content.ToString()!.Contains("반시계"))
                {
                    direction = "CCW";
                }

                try
                {
                    if (arduinoPort?.IsOpen == true) arduinoPort.WriteLine($"RUN:{pinNum}:{direction}");
                    MessageBox.Show($"{pinText} 서보모터를 {direction} 방향으로 구동합니다.", "서보 테스트", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"시리얼 전송 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ServoZero_Click(object sender, RoutedEventArgs e)
        {
            if (ServoPinSelector.SelectedItem is ComboBoxItem selectedItem)
            {
                string pinText = selectedItem.Content.ToString() ?? "디지털 4";
                string pinNum = pinText.Replace("디지털 ", "").Trim();
                try
                {
                    if (arduinoPort?.IsOpen == true) arduinoPort.WriteLine($"ZERO:{pinNum}");
                    MessageBox.Show($"{pinText} 서보모터를 0도로 정렬합니다.", "서보 정렬", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"시리얼 전송 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadRealWifiNetworks()
        {
            List<WifiNetwork> wifiList = [];
            try
            {
                HashSet<string> connectedSsids = new(StringComparer.OrdinalIgnoreCase);
                foreach (var connSsid in NativeWifi.EnumerateConnectedNetworkSsids())
                {
                    string s = connSsid.ToString();
                    if (!string.IsNullOrEmpty(s)) connectedSsids.Add(s);
                }

                foreach (var clientInterface in NativeWifi.EnumerateInterfaces())
                {
                    var (_, list) = NativeWifi.EnumerateAvailableNetworks(clientInterface.Id);
                    foreach (var net in list)
                    {
                        string ssid = net.Ssid.ToString();
                        if (string.IsNullOrEmpty(ssid)) continue;

                        wifiList.Add(new WifiNetwork
                        {
                            Ssid = ssid,
                            Band = "5GHz",
                            Score = net.SignalQuality,
                            IsConnected = connectedSsids.Contains(ssid),
                            IsLocked = net.IsSecurityEnabled
                        });
                    }
                }
            }
            catch (Exception) { }

            if (wifiList.Count == 0)
            {
                wifiList.Add(new WifiNetwork { Ssid = "검색된 Wi-Fi가 없습니다.", Band = "-", Score = 0, IsConnected = false });
            }
            if (WifiListView != null) WifiListView.ItemsSource = wifiList;
        }

        private void WifiAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string ssid)
            {
                LoadRealWifiNetworks();
            }
        }

        private void LoadArduinoSampleCode()
        {
            if (ArduinoCodeBox != null)
            {
                ArduinoCodeBox.Text = "/* 아두이노 통합 스케치 코드 */";
            }
        }

        private void CopyArduinoCode_Click(object sender, RoutedEventArgs e)
        {
            if (ArduinoCodeBox != null)
            {
                Clipboard.SetText(ArduinoCodeBox.Text);
                MessageBox.Show("아두이노 코드가 복사되었습니다.", "복사 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BrowseCardFolder_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFolderDialog dialog = new() { Title = "카드 등록 폴더 선택" };
            if (dialog.ShowDialog() == true)
            {
                cardRegDirectory = dialog.FolderName;
                if (CardFolderPathBox != null) CardFolderPathBox.Text = cardRegDirectory;
                LoadRegisteredCardsData();
            }
        }

        private void BrowseSjvmstFolder_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFolderDialog dialog = new() { Title = "sjvmst 파일 저장 위치 설정" };
            if (dialog.ShowDialog() == true)
            {
                sjvmstDirectory = dialog.FolderName;
                if (SjvmstPathBox != null) SjvmstPathBox.Text = sjvmstDirectory;
            }
        }

        private void SaveSettingsAndBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(cardRegDirectory)) Directory.CreateDirectory(cardRegDirectory);
                if (!Directory.Exists(sjvmstDirectory)) Directory.CreateDirectory(sjvmstDirectory);
                MessageBox.Show("설정이 저장되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void ConnectPrinter_Click(object sender, RoutedEventArgs e)
        {
            if (PrinterPortComboBox?.SelectedItem is ComboBoxItem selectedItem)
            {
                string portName = selectedItem.Content.ToString() ?? "";
                try
                {
                    printerPort?.Close();
                    printerPort = new SerialPort(portName, 9600);
                    printerPort.Encoding = Encoding.UTF8;
                    printerPort.Open();

                    MessageBox.Show($"영수증 프린터 포트({portName}) 연결 성공!", "프린터 연결", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            }
        }

        private void PrintTestReceipt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (printerPort?.IsOpen == true)
                {
                    PrintReceiptLogo(printerPort, logoFilePath);
                    printerPort.WriteLine("=== TEST RECEIPT ===");
                    printerPort.WriteLine("Smart Vending Machine System\n\n\n");

                    byte[] cutCommand = [0x1D, 0x56, 0x01];
                    printerPort.Write(cutCommand, 0, cutCommand.Length);
                    MessageBox.Show("테스트 영수증이 출력되었습니다.");
                }
                else
                {
                    MessageBox.Show("프린터 포트가 연결되어 있지 않습니다.", "경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void DebugModeCheck_Checked(object sender, RoutedEventArgs e) { isDebugModeActive = true; }
        private void DebugModeCheck_Unchecked(object sender, RoutedEventArgs e) { isDebugModeActive = false; }
        private void RequestAdminPrivileges_Click(object sender, RoutedEventArgs e) { }
    }
}