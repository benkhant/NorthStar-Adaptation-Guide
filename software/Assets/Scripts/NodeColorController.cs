using UnityEngine;

public class NodeColorController : MonoBehaviour
{
    private Renderer nodeRenderer;
    private Color defaultColor = new Color(0.2f, 0.4f, 0.8f); // blue

    void Start()
    {
        nodeRenderer = GetComponentInChildren<Renderer>();
    }

    public void SetCorrect()
    {
        if (nodeRenderer != null)
            nodeRenderer.material.color = Color.green;
    }

    public void SetWrong()
    {
        if (nodeRenderer != null)
            nodeRenderer.material.color = Color.red;
    }

    public void SetDefault()
    {
        if (nodeRenderer != null)
            nodeRenderer.material.color = defaultColor;
    }
}