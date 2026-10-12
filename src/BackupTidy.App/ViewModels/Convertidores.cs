using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BackupTidy.App.ViewModels;

public static class Convertidores
{
    /// <summary>true -> SemiBold, false -> Normal.</summary>
    public static readonly IValueConverter Negrita =
        new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.SemiBold : FontWeight.Normal);
}
