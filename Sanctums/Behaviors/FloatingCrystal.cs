using UnityEngine;

namespace Sanctums.Behaviors;

public class FloatingCrystal : MonoBehaviour
{
    public float floatSpeed = 1.0f;
    public float floatHeight = 0.4f;
    public float rotationSpeed = 30.0f;
    private Vector3 startPos;
    private bool m_enabled;
    public void Start()
    {
        startPos = transform.position;
    }

    public void Update()
    {
        if (m_enabled)
        {
            // Float the crystal up and down using a sinusoidal motion
            float newY = startPos.y + Mathf.Sin(Time.time * floatSpeed) * floatHeight;
            var transform1 = transform;
            var position = transform1.position;
            position = new Vector3(position.x, newY, position.z);
            transform1.position = position;
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        }
        else
        {
            if (transform.position != startPos) transform.position = startPos;
        }
    }

    public void Enable(bool enable) => m_enabled = enable;
}