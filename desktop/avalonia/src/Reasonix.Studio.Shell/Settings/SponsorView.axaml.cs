using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Displays the project's bundled sponsorship links and payment codes.</summary>
internal sealed partial class SponsorView : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="SponsorView"/> class.</summary>
    /// <param name="language">The current interface language.</param>
    /// <param name="open">The desktop's browser operation.</param>
    internal SponsorView(InterfaceLanguage language, Func<string, Task> open)
    {
        InitializeComponent();
        Note.Text = MessageCatalog.Get("feedback.if_reasonix_has_saved_you", language);
        WeChatLabel.Text = MessageCatalog.Get("feedback.wechat_pay", language);
        AlipayLabel.Text = MessageCatalog.Get("feedback.alipay", language);
        AutomationProperties.SetName(WeChatQr, MessageCatalog.Get("feedback.wechat_pay_qr_code", language));
        AutomationProperties.SetName(AlipayQr, MessageCatalog.Get("feedback.alipay_qr_code", language));
        var show = MessageCatalog.Get("feedback.show_payment_qr_codes", language);
        var hide = MessageCatalog.Get("feedback.hide_payment_qr_codes", language);
        ToggleQr.Content = show;
        ToggleQr.Click += (_, _) =>
        {
            Codes.IsVisible = !Codes.IsVisible;
            ToggleQr.Content = Codes.IsVisible ? hide : show;
        };

        const string PayPal = "https://paypal.me/yuhuahui";
        Failure.Text = MessageCatalog.Get("common.could_not_open_a_browser", language, ("at", PayPal));
        PayPalLink.Click += async (_, _) =>
        {
            try
            {
                await open(PayPal).ConfigureAwait(true);
                Failure.IsVisible = false;
            }
            catch (Exception)
            {
                Failure.IsVisible = true;
            }
        };
    }
}
