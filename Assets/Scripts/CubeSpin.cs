using UnityEngine;

/// <summary>
/// Slowly rotates the cube so that a running app is distinguishable from a frozen one.
/// </summary>
public class CubeSpin : MonoBehaviour
{
    [SerializeField]
    private float degreesPerSecond = 30f;

    private void Update()
    {
        transform.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
