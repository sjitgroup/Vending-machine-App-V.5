using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace vendingmachine_V005
{
    public partial class ImageEditorWindow : Window
    {
        private readonly Product targetProduct;
        private FrameworkElement? selectedElement = null;

        // 파워포인트 스타일 선택 핸들 관리용 리스트
        private readonly List<UIElement> selectionHandles = [];
        private Rectangle? selectionBorder = null;

        private bool isDragging = false;
        private bool isResizing = false;
        private Point clickPosition;
        private bool isInternalUpdate = false;

        public ImageEditorWindow(Product product)
        {
            InitializeComponent();
            targetProduct = product;
            txtEditingProductName.Text = $"대상 상품: {targetProduct.Name}";

            // 🌟 캔버스 빈 곳 우클릭 시 배경을 지우는 메뉴 추가
            ContextMenu canvasMenu = new ContextMenu();
            MenuItem clearBgItem = new MenuItem { Header = "⬜ 캔버스 배경 지우기 (흰색으로 초기화)" };
            clearBgItem.Click += (s, e) => { DesignCanvas.Background = Brushes.White; };
            canvasMenu.Items.Add(clearBgItem);
            DesignCanvas.ContextMenu = canvasMenu;

            LoadInitialImage();
        }

        private void LoadInitialImage()
        {
            try
            {
                if (!string.IsNullOrEmpty(targetProduct.ImagePath) && File.Exists(targetProduct.ImagePath))
                {
                    BitmapImage bmp = new();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(targetProduct.ImagePath, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();

                    // 🌟 기존에 저장된 상품 이미지도 불러올 때 완벽하게 캔버스 배경으로 꽉 채워서 고정시킵니다.
                    DesignCanvas.Background = new ImageBrush(bmp) { Stretch = Stretch.Fill };
                }
            }
            catch { }
        }

        private void AttachContextMenu(FrameworkElement element)
        {
            ContextMenu menu = new();

            if (element is Shape shape)
            {
                MenuItem fillMenu = new() { Header = "🎨 채우기 색상 변경" };
                PopulateColorMenu(fillMenu, (color) => shape.Fill = new SolidColorBrush(color));

                MenuItem strokeMenu = new() { Header = "🖊️ 윤곽선 색상 변경" };
                PopulateColorMenu(strokeMenu, (color) => shape.Stroke = new SolidColorBrush(color));

                menu.Items.Add(fillMenu);
                menu.Items.Add(strokeMenu);
            }
            else if (element is TextBlock tb)
            {
                MenuItem fgMenu = new() { Header = "🔤 글자 색상 변경" };
                PopulateColorMenu(fgMenu, (color) => tb.Foreground = new SolidColorBrush(color));

                MenuItem bgMenu = new() { Header = "🎨 배경 색상 변경" };
                PopulateColorMenu(bgMenu, (color) => tb.Background = new SolidColorBrush(color));

                MenuItem sizeUp = new() { Header = "➕ 글자 크기 키우기" };
                sizeUp.Click += (s, e) => { tb.FontSize += 4; UpdatePropertyPanel(); DrawSelectionHandles(); };

                MenuItem sizeDown = new() { Header = "➖ 글자 크기 줄이기" };
                sizeDown.Click += (s, e) => { tb.FontSize = Math.Max(10, tb.FontSize - 4); UpdatePropertyPanel(); DrawSelectionHandles(); };

                menu.Items.Add(fgMenu);
                menu.Items.Add(bgMenu);
                menu.Items.Add(new Separator());
                menu.Items.Add(sizeUp);
                menu.Items.Add(sizeDown);
            }
            else if (element is Image)
            {
                MenuItem infoMenu = new() { Header = "이미지는 색상을 변경할 수 없습니다.", IsEnabled = false };
                menu.Items.Add(infoMenu);
            }

            element.ContextMenu = menu;
        }

        private void PopulateColorMenu(MenuItem parentMenu, Action<Color> applyColor)
        {
            var colors = new (string Name, Color Value)[]
            {
                ("빨강 (Red)", Colors.Red), ("주황 (Orange)", Colors.Orange),
                ("노랑 (Yellow)", Colors.Yellow), ("초록 (Green)", Colors.Green),
                ("파랑 (Blue)", Colors.Blue), ("보라 (Purple)", Colors.Purple),
                ("검정 (Black)", Colors.Black), ("흰색 (White)", Colors.White),
                ("투명 (Transparent)", Colors.Transparent)
            };

            foreach (var c in colors)
            {
                MenuItem item = new() { Header = c.Name };
                item.Click += (s, e) => applyColor(c.Value);
                parentMenu.Items.Add(item);
            }
        }

        private void RegisterElementEvents(FrameworkElement element)
        {
            element.MouseLeftButtonDown += (s, e) =>
            {
                SelectElement(element);
                Point posInElement = e.GetPosition(element);
                clickPosition = e.GetPosition(DesignCanvas);

                if (posInElement.X >= element.Width - 12 && posInElement.Y >= element.Height - 12)
                {
                    isResizing = true;
                }
                else
                {
                    isDragging = true;
                }

                element.CaptureMouse();
                e.Handled = true;
            };

            element.MouseMove += (s, e) =>
            {
                if ((isDragging || isResizing) && selectedElement == element)
                {
                    Point currentPosition = e.GetPosition(DesignCanvas);

                    if (isDragging)
                    {
                        double offsetX = currentPosition.X - clickPosition.X;
                        double offsetY = currentPosition.Y - clickPosition.Y;

                        Canvas.SetLeft(element, Canvas.GetLeft(element) + offsetX);
                        Canvas.SetTop(element, Canvas.GetTop(element) + offsetY);

                        clickPosition = currentPosition;
                    }
                    else if (isResizing)
                    {
                        double newWidth = currentPosition.X - Canvas.GetLeft(element);
                        double newHeight = currentPosition.Y - Canvas.GetTop(element);

                        element.Width = Math.Max(30, newWidth);
                        element.Height = Math.Max(20, newHeight);
                    }

                    UpdatePropertyPanel();
                    DrawSelectionHandles();
                }
                else
                {
                    Point posInElement = e.GetPosition(element);
                    if (posInElement.X >= element.Width - 12 && posInElement.Y >= element.Height - 12)
                        element.Cursor = Cursors.SizeNWSE;
                    else
                        element.Cursor = Cursors.SizeAll;
                }
            };

            element.MouseLeftButtonUp += (s, e) =>
            {
                if (isDragging || isResizing)
                {
                    isDragging = false;
                    isResizing = false;
                    element.ReleaseMouseCapture();
                }
            };
        }

        private void ClearSelectionHandles()
        {
            if (selectionBorder != null)
            {
                DesignCanvas.Children.Remove(selectionBorder);
                selectionBorder = null;
            }
            foreach (var handle in selectionHandles)
            {
                DesignCanvas.Children.Remove(handle);
            }
            selectionHandles.Clear();
        }

        private void DrawSelectionHandles()
        {
            ClearSelectionHandles();
            if (selectedElement == null) return;

            double left = Canvas.GetLeft(selectedElement);
            double top = Canvas.GetTop(selectedElement);
            double width = selectedElement.Width;
            double height = selectedElement.Height;

            selectionBorder = new Rectangle
            {
                Width = width,
                Height = height,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 1.5,
                StrokeDashArray = [2, 2],
                IsHitTestVisible = false
            };
            Canvas.SetLeft(selectionBorder, left);
            Canvas.SetTop(selectionBorder, top);
            DesignCanvas.Children.Add(selectionBorder);

            double handleSize = 8;
            double halfHandle = handleSize / 2;

            Point[] handlePoints =
            [
                new(left - halfHandle, top - halfHandle),
                new(left + width / 2 - halfHandle, top - halfHandle),
                new(left + width - halfHandle, top - halfHandle),
                new(left + width - halfHandle, top + height / 2 - halfHandle),
                new(left + width - halfHandle, top + height - halfHandle),
                new(left + width / 2 - halfHandle, top + height - halfHandle),
                new(left - halfHandle, top + height - halfHandle),
                new(left - halfHandle, top + height / 2 - halfHandle)
            ];

            foreach (var pt in handlePoints)
            {
                Rectangle handle = new()
                {
                    Width = handleSize,
                    Height = handleSize,
                    Fill = Brushes.White,
                    Stroke = Brushes.DodgerBlue,
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(handle, pt.X);
                Canvas.SetTop(handle, pt.Y);
                DesignCanvas.Children.Add(handle);
                selectionHandles.Add(handle);
            }
        }

        private void SelectElement(FrameworkElement element)
        {
            selectedElement = element;
            PropertyPanel.IsEnabled = true;
            txtSelectedInfo.Text = $"선택됨: {element.GetType().Name}";

            if (element is TextBlock) TextPropertyPanel.Visibility = Visibility.Visible;
            else TextPropertyPanel.Visibility = Visibility.Collapsed;

            UpdatePropertyPanel();
            DrawSelectionHandles();
        }

        private void UpdatePropertyPanel()
        {
            if (selectedElement == null) return;
            isInternalUpdate = true;

            txtWidth.Text = selectedElement.Width.ToString("0");
            txtHeight.Text = selectedElement.Height.ToString("0");
            txtLeft.Text = Canvas.GetLeft(selectedElement).ToString("0");
            txtTop.Text = Canvas.GetTop(selectedElement).ToString("0");

            if (selectedElement is TextBlock tb)
            {
                txtTextContent.Text = tb.Text;
                txtFontSize.Text = tb.FontSize.ToString("0");
            }

            isInternalUpdate = false;
        }

        private void Property_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isInternalUpdate || selectedElement == null) return;

            if (double.TryParse(txtWidth.Text, out double w)) selectedElement.Width = Math.Max(10, w);
            if (double.TryParse(txtHeight.Text, out double h)) selectedElement.Height = Math.Max(10, h);
            if (double.TryParse(txtLeft.Text, out double l)) Canvas.SetLeft(selectedElement, l);
            if (double.TryParse(txtTop.Text, out double t)) Canvas.SetTop(selectedElement, t);

            DrawSelectionHandles();
        }

        private void TextProperty_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isInternalUpdate || selectedElement is not TextBlock tb) return;

            tb.Text = txtTextContent.Text;
            if (double.TryParse(txtFontSize.Text, out double fs))
            {
                tb.FontSize = Math.Max(5, fs);
            }
            DrawSelectionHandles();
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source == DesignCanvas)
            {
                selectedElement = null;
                PropertyPanel.IsEnabled = false;
                txtSelectedInfo.Text = "캔버스에서 요소를 선택하세요.";
                ClearSelectionHandles();
            }
        }

        private void BtnAddRect_Click(object sender, RoutedEventArgs e)
        {
            Rectangle rect = new()
            {
                Width = 100,
                Height = 100,
                Fill = new SolidColorBrush(Colors.CornflowerBlue),
                Stroke = new SolidColorBrush(Colors.Black),
                StrokeThickness = 2
            };
            Canvas.SetLeft(rect, 100); Canvas.SetTop(rect, 100);
            RegisterElementEvents(rect);
            AttachContextMenu(rect);
            DesignCanvas.Children.Add(rect);
            SelectElement(rect);
        }

        private void BtnAddEllipse_Click(object sender, RoutedEventArgs e)
        {
            Ellipse ellipse = new()
            {
                Width = 100,
                Height = 100,
                Fill = new SolidColorBrush(Colors.OrangeRed),
                Stroke = new SolidColorBrush(Colors.Black),
                StrokeThickness = 2
            };
            Canvas.SetLeft(ellipse, 150); Canvas.SetTop(ellipse, 150);
            RegisterElementEvents(ellipse);
            AttachContextMenu(ellipse);
            DesignCanvas.Children.Add(ellipse);
            SelectElement(ellipse);
        }

        private void BtnAddImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new() { Filter = "이미지 파일|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    MessageBoxResult result = MessageBox.Show(
                        $"이미지를 캔버스 전체 화면에 꽉 채우시겠습니까?\n\n[예] 배경으로 꽉 채우기 (어긋남 방지 고정)\n[아니오] 일반 크기로 배치하기",
                        "이미지 배치 방식 선택",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    BitmapImage bmp = new();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(dlg.FileName, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();

                    if (result == MessageBoxResult.Yes)
                    {
                        // 🌟 요소를 그리지 않고 아예 캔버스 배경(Background)으로 고정시켜버림
                        // 1픽셀도 어긋나지 않고 100% 캔버스 전체를 완벽하게 채웁니다.
                        DesignCanvas.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                        MessageBox.Show("이미지가 캔버스 배경으로 완벽하게 채워졌습니다!\n(배경을 지우려면 캔버스 빈 곳을 우클릭하세요.)", "배경 채우기 완료");
                    }
                    else
                    {
                        Image img = new()
                        {
                            Source = bmp,
                            Width = 150,
                            Height = 150,
                            Stretch = Stretch.Uniform
                        };
                        Canvas.SetLeft(img, 200);
                        Canvas.SetTop(img, 200);

                        RegisterElementEvents(img);
                        AttachContextMenu(img);
                        DesignCanvas.Children.Add(img);
                        SelectElement(img);
                    }
                }
                catch (Exception ex) { MessageBox.Show("이미지 로드 오류: " + ex.Message); }
            }
        }

        private void BtnAddText_Click(object sender, RoutedEventArgs e)
        {
            TextBlock textBlock = new()
            {
                Text = "텍스트 입력",
                FontSize = 24,
                Foreground = new SolidColorBrush(Colors.Black),
                Background = new SolidColorBrush(Colors.Transparent),
                TextWrapping = TextWrapping.Wrap,
                Width = 150,
                Height = 50
            };
            Canvas.SetLeft(textBlock, 250); Canvas.SetTop(textBlock, 250);
            RegisterElementEvents(textBlock);
            AttachContextMenu(textBlock);
            DesignCanvas.Children.Add(textBlock);
            SelectElement(textBlock);
        }

        private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (selectedElement != null)
            {
                ClearSelectionHandles();
                DesignCanvas.Children.Remove(selectedElement);
                selectedElement = null;
                PropertyPanel.IsEnabled = false;
                txtSelectedInfo.Text = "선택된 요소 없음";
            }
            else MessageBox.Show("삭제할 요소를 먼저 선택해주세요.");
        }

        private void BtnSaveDesign_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ClearSelectionHandles();

                if (!Directory.Exists(@"C:\VendingMachine\Images")) Directory.CreateDirectory(@"C:\VendingMachine\Images");

                int width = 800;
                int height = 600;

                // 🌟 저장 렌더링 로직 강화: 화면 배율 렌더링 오차 방지를 위해 
                // 화면(Visual)을 통째로 800x600 Rect에 완벽하게 압착하여 캡처합니다.
                RenderTargetBitmap rtb = new(width, height, 96, 96, PixelFormats.Pbgra32);
                DrawingVisual dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    VisualBrush vb = new VisualBrush(DesignCanvas);
                    dc.DrawRectangle(vb, null, new Rect(0, 0, width, height));
                }
                rtb.Render(dv);

                string savePath = System.IO.Path.Combine(@"C:\VendingMachine\Images", $"prod_design_{DateTime.Now.Ticks}.jpg");

                using (FileStream fs = new(savePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JpegBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(rtb));
                    encoder.Save(fs);
                }

                targetProduct.ImagePath = savePath;
                MessageBox.Show("디자인이 성공적으로 적용되었습니다!", "성공");
                DialogResult = true;
                Close();
            }
            catch (Exception ex) { MessageBox.Show("저장 오류: " + ex.Message, "오류"); }
        }

        private void BtnCloseEditor_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}