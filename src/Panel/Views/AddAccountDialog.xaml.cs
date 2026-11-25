// src/Panel/Views/AddAccountDialog.xaml.cs
namespace RobloxAutoManager.Panel.Views;

using System.Windows;
using System.Windows.Media;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Watcher.Auth;

public partial class AddAccountDialog : Window
{
    private readonly RobloxAuthClient _auth;
    private readonly CookieJar _jar;

    public Account? Result { get; private set; }

    public AddAccountDialog(RobloxAuthClient auth, CookieJar jar)
    {
        InitializeComponent();
        _auth = auth;
        _jar = jar;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        string username = UsernameBox.Text.Trim();
        string cookie = CookieBox.Text.Trim();

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(cookie))
        {
            SetStatus("All fields required.", "#F44336");
            return;
        }

        AddBtn.IsEnabled = false;
        SetStatus("Validating cookie...", "#9E9E9E");

        try
        {
            var result = await _auth.ValidateAsync(username, cookie);

            if (!result.Success)
            {
                string reason = result.Health switch
                {
                    SessionHealth.CookieInvalid => "Cookie invalid or expired.",
                    SessionHealth.RateLimited => "Rate limited. Wait and retry.",
                    SessionHealth.NetworkDown => "Network unreachable.",
                    _ => result.Error ?? "Validation failed."
                };

                SetStatus(reason, "#F44336");
                AddBtn.IsEnabled = true;
                return;
            }

            // Rotasyon olduysa jar'daki güncel değeri al — eski cookie'yi saklama
            string effectiveCookie = _jar.Get(result.Username) ?? cookie;

            Result = new Account(
                Username: result.Username,
                Cookie: effectiveCookie,
                TargetServer: new ServerConfig(PlaceId: 0),
                UserId: result.UserId
            );

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", "#F44336");
            AddBtn.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SetStatus(string text, string hexColor)
    {
        ValidationStatus.Foreground = (Brush)new BrushConverter().ConvertFromString(hexColor)!;
        ValidationStatus.Text = text;
    }
}