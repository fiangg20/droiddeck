namespace Droideck.Core;

public static class AppState
{
    public static string? Serial { get; private set; }
    public static List<DeviceInfo> Devices { get; private set; } = new();

    public static event Action? DeviceChanged;
    public static event Action? DevicesUpdated;
    public static event Action<string>? StatusChanged;

    public static void Select(string? serial)
    {
        if (Serial == serial) return;
        Serial = serial;
        DeviceChanged?.Invoke();
    }

    public static void SetDevices(List<DeviceInfo> list)
    {
        Devices = list;
        DevicesUpdated?.Invoke();
    }

    public static void SetStatus(string text) => StatusChanged?.Invoke(text);

    public static bool Need()
    {
        if (Serial != null) return true;
        SetStatus("No device selected. Connect a phone and pick it from the list at the top.");
        return false;
    }
}
