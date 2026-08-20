using System.Windows;

namespace Shieldsmith.App;

public partial class ConsentDialog : Window
{
    public ConsentDialog(string providerName, string model, string payload)
    {
        InitializeComponent();
        txtSummary.Text = $"Provider: {providerName}   Model: {model}   " +
                          $"Payload size: about {payload.Length / 1024} KB.";
        txtPayload.Text = payload;
    }

    private void btnSend_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
