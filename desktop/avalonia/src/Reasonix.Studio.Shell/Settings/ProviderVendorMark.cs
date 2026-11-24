using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Instantiates the frontend's configured vendor artwork at the requested size.</summary>
internal static class ProviderVendorMark
{
    /// <summary>Creates the native view of a declared vendor logo, when available.</summary>
    /// <param name="scope">The nearest themed control.</param>
    /// <param name="vendor">Vendor mark identifier from the kernel.</param>
    /// <param name="size">The visual mark size.</param>
    /// <returns>The image control or null if there is no declared artwork.</returns>
    internal static Control? Create(Control scope, string? vendor, double size)
    {
        if (string.IsNullOrEmpty(vendor))
        {
            return null;
        }

        var key = "ProviderMark." + vendor;
        var artwork = scope.FindResource(key);
        if (artwork is UnsetValueType)
        {
            artwork = Application.Current?.FindResource(key);
        }

        return artwork is IControlTemplate template
            ? new ContentControl { Name = "VendorMark", Template = template, Width = size, Height = size, IsHitTestVisible = false }
            : null;
    }
}
