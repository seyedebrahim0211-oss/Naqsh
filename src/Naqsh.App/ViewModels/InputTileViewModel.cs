using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Naqsh.Core.Ipc;
using Naqsh.Core.Model;
using Naqsh.Localization;

namespace Naqsh.App.ViewModels;

/// <summary>One tile in the input grid.</summary>
public sealed partial class InputTileViewModel : ObservableObject
{
    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    private InputDescriptor _descriptor;

    [ObservableProperty]
    private InputStatus _status;

    [ObservableProperty]
    private bool _isOnProgram;

    [ObservableProperty]
    private bool _isOnPreview;

    public InputTileViewModel(InputDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public Guid Id => Descriptor.Id;

    private static LocalizationService L => LocalizationService.Instance;

    public string NumberText => L.Digits(Number.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public string Name => Descriptor.Name;

    public string KindText => L.Get("Input_Kind_" + Descriptor.Kind);

    public string StatusText => L.Get("Input_Status_" + Status);

    public bool IsProblem => Status is InputStatus.Offline or InputStatus.ComponentMissing;

    public string TallyText => IsOnProgram ? L.Get("Tally_Program") : IsOnPreview ? L.Get("Tally_Preview") : string.Empty;

    public string PreviewTooltip => L.Format("Tooltip_Input_Preview", Number);

    public Brush? Swatch
    {
        get
        {
            if (Descriptor.Kind != InputKind.Color)
            {
                return null;
            }

            var c = Descriptor.Color;
            var b = new SolidColorBrush(Color.FromRgb(ToByte(c.R), ToByte(c.G), ToByte(c.B)));
            b.Freeze();
            return b;
        }
    }

    private static byte ToByte(float v) => (byte)Math.Round(Math.Clamp(v, 0f, 1f) * 255f);

    /// <summary>Re-reads every localized property (after a language change).</summary>
    public void RefreshText()
    {
        OnPropertyChanged(string.Empty);
    }

    partial void OnNumberChanged(int value) => RefreshText();

    partial void OnStatusChanged(InputStatus value) => RefreshText();

    partial void OnIsOnProgramChanged(bool value) => OnPropertyChanged(nameof(TallyText));

    partial void OnIsOnPreviewChanged(bool value) => OnPropertyChanged(nameof(TallyText));

    partial void OnDescriptorChanged(InputDescriptor value) => RefreshText();
}
