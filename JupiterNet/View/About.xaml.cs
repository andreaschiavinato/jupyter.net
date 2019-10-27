using System.Diagnostics;
using System.Windows;

namespace JupiterNet.View
{
    public partial class About : Window
    {
        public const string URL = "https://github.com/andreaschiavinato/jupyter.net";

        public About()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => 
            DialogResult = true;

        private void TextBlock_TouchUp(object sender, System.Windows.Input.TouchEventArgs e) => 
            Process.Start(URL);

        private void TextBlock_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            Process.Start(URL);
    }
}
