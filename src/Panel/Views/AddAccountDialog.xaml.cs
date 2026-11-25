// src/Panel/Views/AddAccountDialog.xaml.cs
namespace RobloxAutoManager.Panel.Views;

using System.Net.Http.Json;
using System.Windows;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Utils;

public partial class AddAccountDialog : Window
{
    public Account? Result { get; private set; }

    public AddAccountDialog()
    {
        InitializeComponent();
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        string username = UsernameBox.Text.Trim();
        string cookie = CookieBox.Text.Trim();

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(cookie))
        {
            ValidationStatus.Foreground = System.Windows.Media.Brushes.Parse("#F44336");
            ValidationStatus.Text = "All fields required.";
            return;
        }

        AddBtn.IsEnabled = false;
        ValidationStatus.Foreground = System.Windows.Media.Brushes.Gray;
        ValidationStatus.Text = "Validating cookie...";

        bool valid = await ValidateRobloxCookieAsync(cookie);

        if (valid)
        {
            Result = new Account(
                Username: username,
                Cookie: cookie,
                TargetServer: new ServerConfig(PlaceId: 0) // Default, settings'den değişecek
            );
            DialogResult = true;
            Close();
        }
        else
        {
            ValidationStatus.Foreground = System.Windows.Media.Brushes.Parse("#F44336");
            ValidationStatus.Text = "Invalid or expired cookie.";
            AddBtn.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static async Task<bool> ValidateRobloxCookieAsync(string cookie)
    {
        try
        {
            var req = RobloxHttpClient.CreateRequest(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated", cookie);
            var resp = await RobloxHttpClient.Instance.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}