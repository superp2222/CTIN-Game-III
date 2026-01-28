using UnityEngine;

[ExecuteAlways]
public class AspectRatioEnforcer : MonoBehaviour
{
    [Tooltip("Target aspect ratio, e.g. 16:9")]
    public Vector2 targetAspect = new Vector2(16, 9);

    private void Update()
    {
        Apply();
    }

    private void Apply()
    {
        float target = targetAspect.x / targetAspect.y;
        float window = (float)Screen.width / Screen.height;

        Camera cam = Camera.main;
        if (cam == null) return;

        // Reset to full screen rect first
        Rect rect = new Rect(0, 0, 1, 1);

        if (window > target)
        {
            // Window too wide -> pillarbox (black bars left/right)
            float scale = target / window;
            rect.width = scale;
            rect.x = (1f - scale) / 2f;
        }
        else if (window < target)
        {
            // Window too tall -> letterbox (black bars top/bottom)
            float scale = window / target;
            rect.height = scale;
            rect.y = (1f - scale) / 2f;
        }

        cam.rect = rect;
    }
}
