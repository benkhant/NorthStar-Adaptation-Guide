using UnityEngine;

public class FPSLogger : MonoBehaviour
{
    [SerializeField] private float interval = 2f;   // seconds between reports

    private int frames;
    private float elapsed;
    private float worstFrame;

    void Update()
    {
        frames++;
        elapsed += Time.unscaledDeltaTime;
        worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);

        if (elapsed >= interval)
        {
            Debug.Log($"[FPS] avg {frames / elapsed:F1} fps, worst frame {worstFrame * 1000f:F0} ms");
            frames = 0;
            elapsed = 0f;
            worstFrame = 0f;
        }
    }
}