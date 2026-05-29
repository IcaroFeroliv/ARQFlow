using System.Windows;

namespace ARQFlow.App
{
    public partial class LogoutConfirmWindow : Window
    {
        public LogoutConfirmWindow()
        {
            InitializeComponent();
        }

        private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
        {
            if (txtConfirm.Text?.Trim() == "LOGOUT")
            {
                DialogResult = true;
                Close();
                return;
            }

            MessageBox.Show("Texto incorreto. Para confirmar, digite exatamente LOGOUT.");
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
