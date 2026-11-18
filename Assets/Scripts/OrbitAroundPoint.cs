using UnityEngine;

/// <summary>
/// Smoothly orbits the XR rig around the CesiumGeoreference origin (the target point, which sits at
/// Unity world origin) at a fixed horizontal radius, while holding a constant height above the tile
/// surface directly below the rig. Because the height is measured against the actual surface each
/// frame (via a downward raycast), the clearance stays the same regardless of how the terrain's
/// absolute elevation changes along the orbit.
///
/// Requires the Cesium3DTileset to generate physics meshes/colliders (on by default) so the ray has
/// something to hit. Attach this component to the XR Origin GameObject.
/// </summary>
public class OrbitAroundPoint : MonoBehaviour
{
    [Tooltip("Horizontal orbit radius around the target point (the Cesium georeference origin), in meters.")]
    public float radiusMeters = 150f;

    [Tooltip("Clearance to hold above the surface directly below the rig, in meters.")]
    public float heightAboveSurfaceMeters = 50f;

    [Tooltip("Orbit speed in degrees per second. Positive = counter-clockwise seen from above. " +
             "6 deg/s = one lap per minute.")]
    public float degreesPerSecond = 6f;

    [Tooltip("Rotate the rig so the target point stays in front while orbiting. " +
             "Turn off if the continuous rotation feels disorienting.")]
    public bool faceCenter = true;

    [Tooltip("Height above the orbit path from which the downward surface-measuring ray starts, in meters.")]
    public float raycastStartHeight = 3000f;

    [Tooltip("Vertical easing time so height changes stay smooth as tiles stream in, in seconds.")]
    public float heightSmoothTime = 0.6f;

    float _angleDeg;
    float _currentY;
    float _yVelocity;
    bool _initialized;

    void Update()
    {
        _angleDeg += degreesPerSecond * Time.deltaTime;
        float rad = _angleDeg * Mathf.Deg2Rad;
        float x = Mathf.Cos(rad) * radiusMeters;
        float z = Mathf.Sin(rad) * radiusMeters;

        // Measure the surface height directly below this orbit position.
        float targetY;
        var rayStart = new Vector3(x, raycastStartHeight, z);
        if (Physics.Raycast(rayStart, Vector3.down, out var hit, raycastStartHeight * 2f))
            targetY = hit.point.y + heightAboveSurfaceMeters;
        else if (_initialized)
            targetY = _currentY;                 // no tile loaded under us yet; hold current height
        else
            targetY = heightAboveSurfaceMeters;  // first frames, before any tiles have loaded

        _currentY = _initialized
            ? Mathf.SmoothDamp(_currentY, targetY, ref _yVelocity, heightSmoothTime)
            : targetY;
        _initialized = true;

        transform.position = new Vector3(x, _currentY, z);

        if (faceCenter)
        {
            var toCenter = new Vector3(-x, 0f, -z);
            if (toCenter.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(toCenter, Vector3.up);
        }
    }
}
