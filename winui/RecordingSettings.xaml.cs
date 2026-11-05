using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Otoasobi;

public sealed partial class RecordingSettings : UserControl
{
    bool _applying = true;
    bool _ready;
    bool _closed;

    public RecordingSettings()
    {
        InitializeComponent();
        ShowError(RecAppSettings.CaptureMessage);
        RecAppSettings.CaptureStateChanged += OnCapture;
        Unloaded += (_, _) =>
        {
            _closed = true;
            RecAppSettings.CaptureStateChanged -= OnCapture;
        };
        _ = LoadAsync();
    }

    async Task LoadAsync()
    {
        var settings = RecAppSettings.Load();
        try
        {
            var inputs = await RecCapture.DevicesAsync();
            var outputs = await RecCapture.PlaybackDevicesAsync();
            if (_closed)
                return;
            Fill(InputBox, inputs, settings.DeviceId);
            Fill(OutputBox, outputs, settings.OutputDeviceId);
            _ready = true;
            _applying = false;
        }
        catch (Exception ex)
        {
            if (_closed)
                return;
            ShowError(ex.Message);
        }
    }

    void Device_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || !_ready)
            return;
        if (InputBox.SelectedItem is not RecDeviceChoice || OutputBox.SelectedItem is not RecDeviceChoice)
            return;
        RecAppSettings.SaveDevices(IdOf(InputBox), IdOf(OutputBox));
    }

    static void Fill(ComboBox box, IReadOnlyList<RecDeviceChoice> devices, string? selectedId)
    {
        box.Items.Clear();
        box.Items.Add(new RecDeviceChoice("", "規定のデバイス"));
        foreach (var device in devices)
            box.Items.Add(device);
        RecDeviceChoice? selected = null;
        var id = selectedId ?? "";
        foreach (var item in box.Items)
        {
            if (item is RecDeviceChoice choice && choice.Id == id)
                selected = choice;
        }

        box.SelectedItem = selected ?? box.Items.FirstOrDefault();
    }

    static string IdOf(ComboBox box) =>
        box.SelectedItem is RecDeviceChoice choice ? choice.Id : "";

    void OnCapture(object? sender, string? message)
    {
        if (_closed)
            return;
        ShowError(message);
    }

    void ShowError(string? message)
    {
        var text = message ?? "";
        DeviceError.Text = text;
        DeviceError.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
