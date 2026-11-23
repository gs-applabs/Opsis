using Windows.Devices.Geolocation;

namespace MotionDesk.Services;

public static class DeviceLocation
{
    // Weather is local, but an IP-based fix is often reported in another city.
    public static bool IsUsable(double lat, double lon, double accuracyMeters, PositionSource source) =>
        WeatherCoord.Plausible(lat, lon) && double.IsFinite(accuracyMeters) &&
        accuracyMeters is > 0 and <= 10000 && source is not (PositionSource.IPAddress or PositionSource.Default);

    public static async Task<(double Lat, double Lon)?> GetApproximateAsync()
    {
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed) return null;
            var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.High, DesiredAccuracyInMeters = 1500 };
            var pos = await locator.GetGeopositionAsync(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(16));
            var coordinate = pos.Coordinate;
            var point = coordinate.Point.Position;
            if (!IsUsable(point.Latitude, point.Longitude, coordinate.Accuracy, coordinate.PositionSource)) return null;
            // Save and send only the rounded point.
            return (WeatherCoord.Round(point.Latitude), WeatherCoord.Round(point.Longitude));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or
            System.Runtime.InteropServices.COMException or TimeoutException or OperationCanceledException) { return null; }
    }
}
