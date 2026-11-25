// src/Panel/Auth/LoginWindow.xaml.cs
namespace RobloxAutoManager.Panel.Auth;

using System.Windows;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Core.Utils;

public partial class LoginWindow : Window
{
    private readonly ILicenseValidator _validator;
    public bool IsAuthenticated { get; private set; }

    public LoginWindow(ILicenseValidator validator)
    {
        InitializeComponent();
        _validator = validator;
    }

    private async void ActivateBtn_Click(object sender, RoutedEventArgs e)
    {
        string key = KeyBox.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            StatusText.Text = "Key cannot be empty.";
            return;
        }

        ActivateBtn.IsEnabled = false;
        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        StatusText.Text = "Validating...";

        var result = await _validator.ValidateAsync(key);

        if (result.Success && result.License!.IsValidFor(_validator.GetCurrentHwid()))
        {
            IsAuthenticated = true;
            DialogResult = true;
            Close();
        }
        else
        {
            StatusText.Foreground = System.Windows.Media.Brushes.Parse("#FF6B6B");
            StatusText.Text = result.ErrorMessage ?? "Invalid license or HWID mismatch.";
            ActivateBtn.IsEnabled = true;
        }
    }
}