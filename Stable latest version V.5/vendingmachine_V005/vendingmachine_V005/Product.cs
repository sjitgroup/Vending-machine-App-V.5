using System.ComponentModel;

namespace vendingmachine_V005
{
    public class Product : INotifyPropertyChanged
    {
        private string name = "상품명";
        private int price = 1500;
        private int stock = 10;
        private int servoChannel = 0;
        private string imagePath = string.Empty;

        public string Name
        {
            get => name;
            set { name = value; OnPropertyChanged(nameof(Name)); }
        }

        public int Price
        {
            get => price;
            set { price = value; OnPropertyChanged(nameof(Price)); }
        }

        public int Stock
        {
            get => stock;
            set { stock = value; OnPropertyChanged(nameof(Stock)); }
        }

        public int ServoChannel
        {
            get => servoChannel;
            set { servoChannel = value; OnPropertyChanged(nameof(ServoChannel)); }
        }

        public string ImagePath
        {
            get => imagePath;
            set { imagePath = value; OnPropertyChanged(nameof(ImagePath)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}