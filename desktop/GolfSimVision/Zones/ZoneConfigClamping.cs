using System;

namespace GolfSimVision.Zones;

/// <summary>
/// Server-side clamps on an incoming zone/gateway update -- the UI already applies these
/// during drag (CameraView.tsx's handleMouseMove/handleMouseUp), but the backend
/// shouldn't trust client input blindly. Same bounds as the reference UI: a minimum
/// zone size of 0.05, gateway_x kept within [0.1, 0.9].
/// </summary>
public static class ZoneConfigClamping
{
    private const double MinZoneDimension = 0.05;
    private const double MinGatewayX = 0.1;
    private const double MaxGatewayX = 0.9;

    public static ZoneConfig Clamp(ZoneConfig config)
    {
        var zone = config.StartZone;
        var clampedZone = zone with
        {
            W = Math.Max(MinZoneDimension, zone.W),
            H = Math.Max(MinZoneDimension, zone.H),
        };
        var clampedGatewayX = Math.Clamp(config.GatewayX, MinGatewayX, MaxGatewayX);
        return config with { StartZone = clampedZone, GatewayX = clampedGatewayX };
    }
}
